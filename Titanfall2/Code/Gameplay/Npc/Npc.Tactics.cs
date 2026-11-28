using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using Sandbox.Navigation;

/// <summary>Composed NPC combat contact memory, hazard evasion, cover/flank planning, and squad callout controller.</summary>
internal sealed class Titanfall2NpcTacticsController
{
	Vector3? _lastContact;
	Vector3 _lastAimPoint;
	Vector3 _seenVelocity;
	Vector3 _seenForward;
	TimeSince _visualContactAge = 100;
	GameObject _lastAttacker;
	TimeUntil _lastAttackerMemory;
	TimeSince _contactAge = 100;
	TimeUntil _contactScan;
	TimeUntil _reaction;
	TimeUntil _plan;
	TimeUntil _squadCall;
	TimeUntil _underFire;
	TimeUntil _relocate;
	Vector3? _combatDestination;
	Vector3? _failedDestination;
	TimeUntil _avoidFailed;
	Vector3 _progressPosition;
	TimeSince _progressAge;
	TimeSince _moveAge;
	bool _seesContact;

	internal Titanfall2Npc.CombatActions CombatAction { get; private set; }
	internal Vector3? CombatDestination => _combatDestination;
	internal Vector3? LastContact => _lastContact;
	internal Vector3 LastAimPoint => _lastAimPoint;
	internal bool SeesContact => _seesContact;
	internal float ContactAge => _contactAge;
	internal float VisualContactAge => _visualContactAge;
	internal bool ReactionReady => _reaction <= 0;
	internal bool IsUnderFire => _underFire > 0;

	internal void ResetActionIdle() => CombatAction = Titanfall2Npc.CombatActions.Idle;

	internal bool HasCombatContact( Titanfall2Npc npc ) => npc.CanFight && CanEngage( npc, npc.CurrentTarget ) && _lastContact.HasValue
		&& _contactAge < MathF.Max( 0.1f, npc.ContactMemorySeconds );

	static bool CanEngage( Titanfall2Npc npc, GameObject target ) => npc.CanTarget( target )
		&& (target.GetComponentInParent<PlayerController>() is null || (npc.AttackPlayers && !npc.Friendly))
		&& npc.IsHostileToFaction( target.GetComponentInParent<BaseNpc>()?.Faction ?? Factions.Player );

	// Senses remains the acquisition authority. Once occluded, navigate only toward the snapshot.
	internal void RefreshContact( Titanfall2Npc npc, bool force = false )
	{
		if ( !force && _contactScan > 0 ) return;
		_contactScan = 0.2f;
		var wasVisible = _seesContact;
		_seesContact = false;
		if ( !npc.CanFight ) { ForgetContact( npc ); return; }
		GameObject best = null;
		var score = float.MaxValue;
		if ( npc.Senses.IsValid() )
			foreach ( var candidate in npc.Senses.VisibleHostiles )
			{
				if ( !CanEngage( npc, candidate ) || !npc.HasLineOfSight( candidate ) ) continue;
				var candidateScore = EvaluateTargetScore( npc, candidate );
				if ( candidateScore >= score ) continue;
				score = candidateScore;
				best = candidate;
			}
		if ( best.IsValid() )
		{
			RememberContact( npc, best, best.WorldPosition, Titanfall2Npc.AimAtInternal( best ), seen: true );
			_seenVelocity = BaseNpc.GetVelocity( best );
			if ( _squadCall <= 0 )
			{
				_squadCall = 1;
				foreach ( var ally in npc.Scene.GetAllComponents<Titanfall2Npc>() )
				{
					if ( ally == npc || ally.Dead || !ally.CanFight || !ally.UsesEngineSchedules || ally.SeesContact
						|| ally.IsHostileToFaction( npc.Faction ) || Titanfall2Npc.IsNeutralFaction( ally.Faction )
						|| ally.WorldPosition.Distance( npc.WorldPosition ) > 850 || !CanEngage( ally, best ) ) continue;
					// A radio call is a position report, never a live hidden-target transform.
					ally.RememberCombatContactInternal( best, _lastContact.Value, _lastAimPoint, seen: false );
				}
			}
		}
		else
		{
			if ( wasVisible ) { StopMove( npc, Titanfall2Npc.CombatActions.Search ); _plan = 0; }
			if ( !HasCombatContact( npc ) ) ForgetContact( npc );
		}
	}

