using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using Sandbox.Navigation;

public sealed partial class Titanfall2Npc
{
	public enum CombatStyles { Infantry, Assault, Pilot }
	public enum CombatActions { Idle, Advance, Attack, Reposition, Retreat, TakeCover, Search }
	[Property] public CombatStyles CombatStyle { get; set; }
	[Property] public bool UseCover { get; set; } = true;
	[Property] public float ContactMemorySeconds { get; set; } = 6;
	[Property] public float ReactionSeconds { get; set; } = 0.25f;
	[Property] public int BurstShots { get; set; } = 3;
	[Property] public float BurstPause { get; set; } = 0.45f;
	public CombatActions CombatAction { get; private set; }
	public Vector3? CombatDestination => _combatDestination;
	public Vector3? LastSeenPosition => _lastContact;

	Vector3? _lastContact;
	Vector3 _lastAimPoint;
	Vector3 _seenVelocity;
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

	internal bool HasCombatContact => CanFight && CanEngage( _target ) && _lastContact.HasValue
		&& _contactAge < MathF.Max( 0.1f, ContactMemorySeconds );

	bool CanEngage( GameObject target ) => CanTarget( target )
		&& (target.GetComponentInParent<PlayerController>() is null || (AttackPlayers && !Friendly))
		&& IsHostileToFaction( target.GetComponentInParent<BaseNpc>()?.Faction ?? Factions.Player );

	// Senses remains the acquisition authority. Once occluded, navigate only toward the snapshot.
	internal void RefreshCombatContact( bool force = false )
	{
		if ( !force && _contactScan > 0 ) return;
		_contactScan = 0.2f;
		var wasVisible = _seesContact;
		_seesContact = false;
		if ( !CanFight ) { ForgetCombatContact(); return; }
		GameObject best = null;
		var score = float.MaxValue;
		if ( Senses.IsValid() )
			foreach ( var candidate in Senses.VisibleHostiles )
			{
				if ( !CanEngage( candidate ) || !HasLineOfSight( candidate ) ) continue;
				var distance = candidate.WorldPosition.Distance( WorldPosition );
				// Keep the current enemy unless a substantially closer threat appears.
				var candidateScore = distance * (candidate == _target ? 0.7f : 1f);
				if ( candidateScore >= score ) continue;
				score = candidateScore;
				best = candidate;
			}
		if ( best.IsValid() )
		{
			RememberCombatContact( best, best.WorldPosition, AimAt( best ), seen: true );
			_seenVelocity = BaseNpc.GetVelocity( best );
			if ( _squadCall <= 0 )
			{
				_squadCall = 1;
				foreach ( var ally in Scene.GetAllComponents<Titanfall2Npc>() )
				{
					if ( ally == this || ally.Dead || !ally.CanFight || !ally.UsesEngineSchedules || ally._seesContact
						|| ally.IsHostileToFaction( Faction ) || IsNeutralFaction( ally.Faction )
						|| ally.WorldPosition.Distance( WorldPosition ) > 700 || !ally.CanEngage( best ) ) continue;
					// A radio call is a position report, never a live hidden-target transform.
					ally.RememberCombatContact( best, _lastContact.Value, _lastAimPoint, seen: false );
				}
			}
		}
		else
		{
			if ( wasVisible ) { StopCombatMove( CombatActions.Search ); _plan = 0; }
			if ( !HasCombatContact ) ForgetCombatContact();
		}
	}

	void RememberCombatContact( GameObject target, Vector3 position, Vector3 aimPoint, bool seen )
	{
		if ( target != _target )
		{
			_reaction = MathF.Max( 0, ReactionSeconds );
			_plan = 0;
			_burst = 0;
			SayVoice( VoiceCue.Spot );
		}
		_target = target;
		_lastContact = position;
		_lastAimPoint = aimPoint;
		_contactAge = 0;
		_seesContact = seen;
		if ( !seen && ActiveSchedule is { } current && current.Priority < Sandbox.Npcs.Schedules.SchedulePriorities.Combat ) EndCurrentSchedule();
	}

	void ForgetCombatContact()
	{
		_target = null;
		_lastContact = null;
		_seesContact = false;
		_combatDestination = null;
		CombatAction = CombatActions.Idle;
	}

	void ReactToCombatDamage( GameObject attacker )
	{
		_underFire = 2;
		_plan = 0;
		if ( !UsesEngineSchedules || !CanEngage( attacker ) ) return;
		// A hit gives one location report; future hidden movement does not update it.
		if ( !_seesContact ) RememberCombatContact( attacker, attacker.WorldPosition, AimAt( attacker ), seen: false );
	}

