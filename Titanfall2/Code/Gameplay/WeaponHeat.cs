/// <summary>Retail L-STAR uses regenerating clip ammunition as heat, not a second heat meter.</summary>
public record struct Titanfall2HeatProfile
{
	public bool Enabled { get; set; }
	public float RefillRate { get; set; }
	public float RefillDelay { get; set; }
	public float CooldownSeconds { get; set; }
	public float FireDuration { get; set; }
	public readonly bool Valid => Enabled && float.IsFinite( RefillRate ) && RefillRate is > 0 and <= 1000
		&& float.IsFinite( RefillDelay ) && RefillDelay is >= 0 and <= 30
		&& float.IsFinite( CooldownSeconds ) && CooldownSeconds is >= 0 and <= 30
		&& float.IsFinite( FireDuration ) && FireDuration is >= 0 and <= 30;
}

/// <summary>Host-clock firing run, vent lockout and fractional ammunition recovery.</summary>
public record struct Titanfall2HeatState
{
	public bool FiringRun { get; set; }
	public bool HasFired { get; set; }
	public double LastShot { get; set; }
	public double VentUntil { get; set; }
	public bool Recovering { get; set; }
	public double RefillTimestamp { get; set; }

	public readonly bool CanFire( int clip, int minimum, int cost, double now ) => now >= VentUntil
		&& clip >= (FiringRun ? cost : Math.Max( minimum, cost ));

	public void AcceptedShot( double now )
	{
		HasFired = true; FiringRun = true; LastShot = now; Recovering = false;
	}

	public bool Stop( Titanfall2HeatProfile profile, int clip, double now )
	{
		if ( !FiringRun ) return false;
		FiringRun = false;
		if ( clip <= 0 ) return false;
		VentUntil = Math.Max( VentUntil, now + profile.CooldownSeconds );
		return true;
	}

	public int Recover( Titanfall2HeatProfile profile, int clip, int maximum, bool reloading, double now, float delta )
	{
		// Native 5A1E90 blocks empty clips (they require the overheat reload),
		// and waits lastPrimaryAttack + fire_duration + regen_delay + cooldown.
		if ( !profile.Valid || !HasFired || clip <= 0 || clip >= maximum || reloading || FiringRun
			|| now <= LastShot + profile.FireDuration + profile.RefillDelay + profile.CooldownSeconds )
		{
			Recovering = false; return clip;
		}
		if ( !Recovering )
		{
			RefillTimestamp = now - Math.Max( 0, float.IsFinite( delta ) ? delta : 0 ); Recovering = true;
		}
		var accrued = Math.Max( 0, now - RefillTimestamp ) * profile.RefillRate;
		var rounds = (int)Math.Min( maximum - clip, Math.Floor( accrued ) );
		if ( rounds <= 0 ) return clip;
		RefillTimestamp += rounds / (double)profile.RefillRate;
		return clip + rounds;
	}
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2HeatProfile HeatProfile { get; init; }
	static Titanfall2HeatProfile ReadHeatProfile( Func<string, string> get, Func<string, float, float> number )
	{
		if ( get( "OnWeaponPrimaryAttack" ) != "OnWeaponPrimaryAttack_weapon_lstar" ) return default;
		return new()
		{
			Enabled = true, RefillRate = number( "regen_ammo_refill_rate", 9 ),
			RefillDelay = number( "regen_ammo_refill_start_delay", 0.2f ),
			CooldownSeconds = number( "cooldown_time", 0.6f ), FireDuration = number( "fire_duration", 0 )
		};
	}
}
