/// <summary>
/// One host director owns autonomous skill decisions. Skill executors remain shared with the cockpit;
/// an accepted action keeps its target and phase until commit, cancellation or recovery.
/// </summary>
public sealed partial class Titanfall2TitanKit
{
	[Sync( SyncFlags.FromHost )] public Titanfall2TitanAiAction AiAction { get; private set; }
	[Sync( SyncFlags.FromHost )] public Titanfall2TitanAiPhase AiPhase { get; private set; }
	[Sync( SyncFlags.FromHost )] public int AiActionSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] public float AiActionProgress { get; private set; }
	[Sync( SyncFlags.FromHost )] public string AiAnimationRole { get; private set; }
	[Sync( SyncFlags.FromHost )] public string AiDecisionReason { get; private set; }
	[Sync( SyncFlags.FromHost )] public string AiTacticalState { get; private set; } = "Combat";
	readonly Titanfall2TitanAiActionClock _aiAction = new();
	GameObject _aiActionTarget;
	GameObject _aiMeleeTarget;
	Ray _aiCommittedAim;
	bool _aiCommitting;
	bool _aiWasControlling;
	Titanfall2NpcAiMode _aiActionMode;
	bool _aiActionRetailSettings;
	float _aiDefenseElapsed, _aiDefenseRest, _aiCoreReadyElapsed;
	bool _aiHadGuard;
	float _aiVisualDelay, _aiVisualDuration;
	float _aiPrepareClipDuration, _aiRecoverClipDuration;
	string _aiPrepareRole, _aiRecoverRole;
	int _aiMeleeCombo;
	TimeUntil _aiMeleeComboRest;
	float _aiLastRange, _aiHealthSample, _aiEnemyHealthSample, _aiModeAge;
	GameObject _aiSampleTarget;
	float _aiModeSampleTime;
	bool _aiEvasive;
	TimeUntil _aiUtilityOpportunity;
	readonly Queue<double> _aiDashTimes = new();
	bool AiAutonomous => UseAi && Actor.IsValid()
		&& !(Actor.Seat is { IsValid: true, SeatPosition: { IsValid: true } } seat && (seat.IsOccupied || seat.IsTransitioning));
	internal bool AiBlocksPrimary => AiAutonomous && Titanfall2TitanAiRules.BlocksPrimary( AiAction, AiPhase );
	internal float AiPreferredRangeOverride => !AiAutonomous ? 0 : Titanfall2TitanAiRules.PreferredRange( Kit,
		Actor.GetComponent<Titanfall2TitanCore>() is { SwordCoreActive: true }, _aiEvasive,
		AiProfile?.ChaseStopHeavyDistance ?? Titanfall2TacticsRules.PreferredRange( true, Kit, Actor.CombatStyle, Actor.AttackRange ) );
	internal float AiMoveSpeedMultiplier => CurrentMoveSpeedMultiplier
		* (AiAutonomous && Actor.GetComponent<Titanfall2TitanCore>() is { SwordCoreActive: true } ? 1.25f : 1f);
	Titanfall2NpcAiProfile AiProfile => Actor.IsValid() ? Actor.AiProfile : null;
	internal bool CanActivateCore => Ready && !IsPhasing && !Actor.IsTraversing && !_aiDashActive && !_aiLanding
		&& _rockets == 0 && (!DefenseVolumeActive || Kit == Chassis.Legion) && !RoninBlocking
		&& (Kit != Chassis.Northstar || !AiAutonomous || HasAiFlightClearance())
		&& (!_aiAction.Busy || _aiCommitting && _aiAction.Action == Titanfall2TitanAiAction.Core);

	bool CanActivateOffhand( Titanfall2TitanAiAction action )
	{
		if ( !Ready || Actor.IsTraversing || Actor.IsInTermination ) return false;
		if ( _aiAction.Busy && (!_aiCommitting || _aiAction.Action != action) ) return false;
		var core = Actor.GetComponent<Titanfall2TitanCore>();
		if ( core is { IsCharging: true } ) return false;
		if ( core is { IsActive: true, BlocksPrimary: true, SwordCoreActive: false } ) return false;
		if ( core is { SwordCoreActive: true } && action is not (Titanfall2TitanAiAction.Melee or Titanfall2TitanAiAction.Defense or Titanfall2TitanAiAction.Utility or Titanfall2TitanAiAction.Smoke) ) return false;
		if ( IsPhasing && action != Titanfall2TitanAiAction.Defense ) return false;
		if ( action is Titanfall2TitanAiAction.Offense or Titanfall2TitanAiAction.Utility or Titanfall2TitanAiAction.Melee )
		{
			if ( _rockets > 0 || RoninBlocking || DefenseVolumeActive && Kit is Chassis.Ion or Chassis.Scorch ) return false;
			if ( Kit == Chassis.Legion && action == Titanfall2TitanAiAction.Offense && (Actor.Reloading || Actor.Rounds <= 0) ) return false;
		}
		return true;
	}

	void TickAiSession()
	{
		if ( !AiAutonomous )
		{
			if ( _aiWasControlling ) CancelAiActions( "control-transferred" );
			_aiWasControlling = false;
			return;
		}
		_aiWasControlling = true;
		_aiDefenseRest = MathF.Max( 0, _aiDefenseRest - Time.Delta );
		var guarding = DefenseVolumeActive || RoninBlocking;
		if ( guarding ) _aiDefenseElapsed += Time.Delta;
		else
		{
			if ( _aiHadGuard ) _aiDefenseRest = MathF.Max( _aiDefenseRest, RandomRest( AiProfile?.Defense, 0.6f ) );
			_aiDefenseElapsed = 0;
		}
		_aiHadGuard = guarding;
		if ( !_aiAction.Busy ) { PublishAiAction(); return; }
		if ( Actor.IsTraversing || Actor.IsInTermination ) { CancelAiActions( "full-body-action" ); return; }
		if ( _aiAction.Phase == Titanfall2TitanAiPhase.Prepare
			&& (_aiActionMode != Actor.AiMode || _aiActionRetailSettings != Actor.UseRetailAiSettings) )
		{ FinishAiAction( "ai-policy-changed-before-commit" ); return; }
		var commit = _aiAction.Advance( Time.Delta );
		if ( _aiAction.Phase == Titanfall2TitanAiPhase.Recover && _aiAction.RecoveryFinished )
		{
			FinishAiAction( "completed" ); return;
		}
		if ( commit )
		{
			var targetRequired = _aiAction.Action is not (Titanfall2TitanAiAction.Smoke or Titanfall2TitanAiAction.VortexRelease);
			if ( targetRequired && (!_aiActionTarget.IsValid() || !Actor.CanTarget( _aiActionTarget ) || !Actor.HasLineOfSight( _aiActionTarget )) )
			{
				FinishAiAction( "target-lost-before-commit" ); return;
			}
			if ( targetRequired ) _aiCommittedAim = new Ray( Eye, (_aiActionTarget.GetBounds().Center - Eye).Normal );
			if ( targetRequired && !RecheckAiCommit() ) { FinishAiAction( "eligibility-changed-before-commit" ); return; }
			_aiCommitting = true;
			var accepted = false;
			try
			{
				accepted = _aiAction.Action switch
				{
					Titanfall2TitanAiAction.Offense => TryOffense( _aiCommittedAim, Actor.GameObject ),
					Titanfall2TitanAiAction.Defense => TryDefense( _aiCommittedAim, Actor.GameObject ),
					Titanfall2TitanAiAction.Utility => TryUtility( _aiCommittedAim, Actor.GameObject ),
					Titanfall2TitanAiAction.Smoke => TryElectricSmoke( Actor.GameObject ),
					Titanfall2TitanAiAction.Melee => Actor.GetComponent<Titanfall2TitanCore>() is { SwordCoreActive: true } sword
						? sword.TryPrimaryAttack( _aiCommittedAim, Actor.GameObject ) : TryTitanMelee( _aiCommittedAim, Actor.GameObject ),
					Titanfall2TitanAiAction.Core => Actor.GetComponent<Titanfall2TitanCore>()?.TryActivate( _aiCommittedAim, Actor.GameObject ) == true,
					Titanfall2TitanAiAction.VortexRelease => TryReleaseIonVortex( _aiCommittedAim ),
					_ => false
				};
			}
			finally { _aiCommitting = false; }
			if ( !accepted ) { FinishAiAction( "executor-rejected" ); return; }
			if ( _aiAction.Action == Titanfall2TitanAiAction.VortexRelease ) _aiDefenseRest = RandomRest( AiProfile?.Defense, 0.6f );
			if ( _aiAction.Action == Titanfall2TitanAiAction.Offense )
				OffenseRemaining = RandomRest( AiProfile?.Offense, OffenseRemaining );
			if ( _aiAction.Action == Titanfall2TitanAiAction.Melee )
			{
				if ( _aiMeleeTarget != _aiActionTarget ) { _aiMeleeCombo = 0; _aiMeleeTarget = _aiActionTarget; }
				MeleeRemaining = MathF.Max( 0.25f, AiProfile?.MeleeInterval ?? CurrentMeleeCooldown );
				if ( ++_aiMeleeCombo >= (AiProfile?.MaxMeleeCombo ?? 2) ) { _aiMeleeCombo = 0; _aiMeleeComboRest = 0.8f; }
			}
			if ( _aiAction.Action == Titanfall2TitanAiAction.Defense ) _aiDefenseElapsed = 0;
			_aiAction.Recover();
		}
		PublishAiAction();
	}

	bool RecheckAiCommit()
	{
		var profile = AiProfile ?? new Titanfall2NpcAiProfile { Mode = Actor.AiMode };
		if ( _aiAction.Action == Titanfall2TitanAiAction.Core ) return profile.AllowCore && !Actor.MeleePending;
		if ( _aiAction.Action == Titanfall2TitanAiAction.Offense && !profile.AllowOffense
			|| _aiAction.Action == Titanfall2TitanAiAction.Utility && !profile.AllowUtility ) return false;
		if ( _aiAction.Action is not (Titanfall2TitanAiAction.Offense or Titanfall2TitanAiAction.Defense or Titanfall2TitanAiAction.Utility) ) return true;
		var weapon = _aiAction.Action switch
		{
			Titanfall2TitanAiAction.Offense => profile?.Offense,
			Titanfall2TitanAiAction.Defense => profile?.Defense,
			_ => profile?.Utility
		};
		var distance = _aiActionTarget.WorldPosition.Distance( Actor.WorldPosition );
		if ( Kit == Chassis.Legion && _aiAction.Action == Titanfall2TitanAiAction.Offense
			&& Vector3.Dot( Actor.WorldRotation.Forward, _aiCommittedAim.Forward ) < 0.85f ) return false;
		var heavy = _aiActionTarget.GetComponent<Titanfall2Npc>() is { IsTitan: true };
		if ( _aiAction.Action == Titanfall2TitanAiAction.Offense && weapon is { Available: true }
			&& (distance < (heavy ? weapon.MinHeavyEngageRange : weapon.MinEngageRange)
				|| distance > (heavy ? weapon.MaxHeavyEngageRange : weapon.MaxEngageRange)) ) return false;
		var context = new Titanfall2NpcAbilityContext( Moving: _aiDashActive || Actor.AiIsMoving, Evasive: _aiEvasive,
			GroundClear: GroundAbilityPathClear( _aiActionTarget.WorldPosition ), WideSightClear: WideAbilitySightClear( _aiActionTarget ),
			HasTrackerLock: HasTrackerLock && TrackedTarget == _aiActionTarget, SharedEnergy: SharedEnergy,
			PhaseActive: IsPhasing, Range: distance, PreviousRange: PreviousAbilityRange() );
		return AbilityEligible( weapon, context, _aiAction.Action );
	}

	void TickAiAbilities()
	{
		if ( !AiAutonomous || !Ready || Actor.IsTraversing ) return;
		var profile = AiProfile;
		var target = Actor.CombatTarget;
		var visible = Actor.CanTarget( target ) && Actor.HasLineOfSight( target );
		var aim = visible ? new Ray( Eye, (target.GetBounds().Center - Eye).Normal ) : new Ray( Eye, Actor.WorldRotation.Forward );
		var distance = visible ? target.WorldPosition.Distance( Actor.WorldPosition ) : float.MaxValue;
		var health = Actor.MaxHealth > 0 ? Actor.Health / Actor.MaxHealth : 1;
		var shield = Actor.MaxShield > 0 ? Actor.Shield / Actor.MaxShield : 0;
		var core = Actor.GetComponent<Titanfall2TitanCore>();
		var incoming = FindIncomingThreat( out var incomingOrigin );
		var underFire = Actor.IsUnderFire || incoming;
		UpdateAiTacticalMode( visible ? target : null, profile );
		if ( _aiAction.Phase == Titanfall2TitanAiPhase.Prepare && _aiAction.Action != Titanfall2TitanAiAction.Defense
			&& (incoming || Actor.TryFindNearbyHazard( out _, out _ )) )
			FinishAiAction( "hazard-interrupted-prepare" );

		// Sustained guards may finish early; this releases exactly the state that began the guard.
		if ( Kit == Chassis.Ion && DefenseVolumeActive && !_aiAction.Busy
			&& Titanfall2TitanAiRules.ShouldReleaseVortex( _aiDefenseElapsed, profile?.Defense.NormalDuration ?? 2.5f,
				SharedEnergy, 220, CapturedAmmo, underFire, visible ) )
		{
			StartAiAction( Titanfall2TitanAiAction.VortexRelease, target, aim, 0, 0.35f, "vortex-refire-window" ); return;
		}
		if ( RoninBlocking && !underFire && _aiDefenseElapsed > 0.6f ) { GuardRemaining = 0; RoninBlocking = false; _aiDefenseRest = 0.5f; }
		if ( _aiAction.Busy || _aiLanding || _rockets > 0 ) return;
		if ( core is { IsCharging: true } ) return;
		if ( core is { SwordCoreActive: true } && visible )
		{
			if ( underFire && shield < 0.4f && distance > 325 && !RoninBlocking && _aiDefenseRest <= 0 && DefenseRemaining <= 0 )
			{ StartAiAction( Titanfall2TitanAiAction.Defense, target, aim, 0.1f, 0.2f, "sword-core-approach-block" ); return; }
			if ( underFire && Actor.AiIsMoving && UtilityRemaining <= 0 && profile?.AllowUtility == true && !IsPhasing && !RoninBlocking )
			{ StartAiAction( Titanfall2TitanAiAction.Utility, target, aim, 0.1f, 0.2f, "sword-core-phase-approach" ); return; }
			if ( RoninBlocking || IsPhasing ) return;
			if ( distance < 325 && _aiMeleeComboRest <= 0 ) StartAiAction( Titanfall2TitanAiAction.Melee, target, aim, 0, MathF.Max( 0.35f, profile?.MeleeInterval ?? 0.9f ), "sword-core-melee" );
			else TryTacticalDash( target, distance, health, shield, incoming, incomingOrigin, profile );
			return;
		}
		if ( core is { IsActive: true, BlocksPrimary: true } ) return;
		if ( incoming || Actor.TryFindNearbyHazard( out _, out _ ) )
		{
			if ( TryTacticalDash( visible ? target : null, distance, health, shield, incoming, incomingOrigin, profile ) ) return;
		}
		if ( !visible ) { _aiCoreReadyElapsed = 0; return; }
		profile ??= new Titanfall2NpcAiProfile { Mode = Actor.AiMode };
		if ( !profile.Available ) AiDecisionReason = Actor.UseRetailAiSettings ? "fallback:missing-retail-ai-profile" : "compatibility:retail-ai-disabled";
		var heavy = target.GetComponent<Titanfall2Npc>() is { IsTitan: true };
		var groundClear = GroundAbilityPathClear( target.WorldPosition );
		var context = new Titanfall2NpcAbilityContext(
			Moving: _aiDashActive || Actor.AiIsMoving, Evasive: _aiEvasive, GroundClear: groundClear,
			WideSightClear: WideAbilitySightClear( target ), HasTrackerLock: HasTrackerLock && TrackedTarget == target,
			SharedEnergy: SharedEnergy, PhaseActive: IsPhasing, Range: distance, PreviousRange: PreviousAbilityRange() );
		_aiLastRange = distance;
		if ( _aiDefenseRest <= 0 && DefenseRemaining <= 0 && !DefenseVolumeActive && !RoninBlocking && !IsPhasing
			&& Titanfall2TacticsRules.ShouldUseTitanDefense( Kit, health, shield, underFire, distance, SharedEnergy, false )
			&& AbilityEligible( profile.Defense, context, Titanfall2TitanAiAction.Defense ) )
		{
			// Only observed damage/projectiles authorize reactive shields; deployables have positional policies.
			if ( Kit is Chassis.Monarch or Chassis.Northstar || underFire )
			{
				StartAiAction( Titanfall2TitanAiAction.Defense, target, aim, incoming ? 0.12f : 0.2f, 0.25f, "observed-threat-defense" ); return;
			}
		}
		if ( DefenseVolumeActive && Kit is Chassis.Ion or Chassis.Scorch || RoninBlocking ) return;
		if ( distance < (Kit == Chassis.Ronin ? 260 : 190) && MeleeRemaining <= 0 && _aiMeleeComboRest <= 0 && !IsPhasing )
		{
			StartAiAction( Titanfall2TitanAiAction.Melee, target, aim, 0.25f, MathF.Max( 0.35f, CurrentMeleeCooldown - 0.25f ), "close-contact-melee" ); return;
		}
		if ( profile.AllowUtility && UtilityRemaining <= 0 && _aiUtilityOpportunity <= 0 && AbilityEligible( profile.Utility, context, Titanfall2TitanAiAction.Utility )
			&& UtilityHasBenefit( distance, shield, underFire ) )
		{
			StartAiAction( Titanfall2TitanAiAction.Utility, target, aim, MathF.Max( 0.15f, profile.Utility.PreFireDelay ), 0.35f, "chassis-utility-opportunity" );
			_aiUtilityOpportunity = 1f; return;
		}
		if ( SmokeRemaining <= 0 && distance < 260 && (shield < 0.3f || _aiEvasive) )
		{
			StartAiAction( Titanfall2TitanAiAction.Smoke, target, aim, 0.1f, 0.2f, "close-pressure-smoke" ); return;
		}
		if ( core is { CanActivate: true, UseAi: true } && profile.AllowCore && !Actor.MeleePending && CanActivateCore )
		{
			_aiCoreReadyElapsed += 0.35f;
			var npc = target.GetComponent<Titanfall2Npc>();
			if ( _aiCoreReadyElapsed >= 0.7f && Titanfall2TitanAiRules.CoreHasBenefit( Kit, core.UpgradeLevel, shield, distance,
				npc.IsValid() ? npc.Health + npc.Shield : 100, heavy, groundClear ) )
			{
				StartAiAction( Titanfall2TitanAiAction.Core, target, aim, 0.15f, 0.25f, "core-benefit-window" ); return;
			}
		}
		else _aiCoreReadyElapsed = 0;
		var minimum = heavy ? profile.Offense.MinHeavyEngageRange : profile.Offense.MinEngageRange;
		var maximum = heavy ? profile.Offense.MaxHeavyEngageRange : profile.Offense.MaxEngageRange;
		if ( profile.AllowOffense && OffenseRemaining <= 0 && _rockets == 0 && !Actor.Reloading && !IsPhasing
			&& distance >= minimum && distance <= maximum && AbilityEligible( profile.Offense, context, Titanfall2TitanAiAction.Offense )
			&& Titanfall2TacticsRules.ShouldUseTitanOffense( Kit, distance, Actor.AttackRange, groundClear, HasTrackerLock, SharedEnergy, underFire ) )
		{
			StartAiAction( Titanfall2TitanAiAction.Offense, target, aim,
				MathF.Max( profile.Offense.PreFireDelay, profile.Offense.MaxCharge ), 0.35f, "weapon-eligibility-passed" ); return;
		}
		TryTacticalDash( target, distance, health, shield, incoming, incomingOrigin, profile );
	}

	bool AbilityEligible( Titanfall2NpcWeaponAiProfile profile, Titanfall2NpcAbilityContext context, Titanfall2TitanAiAction action )
	{
		if ( profile is { Available: true } ) return profile.CanUse( context, out _ );
		// Explicit compatibility fallback for mounted variants whose optional DLC scripts are absent.
		// Unknown declared use-check types remain denied by CanUse above.
		AiDecisionReason = "fallback:missing-" + action.ToString().ToLowerInvariant() + "-profile";
		if ( !context.WideSightClear || context.PhaseActive ) return false;
		if ( action == Titanfall2TitanAiAction.Offense && Kit is Chassis.Ronin or Chassis.Scorch ) return context.GroundClear;
		if ( action == Titanfall2TitanAiAction.Offense && Kit == Chassis.Tone ) return context.HasTrackerLock;
		if ( action == Titanfall2TitanAiAction.Utility && Kit == Chassis.Ronin ) return context.Moving;
		return Kit != Chassis.Ion || context.SharedEnergy >= (action == Titanfall2TitanAiAction.Utility ? 650 : action == Titanfall2TitanAiAction.Offense ? 600 : 220);
	}

	float LegionRangeThreshold => MathF.Max( 1, AiProfile?.Utility.RangeChangeThreshold ?? 1500 );
	float PreviousAbilityRange() => Kit == Chassis.Legion ? LegionRangeThreshold + (LegionLongRangeMode ? 1 : -1) : _aiLastRange;

	bool UtilityHasBenefit( float distance, float shield, bool underFire ) => Kit switch
	{
		Chassis.Monarch => Titanfall2TitanAiRules.RearmHasBenefit( OffenseRemaining, DefenseRemaining, Actor.Rounds, Actor.MagazineSize, DashCharges, MaxDashCharges ),
		Chassis.Ion => SharedEnergy >= 650 && distance is > 250 and < 850 && underFire,
		Chassis.Legion => (distance >= LegionRangeThreshold) != LegionLongRangeMode,
		Chassis.Tone => !HasTrackerLock && TrackerMarks > 0,
		Chassis.Scorch => distance is > 280 and < 1100 && !Scene.GetAllComponents<Titanfall2GasCanister>().Any( x => x.OwnerTitan == Actor && !x.Ignited ),
		Chassis.Northstar => !_aiFlightActive && distance > 900 && underFire,
		Chassis.Ronin => Actor.AiIsMoving && underFire && (shield < 0.45f || Actor.Reloading),
		_ => false
	};

	void StartAiAction( Titanfall2TitanAiAction action, GameObject target, Ray aim, float prepare, float recovery, string reason )
	{
		_aiVisualDelay = 0; _aiVisualDuration = prepare + recovery;
		var role = AnimationRoleFor( action );
		var clips = Actor.AnimationData?.Clips;
		var clip = clips?.FirstOrDefault( x => x.Role == role );
		var prepareClip = clips?.FirstOrDefault( x => x.Role == role + "_prepare" && x.Duration > 0 );
		var recoverClip = clips?.FirstOrDefault( x => x.Role == role + "_recover" && x.Duration > 0 );
		_aiPrepareRole = prepareClip?.Role; _aiPrepareClipDuration = prepareClip?.Duration ?? 0;
		_aiRecoverRole = recoverClip?.Role; _aiRecoverClipDuration = recoverClip?.Duration ?? 0;
		var evt = clip?.Sequence?.Events?.FirstOrDefault( e => e.Name is "AE_NPC_WEAPON_DIRECTED_FIRE" or "AE_NPC_WEAPON_FIRE" );
		if ( action == Titanfall2TitanAiAction.Offense && clip is { Duration: > 0 } && evt is not null
			&& float.IsFinite( evt.Cycle ) && evt.Cycle is >= 0 and <= 1 )
		{
			var timing = Titanfall2TitanAiRules.EventTiming( MathF.Max( prepare, _aiPrepareClipDuration + clip.Duration * evt.Cycle ),
				recovery, clip.Duration, evt.Cycle );
			prepare = timing.Prepare; recovery = timing.Recover;
			_aiVisualDelay = timing.VisualDelay; _aiVisualDuration = timing.VisualDuration;
			recovery += _aiRecoverClipDuration;
			reason += ":animation-event";
		}
		else if ( action is not (Titanfall2TitanAiAction.Core or Titanfall2TitanAiAction.Melee) && clip is { Duration: > 0 } )
		{
			// With no authoritative effect event, finish the authored main activity before committing.
			// This is an explicit end-of-activity fallback, not an invented event in the retail data.
			_aiVisualDelay = MathF.Max( 0, MathF.Max( prepare - clip.Duration, _aiPrepareClipDuration ) );
			_aiVisualDuration = clip.Duration;
			prepare = _aiVisualDelay + clip.Duration;
			recovery = MathF.Max( recovery, _aiRecoverClipDuration );
			reason += ":activity-end-fallback";
		}
		else { _aiPrepareRole = null; _aiRecoverRole = null; reason += ":timing-fallback"; }
		if ( !_aiAction.Start( action, prepare, recovery ) ) return;
		if ( action == Titanfall2TitanAiAction.Melee
			|| action == Titanfall2TitanAiAction.Offense && Kit is Chassis.Ronin or Chassis.Scorch
			|| action == Titanfall2TitanAiAction.Core && Kit is Chassis.Ion or Chassis.Tone or Chassis.Scorch )
		{
			Actor.Movement?.Stop();
			if ( Actor.Agent.IsValid() ) Actor.Agent.Stop();
		}
		_aiActionTarget = target; _aiCommittedAim = aim; AiDecisionReason = reason;
		_aiActionMode = Actor.AiMode; _aiActionRetailSettings = Actor.UseRetailAiSettings;
		PublishAiAction();
	}
	void FinishAiAction( string reason )
	{
		_aiAction.Cancel(); _aiActionTarget = null; AiDecisionReason = reason; PublishAiAction();
	}
	void PublishAiAction()
	{
		AiAction = _aiAction.Action; AiPhase = _aiAction.Phase; AiActionSerial = _aiAction.Serial;
		var duration = _aiAction.PrepareSeconds + _aiAction.RecoverySeconds;
		var elapsed = _aiAction.Elapsed + (AiPhase == Titanfall2TitanAiPhase.Recover ? _aiAction.PrepareSeconds : 0);
		AiActionProgress = _aiVisualDuration > 0 ? Math.Clamp( (elapsed - _aiVisualDelay) / _aiVisualDuration, 0, 1 ) : duration > 0 ? Math.Clamp( elapsed / duration, 0, 1 ) : 0;
		AiAnimationRole = AnimationRoleFor( AiAction );
		if ( _aiAction.Busy && _aiPrepareRole is not null && elapsed < _aiVisualDelay )
		{
			AiAnimationRole = _aiPrepareRole;
			AiActionProgress = Math.Clamp( elapsed / MathF.Max( 0.01f, _aiPrepareClipDuration ), 0, 1 );
		}
		else if ( _aiAction.Busy && _aiRecoverRole is not null && elapsed >= _aiVisualDelay + _aiVisualDuration )
		{
			AiAnimationRole = _aiRecoverRole;
			AiActionProgress = Math.Clamp( (elapsed - _aiVisualDelay - _aiVisualDuration) / MathF.Max( 0.01f, _aiRecoverClipDuration ), 0, 1 );
		}
		// The short activation request must not release the visible shield/block while its executor still holds it.
		// Defense itself does not own the primary gun; Legion can keep firing beneath its upper-body guard.
		if ( !_aiAction.Busy && AiAutonomous && (DefenseVolumeActive || RoninBlocking) )
		{
			var guardRole = AnimationRoleFor( Titanfall2TitanAiAction.Defense );
			var guardClip = Actor.AnimationData?.Clips?.FirstOrDefault( x => x.Role == guardRole && x.Duration > 0 );
			if ( guardClip is not null )
			{
				AiAction = Titanfall2TitanAiAction.Defense; AiPhase = Titanfall2TitanAiPhase.Active;
				AiAnimationRole = guardRole;
				AiActionProgress = Math.Clamp( _aiDefenseElapsed / guardClip.Duration, 0, 1 );
			}
		}
		// Long core presentations outlive the request's brief recovery. Their visual clock follows the core executor.
		var core = Actor.IsValid() ? Actor.GetComponent<Titanfall2TitanCore>() : null;
		if ( !_aiAction.Busy && core is not null && (core.IsActive || core.IsCharging)
			&& Kit is Chassis.Ion or Chassis.Tone or Chassis.Scorch or Chassis.Northstar )
		{
			AiAction = Titanfall2TitanAiAction.Core; AiPhase = Titanfall2TitanAiPhase.Active;
			AiAnimationRole = AnimationRoleFor( AiAction );
			var charge = Titanfall2TitanCore.CoreChargeup( Kit );
			var total = MathF.Max( 0.1f, core.Duration + charge );
			var remaining = core.IsCharging ? core.Duration + core.ChargeupRemaining : core.ActiveRemaining;
			AiActionProgress = Math.Clamp( 1f - remaining / total, 0, 1 );
		}
	}
	string AnimationRoleFor( Titanfall2TitanAiAction action ) => action switch
		{
			Titanfall2TitanAiAction.Offense => "ai_" + Kit.ToString().ToLowerInvariant() + "_offense",
			Titanfall2TitanAiAction.Defense => "ai_" + Kit.ToString().ToLowerInvariant() + "_defense",
			Titanfall2TitanAiAction.Utility => "ai_" + Kit.ToString().ToLowerInvariant() + "_utility",
			Titanfall2TitanAiAction.Core => "ai_" + Kit.ToString().ToLowerInvariant() + "_core",
			Titanfall2TitanAiAction.Melee => ModelLoader.MeleeRole,
			Titanfall2TitanAiAction.VortexRelease => "ai_ion_refire",
			_ => null
		};
	internal void CancelAiActions( string reason )
	{
		if ( !Networking.IsHost || IsProxy ) return;
		FinishAiAction( reason ); _rockets = 0; _volleyTarget = null;
		if ( _aiWasControlling )
		{
			GuardRemaining = 0; RoninBlocking = false; ClearDefenseVolume(); HoverRemaining = 0;
			if ( PhaseRemaining > 0 ) { PhaseRemaining = 0; _wasPhasingHost = false; if ( Actor.IsValid() && !Actor.Dead ) ResolveRoninPhaseExitHost(); }
			if ( Actor.IsValid() ) Actor.GetComponent<Titanfall2TitanCore>()?.CancelForTitanLife();
		}
		CancelAiMotion(); _aiCoreReadyElapsed = 0; _aiWasControlling = false; PublishAiAction();
	}
	static float RandomRest( Titanfall2NpcWeaponAiProfile profile, float fallback ) => profile is { Available: true }
		? profile.MinRest + Random.Shared.NextSingle() * (profile.MaxRest - profile.MinRest) : fallback;

	void UpdateAiTacticalMode( GameObject target, Titanfall2NpcAiProfile profile )
	{
		_aiModeAge += 0.35f; _aiModeSampleTime += 0.35f;
		if ( _aiModeSampleTime < 1 || profile is null ) return;
		var own = Actor.Health + Actor.Shield;
		var enemy = target?.GetComponent<Titanfall2Npc>();
		var other = enemy.IsValid() ? enemy.Health + enemy.Shield : 100;
		if ( _aiSampleTarget == target && _aiModeAge >= 1.5f )
		{
			var difference = ((own - _aiHealthSample) - (other - _aiEnemyHealthSample)) / _aiModeSampleTime;
			var lowShield = Actor.MaxShield > 0 && Actor.Shield / Actor.MaxShield < profile.EvasiveShieldFraction;
			var relativeHealth = (own - other) / MathF.Max( 1, own + other );
			var next = difference < profile.EvasiveHealthRateDifference || lowShield && Actor.IsUnderFire
				|| relativeHealth < -profile.EvasiveTotalHealthDifference;
			if ( difference > profile.AggressiveHealthRateDifference || relativeHealth > profile.AggressiveTotalHealthDifference ) next = false;
			if ( _aiEvasive != next ) { _aiEvasive = next; _aiModeAge = 0; }
			AiTacticalState = _aiEvasive ? "CombatEvasive" : difference > profile.AggressiveHealthRateDifference ? "CombatAggressive" : "Combat";
		}
		_aiSampleTarget = target; _aiHealthSample = own; _aiEnemyHealthSample = other; _aiModeSampleTime = 0;
	}
	bool WideAbilitySightClear( GameObject target )
	{
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( Eye, target.GetBounds().Center ).Radius( 8 )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).IgnoreGameObjectHierarchy( target ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		return !hit.StartedSolid && !hit.Hit;
	}
	bool GroundAbilityPathClear( Vector3 target )
	{
		var delta = target - Actor.WorldPosition;
		if ( MathF.Abs( delta.z ) > 180 ) return false;
		var count = Math.Clamp( (int)(delta.WithZ( 0 ).Length / 160), 1, 12 );
		var previous = Actor.WorldPosition;
		for ( var i = 1; i <= count; ++i )
		{
			var point = Actor.WorldPosition + delta * (i / (float)count);
			var ground = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( point + Vector3.Up * 80, point - Vector3.Up * 120 )
				.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile", "npc", "player" ), Titanfall2TraceMask.NpcSolid );
			if ( !ground.Hit || ground.StartedSolid || ground.Normal.z < 0.5f ) return false;
			var obstacle = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( previous + Vector3.Up * 24, ground.EndPosition + Vector3.Up * 24 ).Radius( 8 )
				.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile", "npc", "player" ), Titanfall2TraceMask.NpcSolid );
			if ( obstacle.StartedSolid || obstacle.Hit ) return false;
			previous = ground.EndPosition;
		}
		return true;
	}
	bool FindIncomingThreat( out Vector3 origin )
	{
		origin = default;
		foreach ( var projectile in Scene.GetAllComponents<Titanfall2Projectile>() )
		{
			if ( !projectile.IsInFlight || !ShouldBlock( projectile.CombatAttacker ) ) continue;
			var offset = projectile.WorldPosition - Eye;
			if ( offset.LengthSquared > 1800f * 1800f || Vector3.Dot( Actor.WorldRotation.Forward, offset.Normal ) < 0.15f ) continue;
			var velocity = projectile.Velocity;
			if ( velocity.LengthSquared < 1 ) continue;
			var time = -Vector3.Dot( offset, velocity ) / velocity.LengthSquared;
			if ( time is < 0 or > 1.2f || (offset + velocity * time).Length > Actor.BodyRadius + 100 ) continue;
			var sight = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( Eye, projectile.WorldPosition )
				.IgnoreGameObjectHierarchy( Actor.GameObject ).IgnoreGameObjectHierarchy( projectile.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
			if ( sight.StartedSolid || sight.Hit ) continue;
			origin = projectile.WorldPosition; return true;
		}
		return false;
	}
}
