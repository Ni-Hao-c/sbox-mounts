using Chassis = Titanfall2TitanKit.Chassis;

/// <summary>Per-shot overrides produced by chassis-specific primary ADS mechanics (Northstar charge, etc.).</summary>
internal readonly record struct Titanfall2TitanPrimaryShot(
	float PilotBonusDamage,
	float TitanBonusDamage,
	string TrailOverride,
	int NorthstarChargeLevel );

/// <summary>
/// Pure deterministic rules for Titan chassis-specific primary-weapon ADS mechanics:
/// Northstar Plasma Railgun charge-up, Legion Predator Cannon barrel spin-up,
/// Ronin Leadwall ADS horizontal spread narrowing, and Scorch T-203 ballistic arc preview.
/// </summary>
internal static class Titanfall2TitanPrimaryAdsRules
{
	// Retail mp_titanweapon_sniper.txt / mp_titanweapon_sniper.nut constants.
	internal const float NorthstarChargeSeconds = 2.25f;
	internal const float NorthstarCooldownSeconds = 0.5f;
	internal const int NorthstarMaxChargeLevel = 5;
	internal const float NorthstarExtraPilotDamagePerLevel = 250f;
	internal const float NorthstarExtraTitanDamagePerLevel = 300f;

	// Retail mp_titanweapon_predator_cannon.nut fires only once zoom_frac reaches 1.0.
	internal const float LegionSpinReadyThreshold = 0.99f;

	// Matches Titanfall2Projectile gravity acceleration (800 * GravityScale).
	internal const float ProjectileGravity = 800f;

	// Retail mp_titanweapon_leadwall.nut 8-pellet horizontal-biased (up, right) offsets.
	static readonly (float Up, float Right)[] LeadwallOffsets =
	[
		(0.2f, 0.8f),
		(0.2f, -0.8f),
		(-0.2f, 0.65f),
		(-0.2f, -0.65f),
		(0.2f, 0.2f),
		(0.2f, -0.2f),
		(-0.2f, 0.2f),
		(-0.2f, -0.2f)
	];

	internal static float StepNorthstarCharge( float current, bool charging, float deltaTime )
	{
		if ( !float.IsFinite( current ) ) current = 0f;
		current = Math.Clamp( current, 0f, 1f );
		if ( !float.IsFinite( deltaTime ) || deltaTime <= 0f ) return current;
		var rate = charging ? 1f / NorthstarChargeSeconds : -1f / NorthstarCooldownSeconds;
		return Math.Clamp( current + rate * deltaTime, 0f, 1f );
	}

	internal static int NorthstarChargeLevel( float fraction )
	{
		if ( !float.IsFinite( fraction ) || fraction <= 0f ) return 0;
		return Math.Clamp( (int)MathF.Floor( fraction * NorthstarMaxChargeLevel + 0.0001f ), 0, NorthstarMaxChargeLevel );
	}

	internal static (float PilotBonus, float TitanBonus) NorthstarBonusDamage( int level )
	{
		var clamped = Math.Clamp( level, 0, NorthstarMaxChargeLevel );
		return (clamped * NorthstarExtraPilotDamagePerLevel, clamped * NorthstarExtraTitanDamagePerLevel);
	}

	internal static string NorthstarTrailEffect( int level ) => level switch
	{
		>= 4 => "P_titan_sniper3",
		>= 2 => "P_titan_sniper2",
		_ => "P_titan_sniper1"
	};

	internal static string NorthstarLevelTickSound( int level ) =>
		level >= NorthstarMaxChargeLevel
			? "Weapon_Titan_Sniper_LevelTick_Final"
			: $"Weapon_Titan_Sniper_LevelTick_{Math.Clamp( level, 1, NorthstarMaxChargeLevel - 1 )}";

	internal static bool IsLegionSpinReady( float spinFraction ) =>
		float.IsFinite( spinFraction ) && spinFraction >= LegionSpinReadyThreshold;

	internal static Vector3 LeadwallPelletDirection( Vector3 forward, int pelletIndex, float zoomFraction, float jitter = 1.45f )
	{
		var dir = forward.LengthSquared > 0.0001f ? forward.Normal : Vector3.Forward;
		var offset = LeadwallOffsets[((pelletIndex % LeadwallOffsets.Length) + LeadwallOffsets.Length) % LeadwallOffsets.Length];
		var clampedZoom = float.IsFinite( zoomFraction ) ? Math.Clamp( zoomFraction, 0f, 1f ) : 0f;
		var adsMultiplier = float.Lerp( 1f, 0.5f, clampedZoom );
		var clampedJitter = float.IsFinite( jitter ) ? Math.Clamp( jitter, 0.5f, 2.5f ) : 1.45f;
		var scale = 0.05f * clampedJitter * adsMultiplier;

		var basis = Rotation.LookAt( dir );
		return (dir + basis.Up * (offset.Up * scale) + basis.Right * (offset.Right * scale)).Normal;
	}

