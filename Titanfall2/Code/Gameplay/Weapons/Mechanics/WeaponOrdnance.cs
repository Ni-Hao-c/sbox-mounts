using Titanfall2.Formats;

/// <summary>Retail offhand units, recovery and toss timing, resolved after MP_BASE and mods.</summary>
public record struct Titanfall2OrdnanceProfile
{
	public bool Enabled { get; set; }
	public float RefillRate { get; set; }
	public float RefillDelay { get; set; }
	public float FireDuration { get; set; }
	public float CooldownSeconds { get; set; }
	public float PulloutSeconds { get; set; }
	public float ThrowSeconds { get; set; }
	public float ReleaseCycle { get; set; }
	public float ReadyCycle { get; set; }
	public float DetonateSeconds { get; set; }
	public float DetonateCycle { get; set; }
	public float MaxCookSeconds { get; set; }
	public float FuseSeconds { get; set; }
	public float PitchOffset { get; set; }
	public float InheritVelocity { get; set; }
	public bool DropOnCancel { get; set; }
	public string PrepSound1P { get; set; }
	public string PrepSound3P { get; set; }
	public string ThrowSound1P { get; set; }
	public string ThrowSound3P { get; set; }
	public bool HasModelTiming { get; set; }
	public readonly bool Valid => Enabled && HasModelTiming && float.IsFinite( RefillRate ) && RefillRate is >= 0 and <= 1000
		&& float.IsFinite( RefillDelay ) && RefillDelay is >= 0 and <= 30
		&& float.IsFinite( FireDuration ) && FireDuration is >= 0 and <= 30
		&& float.IsFinite( CooldownSeconds ) && CooldownSeconds is >= 0 and <= 30
		&& float.IsFinite( PulloutSeconds ) && PulloutSeconds is >= 0 and <= 5
		&& float.IsFinite( ThrowSeconds ) && ThrowSeconds is > 0 and <= 5
		&& float.IsFinite( ReleaseCycle ) && ReleaseCycle is >= 0 and <= 1
		&& float.IsFinite( ReadyCycle ) && ReadyCycle >= ReleaseCycle && ReadyCycle <= 1
		&& float.IsFinite( MaxCookSeconds ) && MaxCookSeconds is >= 0 and <= 30
		&& float.IsFinite( FuseSeconds ) && FuseSeconds is >= 0 and <= 30
		&& float.IsFinite( PitchOffset ) && PitchOffset is >= -45 and <= 45
		&& float.IsFinite( InheritVelocity ) && InheritVelocity is >= 0 and <= 2;

	internal readonly float RemainingFuse( double preparedAt, double now ) => FuseSeconds > 0
		? (float)Math.Max( 0.001, FuseSeconds - Math.Max( 0, now - preparedAt ) ) : 0;
	internal readonly bool HasDetonationTiming => float.IsFinite( DetonateSeconds ) && DetonateSeconds is > 0 and <= 5
		&& float.IsFinite( DetonateCycle ) && DetonateCycle is > 0 and <= 1;
	internal readonly Vector3 ThrowDirection( Vector3 direction )
	{
		var angles = Rotation.LookAt( direction.Normal ).Angles();
		// client!1805B50A0 reduces the pitch offset as the view approaches straight up.
		var scale = Math.Clamp( (angles.pitch + 90) / 90, 0, 1 );
		return (angles.WithPitch( angles.pitch - PitchOffset * scale )).Forward;
	}
}