	internal float EvaluateTargetScore( Titanfall2Npc npc, GameObject candidate )
	{
		var distance = candidate.WorldPosition.Distance( npc.WorldPosition );
		var candidateNpc = candidate.GetComponent<Titanfall2Npc>();
		var baseNpc = candidateNpc ?? candidate.GetComponent<BaseNpc>();
		var targetIsTitan = candidateNpc is { IsTitan: true };
		var highThreat = candidateNpc is { Role: Titanfall2Npc.FrontierRole.NukeTitan or Titanfall2Npc.FrontierRole.ArcTitan or Titanfall2Npc.FrontierRole.MortarTitan }
			|| candidateNpc is { IsNukeHazard: true };
		var healthFrac = baseNpc is { MaxHealth: > 0 } ? Math.Clamp( baseNpc.Health / baseNpc.MaxHealth, 0f, 1f ) : 1f;
		var recentAttacker = _lastAttackerMemory > 0 && candidate == _lastAttacker;
		return Titanfall2TacticsRules.ScoreTarget(
			distance,
			candidate == npc.CurrentTarget,
			recentAttacker,
			npc.IsTitan,
			targetIsTitan,
			highThreat,
			healthFrac,
			selfPrefersShielded: npc.Role == Titanfall2Npc.FrontierRole.ArcTitan,
			targetHasShield: candidateNpc is { Shield: > 50f } );
	}

	internal void RememberContact( Titanfall2Npc npc, GameObject target, Vector3 position, Vector3 aimPoint, bool seen )
	{
		if ( target != npc.CurrentTarget )
		{
			_visualContactAge = 100;
			_seenVelocity = Vector3.Zero;
			_seenForward = Vector3.Zero;
			_reaction = MathF.Max( 0, npc.ReactionSeconds );
			_plan = 0;
			npc.ResetBurstCount();
			npc.SayVoiceInternal( Titanfall2Npc.VoiceCue.Spot );
		}
		npc.CurrentTarget = target;
		_lastContact = position;
		_lastAimPoint = aimPoint;
		_contactAge = 0;
		_seesContact = seen;
		if ( seen )
		{
			_visualContactAge = 0;
			_seenForward = target.WorldRotation.Forward;
		}
		if ( !seen && npc.ActiveSchedule is { } current && current.Priority < Sandbox.Npcs.Schedules.SchedulePriorities.Combat )
			npc.EndCurrentScheduleInternal();
	}

	void ForgetContact( Titanfall2Npc npc )
	{
		npc.CurrentTarget = null;
		_lastContact = null;
		_seesContact = false;
		_combatDestination = null;
		CombatAction = Titanfall2Npc.CombatActions.Idle;
	}

	internal void ReactToDamage( Titanfall2Npc npc, GameObject attacker )
	{
		_underFire = 2.4f;
		_plan = 0;
		if ( attacker.IsValid() && CanEngage( npc, attacker ) )
		{
			_lastAttacker = attacker;
			_lastAttackerMemory = 4.5f;
		}
		if ( !npc.UsesEngineSchedules || !CanEngage( npc, attacker ) ) return;
		// A hit gives one location report; future hidden movement does not update it.
		if ( !_seesContact ) RememberContact( npc, attacker, attacker.WorldPosition, Titanfall2Npc.AimAtInternal( attacker ), seen: false );
	}

	internal bool TryFindNearbyHazard( Titanfall2Npc npc, out Vector3 hazardOrigin, out float escapeDistance )
	{
		hazardOrigin = default;
		escapeDistance = 0;

		foreach ( var other in npc.Scene.GetAllComponents<Titanfall2Npc>() )
		{
			if ( other == npc || other.Dead || !other.IsNukeHazard ) continue;
			var radius = MathF.Max( 420f, other.NukeBlastRadius + npc.BodyRadius );
			if ( !Titanfall2TacticsRules.ShouldEvadeHazard( npc.WorldPosition, other.WorldPosition, radius ) ) continue;
			hazardOrigin = other.WorldPosition;
			escapeDistance = radius + 120f;
			return true;
		}

		foreach ( var shot in npc.Scene.GetAllComponents<Titanfall2Projectile>() )
		{
			if ( !shot.IsValid() ) continue;
			var behavior = shot.Settings.Behavior;
			if ( behavior is not (Titanfall2ProjectileBehavior.FragGrenade or Titanfall2ProjectileBehavior.ThermiteBurn or Titanfall2ProjectileBehavior.ElectricSmoke) )
				continue;
			var danger = MathF.Max( 220f, shot.Settings.SplashRadius + npc.BodyRadius + 48f );
			if ( !Titanfall2TacticsRules.ShouldEvadeHazard( npc.WorldPosition, shot.WorldPosition, danger ) ) continue;
			hazardOrigin = shot.WorldPosition;
			escapeDistance = danger + 100f;
			return true;
		}

		return false;
	}

