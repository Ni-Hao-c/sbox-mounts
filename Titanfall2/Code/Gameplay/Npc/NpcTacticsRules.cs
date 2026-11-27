using Chassis = Titanfall2TitanKit.Chassis;

internal enum Titanfall2AiDashIntent { None, EvadeHazard, KiteBack, CloseIn, LateralStrafe }

/// <summary>
/// Pure deterministic AI tactical rules for target scoring, chassis ranges, cover/flank
/// positioning, hazard evasion, squad formations, and Titan ability/core gating.
/// </summary>
internal static class Titanfall2TacticsRules
{
	internal static float PreferredRange( bool isTitan, Chassis chassis, Titanfall2Npc.CombatStyles style, float attackRange )
	{
		var range = MathF.Max( 120f, attackRange );
		if ( isTitan )
		{
			return chassis switch
			{
				Chassis.Northstar => MathF.Min( range * 0.85f, 2100f ),
				Chassis.Ronin => MathF.Min( range * 0.45f, 340f ),
				Chassis.Scorch => MathF.Min( range * 0.55f, 620f ),
				Chassis.Legion => MathF.Min( range * 0.65f, 1150f ),
				Chassis.Ion or Chassis.Tone or Chassis.Monarch => MathF.Min( range * 0.62f, 1050f ),
				_ => MathF.Min( range * 0.6f, 1400f )
			};
		}

		return style switch
		{
			Titanfall2Npc.CombatStyles.Assault => MathF.Min( range * 0.35f, 360f ),
			Titanfall2Npc.CombatStyles.Pilot => MathF.Min( range * 0.55f, 520f ),
			_ => MathF.Min( range * 0.62f, 680f )
		};
	}

	internal static float TooCloseRange( bool isTitan, Chassis chassis, Titanfall2Npc.CombatStyles style, float preferredRange )
	{
		if ( isTitan )
		{
			return chassis switch
			{
				Chassis.Northstar => MathF.Min( preferredRange * 0.48f, 820f ),
				Chassis.Ronin or Chassis.Scorch => 0f,
				_ => preferredRange * 0.36f
			};
		}
		return style == Titanfall2Npc.CombatStyles.Assault ? 0f : preferredRange * 0.45f;
	}

	/// <summary>Lower score = higher priority target.</summary>
	internal static float ScoreTarget(
		float distance,
		bool isCurrentTarget,
		bool isRecentAttacker,
		bool selfIsTitan,
		bool targetIsTitan,
		bool targetIsHighThreatFrontier,
		float targetHealthFraction,
		bool selfPrefersShielded = false,
		bool targetHasShield = false )
	{
		var score = MathF.Max( 1f, distance );
		if ( isCurrentTarget ) score *= 0.72f;
		if ( isRecentAttacker ) score *= 0.65f;
		if ( targetIsHighThreatFrontier ) score *= 0.55f;
		if ( targetHealthFraction is > 0f and <= 0.30f ) score *= 0.82f;
		if ( selfPrefersShielded && targetHasShield ) score *= 0.70f;

		if ( selfIsTitan && targetIsTitan ) score *= 0.78f;
		else if ( !selfIsTitan && targetIsTitan ) score *= 1.35f;

		return score;
	}

	/// <summary>
	/// Rewards lateral flanking positions (~50°–120° off the enemy's facing) and penalizes
	/// sectors already occupied by squadmates so units naturally create crossfire.
	/// Returns a score adjustment (negative = better).
	/// </summary>
	internal static float FlankScoreAdjustment(
		Vector3 candidateFromTarget,
		Vector3 targetForward,
		IReadOnlyList<Vector3> allyDirectionsFromTarget,
		bool prefersFlank )
	{
		var dir = candidateFromTarget.WithZ( 0 );
		if ( dir.LengthSquared < 0.01f ) return 0f;
		dir = dir.Normal;

		var adjustment = 0f;
		var fwd = targetForward.WithZ( 0 );
		if ( fwd.LengthSquared > 0.01f )
		{
			var facingDot = Vector3.Dot( dir, fwd.Normal );
			// Lateral flank sits near dot ~= 0 (60°–110°); rear is dot < -0.3.
			var lateralQuality = 1f - MathF.Abs( facingDot + 0.15f );
			adjustment -= lateralQuality * (prefersFlank ? 240f : 90f);
		}

		if ( allyDirectionsFromTarget is null || allyDirectionsFromTarget.Count == 0 ) return adjustment;
		foreach ( var allyDir in allyDirectionsFromTarget )
		{
			var alignment = Vector3.Dot( dir, allyDir );
			if ( alignment > 0.78f ) adjustment += 180f * alignment;
			else if ( alignment is > -0.35f and < 0.45f ) adjustment -= 65f;
		}
		return adjustment;
	}