	internal bool TickCombatTactics()
	{
		RefreshCombatContact();
		if ( TickNpcOrdnance() ) return true;
		if ( !HasCombatContact ) return false;
		var visible = _seesContact && HasLineOfSight( _target );
		Animation.SetLookTarget( _lastAimPoint );
		if ( visible )
		{
			var delta = (_lastContact.Value - WorldPosition).WithZ( 0 );
			if ( delta.LengthSquared > 1 ) WorldRotation = Rotation.Lerp( WorldRotation, Rotation.LookAt( delta ), Math.Clamp( Time.Delta * 7, 0, 1 ) );
			if ( _reaction <= 0 && WorldPosition.Distance( _lastContact.Value ) <= MeleeRange && MeleeDamage > 0 )
			{
				StopCombatMove();
				TryStartMelee( new Ray( Eye, (AimAt( _target ) - Eye).Normal ), GameObject );
			}
			else if ( !MeleeOnly && _reaction <= 0 && _nextShot <= 0 && WorldPosition.Distance( _lastContact.Value ) <= AttackRange )
			{
				var aimPoint = AimAt( _target );
				if ( Projectile.Enabled ) aimPoint += _seenVelocity * Math.Clamp( Eye.Distance( aimPoint ) / Projectile.Speed, 0, 0.45f );
				var origin = CombatMuzzle;
				if ( HasClearShot( aimPoint ) ) Fire( new Ray( origin, (aimPoint - origin).Normal ), GameObject, piloted: false );
			}
		}
		if ( !MeleeOnly && !Reloading && Rounds < MagazineSize
			&& (Rounds <= Math.Max( 1, MagazineSize / 5 ) || (!visible && _contactAge > 0.8f && Rounds < MagazineSize * 0.6f)) ) StartReload();
		CheckCombatProgress();
		if ( _plan <= 0 )
		{
			_plan = 0.6f;
			PlanCombatMove( visible );
		}
		return true;
	}

	Vector3 CombatMuzzle => _gun.IsValid() ? _gun.GetComponent<BaseWeaponModel>()?.GetMuzzleTransform().Position ?? Eye : Eye;

	bool HasClearShot( Vector3 point )
	{
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( CombatMuzzle, point ).UseHitboxes()
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		if ( hit.StartedSolid ) return false;
		if ( !hit.Hit ) return true;
		if ( hit.GameObject?.GetComponentInParent<BaseNpc>() is { } npc ) return IsHostileToFaction( npc.Faction );
		if ( hit.GameObject?.GetComponentInParent<PlayerController>() is not null ) return AttackPlayers && !Friendly;
		return false;
	}

	void PlanCombatMove( bool visible )
	{
		var contact = _lastContact.Value;
		var distance = contact.Distance( WorldPosition );
		if ( _meleePending ) { StopCombatMove(); return; }
		if ( !Movement.IsValid() || !Scene.NavMesh.IsEnabled || MoveSpeed <= 0 )
		{
			CombatAction = visible ? CombatActions.Attack : CombatActions.Search;
			return;
		}
		if ( MeleeOnly )
		{
			if ( !visible || distance > AttackRange * 0.85f ) CombatMoveTo( contact, visible ? CombatActions.Advance : CombatActions.Search, MathF.Max( 12, AttackRange * 0.65f ) );
			else StopCombatMove();
			return;
		}
		var seekCover = UseCover && !IsTitan && CombatStyle != CombatStyles.Assault
			&& (Reloading || (_underFire > 0 && Health < MaxHealth * 0.5f));
		if ( seekCover )
		{
			if ( CombatAction == CombatActions.TakeCover && _combatDestination.HasValue && _moveAge < 3.5f ) return;
			if ( TryCombatPosition( contact, cover: true, retreat: false, out var cover ) )
			{
				CombatMoveTo( cover, CombatActions.TakeCover );
				return;
			}
		}
		if ( !visible )
		{
			// Do not chase the hidden target's current transform or stop at weapon range.
			if ( distance > MathF.Max( 36, BodyRadius * 2 ) ) CombatMoveTo( contact, CombatActions.Search );
			else
			{
				if ( _combatDestination.HasValue && CombatAction == CombatActions.Reposition && _moveAge < 2.5f ) return;
				if ( _relocate <= 0 && TryCombatPosition( contact, false, false, out var sweep ) )
				{
					_relocate = 2;
					CombatMoveTo( sweep, CombatActions.Reposition );
				}
				else StopCombatMove( CombatActions.Search );
			}
			return;
		}
		var tooClose = CombatStyle != CombatStyles.Assault && distance < PreferredCombatRange * 0.45f;
		var blocked = !HasClearShot( AimAt( _target ) );
		if ( _combatDestination.HasValue && (CombatAction is CombatActions.Reposition or CombatActions.Retreat) && _moveAge < 2.5f
			&& Movement.Status == MovementLayer.MovementStatus.Moving ) return;
		if ( (tooClose || blocked || (distance <= AttackRange && _relocate <= 0))
			&& TryCombatPosition( contact, false, tooClose, out var position ) )
		{
			_relocate = CombatStyle == CombatStyles.Pilot ? 1.2f : 2.5f;
			CombatMoveTo( position, tooClose ? CombatActions.Retreat : CombatActions.Reposition );
			return;
		}
		if ( distance > AttackRange * 0.9f || blocked ) CombatMoveTo( contact, CombatActions.Advance, blocked ? 24 : PreferredCombatRange );
		else StopCombatMove();
	}

