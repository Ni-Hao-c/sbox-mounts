/// <summary>Retail full-cone angles and the two independent firing kick channels.</summary>
public record struct Titanfall2SpreadStance
{
	public float Hip { get; set; }
	public float Ads { get; set; }
	public float KickHip { get; set; }
	public float KickAds { get; set; }
	public float MaxKickHip { get; set; }
	public float MaxKickAds { get; set; }
}

public record struct Titanfall2SpreadProfile
{
	public bool Enabled { get; set; }
	public Titanfall2SpreadStance Stand { get; set; }
	public Titanfall2SpreadStance Crouch { get; set; }
	public Titanfall2SpreadStance Air { get; set; }
	public float RunHip { get; set; }
	public float SprintHip { get; set; }
	public float WallRun { get; set; }
	public float WallHang { get; set; }
	public float NpcCone { get; set; }
	public float DecayDelay { get; set; }
	public float DecayRate { get; set; }
	public float ZoomIn { get; set; }
	public float ZoomOut { get; set; }
	// Descriptor 0x1809459A0 defaults to zero; positive values replace per-shot kick.
	public float TimeToMax { get; set; }

	internal readonly Titanfall2SpreadStance Stance( bool grounded, bool crouched ) => !grounded ? Air : crouched ? Crouch : Stand;

	internal readonly float BaseCone( bool grounded, bool crouched, float movement, bool sprinting, float zoom, bool wallrunning = false )
	{
		if ( wallrunning ) return float.Lerp( WallRun, WallHang, Math.Clamp( zoom, 0, 1 ) );
		var stance = Stance( grounded, crouched );
		var hip = grounded && !crouched ? float.Lerp( Stand.Hip, sprinting ? SprintHip : RunHip, Math.Clamp( movement, 0, 1 ) ) : stance.Hip;
		return float.Lerp( hip, stance.Ads, Math.Clamp( zoom, 0, 1 ) );
	}

	internal readonly Vector2 Decay( Vector2 kick, float elapsed ) => Vector2.Max( Vector2.Zero,
		kick - Vector2.One * (MathF.Max( 0, elapsed - DecayDelay ) * DecayRate) );

	internal readonly Vector2 AddShot( Vector2 kick, Titanfall2SpreadStance stance, float zoom )
	{
		// client.dll 0x1805BD7A0: hip only below half zoom; ADS always accumulates.
		if ( zoom < 0.5f ) kick.x = MathF.Min( stance.MaxKickHip, kick.x + stance.KickHip );
		kick.y = MathF.Min( stance.MaxKickAds, kick.y + stance.KickAds );
		return kick;
	}

	internal readonly float AdvanceZoom( float zoom, bool aiming, float delta )
	{
		var duration = aiming ? ZoomIn : ZoomOut;
		return duration <= 0 ? aiming ? 1 : 0 : Math.Clamp( zoom + (aiming ? delta : -delta) / duration, 0, 1 );
	}
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2SpreadProfile SpreadProfile { get; init; }

	static Titanfall2SpreadProfile ReadSpreadProfile( Func<string, float, float> number )
	{
		// Retail descriptor defaults and negative fallback rules are in
		// client.dll 0x180945660 / 0x1803C9F60; explicit zero remains zero.
		float Value( string key, float fallback )
		{
			var value = number( key, -1 );
			return value < 0 ? fallback : Math.Clamp( value, 0, 180 );
		}
		var stand = Value( "spread_stand_hip", 5 );
		var run = Value( "spread_stand_hip_run", stand );
		var airHip = Value( "spread_air_hip", stand );
		var airAds = Value( "spread_air_ads", stand );
		Titanfall2SpreadStance Stance( string name, float hip, float ads ) => new()
		{
			Hip = hip, Ads = ads,
			KickHip = Value( "spread_kick_on_fire_" + name + "_hip", 0 ),
			KickAds = Value( "spread_kick_on_fire_" + name + "_ads", 0 ),
			MaxKickHip = Value( "spread_max_kick_" + name + "_hip", 0 ),
			MaxKickAds = Value( "spread_max_kick_" + name + "_ads", 0 )
		};
		return new()
		{
			Enabled = true,
			Stand = Stance( "stand", stand, Value( "spread_stand_ads", stand ) ),
			Crouch = Stance( "crouch", Value( "spread_crouch_hip", stand ), Value( "spread_crouch_ads", stand ) ),
			Air = Stance( "air", airHip, airAds ),
			RunHip = run, SprintHip = Value( "spread_stand_hip_sprint", run ),
			WallRun = Value( "spread_wallrunning", airHip * 0.9f ), WallHang = Value( "spread_wallhanging", airAds ),
			NpcCone = Value( "npc_attack_cone_angle", stand ),
			DecayDelay = Value( "spread_decay_delay", 0.1f ), DecayRate = Value( "spread_decay_rate", 5 ),
			ZoomIn = Value( "zoom_time_in", 0.1f ), ZoomOut = Value( "zoom_time_out", 0.1f ),
			TimeToMax = Value( "spread_time_to_max", 0 )
		};
	}
}