	/// <summary>Deterministic wedge/echelon escort offset around a leader so followers keep formation.</summary>
	internal static Vector3 FormationOffset( int slotIndex, float spacing, Vector3 leaderForward, Vector3 leaderRight )
	{
		var forward = leaderForward.WithZ( 0 );
		forward = forward.LengthSquared > 0.001f ? forward.Normal : Vector3.Forward;
		var right = leaderRight.WithZ( 0 );
		right = right.LengthSquared > 0.001f ? right.Normal : Vector3.Cross( forward, Vector3.Up ).Normal;

		var unit = MathF.Max( 48f, spacing );
		var row = (slotIndex / 2) + 1;
		var side = (slotIndex & 1) == 0 ? 1f : -1f;
		return -forward * (unit * (0.85f * row)) + right * (side * unit * (0.75f + 0.45f * (row - 1)));
	}

	internal static bool ShouldEvadeHazard( Vector3 selfPosition, Vector3 hazardPosition, float dangerRadius ) =>
		dangerRadius > 0 && selfPosition.Distance( hazardPosition ) < dangerRadius;

	internal static Vector3 ComputeEvadePoint( Vector3 selfPosition, Vector3 hazardPosition, float escapeDistance )
	{
		var away = (selfPosition - hazardPosition).WithZ( 0 );
		var dir = away.LengthSquared > 1f ? away.Normal : Vector3.Forward;
		return selfPosition + dir * MathF.Max( 120f, escapeDistance );
	}

	internal static bool ShouldUseTitanDefense(
		Chassis chassis,
		float healthFraction,
		float shieldFraction,
		bool underFire,
		float targetDistance,
		int sharedEnergy,
		bool defenseActive )
	{
		if ( defenseActive ) return false;
		return chassis switch
		{
			Chassis.Monarch => shieldFraction < 0.85f,
			Chassis.Ion => underFire && sharedEnergy >= 320,
			Chassis.Legion => underFire || (targetDistance <= 1400f && shieldFraction < 0.75f),
			Chassis.Tone => underFire || shieldFraction < 0.65f,
			Chassis.Scorch => targetDistance <= 280f || (underFire && shieldFraction < 0.6f),
			Chassis.Northstar => targetDistance <= 760f,
			Chassis.Ronin => underFire && targetDistance > 200f && (shieldFraction < 0.65f || healthFraction < 0.7f),
			_ => healthFraction < 0.65f || shieldFraction < 0.45f
		};
	}

	internal static bool ShouldUseTitanOffense(
		Chassis chassis,
		float targetDistance,
		float attackRange,
		bool targetGrounded,
		bool hasTrackerLock,
		int sharedEnergy,
		bool underFire )
	{
		if ( targetDistance > MathF.Max( 400f, attackRange ) ) return false;
		return chassis switch
		{
			Chassis.Tone => hasTrackerLock,
			Chassis.Ion => sharedEnergy >= (underFire ? 750 : 600) && targetDistance >= 320f,
			Chassis.Scorch => targetGrounded && targetDistance <= 1100f,
			Chassis.Ronin => targetGrounded && targetDistance <= 950f,
			Chassis.Northstar => targetDistance >= 280f,
			Chassis.Legion => targetDistance <= 1800f,
			Chassis.Monarch => targetDistance <= 1500f,
			_ => true
		};
	}

	internal static Titanfall2AiDashIntent ChooseAiDash(
		Chassis chassis,
		float healthFraction,
		float shieldFraction,
		bool underFire,
		bool hazardNearby,
		float targetDistance,
		float preferredRange )
	{
		if ( hazardNearby ) return Titanfall2AiDashIntent.EvadeHazard;
		if ( chassis == Chassis.Northstar && targetDistance < MathF.Max( 450f, preferredRange * 0.42f ) )
			return Titanfall2AiDashIntent.KiteBack;
		if ( chassis == Chassis.Ronin && targetDistance > preferredRange * 1.45f )
			return Titanfall2AiDashIntent.CloseIn;
		if ( underFire && (shieldFraction < 0.35f || healthFraction < 0.55f) )
			return Titanfall2AiDashIntent.LateralStrafe;
		return Titanfall2AiDashIntent.None;
	}

	internal static bool ShouldActivateTitanCore(
		Chassis chassis,
		float healthFraction,
		float shieldFraction,
		float targetDistance,
		float attackRange,
		bool targetIsHeavy,
		bool targetGrounded )
	{
		if ( chassis == Chassis.Monarch )
			return shieldFraction < 0.55f || healthFraction < 0.8f || targetDistance <= attackRange;
		if ( targetDistance > attackRange ) return false;
		return chassis switch
		{
			Chassis.Ronin => targetDistance <= 700f,
			Chassis.Scorch => targetGrounded && targetDistance <= 1050f,
			Chassis.Northstar => targetDistance <= 1600f && (targetIsHeavy || healthFraction < 0.65f),
			Chassis.Ion or Chassis.Tone or Chassis.Legion => targetIsHeavy || healthFraction < 0.55f || targetDistance <= attackRange * 0.7f,
			_ => false
		};
	}
}