	float PreferredCombatRange => MathF.Min( AttackRange * (CombatStyle == CombatStyles.Assault ? 0.35f : 0.6f), IsTitan ? 1600 : 650 );

	bool TryCombatPosition( Vector3 contact, bool cover, bool retreat, out Vector3 result )
	{
		result = default;
		if ( !Movement.IsValid() || !Scene.NavMesh.IsEnabled ) return false;
		var candidates = new List<(Vector3 Position, float Score)>();
		var forward = (contact - WorldPosition).WithZ( 0 ).Normal;
		var side = Vector3.Cross( forward, Vector3.Up );
		var reach = MathF.Max( BodyRadius * 3, MathF.Min( MoveSpeed * 1.5f, 320 ) );
		for ( var i = 0; i < 12; i++ )
		{
			var angle = i * MathF.PI / 6;
			var raw = WorldPosition + (forward * MathF.Cos( angle ) + side * MathF.Sin( angle )) * reach;
			var projected = Movement.ProjectPoint( raw );
			if ( !projected.HasValue ) continue;
			var point = projected.Value;
			var travel = point.Distance( WorldPosition );
			if ( travel < BodyRadius * 2 || travel > reach * 1.6f || MathF.Abs( point.z - WorldPosition.z ) > 72 ) continue;
			if ( _avoidFailed > 0 && _failedDestination.HasValue && point.Distance( _failedDestination.Value ) < 80 ) continue;
			var range = point.Distance( contact );
			if ( retreat && range < WorldPosition.Distance( contact ) + 40 ) continue;
			if ( !cover && range > AttackRange * 0.95f ) continue;
			var hull = new BBox( new Vector3( -BodyRadius, -BodyRadius, 4 ), new Vector3( BodyRadius, BodyRadius, BodyHeight - 2 ) );
			if ( Scene.Trace.Box( hull, point, point ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run().StartedSolid ) continue;
			var eye = point + Vector3.Up * EyeHeight;
			var sight = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( eye, _lastAimPoint ).IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( _target ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
			if ( sight.StartedSolid ) continue;
			if ( cover != sight.Hit ) continue;
			// Another actor is not cover. Require geometry at both chest and eye height.
			if ( cover && (sight.GameObject?.GetComponentInParent<BaseNpc>() is not null
				|| sight.GameObject?.GetComponentInParent<PlayerController>() is not null
				|| !Titanfall2CollisionQueries.Run( Scene.Trace.Ray( point + Vector3.Up * BodyHeight * 0.45f, _lastAimPoint )
					.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( _target ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot ).Hit) ) continue;
			var score = MathF.Abs( range - PreferredCombatRange ) + travel * (cover ? 0.8f : 0.15f);
			foreach ( var ally in Scene.GetAllComponents<Titanfall2Npc>() )
			{
				if ( ally == this || ally.Dead || IsHostileToFaction( ally.Faction ) ) continue;
				var occupied = ally._combatDestination ?? ally.WorldPosition;
				if ( occupied.Distance( point ) < BodyRadius * 4 ) score += 600;
			}
			candidates.Add( (point, score) );
		}
		// Bound path searches and reject disconnected islands and partial paths.
		foreach ( var candidate in candidates.OrderBy( x => x.Score ).Take( 3 ) )
		{
			var path = Scene.NavMesh.CalculatePath( new CalculatePathRequest { Start = WorldPosition, Target = candidate.Position, Agent = Agent } );
			if ( path.Status != NavMeshPathStatus.Complete ) continue;
			result = candidate.Position;
			return true;
		}
		return false;
	}

	void CombatMoveTo( Vector3 position, CombatActions action, float stopDistance = 24 )
	{
		if ( _combatDestination.HasValue && _combatDestination.Value.Distance( position ) < 32
			&& Movement.Status == MovementLayer.MovementStatus.Moving && CombatAction == action ) return;
		_combatDestination = position;
		CombatAction = action;
		_progressPosition = WorldPosition;
		_progressAge = 0;
		_moveAge = 0;
		// An occluded enemy must not steer the body by its current transform either.
		Movement.MoveTo( position, stopDistance, MoveSpeed, _seesContact ? _target : null );
	}

	void CheckCombatProgress()
	{
		if ( !_combatDestination.HasValue || !Movement.IsValid() ) return;
		if ( WorldPosition.Distance( _progressPosition ) > 12 ) { _progressPosition = WorldPosition; _progressAge = 0; }
		if ( Movement.Status == MovementLayer.MovementStatus.Failed || (_progressAge > 2 && Movement.Status == MovementLayer.MovementStatus.Moving) )
		{
			_failedDestination = _combatDestination;
			_avoidFailed = 3;
			StopCombatMove( CombatActions.Search );
			_plan = 0.5f;
		}
	}

	void StopCombatMove( CombatActions action = CombatActions.Attack )
	{
		Movement?.Stop();
		_combatDestination = null;
		CombatAction = action;
	}
}