public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2SpreadProfile SpreadProfile { get; set; }
	Titanfall2SpreadState _spreadState;
	bool _spreadInputHeld;
	bool _sentSpreadInput;
	float _lastSpreadZoom;
	double _lastSpreadAccepted;
	int _spreadBurstRemaining;

	Titanfall2SpreadStance CurrentSpreadStance => SpreadProfile.Stance( !Owner.IsValid() || Owner.IsOnGround, Owner.IsValid() && Owner.IsDucking );
	Vector2 RetailKick => _spreadState.Sample( SpreadProfile, CurrentSpreadStance, _lastSpreadZoom, Time.NowDouble );

	Vector2 RetailSpread( float zoom )
	{
		var owner = Owner;
		var grounded = !owner.IsValid() || owner.IsOnGround;
		var crouched = owner.IsValid() && owner.IsDucking;
		var movement = owner.IsValid() ? owner.Velocity.WithZ( 0 ).Length / MathF.Max( owner.WalkSpeed, 1 ) : 0;
		var cone = SpreadProfile.BaseCone( grounded, crouched, movement, IsSprinting, zoom, _parkourPose.IsWallrunning || MathF.Abs( _parkourPose.WallrunSide ) > 0.1f );
		_lastSpreadZoom = zoom;
		var kick = RetailKick;
		return Vector2.One * ((cone + float.Lerp( kick.x, kick.y, zoom )) * SpreadScale);
	}

	void RecordSpreadShot( float zoom )
	{
		if ( !SpreadProfile.Enabled ) return;
		_lastSpreadZoom = zoom;
		_lastSpreadAccepted = Time.NowDouble;
		if ( _spreadBurstRemaining > 0 ) _spreadBurstRemaining--;
		else _spreadBurstRemaining = Math.Max( 0, BurstCount - 1 );
		_spreadState.AcceptShot( SpreadProfile, CurrentSpreadStance, zoom, Time.NowDouble );
	}

	void TickSpreadRun()
	{
		if ( !SpreadProfile.Enabled || SpreadProfile.TimeToMax <= 0 ) return;
		if ( !IsProxy && Owner is { UseInputControls: true } )
		{
			var held = IsHeld && IsActive && !IsReloading && Input.Down( "attack1" );
			_spreadInputHeld = held;
			if ( held != _sentSpreadInput ) { _sentSpreadInput = held; SetSpreadTrigger( held ); }
		}
		var interrupted = !IsHeld || !IsActive || IsReloading || !HasRoundAmmo() || HeatVenting || IsRechambering;
		// A queued burst keeps its firing run after the trigger is released. Semi-auto
		// leaves ACT_VM_PRIMARYATTACK once its initial fire interval has finished.
		if ( Titanfall2SpreadState.ShouldStop( interrupted, _spreadInputHeld, _spreadBurstRemaining, PrimaryAutomatic,
			Time.NowDouble - _lastSpreadAccepted, PrimaryDelay ) ) StopSpreadRun();
		else _spreadState.Sample( SpreadProfile, CurrentSpreadStance, _lastSpreadZoom, Time.NowDouble );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void SetSpreadTrigger( bool held )
	{
		if ( !Networking.IsHost ) return;
		_spreadInputHeld = held && IsHeld && IsActive && !IsReloading;
		if ( !_spreadInputHeld && _spreadBurstRemaining == 0 ) StopSpreadRun();
	}

	void StopSpreadRun()
	{
		_spreadState.Stop( SpreadProfile, CurrentSpreadStance, _lastSpreadZoom, Time.NowDouble );
		_spreadBurstRemaining = 0;
	}
}

public sealed partial class Titanfall2Npc
{
	[Property, Sync( SyncFlags.FromHost )] public Titanfall2SpreadProfile SpreadProfile { get; set; }
	/// <summary>Host difficulty/proficiency tuning. Retail proficiency patterns are not yet simulated.</summary>
	[Property] public float ShotSpreadScale { get; set; } = 1;
	internal float FiringSpread( bool piloted, float zoom = 0f )
	{
		var clampedZoom = float.IsFinite( zoom ) ? Math.Clamp( zoom, 0f, 1f ) : 0f;
		if ( !SpreadProfile.Enabled )
			return MathF.Max( 0f, ShotSpread * (piloted ? float.Lerp( 1f, 0.45f, clampedZoom ) : 1.5f) );
		if ( !piloted )
			return MathF.Max( 0f, SpreadProfile.NpcCone * ShotSpreadScale );
		return MathF.Max( 0f, float.Lerp( SpreadProfile.Stand.Hip, SpreadProfile.Stand.Ads, clampedZoom ) );
	}
}