	internal static bool SimulateBallisticArc(
		Vector3 start,
		Vector3 initialVelocity,
		float gravityScale,
		float maxSeconds,
		float stepSeconds,
		int maxPoints,
		Func<Vector3, Vector3, (bool Hit, Vector3 EndPosition, Vector3 Normal)> trace,
		List<Vector3> outPoints,
		out Vector3 impactPoint,
		out Vector3 impactNormal )
	{
		outPoints.Clear();
		impactPoint = start;
		impactNormal = Vector3.Up;
		if ( !start.IsFinite || !initialVelocity.IsFinite || initialVelocity.LengthSquared < 1f || maxPoints < 2 )
			return false;

		var dt = Math.Clamp( stepSeconds, 0.01f, 0.25f );
		var limit = Math.Clamp( maxSeconds, dt, 6f );
		var gravity = Vector3.Down * (ProjectileGravity * Math.Clamp( gravityScale, 0f, 4f ));
		var pos = start;
		var vel = initialVelocity;
		outPoints.Add( pos );

		var elapsed = 0f;
		var maxSteps = maxPoints - 1;
		for ( var step = 0; step < maxSteps && elapsed < limit; step++ )
		{
			var stepDt = MathF.Min( dt, limit - elapsed );
			var next = pos + vel * stepDt + gravity * (0.5f * stepDt * stepDt);
			var result = trace( pos, next );
			if ( result.Hit )
			{
				impactPoint = result.EndPosition;
				impactNormal = result.Normal.LengthSquared > 0.001f ? result.Normal.Normal : Vector3.Up;
				outPoints.Add( impactPoint );
				return true;
			}
			pos = next;
			vel += gravity * stepDt;
			elapsed += stepDt;
			outPoints.Add( pos );
		}

		impactPoint = pos;
		return false;
	}
}

public sealed partial class Titanfall2TitanKit
{
	[Sync( SyncFlags.FromHost )] public float NorthstarChargeFraction { get; private set; }
	public int NorthstarChargeLevel => Kit == Chassis.Northstar
		? Titanfall2TitanPrimaryAdsRules.NorthstarChargeLevel( NorthstarChargeFraction )
		: 0;
	public float LegionSpinFraction => Kit == Chassis.Legion ? PrimaryAdsFraction : 0f;
	public bool LegionSpinReady => Kit != Chassis.Legion || Titanfall2TitanPrimaryAdsRules.IsLegionSpinReady( PrimaryAdsFraction );

	void TickChassisPrimaryAds()
	{
		if ( Kit != Chassis.Northstar )
		{
			NorthstarChargeFraction = 0f;
			return;
		}
		var charging = PrimaryAdsFraction >= 0.85f && CanPrimaryAds;
		NorthstarChargeFraction = Titanfall2TitanPrimaryAdsRules.StepNorthstarCharge( NorthstarChargeFraction, charging, Time.Delta );
	}

	void ClearChassisPrimaryAds()
	{
		NorthstarChargeFraction = 0f;
	}

	/// <summary>
	/// Host primary-fire gate and shot modifier for chassis-specific ADS mechanics.
	/// Returns false when piloted Legion has not finished spinning up the Predator Cannon.
	/// </summary>
	internal bool TryPreparePrimaryShot( bool piloted, out Titanfall2TitanPrimaryShot shot )
	{
		shot = default;
		if ( !piloted || !Ready ) return true;

		if ( Kit == Chassis.Legion )
			return LegionSpinReady;

		if ( Kit == Chassis.Northstar )
		{
			var level = NorthstarChargeLevel;
			var (pilotBonus, titanBonus) = Titanfall2TitanPrimaryAdsRules.NorthstarBonusDamage( level );
			shot = new( pilotBonus, titanBonus, Titanfall2TitanPrimaryAdsRules.NorthstarTrailEffect( level ), level );
			NorthstarChargeFraction = 0f;
			return true;
		}

		return true;
	}
}