/// <summary>Fractional clip-unit recovery, including zero units and holstered weapons.</summary>
public record struct Titanfall2OrdnanceRecovery
{
	public bool HasUsed { get; set; }
	public double LastUse { get; set; }
	public bool Recovering { get; set; }
	public double Timestamp { get; set; }
	public void Used( double now ) { HasUsed = true; LastUse = now; Recovering = false; }
	public int Recover( Titanfall2OrdnanceProfile profile, int units, int maximum, double now, float delta )
	{
		if ( !profile.Valid || !HasUsed || profile.RefillRate <= 0 || units >= maximum
			|| now <= LastUse + profile.FireDuration + profile.RefillDelay + profile.CooldownSeconds )
		{ Recovering = false; return units; }
		if ( !Recovering ) { Timestamp = now - Math.Max( 0, float.IsFinite( delta ) ? delta : 0 ); Recovering = true; }
		var amount = (int)Math.Min( maximum - units, Math.Floor( Math.Max( 0, now - Timestamp ) * profile.RefillRate ) );
		if ( amount <= 0 ) return units;
		Timestamp += amount / (double)profile.RefillRate;
		return units + amount;
	}
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2OrdnanceProfile OrdnanceProfile { get; init; }
	static Titanfall2OrdnanceProfile ReadOrdnanceProfile( Func<string, string> get, Func<string, float, float> number, bool ordnance ) => !ordnance ? default : new()
	{
		Enabled = get( "offhand_toss" ) == "1",
		RefillRate = number( "regen_ammo_refill_rate", 0 ), RefillDelay = number( "regen_ammo_refill_start_delay", 0 ),
		FireDuration = number( "fire_duration", 0 ), CooldownSeconds = number( "cooldown_time", 0 ),
		PulloutSeconds = number( "toss_pullout_time", 0 ), ThrowSeconds = number( "toss_time", 0 ),
		MaxCookSeconds = get( "OnWeaponTossPrep" ) == "Grenade_OnWeaponTossPrep" ? number( "max_cook_time", 0 ) : 0,
		FuseSeconds = number( "grenade_fuse_time", 0 ), PitchOffset = number( "projectile_launch_pitch_offset", 0 ),
		InheritVelocity = number( "projectile_inherit_owner_velocity_scale", 0 ),
		DropOnCancel = get( "OnWeaponTossCancel" ) == "Grenade_OnWeaponTossCancelDrop",
		PrepSound1P = get( "sound_deploy_1p" ), PrepSound3P = get( "sound_deploy_3p" ),
		ThrowSound1P = get( "sound_throw_1p" ), ThrowSound3P = get( "sound_throw_3p" )
	};
}

internal static class Titanfall2OrdnanceData
{
	internal static Titanfall2OrdnanceProfile WithModelTiming( Titanfall2ContentSnapshot snapshot, string viewModelPath, Titanfall2OrdnanceProfile profile )
	{
		if ( !profile.Enabled || snapshot?.GetSource( viewModelPath ) is not { } source
			|| !source.TryReadAllBytes( out var bytes, out _ ) || bytes.Length > 64 * 1024 * 1024 ) return profile;
		try { return WithModelTiming( Titanfall2Mdl53Reader.Parse( bytes, includeGeometry: false ), profile ); }
		catch ( Exception error ) when ( error is InvalidOperationException or InvalidDataException or ArgumentException ) { return profile; }
	}
	internal static Titanfall2OrdnanceProfile WithModelTiming( Titanfall2Mdl53Reader.ParsedModel model, Titanfall2OrdnanceProfile profile )
	{
		var sequences = ModelLoader.ResolvePilotOrdnanceSequences( model );
		var toss = sequences.Toss;
		var prep = sequences.Pullout;
		if ( toss is null || prep is null ) return profile;
		var release = toss.Events.FirstOrDefault( item => item.Name == "AE_WPN_TOSS_RELEASE" || string.IsNullOrEmpty( item.Name ) && item.Event == 78 );
		if ( release is null || !float.IsFinite( release.Cycle ) || release.Cycle is < 0 or > 1 ) return profile;
		var ready = toss.Events.FirstOrDefault( item => item.Name == "AE_WPN_READYTOFIRE" || string.IsNullOrEmpty( item.Name ) && item.Event == 76 );
		var detonate = sequences.Detonate?.Events.FirstOrDefault( item => item.Name == "AE_WPN_PRIMARYATTACK" );
		float Duration( Titanfall2Mdl53Reader.SequenceInfo sequence )
		{
			if ( sequence.AnimationIndices.Length == 0 || sequence.AnimationIndices[0] < 0 || sequence.AnimationIndices[0] >= model.Animations.Length ) return 0;
			var anim = model.Animations[sequence.AnimationIndices[0]].Animation;
			return anim.FramesPerSecond > 0 ? (anim.FrameCount - 1) / anim.FramesPerSecond : 0;
		}
		return profile with
		{
			HasModelTiming = true, ReleaseCycle = release.Cycle, ReadyCycle = ready?.Cycle ?? 1,
			DetonateSeconds = detonate is not null ? Duration( sequences.Detonate ) : 0, DetonateCycle = detonate?.Cycle ?? 0,
			PulloutSeconds = profile.PulloutSeconds > 0 ? profile.PulloutSeconds : Duration( prep ),
			ThrowSeconds = profile.ThrowSeconds > 0 ? profile.ThrowSeconds : Duration( toss )
		};
	}
}
