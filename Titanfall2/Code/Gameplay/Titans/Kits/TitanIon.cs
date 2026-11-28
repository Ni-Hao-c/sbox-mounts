/// <summary>Retail MP Ion resource rules. Weapon costs come from mounted WeaponData.</summary>
internal sealed class Titanfall2IonEnergy
{
	internal const int Maximum = 1000;
	internal const float RegenDelay = 0.5f; // Particle accelerator OnWeaponActivate override.
	internal int Count { get; private set; } = Maximum;
	double _regen, _drain;
	float _delay;
	internal bool Spend( int amount, float regenDelay = RegenDelay )
	{
		if ( amount < 0 || Count < amount ) return false;
		if ( amount == 0 ) return true;
		Count -= amount; _delay = float.IsFinite( regenDelay ) ? Math.Clamp( regenDelay, 0, 30 ) : RegenDelay; _regen = 0;
		return true;
	}
	internal void StartDrain() => _drain = 0;
	internal void Tick( float seconds, bool piloted, bool vortex, int drainCost, float npcRegenRate = 100, float npcRegenDelay = RegenDelay )
	{
		if ( !float.IsFinite( seconds ) || seconds <= 0 ) return;
		if ( vortex )
		{
			// client!1805A3750: delta floor(chargeElapsed / (1/60)) * charge cost.
			_drain += seconds * 60.0;
			var ticks = (int)Math.Floor( _drain + 0.000001 );
			_drain -= ticks;
			Count = Math.Max( 0, Count - ticks * Math.Max( 0, drainCost ) );
			_delay = piloted ? RegenDelay : float.IsFinite( npcRegenDelay ) ? Math.Clamp( npcRegenDelay, 0, 30 ) : RegenDelay; _regen = 0;
			return;
		}
		var eligible = MathF.Max( 0, seconds - _delay );
		_delay = MathF.Max( 0, _delay - seconds );
		if ( Count == Maximum ) { _regen = 0; return; }
		// titan_atlas_stickybomb.set [$mp] = 80; NPC AI settings = 100.
		_regen += eligible * (piloted ? 80.0 : float.IsFinite( npcRegenRate ) ? Math.Clamp( npcRegenRate, 0, 10000 ) : 100.0);
		var amount = (int)Math.Floor( _regen + 0.000001 );
		_regen -= amount;
		Count = Math.Min( Maximum, Count + amount );
	}
}

internal readonly record struct Titanfall2IonPrimaryShot( bool Enabled, int Count, bool Ads, bool Piloted )
{
	// The ADS weapon mod stays installed on the low-energy single-shot fallback.
	internal float DamageScale => Ads ? 0.7f : 1;
	internal Vector3 Bearing( Vector3 direction, int index ) =>
		(direction + Rotation.LookAt( direction ).Right * (index switch { 1 => 0.022f, 2 => -0.022f, _ => 0 })).Normal;
	internal Titanfall2ProjectileSettings Flight( Titanfall2ProjectileSettings settings ) =>
		settings with { Speed = Piloted ? 8000 : 5000 };
}

public sealed partial class Titanfall2TitanKit
{
	[Sync( SyncFlags.FromHost )] public int SharedEnergy { get; private set; } = Titanfall2IonEnergy.Maximum;
	[Sync( SyncFlags.FromHost )] public float IonZoomFraction { get; private set; }
	public int SharedEnergyMaximum => Titanfall2IonEnergy.Maximum;
	public bool IonVortexActive => Kit == Chassis.Ion && GuardRemaining > 0;
	public bool BlocksTitanPrimary =>
		(Kit == Chassis.Ion && (IonVortexActive || IonZoomFraction is > 0 and < 1))
		|| (Kit == Chassis.Scorch && DefenseVolumeActive)
		|| (Kit == Chassis.Ronin && (RoninBlocking || IsPhasing));
	public bool CanIonLaser => Kit == Chassis.Ion && !IonVortexActive && OffenseRemaining <= 0
		&& IonData() is { } data && SharedEnergy >= data.LaserCost;
	readonly Titanfall2IonEnergy _ionEnergy = new();
	bool _ionAdsHeld, _ionVortexHeld, _ionVortexNeedsRelease;
	GameObject _ionPilot;
	Titanfall2IonData IonData() => Actor.IsValid()
		? (Sandbox.Mounting.Directory.Get( Actor.MountIdent ?? "titanfall2" ) as Titanfall2Mount)?.GetIonData() : null;