	internal bool Tick( Titanfall2Npc npc )
	{
		RefreshContact( npc );
		if ( npc.IsTraversing ) return true;
		if ( npc.TickNpcOrdnance() ) return true;
		if ( !HasCombatContact( npc ) ) return false;
		var visible = _seesContact && npc.HasLineOfSight( npc.CurrentTarget );
		npc.Animation.SetLookTarget( _lastAimPoint );
		if ( visible )
		{
			var delta = (_lastContact.Value - npc.WorldPosition).WithZ( 0 );
			if ( delta.LengthSquared > 1 ) npc.WorldRotation = Rotation.Lerp( npc.WorldRotation, Rotation.LookAt( delta ), Math.Clamp( Time.Delta * (npc.IsTitan ? 5.5f : 8f), 0, 1 ) );
			if ( !npc.GetComponent<Titanfall2TitanKit>().IsValid() && _reaction <= 0 && npc.WorldPosition.Distance( _lastContact.Value ) <= npc.MeleeRange && npc.MeleeDamage > 0 )
			{
				StopMove( npc );
				npc.TryStartMelee( new Ray( npc.EyePositionInternal, (Titanfall2Npc.AimAtInternal( npc.CurrentTarget ) - npc.EyePositionInternal).Normal ), npc.GameObject );
			}
			else if ( !npc.MeleeOnly && _reaction <= 0 && npc.NextShotReady && npc.WorldPosition.Distance( _lastContact.Value ) <= npc.AttackRange )
			{
				var aimPoint = Titanfall2Npc.AimAtInternal( npc.CurrentTarget );
				if ( npc.Projectile.Enabled && npc.Projectile.Speed > 1f ) aimPoint += _seenVelocity * Math.Clamp( npc.EyePositionInternal.Distance( aimPoint ) / npc.Projectile.Speed, 0, 0.45f );
				var origin = CombatMuzzle( npc );
				if ( HasClearShot( npc, aimPoint ) ) npc.Fire( new Ray( origin, (aimPoint - origin).Normal ), npc.GameObject, piloted: false );
			}
		}
		if ( !npc.MeleeOnly && !npc.Reloading && npc.Rounds < npc.MagazineSize
			&& (npc.Rounds <= Math.Max( 1, npc.MagazineSize / 5 ) || (!visible && _contactAge > 0.8f && npc.Rounds < npc.MagazineSize * 0.6f)) ) npc.StartReload();
		CheckProgress( npc );
		if ( _plan <= 0 )
		{
			_plan = 0.55f;
			PlanMove( npc, visible );
		}
		return true;
	}

	static Vector3 CombatMuzzle( Titanfall2Npc npc ) => npc.GunObject.IsValid()
		? npc.GunObject.GetComponent<BaseWeaponModel>()?.GetMuzzleTransform().Position ?? npc.EyePositionInternal
		: npc.EyePositionInternal;

