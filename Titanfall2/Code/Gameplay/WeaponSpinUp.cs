/// <summary>Retail fire_rate interpolation operates on rounds per second, before taking the reciprocal.</summary>
public record struct Titanfall2SpinUpProfile
{
	public float BaseRate { get; set; }
	public float MaximumRate { get; set; }
	public float SpeedupSeconds { get; set; }
	public float CooldownSeconds { get; set; }
	public bool UseAds { get; set; }
	public readonly bool Enabled => float.IsFinite( BaseRate ) && BaseRate is >= 0.1f and <= 60
		&& float.IsFinite( MaximumRate ) && MaximumRate is >= 0.1f and <= 60
		&& (UseAds || float.IsFinite( SpeedupSeconds ) && SpeedupSeconds > 0);
	public readonly float Interval( float fraction, float fallback ) => Enabled
		? 1f / float.Lerp( BaseRate, MaximumRate, float.IsFinite( fraction ) ? Math.Clamp( fraction, 0, 1 ) : 0 ) : fallback;
}

/// <summary>Equivalent of retail m_fireRateLerp start/stop times and fractions; clock is supplied by the authority.</summary>
public struct Titanfall2SpinUpState
{
	public bool Firing { get; private set; }
	double _changedAt;
	float _fraction;
	public readonly float Fraction( Titanfall2SpinUpProfile profile, double now )
	{
		if ( !profile.Enabled || !double.IsFinite( now ) ) return 0;
		var elapsed = Math.Max( 0, now - _changedAt );
		if ( Firing ) return profile.SpeedupSeconds > 0 ? (float)Math.Clamp( _fraction + elapsed / profile.SpeedupSeconds, 0, 1 ) : 0;
		// Zero means immediate reset, not an indefinitely retained spin fraction.
		return profile.CooldownSeconds > 0 && float.IsFinite( profile.CooldownSeconds )
			? (float)Math.Clamp( _fraction - elapsed / profile.CooldownSeconds, 0, 1 ) : 0;
	}
	public void Start( Titanfall2SpinUpProfile profile, double now )
	{
		if ( Firing || !double.IsFinite( now ) ) return;
		_fraction = Fraction( profile, now ); _changedAt = now; Firing = true;
	}
	public void Stop( Titanfall2SpinUpProfile profile, double now )
	{
		if ( !Firing || !double.IsFinite( now ) ) return;
		_fraction = Fraction( profile, now ); _changedAt = now; Firing = false;
	}
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2SpinUpProfile SpinUp { get; init; }
	static Titanfall2SpinUpProfile ReadSpinUp( Func<string, float, float> number ) => new()
	{
		BaseRate = number( "fire_rate", 0 ), MaximumRate = number( "fire_rate_max", 0 ),
		SpeedupSeconds = Math.Clamp( number( "fire_rate_max_time_speedup", 0 ), 0, 60 ),
		CooldownSeconds = Math.Clamp( number( "fire_rate_max_time_cooldown", 0 ), 0, 60 ),
		UseAds = number( "fire_rate_max_use_ads", 0 ) != 0
	};
}
