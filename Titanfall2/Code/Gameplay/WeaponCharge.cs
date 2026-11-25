public enum Titanfall2ChargePhase { Idle, Charging, Draining, Cooldown }

/// <summary>Supported retail Charge Rifle trigger and reserve settings, resolved after multiplayer/mod overrides.</summary>
public record struct Titanfall2ChargeProfile
{
	public bool Enabled { get; set; }
	public float ChargeSeconds { get; set; }
	public float DrainSeconds { get; set; }
	public float DrainDelay { get; set; }
	public int InitialReserve { get; set; }
	public int MaximumReserve { get; set; }
	public bool InfiniteReserve { get; set; }
	public readonly bool Valid => Enabled && float.IsFinite( ChargeSeconds ) && ChargeSeconds is > 0 and <= 30
		&& float.IsFinite( DrainSeconds ) && DrainSeconds is >= 0 and <= 30
		&& float.IsFinite( DrainDelay ) && DrainDelay is >= 0 and <= 30 && MaximumReserve is > 0 and <= 10000;
}

/// <summary>Native charge start/end/fraction semantics with an explicit host shot deadline.</summary>
public record struct Titanfall2ChargeState
{
	public bool Charging { get; set; }
	public bool FiredOnPress { get; set; }
	public double ChangedAt { get; set; }
	public float InitialFraction { get; set; }
	public double ReadyAt { get; set; }

	public readonly float Fraction( Titanfall2ChargeProfile profile, double now )
	{
		if ( !profile.Valid || !double.IsFinite( now ) ) return 0;
		var elapsed = Math.Max( 0, now - ChangedAt );
		if ( Charging ) return (float)Math.Clamp( InitialFraction + elapsed / profile.ChargeSeconds, 0, 1 );
		if ( profile.DrainSeconds <= 0 ) return 0;
		return (float)Math.Clamp( InitialFraction - Math.Max( 0, elapsed - profile.DrainDelay ) / profile.DrainSeconds, 0, 1 );
	}

	public readonly Titanfall2ChargePhase Phase( Titanfall2ChargeProfile profile, double now ) => Charging
		? Titanfall2ChargePhase.Charging : now < ReadyAt ? Titanfall2ChargePhase.Cooldown
		: Fraction( profile, now ) > 0 ? Titanfall2ChargePhase.Draining : Titanfall2ChargePhase.Idle;

	public bool Begin( Titanfall2ChargeProfile profile, double now )
	{
		if ( !profile.Valid || !double.IsFinite( now ) || Charging || FiredOnPress || now < ReadyAt ) return false;
		InitialFraction = Fraction( profile, now ); ChangedAt = now; Charging = true;
		return true;
	}

	public void Release( Titanfall2ChargeProfile profile, double now )
	{
		if ( !double.IsFinite( now ) ) return;
		if ( Charging ) { InitialFraction = Fraction( profile, now ); ChangedAt = now; Charging = false; }
		FiredOnPress = false;
	}

	public void AcceptedShot( double now, float interval )
	{
		Charging = false; FiredOnPress = true; InitialFraction = 0; ChangedAt = now;
		ReadyAt = now + (float.IsFinite( interval ) ? Math.Clamp( interval, 0.015f, 30 ) : 1f);
	}

	public void Cancel( double now )
	{
		Charging = false; FiredOnPress = false; InitialFraction = 0; ChangedAt = now;
		// Preserve accepted-shot cooldown across holster, disable, drop and pickup.
	}
}

public record struct Titanfall2ChargeSnapshot
{
	public Guid OwnerId { get; set; }
	public int Serial { get; set; }
	public int InputSerial { get; set; }
	public Titanfall2ChargeState State { get; set; }
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2ChargeProfile ChargeProfile { get; init; }
	static Titanfall2ChargeProfile ReadChargeProfile( Func<string, string> get, Func<string, float, float> number )
	{
		// Other native charge modes (ADS, sustained charge, projectile charge) have
		// different trigger contracts and are deliberately not enabled by this port.
		if ( get( "OnWeaponPrimaryAttack" ) != "OnWeaponPrimaryAttack_weapon_defender" ) return default;
		var maximum = (int)Math.Clamp( number( "ammo_stockpile_max", 20 ), 1, 10000 );
		return new()
		{
			Enabled = true, ChargeSeconds = Math.Clamp( number( "charge_time", 1.3f ), 0, 30 ),
			DrainSeconds = Math.Clamp( number( "charge_cooldown_time", 1 ), 0, 30 ),
			DrainDelay = Math.Clamp( number( "charge_cooldown_delay", 0 ), 0, 30 ),
			MaximumReserve = maximum, InitialReserve = (int)Math.Clamp( number( "ammo_default_total", maximum ), 0, maximum ),
			InfiniteReserve = number( "ammo_no_remove_from_stockpile", 0 ) != 0
		};
	}
}