	/// <summary>Host cockpit input; call before primary fire. Clients cannot spend or activate.</summary>
	internal void SetIonInput( PlayerController pilot, Ray aim, bool ads, bool vortex )
	{
		if ( Kit != Chassis.Ion || !Ready || !pilot.IsValid() || pilot.IsProxy || Actor.Seat?.GetOccupant() != pilot ) return;
		if ( _ionPilot != pilot.GameObject ) ClearIonInput();
		_ionPilot = pilot.GameObject;
		_ionAdsHeld = ads;
		_ionVortexHeld = vortex;
		if ( !vortex )
		{
			_ionVortexNeedsRelease = false;
			if ( DefenseVolumeActive ) ReleaseDefenseVolume( true, aim );
		}
		else if ( !DefenseVolumeActive && !_ionVortexNeedsRelease )
		{
			_ionVortexNeedsRelease = true;
			TryStartIonVortex( aim );
		}
	}

	internal void ClearIonInput()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		if ( Kit == Chassis.Ion && _ionPilot is not null && DefenseVolumeActive ) ReleaseDefenseVolume( true, DefenseAim() );
		_ionPilot = null; _ionAdsHeld = false; _ionVortexHeld = false; _ionVortexNeedsRelease = false;
		IonZoomFraction = 0;
	}

	void TickIonEnergy()
	{
		if ( Kit != Chassis.Ion ) return;
		var pilot = Actor.Seat?.GetOccupant()?.GameObject;
		if ( _ionPilot is not null && (!_ionPilot.IsValid() || _ionPilot != pilot) ) ClearIonInput();
		if ( DefenseVolumeActive && Actor.GetComponent<Titanfall2TitanCore>() is { BlocksPrimary: true } )
			ReleaseDefenseVolume( true, DefenseAim() );
		var data = IonData();
		var target = _ionAdsHeld && pilot.IsValid() ? 1f : 0f;
		var step = Time.Delta / MathF.Max( 0.01f, data?.ZoomSeconds ?? 0.35f );
		IonZoomFraction = target > IonZoomFraction ? MathF.Min( target, IonZoomFraction + step ) : MathF.Max( target, IonZoomFraction - step );
		var aiProfile = !pilot.IsValid() ? AiProfile : null;
		var drain = aiProfile?.Defense is { Available: true } defense
			? (int)defense.Number( "shared_energy_charge_cost", data?.VortexChargeCost ?? 0 ) : data?.VortexChargeCost ?? 0;
		_ionEnergy.Tick( Time.Delta, pilot.IsValid(), DefenseVolumeActive, drain,
			aiProfile?.SharedEnergyRegenRate ?? 100, aiProfile?.SharedEnergyRegenDelay ?? Titanfall2IonEnergy.RegenDelay );
		SharedEnergy = _ionEnergy.Count;
		if ( DefenseVolumeActive && SharedEnergy == 0 ) ReleaseDefenseVolume( true, DefenseAim() );
	}

	bool TryStartIonVortex( Ray aim )
	{
		if ( !Ready || !ValidAim( aim ) || DefenseVolumeActive || IonData() is null || SharedEnergy < 100
			|| Actor.GetComponent<Titanfall2TitanCore>() is { BlocksPrimary: true } ) return false;
		// Retail ApplyActivationCost adjusts charge fraction, not shared energy.
		// With charge_time=-1/cooldown_time=0, native getter returns 0: no invented 50-point fee.
		BeginDefenseVolume(); _ionEnergy.StartDrain();
		return true;
	}
	internal bool TryReleaseIonVortex( Ray aim )
	{
		if ( !Ready || Kit != Chassis.Ion || !DefenseVolumeActive || !ValidAim( aim ) ) return false;
		ReleaseDefenseVolume( true, aim );
		return true;
	}

	internal bool TryPrepareIonPrimary( bool piloted, out Titanfall2IonPrimaryShot shot )
	{
		shot = default;
		if ( Kit != Chassis.Ion ) return true;
		if ( !Ready || BlocksTitanPrimary || IonData() is not { } data ) return false;
		var ads = piloted && IonZoomFraction >= 1;
		var split = ads && _ionEnergy.Spend( data.SplitCost );
		SharedEnergy = _ionEnergy.Count;
		shot = new( true, split ? 3 : 1, ads, piloted );
		return true;
	}
}