	static bool HasClearShot( Titanfall2Npc npc, Vector3 point )
	{
		var hit = Titanfall2CollisionQueries.Run( npc.Scene.Trace.Ray( CombatMuzzle( npc ), point ).UseHitboxes()
			.IgnoreGameObjectHierarchy( npc.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		if ( hit.StartedSolid ) return false;
		if ( !hit.Hit ) return true;
		if ( hit.GameObject?.GetComponentInParent<BaseNpc>() is { } other ) return npc.IsHostileToFaction( other.Faction );
		if ( hit.GameObject?.GetComponentInParent<PlayerController>() is not null ) return npc.AttackPlayers && !npc.Friendly;
		return false;
	}

	void PlanMove( Titanfall2Npc npc, bool visible )
	{
		if ( npc.AiOwnsMovement ) return;
		var contact = _lastContact.Value;
		var distance = contact.Distance( npc.WorldPosition );
		if ( npc.MeleePending ) { StopMove( npc ); return; }
		if ( !npc.Movement.IsValid() || !npc.Scene.NavMesh.IsEnabled || npc.MoveSpeed <= 0 )
		{
			CombatAction = visible ? Titanfall2Npc.CombatActions.Attack : Titanfall2Npc.CombatActions.Search;
			return;
		}

		// Urgent hazard evasion (armed Nuke Titans, grenades, thermite, electric smoke).
		if ( TryFindNearbyHazard( npc, out var hazard, out var escapeDist ) )
		{
			var rawEscape = Titanfall2TacticsRules.ComputeEvadePoint( npc.WorldPosition, hazard, escapeDist );
			var projected = npc.Movement.ProjectPoint( rawEscape );
			if ( projected.HasValue )
			{
				MoveTo( npc, projected.Value, Titanfall2Npc.CombatActions.Retreat, 24 );
				return;
			}
		}

		if ( npc.MeleeOnly )
		{
			if ( !visible || distance > npc.AttackRange * 0.85f ) MoveTo( npc, contact, visible ? Titanfall2Npc.CombatActions.Advance : Titanfall2Npc.CombatActions.Search, MathF.Max( 12, npc.AttackRange * 0.65f ) );
			else StopMove( npc );
			return;
		}

		var targetIsTitan = npc.CurrentTarget?.GetComponent<Titanfall2Npc>() is { IsTitan: true };
		var chassis = npc.GetComponent<Titanfall2TitanKit>()?.Kit ?? Titanfall2TitanKit.Chassis.None;
		var shieldDepleted = npc.MaxShield > 0 && npc.Shield < npc.MaxShield * 0.25f;
		var infantryCoverTrigger = npc.UseCover && !npc.IsTitan && npc.CombatStyle != Titanfall2Npc.CombatStyles.Assault
			&& (npc.Reloading || (_underFire > 0 && (npc.Health < npc.MaxHealth * 0.6f || shieldDepleted || targetIsTitan)));
		var titanCoverTrigger = npc.IsTitan && chassis is not (Titanfall2TitanKit.Chassis.Ronin or Titanfall2TitanKit.Chassis.Scorch)
			&& (npc.Reloading || (_underFire > 0 && shieldDepleted && npc.Health < npc.MaxHealth * 0.5f));

		if ( infantryCoverTrigger || titanCoverTrigger )
		{
			if ( CombatAction == Titanfall2Npc.CombatActions.TakeCover && _combatDestination.HasValue && _moveAge < 3.2f ) return;
			if ( TryPosition( npc, contact, cover: true, retreat: false, out var cover ) )
			{
				MoveTo( npc, cover, Titanfall2Npc.CombatActions.TakeCover );
				return;
			}
		}

		if ( !visible )
		{
			// Do not chase the hidden target's current transform or stop at weapon range.
			if ( distance > MathF.Max( 36, npc.BodyRadius * 2 ) ) MoveTo( npc, contact, Titanfall2Npc.CombatActions.Search );
			else
			{
				if ( _combatDestination.HasValue && CombatAction == Titanfall2Npc.CombatActions.Reposition && _moveAge < 2.5f ) return;
				if ( _relocate <= 0 && TryPosition( npc, contact, false, false, out var sweep ) )
				{
					_relocate = 1.8f;
					MoveTo( npc, sweep, Titanfall2Npc.CombatActions.Reposition );
				}
				else StopMove( npc, Titanfall2Npc.CombatActions.Search );
			}
			return;
		}

		var preferred = PreferredRange( npc );
		var tooCloseThreshold = Titanfall2TacticsRules.TooCloseRange( npc.IsTitan, chassis, npc.CombatStyle, preferred );
		var tooClose = tooCloseThreshold > 0 && distance < tooCloseThreshold;
		var blocked = !HasClearShot( npc, Titanfall2Npc.AimAtInternal( npc.CurrentTarget ) );
		if ( _combatDestination.HasValue && (CombatAction is Titanfall2Npc.CombatActions.Reposition or Titanfall2Npc.CombatActions.Retreat) && _moveAge < 2.2f
			&& npc.Movement.Status == MovementLayer.MovementStatus.Moving ) return;

		if ( (tooClose || blocked || (distance <= npc.AttackRange && _relocate <= 0))
			&& TryPosition( npc, contact, cover: false, retreat: tooClose, out var position ) )
		{
			_relocate = npc.CombatStyle == Titanfall2Npc.CombatStyles.Pilot || chassis == Titanfall2TitanKit.Chassis.Ronin ? 1.1f : 2.3f;
			MoveTo( npc, position, tooClose ? Titanfall2Npc.CombatActions.Retreat : Titanfall2Npc.CombatActions.Reposition );
			return;
		}

		if ( distance > MathF.Min( npc.AttackRange * 0.9f, preferred * 1.35f ) || blocked )
			MoveTo( npc, contact, Titanfall2Npc.CombatActions.Advance, blocked ? 24 : preferred );
		else StopMove( npc );
	}

	static float PreferredRange( Titanfall2Npc npc )
	{
		var kit = npc.GetComponent<Titanfall2TitanKit>();
		return kit is { AiPreferredRangeOverride: > 0 } ? kit.AiPreferredRangeOverride : Titanfall2TacticsRules.PreferredRange(
			npc.IsTitan, kit?.Kit ?? Titanfall2TitanKit.Chassis.None, npc.CombatStyle, npc.AttackRange );
	}

	bool TryPosition( Titanfall2Npc npc, Vector3 contact, bool cover, bool retreat, out Vector3 result )
	{
		result = default;
		if ( !npc.Movement.IsValid() || !npc.Scene.NavMesh.IsEnabled ) return false;
		var candidates = new List<(Vector3 Position, float Score)>();
		var forward = (contact - npc.WorldPosition).WithZ( 0 );
		forward = forward.LengthSquared > 0.01f ? forward.Normal : npc.WorldRotation.Forward.WithZ( 0 ).Normal;
		var side = Vector3.Cross( forward, Vector3.Up );
		var outerReach = MathF.Max( npc.BodyRadius * 3.5f, MathF.Min( npc.MoveSpeed * 1.7f, npc.IsTitan ? 480f : 340f ) );
		float[] rings = [outerReach * 0.58f, outerReach];

		var squadAllies = new List<Titanfall2Npc>();
		var allyDirections = new List<Vector3>();
		foreach ( var ally in npc.Scene.GetAllComponents<Titanfall2Npc>() )
		{
			if ( ally == npc || ally.Dead || npc.IsHostileToFaction( ally.Faction ) ) continue;
			squadAllies.Add( ally );
			var allyPos = ally.CombatDestination ?? ally.WorldPosition;
			var fromTarget = (allyPos - contact).WithZ( 0 );
			if ( fromTarget.LengthSquared > 16f && allyPos.Distance( npc.WorldPosition ) < 1200f )
				allyDirections.Add( fromTarget.Normal );
		}

		var targetForward = _seenForward.LengthSquared > 0.01f ? _seenForward : -forward;
		var chassis = npc.GetComponent<Titanfall2TitanKit>()?.Kit ?? Titanfall2TitanKit.Chassis.None;
		var prefersFlank = npc.CombatStyle == Titanfall2Npc.CombatStyles.Pilot || chassis == Titanfall2TitanKit.Chassis.Ronin;
		var preferred = PreferredRange( npc );

		foreach ( var reach in rings )
		{
			for ( var i = 0; i < 12; i++ )
			{
				var angle = i * MathF.PI / 6f;
				var raw = npc.WorldPosition + (forward * MathF.Cos( angle ) + side * MathF.Sin( angle )) * reach;
				var projected = npc.Movement.ProjectPoint( raw );
				if ( !projected.HasValue ) continue;
				var point = projected.Value;
				var travel = point.Distance( npc.WorldPosition );
				if ( travel < npc.BodyRadius * 1.8f || travel > outerReach * 1.65f || MathF.Abs( point.z - npc.WorldPosition.z ) > 84 ) continue;
				if ( _avoidFailed > 0 && _failedDestination.HasValue && point.Distance( _failedDestination.Value ) < 80 ) continue;
				var range = point.Distance( contact );
				if ( retreat && range < npc.WorldPosition.Distance( contact ) + 36 ) continue;
				if ( !cover && range > npc.AttackRange * 0.95f ) continue;
				var hull = new BBox( new Vector3( -npc.BodyRadius, -npc.BodyRadius, 4 ), new Vector3( npc.BodyRadius, npc.BodyRadius, npc.BodyHeight - 2 ) );
				if ( npc.Scene.Trace.Box( hull, point, point ).IgnoreGameObjectHierarchy( npc.GameObject ).WithoutTags( "trigger" ).Run().StartedSolid ) continue;
				var eye = point + Vector3.Up * npc.EyeHeight;
				var sight = Titanfall2CollisionQueries.Run( npc.Scene.Trace.Ray( eye, _lastAimPoint ).IgnoreGameObjectHierarchy( npc.GameObject )
					.IgnoreGameObjectHierarchy( npc.CurrentTarget ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
				if ( sight.StartedSolid ) continue;
				if ( cover != sight.Hit ) continue;
				// Another actor is not cover. Require geometry at both chest and eye height.
				if ( cover && (sight.GameObject?.GetComponentInParent<BaseNpc>() is not null
					|| sight.GameObject?.GetComponentInParent<PlayerController>() is not null
					|| !Titanfall2CollisionQueries.Run( npc.Scene.Trace.Ray( point + Vector3.Up * npc.BodyHeight * 0.45f, _lastAimPoint )
						.IgnoreGameObjectHierarchy( npc.GameObject ).IgnoreGameObjectHierarchy( npc.CurrentTarget ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot ).Hit) ) continue;

				var score = MathF.Abs( range - preferred ) + travel * (cover ? 0.7f : 0.16f);
				if ( !cover )
					score += Titanfall2TacticsRules.FlankScoreAdjustment( point - contact, targetForward, allyDirections, prefersFlank );
				foreach ( var ally in squadAllies )
				{
					var occupied = ally.CombatDestination ?? ally.WorldPosition;
					if ( occupied.Distance( point ) < npc.BodyRadius * 4f ) score += 600f;
				}
				candidates.Add( (point, score) );
			}
		}

		// Bound path searches and reject disconnected islands and partial paths.
		foreach ( var candidate in candidates.OrderBy( x => x.Score ).Take( 4 ) )
		{
			var path = npc.Scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = npc.WorldPosition, Target = candidate.Position, Agent = npc.Agent } );
			if ( path.Status != NavMeshPathStatus.Complete ) continue;
			result = candidate.Position;
			return true;
		}
		return false;
	}

	void MoveTo( Titanfall2Npc npc, Vector3 position, Titanfall2Npc.CombatActions action, float stopDistance = 24 )
	{
		if ( npc.AiOwnsMovement ) return;
		if ( _combatDestination.HasValue && _combatDestination.Value.Distance( position ) < 32
			&& npc.Movement.Status == MovementLayer.MovementStatus.Moving && CombatAction == action ) return;
		_combatDestination = position;
		CombatAction = action;
		_progressPosition = npc.WorldPosition;
		_progressAge = 0;
		_moveAge = 0;
		// An occluded enemy must not steer the body by its current transform either.
		var speed = npc.MoveSpeed * (npc.GetComponent<Titanfall2TitanKit>()?.AiMoveSpeedMultiplier ?? 1f);
		npc.Movement.MoveTo( position, stopDistance, speed, _seesContact ? npc.CurrentTarget : null );
	}

	void CheckProgress( Titanfall2Npc npc )
	{
		if ( npc.AiOwnsMovement ) { _progressPosition = npc.WorldPosition; _progressAge = 0; return; }
		if ( !_combatDestination.HasValue || !npc.Movement.IsValid() ) return;
		if ( npc.WorldPosition.Distance( _progressPosition ) > 12 ) { _progressPosition = npc.WorldPosition; _progressAge = 0; }
		if ( npc.Movement.Status == MovementLayer.MovementStatus.Failed || (_progressAge > 2 && npc.Movement.Status == MovementLayer.MovementStatus.Moving) )
		{
			_failedDestination = _combatDestination;
			_avoidFailed = 3;
			StopMove( npc, Titanfall2Npc.CombatActions.Search );
			_plan = 0.5f;
		}
	}

	internal void StopMove( Titanfall2Npc npc, Titanfall2Npc.CombatActions action = Titanfall2Npc.CombatActions.Attack )
	{
		if ( npc.AiOwnsMovement ) return;
		npc.Movement?.Stop();
		_combatDestination = null;
		CombatAction = action;
	}
}
