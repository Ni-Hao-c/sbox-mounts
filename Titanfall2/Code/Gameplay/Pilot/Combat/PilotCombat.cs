/// <summary>
/// Intercepts Sandbox actions in Stage.StartUpdate—before PlayerFlashlight, PlayerInventory,
/// Player.Noclip, and ContextMenu execute—for both On-Foot Pilot Combat State and Piloted Titan Cockpit Mode.
/// Always preserves SpawnMenu (Q) and automatically yields when UI menus or Sandbox builder tools are active.
/// </summary>
public sealed class Titanfall2PilotInputInterceptor : GameObjectSystem<Titanfall2PilotInputInterceptor>
{
	public Titanfall2PilotInputInterceptor( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, -100, InterceptPilotCombatInput, nameof( InterceptPilotCombatInput ) );
	}

	void InterceptPilotCombatInput()
	{
		if ( !Scene.IsValid() || Application.IsDedicatedServer ) return;

		// 1. Titan Cockpit Mode takes precedence when the local Pilot is seated inside a Titan.
		Titanfall2TitanSeat activeSeat = null;
		foreach ( var seat in Scene.GetAllComponents<Titanfall2TitanSeat>() )
		{
			if ( !seat.IsValid() || !seat.Active || !seat.IsOccupied ) continue;
			var occupant = seat.GetOccupant();
			if ( !occupant.IsValid() || occupant.IsProxy || occupant.Mode is not Sandbox.Movement.SitMoveMode ) continue;
			activeSeat = seat;
			break;
		}
		if ( activeSeat is not null )
		{
			InterceptTitanCockpitInput( activeSeat );
			return;
		}

		// 2. On-Foot Pilot Combat State (Jump Kit equipped).
		Titanfall2PilotCombat localCombat = null;
		foreach ( var combat in Scene.GetAllComponents<Titanfall2PilotCombat>() )
		{
			if ( !combat.IsValid() || !combat.Active || combat.IsProxy ) continue;
			if ( combat.Parkour is not { IsValid: true, Equipped: true } ) continue;
			localCombat = combat;
			break;
		}
		if ( localCombat is null ) return;

		var yielded = localCombat.EvaluateYieldState();
		if ( yielded )
		{
			localCombat.OnInputYielded();
			return;
		}

		if ( Application.IsHeadless ) return;

		var tactical = localCombat.GameObject.GetComponent<Titanfall2PilotTactical>();
		var inPhase = tactical is { IsValid: true, Active: true, IsPhaseActive: true };

		var meleePressed = Input.Pressed( "Flashlight" );
		var ordnancePressed = Input.Pressed( "drop" );
		var ordnanceDown = Input.Down( "drop" );
		var ordnanceReleased = Input.Released( "drop" );
		var tacticalPressed = Input.Pressed( "toolmenu" );
		var tacticalCyclePressed = Input.Pressed( "spray" );

		// Clear intercepted actions for downstream Sandbox components (PlayerFlashlight / PlayerInventory.Drop / ToolMenu / Spray)
		// while leaving SpawnMenu (Q) and ContextMenu (C) untouched.
		ClearAction( "Flashlight" );
		ClearAction( "drop" );
		ClearAction( "toolmenu" );
		ClearAction( "spray" );

		if ( inPhase )
		{
			// Retail Phase Dimension rule: no firing, ADS, melee, or ordnance throws while phased (reload & parkour stay allowed).
			ClearAction( "attack1" );
			ClearAction( "attack2" );
			localCombat.OnInputYielded();
		}
		else
		{
			localCombat.ProcessFrameInput( meleePressed, ordnancePressed, ordnanceDown, ordnanceReleased );
		}

		if ( tactical is { IsValid: true, Active: true } )
		{
			if ( !inPhase && tacticalCyclePressed ) tactical.CycleTactical();
			if ( !inPhase && tacticalPressed ) tactical.TriggerTactical();
		}
	}

	static void InterceptTitanCockpitInput( Titanfall2TitanSeat seat )
	{
		var uiCapturing = !Application.IsHeadless
			&& (Mouse.Active || Sandbox.UI.InputFocus.Current is Sandbox.UI.TextEntry);
		if ( uiCapturing )
		{
			seat.OnCockpitInputYielded();
			return;
		}

		if ( Application.IsHeadless ) return;

		var kit = seat.Actor?.GetComponent<Titanfall2TitanKit>();
		var isIon = kit is { Kit: Titanfall2TitanKit.Chassis.Ion };

		var offensePressed = IsActionPressed( seat.OffenseAction );
		var adsDown = Input.Down( "attack2" );
		// Non-Ion Titans support RMB as Defensive ability in addition to Ctrl/Mouse3; Ion keeps RMB for Split Shot ADS.
		var defensePressed = IsActionPressed( seat.DefenseAction )
			|| IsActionPressed( seat.DefenseAltAction )
			|| (!isIon && Input.Pressed( "attack2" ));
		var defenseDown = IsActionDown( seat.DefenseAction )
			|| IsActionDown( seat.DefenseAltAction )
			|| (!isIon && adsDown);
		var utilityPressed = IsActionPressed( seat.UtilityAction );
		var smokePressed = IsActionPressed( seat.SmokeAction );
		var meleePressed = IsActionPressed( seat.MeleeAction );
		var corePressed = IsActionPressed( seat.CoreAction );

		// Intercept & clear all cockpit ability/core actions while deliberately leaving "spawnmenu" (Q) untouched.
		ClearAction( seat.OffenseAction );
		ClearAction( seat.DefenseAction );
		ClearAction( seat.DefenseAltAction );
		ClearAction( seat.UtilityAction );
		ClearAction( seat.SmokeAction );
		ClearAction( seat.MeleeAction );
		ClearAction( seat.CoreAction );
		ClearAction( "drop" );

		seat.ConsumeCockpitInput( new Titanfall2TitanCockpitInput(
			offensePressed, defensePressed, defenseDown, utilityPressed, smokePressed, meleePressed, corePressed, adsDown ) );
	}

	static bool IsActionPressed( string action ) => !string.IsNullOrWhiteSpace( action ) && Input.Pressed( action );
	static bool IsActionDown( string action ) => !string.IsNullOrWhiteSpace( action ) && Input.Down( action );

	static void ClearAction( string action )
	{
		if ( string.IsNullOrWhiteSpace( action ) ) return;
		Input.Clear( action );
		Input.SetLastAction( action, false );
	}
}

/// <summary>
/// Pilot Combat State controller attached alongside <see cref="Titanfall2PilotParkour"/> when the Jump Kit is equipped.
/// Owns [F] contextual Pilot melee (with target lunge and retail Miles audio) and [G] offhand ordnance
/// (default Frag Grenade, auto-binding to any picked-up TF2 ordnance weapon, with retail cook/throw/recharge).
/// </summary>
[Title( "Titanfall 2 Pilot Combat" ), Group( "Titanfall 2" )]
public sealed class Titanfall2PilotCombat : Component
{
	const float MeleeHitSweepRadius = 16f;
	const float SatchelDoubleTapWindow = 0.35f;

	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public Titanfall2PilotParkour Parkour { get; set; }
	[Property] public Titanfall2PilotMeleeSettings MeleeSettings { get; set; } = Titanfall2PilotMeleeSettings.RetailDefault;

	[Sync( SyncFlags.FromHost )] public string OrdnanceShortLabel { get; private set; } = "FRAG";
	[Sync( SyncFlags.FromHost )] public string OrdnanceDisplayName { get; private set; } = "Frag Grenade";
	[Sync( SyncFlags.FromHost )] public int OrdnanceUnits { get; private set; } = 200;
	[Sync( SyncFlags.FromHost )] public int OrdnanceMaxUnits { get; private set; } = 200;
	[Sync( SyncFlags.FromHost )] public int OrdnanceAmmoCost { get; private set; } = 100;
	[Sync( SyncFlags.FromHost )] public bool OrdnanceBusy { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool OrdnanceCooking { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool OrdnanceThrowing { get; private set; }
	[Sync( SyncFlags.FromHost )] public int OrdnanceThrowSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] public double OrdnanceReadyAt { get; private set; }

	[Sync( SyncFlags.FromHost )] public bool MeleeActive { get; private set; }
	[Sync( SyncFlags.FromHost )] public Titanfall2PilotMeleeStrikeKind MeleeStrikeKind { get; private set; }
	[Sync( SyncFlags.FromHost )] public int MeleeHitSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool MeleeLastHitWasKill { get; private set; }
	[Sync( SyncFlags.FromHost )] public double MeleeReadyAt { get; private set; }

	public bool YieldedToSandbox { get; private set; }
	public bool CombatBusy => MeleeActive || OrdnanceBusy || _ordnanceLocalHeld;
	public int OrdnanceCharges => OrdnanceAmmoCost > 0 ? Math.Max( 0, OrdnanceUnits ) / OrdnanceAmmoCost : 0;
	public int OrdnanceMaxCharges => OrdnanceAmmoCost > 0 ? Math.Max( 1, OrdnanceMaxUnits ) / OrdnanceAmmoCost : 1;
	public float OrdnanceChargeFill => Titanfall2PilotCombatRules.ComputeChargeFillFraction( OrdnanceUnits, OrdnanceMaxUnits );
	public float OrdnanceCookFraction => OrdnanceCooking && _ordnanceConfig.Profile.MaxCookSeconds > 0.01f
		? Titanfall2PilotCombatRules.ComputeCookFraction( _localOrdnancePreparedAt, Time.NowDouble, _ordnanceConfig.Profile.MaxCookSeconds )
		: 0f;
	public float MeleeProgress => MeleeActive && _localMeleeDuration > 0.01f
		? Math.Clamp( (float)((Time.NowDouble - _localMeleeStartedAt) / _localMeleeDuration), 0f, 1f )
		: 0f;
	public float MeleeCooldownFraction
	{
		get
		{
			var duration = MathF.Max( MeleeSettings.AttackDuration, 0.1f );
			var remaining = (float)Math.Max( 0.0, MeleeReadyAt - Time.NowDouble );
			return Math.Clamp( 1f - remaining / duration, 0f, 1f );
		}
	}
	public float OffhandDip => _offhandDip;
	public Angles CameraKickAngles => _cameraKickAngles;

	PlayerController _controller;
	BaseInventoryComponent _inventory;

	// Offhand ordnance state (host authoritative + local visual prediction).
	Titanfall2OffhandOrdnanceConfig _ordnanceConfig = Titanfall2OffhandOrdnanceConfig.RetailFragDefault;
	Titanfall2OrdnanceRecovery _ordnanceRecovery;
	Titanfall2Weapon _boundInventoryOrdnance;
	readonly HashSet<Guid> _seenOrdnanceItems = [];
	bool _ordnanceLocalHeld;
	bool _ordnanceHostHeld;
	bool _ordnanceReleased;
	bool _ordnanceLaunched;
	int _ordnanceInputSerial;
	int _ordnanceLastInputSerial;
	double _ordnancePreparedAt;
	double _ordnanceTossAt;
	double _ordnanceLaunchAt;
	double _localOrdnancePreparedAt;
	double _localOrdnanceTossAt;
	double _lastSatchelTapTime = -10.0;

	// Pilot melee state.
	int _swingCounter;
	double _hostMeleeStartedAt;
	double _hostMeleeImpactAt;
	double _localMeleeStartedAt;
	float _localMeleeDuration = 0.78f;
	bool _hostMeleeImpactDone;
	bool _localMeleeHitConfirmed;
	int _observedMeleeHitSerial;
	GameObject _lungeTarget;
	double _lungeEndTime;

	// Smoothed local presentation state.
	float _offhandDip;
	Angles _cameraKickAngles;

	internal static Titanfall2PilotMeleeSettings ResolveMeleeSettings( string mountIdent ) =>
		Sandbox.Mounting.Directory.Get( mountIdent ?? "titanfall2" ) is Titanfall2Mount mount
			? mount.GetPilotMeleeSettings()
			: Titanfall2PilotMeleeSettings.RetailDefault;

	internal static Titanfall2OffhandOrdnanceConfig ResolveDefaultOrdnance( string mountIdent ) =>
		Sandbox.Mounting.Directory.Get( mountIdent ?? "titanfall2" ) is Titanfall2Mount mount
			? mount.GetDefaultOffhandOrdnance()
			: Titanfall2OffhandOrdnanceConfig.RetailFragDefault;

	internal void ConfigureForEquip( Titanfall2PilotParkour parkour, string mountIdent, Titanfall2OffhandOrdnanceConfig? ordnance = null )
	{
		Parkour = parkour;
		MountIdent = mountIdent ?? "titanfall2";
		MeleeSettings = ResolveMeleeSettings( MountIdent );
		if ( ordnance is { Valid: true } persisted )
			ApplyOrdnanceConfig( persisted, resetUnits: true );
		else if ( !_ordnanceConfig.Valid )
			ApplyOrdnanceConfig( ResolveDefaultOrdnance( MountIdent ), resetUnits: true );
		else if ( Networking.IsHost )
			SyncOrdnanceMetadata();
		Enabled = true;
	}

	internal void ResetOnUnequip()
	{
		CancelAllCombatState();
		YieldedToSandbox = false;
		_offhandDip = 0f;
		_cameraKickAngles = Angles.Zero;
		Enabled = false;
	}

	protected override void OnAwake()
	{
		_controller = GetComponent<PlayerController>();
		_inventory = GetComponent<BaseInventoryComponent>();
		Parkour ??= GetComponent<Titanfall2PilotParkour>();
		if ( !_ordnanceConfig.Valid )
			_ordnanceConfig = ResolveDefaultOrdnance( MountIdent );
	}

	protected override void OnUpdate()
	{
		_controller ??= GetComponent<PlayerController>();
		_inventory ??= GetComponent<BaseInventoryComponent>();
		Parkour ??= GetComponent<Titanfall2PilotParkour>();

		if ( Parkour is not { IsValid: true, Equipped: true } || !_controller.IsValid() )
		{
			CancelAllCombatState();
			return;
		}

		if ( Networking.IsHost )
		{
			ScanInventoryOrdnanceBinding();
			TickOrdnanceHost();
			TickMeleeHost();
		}

		if ( !IsProxy )
		{
			TickOwnerLunge();
			UpdatePresentation();
		}
	}

	protected override void OnDisabled() => CancelAllCombatState();
	protected override void OnDestroy() => CancelAllCombatState();

	internal bool EvaluateYieldState()
	{
		_controller ??= GetComponent<PlayerController>();
		_inventory ??= GetComponent<BaseInventoryComponent>();
		Parkour ??= GetComponent<Titanfall2PilotParkour>();

		var equipped = Parkour is { IsValid: true, Equipped: true };
		var pilotModeActive = _controller.IsValid() && _controller.Mode is Titanfall2PilotMoveMode;
		var useInput = _controller.IsValid() && _controller.UseInputControls;
		var uiCapturing = !Application.IsHeadless
			&& (Mouse.Active || Sandbox.UI.InputFocus.Current is Sandbox.UI.TextEntry);
		var activeType = _inventory.IsValid() ? _inventory.ActiveItem?.GetType().Name : null;

		YieldedToSandbox = Titanfall2PilotCombatRules.ShouldYieldCombatInput(
			equipped, pilotModeActive, useInput, uiCapturing, activeType );
		return YieldedToSandbox;
	}

	internal void NotifyTelefragKillHost()
	{
		if ( !Networking.IsHost ) return;
		MeleeLastHitWasKill = true;
		MeleeHitSerial = MeleeHitSerial == int.MaxValue ? 1 : MeleeHitSerial + 1;
	}

	internal void OnInputYielded()
	{
		if ( _ordnanceLocalHeld )
			SetOrdnanceInput( false );
	}

	internal void ProcessFrameInput( bool meleePressed, bool ordnancePressed, bool ordnanceDown, bool ordnanceReleased )
	{
		if ( IsProxy || Parkour is not { IsValid: true, Equipped: true } || !_controller.IsValid() ) return;

		if ( meleePressed )
			TriggerMelee();

		if ( ordnancePressed )
		{
			if ( TryHandleSatchelDetonateTap() )
				return;
			SetOrdnanceInput( true );
		}
		else if ( _ordnanceLocalHeld && (ordnanceReleased || !ordnanceDown) )
		{
			SetOrdnanceInput( false );
		}
	}

	internal void TriggerMelee()
	{
		if ( IsProxy || !_controller.IsValid() || Parkour is not { Equipped: true } ) return;
		if ( MeleeActive || OrdnanceBusy || _ordnanceLocalHeld || Time.NowDouble < MeleeReadyAt ) return;

		var kind = Titanfall2PilotCombatRules.SelectMeleeStrikeKind(
			Parkour.IsWallrunning, Parkour.IsSliding, Parkour.IsAirborne, _swingCounter++ );
		var target = FindBestMeleeLungeTarget();

		// Apply immediate responsive lunge on the owner while host runs authoritative impact & damage.
		_lungeTarget = target;
		_lungeEndTime = target.IsValid() ? Time.NowDouble + MeleeSettings.LungeDuration : 0.0;
		_localMeleeStartedAt = Time.NowDouble;
		_localMeleeDuration = MeleeSettings.AttackDuration;
		_localMeleeHitConfirmed = false;
		ApplyOwnerMeleeImpulse( kind, target );

		if ( Networking.IsHost )
			StartMeleeHost( kind, target );
		else
			RequestMeleeOnHost( kind, target );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestMeleeOnHost( Titanfall2PilotMeleeStrikeKind kind, GameObject suggestedTarget )
	{
		if ( !Networking.IsHost ) return;
		StartMeleeHost( kind, suggestedTarget );
	}

	void StartMeleeHost( Titanfall2PilotMeleeStrikeKind kind, GameObject suggestedTarget )
	{
		if ( !Networking.IsHost || !_controller.IsValid() || Parkour is not { Equipped: true } ) return;
		if ( MeleeActive || OrdnanceBusy || Time.NowDouble < MeleeReadyAt ) return;

		MeleeActive = true;
		MeleeStrikeKind = kind;
		_hostMeleeStartedAt = Time.NowDouble;
		_hostMeleeImpactAt = _hostMeleeStartedAt + MeleeSettings.ImpactDelay;
		MeleeReadyAt = _hostMeleeStartedAt + MeleeSettings.AttackDuration;
		_hostMeleeImpactDone = false;
		_lungeTarget = ValidateHostLungeTarget( suggestedTarget );
		_lungeEndTime = _lungeTarget.IsValid() ? _hostMeleeStartedAt + MeleeSettings.LungeDuration : 0.0;

		if ( !IsProxy )
		{
			_localMeleeStartedAt = _hostMeleeStartedAt;
			_localMeleeDuration = MeleeSettings.AttackDuration;
			_localMeleeHitConfirmed = false;
		}

		PlayMeleeSwingSoundRpc( kind );
	}

	void TickMeleeHost()
	{
		if ( !MeleeActive ) return;

		var now = Time.NowDouble;
		if ( !_hostMeleeImpactDone )
		{
			var closeToTarget = _lungeTarget.IsValid()
				&& _lungeTarget.WorldPosition.Distance( WorldPosition ) <= MeleeSettings.Range * 0.85f
				&& now >= _hostMeleeStartedAt + 0.06f;
			if ( now >= _hostMeleeImpactAt || closeToTarget )
			{
				_hostMeleeImpactDone = true;
				CommitMeleeImpactHost();
			}
		}

		if ( now >= MeleeReadyAt )
		{
			MeleeActive = false;
			MeleeStrikeKind = Titanfall2PilotMeleeStrikeKind.None;
			_lungeTarget = null;
		}
	}

	void CommitMeleeImpactHost()
	{
		if ( !Networking.IsHost || !_controller.IsValid() ) return;
		var eye = _controller.EyeTransform;
		var forward = eye.Forward.LengthSquared > 0.01f ? eye.Forward.Normal : WorldRotation.Forward;
		var start = eye.Position;
		var end = start + forward * MeleeSettings.Range;

		var trace = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( start, end )
			.Radius( MeleeHitSweepRadius )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );

		var hitGo = trace.Hit ? trace.GameObject : null;
		var hitPos = trace.Hit ? trace.HitPosition : end;
		var surfaceName = trace.Hit ? trace.Surface?.ResourceName : null;

		// If lunging onto a valid living target just outside the sweep radius, honor the locked lunge contact if clear LOS.
		if ( (hitGo is null || hitGo.GetComponentInParent<Component.IDamageable>() is null) && _lungeTarget.IsValid() )
		{
			var targetCenter = TargetAimPoint( _lungeTarget );
			if ( targetCenter.Distance( start ) <= MeleeSettings.Range + 22f
				&& Vector3.Dot( forward, (targetCenter - start).Normal ) >= 0.45f
				&& HasClearMeleeLine( start, targetCenter, _lungeTarget ) )
			{
				hitGo = _lungeTarget;
				hitPos = targetCenter;
			}
		}

		if ( !hitGo.IsValid() ) return;

		var damageable = hitGo.GetComponentInParent<Component.IDamageable>();
		var targetNpc = hitGo.GetComponentInParent<Titanfall2Npc>();
		var targetPlayer = hitGo.GetComponentInParent<PlayerController>();
		var wasDead = targetNpc is { Dead: true };

		if ( damageable is not null && !ReferenceEquals( damageable, _controller ) )
		{
			var amount = targetNpc is { IsTitan: true } ? MeleeSettings.TitanDamage : MeleeSettings.Damage;
			if ( amount > 0f )
			{
				var info = new DamageInfo( amount, GameObject, GameObject )
				{
					Origin = start,
					Position = hitPos
				};
				info.Tags.Add( "melee" );
				Titanfall2HostDamage.Deal( damageable, info );
			}

			var killed = !wasDead && targetNpc is { Dead: true };
			MeleeLastHitWasKill = killed;
			MeleeHitSerial = MeleeHitSerial == int.MaxValue ? 1 : MeleeHitSerial + 1;
			if ( !IsProxy ) _localMeleeHitConfirmed = true;
		}

		// Apply crisp melee knockback to dynamic physics props or hit actors.
		if ( hitGo.GetComponentInParent<Rigidbody>() is { IsValid: true, Enabled: true } victimBody
			&& victimBody != _controller.Body )
		{
			victimBody.Velocity += (forward * MeleeSettings.Impulse + Vector3.Up * 110f) * 0.65f;
		}

		var impactKind = ClassifyImpactKind( targetNpc, targetPlayer, surfaceName );
		PlayMeleeImpactSoundRpc( hitPos, impactKind );
	}

	void ApplyOwnerMeleeImpulse( Titanfall2PilotMeleeStrikeKind kind, GameObject target )
	{
		if ( !_controller.IsValid() || !_controller.Body.IsValid() ) return;
		var mode = GetComponent<Titanfall2PilotMoveMode>();
		if ( mode.IsValid() && Parkour.IsWallrunning )
			mode.BreakWallrunForMelee();

		if ( target.IsValid() )
		{
			var targetPos = TargetAimPoint( target );
			_controller.Body.Velocity = Titanfall2PilotCombatRules.ComputeMeleeLungeVelocity(
				WorldPosition, targetPos, _controller.Body.Velocity, MeleeSettings );
			if ( !Parkour.IsSliding )
				_controller.PreventGrounding( 0.08f );
			Parkour?.TriggerThrusterBurst( 1.0f );
			return;
		}

		// Small forward burst on air-kick / slide-kick / wall-kick for authentic TF2 feel.
		if ( kind is Titanfall2PilotMeleeStrikeKind.AirKick or Titanfall2PilotMeleeStrikeKind.SlideKick or Titanfall2PilotMeleeStrikeKind.WallKick )
		{
			var fwd = _controller.EyeTransform.Forward.WithZ( 0 );
			if ( fwd.LengthSquared > 0.01f )
			{
				var v = _controller.Body.Velocity;
				var nextFlat = v.WithZ( 0 ) + fwd.Normal * 95f;
				_controller.Body.Velocity = Titanfall2PilotParkourRules.ClampHorizontal( nextFlat, Titanfall2PilotParkourRules.MaxHorizontalSpeed ).WithZ( v.z );
			}
			Parkour?.TriggerThrusterBurst( 0.65f );
		}
	}

	void TickOwnerLunge()
	{
		if ( !MeleeActive || !_lungeTarget.IsValid() || Time.NowDouble >= _lungeEndTime ) return;
		if ( !_controller.IsValid() || !_controller.Body.IsValid() ) return;
		var targetPos = TargetAimPoint( _lungeTarget );
		if ( targetPos.Distance( WorldPosition ) <= MeleeSettings.LungeStopDistance )
		{
			_lungeEndTime = 0.0;
			return;
		}
		_controller.Body.Velocity = Titanfall2PilotCombatRules.ComputeMeleeLungeVelocity(
			WorldPosition, targetPos, _controller.Body.Velocity, MeleeSettings );
	}

	GameObject FindBestMeleeLungeTarget()
	{
		if ( !Scene.IsValid() || !_controller.IsValid() ) return null;
		var eye = _controller.EyeTransform;
		var eyePos = eye.Position;
		var forward = eye.Forward.LengthSquared > 0.01f ? eye.Forward.Normal : WorldRotation.Forward;
		var maxRange = MeleeSettings.LungeRange;
		var minCos = MeleeSettings.LungeMinCos;

		GameObject best = null;
		var bestScore = float.NegativeInfinity;

		foreach ( var npc in Scene.GetAllComponents<Titanfall2Npc>() )
		{
			if ( !npc.IsValid() || !npc.Active || npc.Dead || npc.Friendly || npc.IsTitan ) continue;
			var point = TargetAimPoint( npc.GameObject );
			var score = Titanfall2PilotCombatRules.ScoreMeleeTarget( eyePos, forward, point, maxRange, minCos );
			if ( score <= bestScore ) continue;
			if ( !HasClearMeleeLine( eyePos, point, npc.GameObject ) ) continue;
			bestScore = score;
			best = npc.GameObject;
		}

		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			if ( !player.IsValid() || !player.Active || player == _controller ) continue;
			var point = TargetAimPoint( player.GameObject );
			var score = Titanfall2PilotCombatRules.ScoreMeleeTarget( eyePos, forward, point, maxRange, minCos );
			if ( score <= bestScore ) continue;
			if ( !HasClearMeleeLine( eyePos, point, player.GameObject ) ) continue;
			bestScore = score;
			best = player.GameObject;
		}

		return best;
	}

	GameObject ValidateHostLungeTarget( GameObject candidate )
	{
		if ( !candidate.IsValid() || !_controller.IsValid() ) return null;
		if ( candidate == GameObject ) return null;
		if ( candidate.GetComponentInParent<Titanfall2Npc>() is { Dead: true } or { IsTitan: true } ) return null;
		var eyePos = _controller.EyeTransform.Position;
		var point = TargetAimPoint( candidate );
		if ( point.Distance( eyePos ) > MeleeSettings.LungeRange + 24f ) return null;
		return HasClearMeleeLine( eyePos, point, candidate ) ? candidate : null;
	}

	bool HasClearMeleeLine( Vector3 start, Vector3 end, GameObject target )
	{
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( start, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.PlayerSolid );
		if ( !hit.Hit ) return true;
		return hit.GameObject.IsValid() && (hit.GameObject == target || hit.GameObject.IsDescendant( target ) || target.IsDescendant( hit.GameObject ));
	}

	static Vector3 TargetAimPoint( GameObject target )
	{
		if ( target.GetComponent<Titanfall2Npc>() is { IsValid: true } npc )
			return npc.WorldPosition + Vector3.Up * Math.Clamp( npc.BodyHeight * 0.55f, 20f, 52f );
		if ( target.GetComponent<PlayerController>() is { IsValid: true } pc )
			return pc.WorldPosition + Vector3.Up * (pc.CurrentHeight * 0.55f);
		return target.WorldPosition + Vector3.Up * 36f;
	}

	// ---- Offhand [G] Ordnance ----

	internal void SetOrdnanceHold( bool held ) => SetOrdnanceInput( held );

	bool TryHandleSatchelDetonateTap()
	{
		if ( _ordnanceConfig.Projectile.Behavior != Titanfall2ProjectileBehavior.RemoteCharge )
			return false;

		var now = Time.NowDouble;
		var doubleTap = (now - _lastSatchelTapTime) <= SatchelDoubleTapWindow;
		_lastSatchelTapTime = now;
		var noAmmo = OrdnanceUnits < _ordnanceConfig.AmmoCost;
		if ( !doubleTap && !noAmmo ) return false;

		var hasDeployed = Scene.GetAllComponents<Titanfall2Projectile>()
			.Any( p => p.IsValid() && p.Settings.Behavior == Titanfall2ProjectileBehavior.RemoteCharge );
		if ( !hasDeployed ) return false;

		if ( Networking.IsHost ) DetonateOwnedSatchelsHost();
		else RequestSatchelDetonateOnHost();
		return true;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestSatchelDetonateOnHost() => DetonateOwnedSatchelsHost();

	void DetonateOwnedSatchelsHost()
	{
		if ( !Networking.IsHost || !_controller.IsValid() ) return;
		foreach ( var charge in Scene.GetAllComponents<Titanfall2Projectile>().ToArray() )
			charge.DetonateOwned( GameObject, _controller.GameObject );
	}

	void SetOrdnanceInput( bool held )
	{
		if ( IsProxy || _ordnanceLocalHeld == held ) return;
		if ( held && (MeleeActive || OrdnanceBusy || Time.NowDouble < OrdnanceReadyAt || OrdnanceUnits < OrdnanceAmmoCost) )
			return;

		_ordnanceLocalHeld = held;
		if ( held )
		{
			_localOrdnancePreparedAt = Time.NowDouble;
			_localOrdnanceTossAt = double.PositiveInfinity;
		}
		else
		{
			_localOrdnanceTossAt = Math.Max( Time.NowDouble, _localOrdnancePreparedAt + _ordnanceConfig.Profile.PulloutSeconds );
		}

		_ordnanceInputSerial = _ordnanceInputSerial == int.MaxValue ? 1 : _ordnanceInputSerial + 1;
		if ( Networking.IsHost ) ApplyOrdnanceInputHost( _ordnanceInputSerial, held );
		else RequestOrdnanceInputOnHost( _ordnanceInputSerial, held );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestOrdnanceInputOnHost( int serial, bool held ) => ApplyOrdnanceInputHost( serial, held );

	void ApplyOrdnanceInputHost( int serial, bool held )
	{
		if ( !Networking.IsHost || !_controller.IsValid() || Parkour is not { Equipped: true } ) return;
		if ( serial <= _ordnanceLastInputSerial ) return;
		_ordnanceLastInputSerial = serial;

		if ( !held )
		{
			_ordnanceHostHeld = false;
			if ( OrdnanceBusy && !_ordnanceReleased )
				ScheduleOrdnanceTossHost( Time.NowDouble );
			return;
		}

		if ( !_ordnanceConfig.Valid )
			ApplyOrdnanceConfig( ResolveDefaultOrdnance( MountIdent ), resetUnits: true );

		if ( MeleeActive || OrdnanceBusy || _ordnanceHostHeld || Time.NowDouble < OrdnanceReadyAt || OrdnanceUnits < _ordnanceConfig.AmmoCost )
			return;

		_ordnanceHostHeld = true;
		OrdnanceBusy = true;
		OrdnanceCooking = true;
		OrdnanceThrowing = false;
		_ordnanceReleased = false;
		_ordnanceLaunched = false;
		_ordnancePreparedAt = Time.NowDouble;
		_ordnanceTossAt = _ordnanceLaunchAt = double.PositiveInfinity;
		if ( !IsProxy )
		{
			_localOrdnancePreparedAt = _ordnancePreparedAt;
			_localOrdnanceTossAt = double.PositiveInfinity;
		}
		PlayOrdnanceSoundRpc( thrown: false );
	}

	void ScheduleOrdnanceTossHost( double now )
	{
		if ( _ordnanceReleased ) return;
		var profile = _ordnanceConfig.Profile;
		_ordnanceReleased = true;
		OrdnanceCooking = false;
		OrdnanceThrowing = true;
		_ordnanceTossAt = Math.Max( now, _ordnancePreparedAt + profile.PulloutSeconds );
		_ordnanceLaunchAt = _ordnanceTossAt + profile.ThrowSeconds * profile.ReleaseCycle;
		OrdnanceReadyAt = _ordnanceTossAt + Math.Max( _ordnanceConfig.PrimaryDelay, profile.ThrowSeconds * profile.ReadyCycle );
		if ( !IsProxy ) _localOrdnanceTossAt = _ordnanceTossAt;
	}

	void TickOrdnanceHost()
	{
		if ( !_ordnanceConfig.Valid ) return;
		var now = Time.NowDouble;

		if ( OrdnanceBusy )
		{
			if ( !_controller.Active || Parkour is not { Equipped: true } )
			{
				CancelOrdnanceHost();
			}
			else
			{
				var maxCook = _ordnanceConfig.Profile.MaxCookSeconds;
				if ( !_ordnanceReleased && maxCook > 0f && now >= _ordnancePreparedAt + maxCook )
					ScheduleOrdnanceTossHost( _ordnancePreparedAt + maxCook );

				if ( _ordnanceReleased && !_ordnanceLaunched && now >= _ordnanceLaunchAt )
					CommitOrdnanceHost( drop: false );

				if ( _ordnanceLaunched && now >= OrdnanceReadyAt )
				{
					OrdnanceBusy = false;
					OrdnanceCooking = false;
					OrdnanceThrowing = false;
					if ( !IsProxy ) _ordnanceLocalHeld = false;
				}
			}
		}

		OrdnanceUnits = _ordnanceRecovery.Recover( _ordnanceConfig.Profile, OrdnanceUnits, _ordnanceConfig.ClipMaxSize, now, Time.Delta );
	}

	void CommitOrdnanceHost( bool drop )
	{
		if ( !Networking.IsHost || _ordnanceLaunched || !_controller.IsValid() || OrdnanceUnits < _ordnanceConfig.AmmoCost ) return;
		var aim = _controller.EyeTransform.ForwardRay;
		if ( !aim.Position.IsFinite || !aim.Forward.IsFinite ) return;

		var profile = _ordnanceConfig.Profile;
		var flight = _ordnanceConfig.Projectile;
		var direction = profile.ThrowDirection( aim.Forward );
		var origin = aim.Position;

		// Sweep slight forward launch offset so Satchels / grenades never clip through thin walls.
		var forwardOffset = flight.Behavior == Titanfall2ProjectileBehavior.RemoteCharge ? 15f : 10f;
		var clearance = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( origin, origin + aim.Forward * forwardOffset )
			.Radius( Math.Clamp( flight.Radius, 0f, 12f ) )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
		if ( !clearance.StartedSolid ) origin = clearance.EndPosition;

		var settings = flight with { FuseSeconds = profile.RemainingFuse( _ordnancePreparedAt, Time.NowDouble ) };
		var shot = Titanfall2Projectile.Launch( Scene, new Ray( origin, direction ), settings,
			_ordnanceConfig.Damage, _ordnanceConfig.TitanDamage, GameObject, GameObject, GameObject, _ordnanceConfig.DamageProfile );
		if ( !shot.IsValid() ) return;

		shot.SetHostVelocity( direction * settings.Speed * (drop ? 0.2f : 1f) + _controller.Velocity * profile.InheritVelocity );
		OrdnanceUnits = Math.Max( 0, OrdnanceUnits - _ordnanceConfig.AmmoCost );
		_ordnanceLaunched = true;
		_ordnanceRecovery.Used( _ordnanceReleased ? _ordnanceTossAt : Time.NowDouble );
		OrdnanceThrowSerial = OrdnanceThrowSerial == int.MaxValue ? 1 : OrdnanceThrowSerial + 1;
		PlayOrdnanceSoundRpc( thrown: true );
	}

	void CancelOrdnanceHost()
	{
		if ( !Networking.IsHost ) return;
		if ( OrdnanceBusy && !_ordnanceLaunched && _ordnanceConfig.Profile.DropOnCancel )
			CommitOrdnanceHost( drop: true );
		OrdnanceBusy = false;
		OrdnanceCooking = false;
		OrdnanceThrowing = false;
		_ordnanceHostHeld = false;
		_ordnanceReleased = false;
		_ordnanceLaunched = false;
	}

	void ScanInventoryOrdnanceBinding()
	{
		if ( !_inventory.IsValid() ) return;

		Titanfall2Weapon newlySeen = null;
		Titanfall2Weapon activeOrdnance = _inventory.ActiveItem is Titanfall2Weapon { UsesOrdnance: true } active ? active : null;

		foreach ( var item in _inventory.Items )
		{
			if ( item is not Titanfall2Weapon { UsesOrdnance: true } weapon ) continue;
			if ( _seenOrdnanceItems.Add( weapon.Id ) )
				newlySeen = weapon;
		}

		// Rebind when the player picks up a new TF2 ordnance weapon or explicitly switches to one.
		var candidate = newlySeen ?? (activeOrdnance != _boundInventoryOrdnance ? activeOrdnance : null);
		if ( candidate is null || !candidate.OrdnanceProfile.Valid ) return;
		if ( OrdnanceBusy ) return;

		_boundInventoryOrdnance = candidate;
		ApplyOrdnanceConfig( Titanfall2OffhandOrdnanceConfig.FromWeapon( candidate ), resetUnits: true );
	}

	internal void BindOrdnanceConfigHost( in Titanfall2OffhandOrdnanceConfig config )
	{
		if ( !Networking.IsHost ) return;
		ApplyOrdnanceConfig( config, resetUnits: true );
	}

	void ApplyOrdnanceConfig( in Titanfall2OffhandOrdnanceConfig config, bool resetUnits )
	{
		_ordnanceConfig = config.Valid ? config : Titanfall2OffhandOrdnanceConfig.RetailFragDefault;
		if ( Networking.IsHost )
		{
			SyncOrdnanceMetadata();
			if ( _controller.IsValid() )
				Scene?.GetSystem<Titanfall2JumpKitPersistence>()?.RememberOrdnance( _controller, _ordnanceConfig );
			if ( resetUnits )
			{
				OrdnanceUnits = _ordnanceConfig.ClipMaxSize;
				_ordnanceRecovery = default;
			}
		}
	}

	void SyncOrdnanceMetadata()
	{
		OrdnanceShortLabel = _ordnanceConfig.ShortLabel;
		OrdnanceDisplayName = _ordnanceConfig.DisplayName;
		OrdnanceMaxUnits = _ordnanceConfig.ClipMaxSize;
		OrdnanceAmmoCost = _ordnanceConfig.AmmoCost;
	}

	void CancelAllCombatState()
	{
		_ordnanceLocalHeld = false;
		_lungeTarget = null;
		_lungeEndTime = 0.0;
		if ( Networking.IsHost )
		{
			CancelOrdnanceHost();
			MeleeActive = false;
			MeleeStrikeKind = Titanfall2PilotMeleeStrikeKind.None;
		}
	}

	void UpdatePresentation()
	{
		var dt = Math.Clamp( Time.Delta, 0f, 0.05f );
		if ( MeleeHitSerial != _observedMeleeHitSerial )
		{
			_observedMeleeHitSerial = MeleeHitSerial;
			_localMeleeHitConfirmed = true;
		}

		var throwDuration = MathF.Max( _ordnanceConfig.Profile.ThrowSeconds, 0.25f );
		var throwProgress = OrdnanceThrowing && double.IsFinite( _localOrdnanceTossAt )
			? Math.Clamp( (float)((Time.NowDouble - _localOrdnanceTossAt) / throwDuration), 0f, 1f )
			: 0f;
		var targetDip = Titanfall2PilotCombatRules.ComputeOffhandDipFactor( OrdnanceCooking || _ordnanceLocalHeld, OrdnanceThrowing, throwProgress );
		_offhandDip = MathX.Lerp( _offhandDip, targetDip, Math.Clamp( dt * 16f, 0f, 1f ), true );

		var targetKick = MeleeActive
			? Titanfall2PilotCombatRules.ComputeMeleeViewKick( MeleeStrikeKind, MeleeProgress, _localMeleeHitConfirmed )
			: OrdnanceThrowing
				? new Angles( -3.5f * MathF.Sin( throwProgress * MathF.PI ), 0f, -2.5f * MathF.Sin( throwProgress * MathF.PI ) )
				: Angles.Zero;
		_cameraKickAngles = Angles.Lerp( _cameraKickAngles, targetKick, Math.Clamp( dt * 22f, 0f, 1f ) );
	}

	enum MeleeImpactKind : byte { Default, Flesh, Android, Metal, Concrete }

	static MeleeImpactKind ClassifyImpactKind( Titanfall2Npc npc, PlayerController player, string surfaceName )
	{
		if ( npc.IsValid() )
		{
			var model = npc.ModelPath ?? "";
			if ( model.Contains( "spectre", StringComparison.OrdinalIgnoreCase )
				|| model.Contains( "stalker", StringComparison.OrdinalIgnoreCase )
				|| model.Contains( "marvin", StringComparison.OrdinalIgnoreCase )
				|| model.Contains( "drone", StringComparison.OrdinalIgnoreCase )
				|| model.Contains( "turret", StringComparison.OrdinalIgnoreCase )
				|| npc.IsTitan )
				return MeleeImpactKind.Android;
			return MeleeImpactKind.Flesh;
		}
		if ( player.IsValid() ) return MeleeImpactKind.Flesh;
		if ( !string.IsNullOrEmpty( surfaceName ) )
		{
			if ( surfaceName.Contains( "metal", StringComparison.OrdinalIgnoreCase ) ) return MeleeImpactKind.Metal;
			if ( surfaceName.Contains( "concrete", StringComparison.OrdinalIgnoreCase )
				|| surfaceName.Contains( "stone", StringComparison.OrdinalIgnoreCase ) ) return MeleeImpactKind.Concrete;
		}
		return MeleeImpactKind.Default;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMeleeSwingSoundRpc( Titanfall2PilotMeleeStrikeKind kind )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var localFirstPerson = !IsProxy && _controller is { ThirdPerson: false };
		var sound = localFirstPerson ? kind switch
		{
			Titanfall2PilotMeleeStrikeKind.LeftHook => MeleeSettings.SoundLeftHook1P,
			Titanfall2PilotMeleeStrikeKind.AirKick => MeleeSettings.SoundAirKick1P,
			Titanfall2PilotMeleeStrikeKind.SlideKick => MeleeSettings.SoundSlideKick1P,
			Titanfall2PilotMeleeStrikeKind.WallKick => MeleeSettings.SoundWallKick1P,
			_ => MeleeSettings.SoundRightHook1P
		} : MeleeSettings.SoundSwing3P;
		Titanfall2SoundEventEmitter.OneShot( Scene, MountIdent, sound, WorldPosition + Vector3.Up * 48f, firstPerson: localFirstPerson );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMeleeImpactSoundRpc( Vector3 position, MeleeImpactKind kind )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var localFirstPerson = !IsProxy && _controller is { ThirdPerson: false };
		var sound = kind switch
		{
			MeleeImpactKind.Flesh => localFirstPerson ? MeleeSettings.SoundImpactFlesh1P : MeleeSettings.SoundImpactFlesh3P,
			MeleeImpactKind.Android => localFirstPerson ? MeleeSettings.SoundImpactAndroid1P : MeleeSettings.SoundImpactAndroid3P,
			MeleeImpactKind.Metal => MeleeSettings.SoundImpactMetal1P,
			MeleeImpactKind.Concrete => MeleeSettings.SoundImpactConcrete1P,
			_ => localFirstPerson ? MeleeSettings.SoundImpactDefault1P : MeleeSettings.SoundImpactDefault3P
		};
		Titanfall2SoundEventEmitter.OneShot( Scene, MountIdent, sound, position, firstPerson: localFirstPerson );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayOrdnanceSoundRpc( bool thrown )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var localFirstPerson = !IsProxy && _controller is { ThirdPerson: false };
		var profile = _ordnanceConfig.Profile;
		var sound = thrown
			? (localFirstPerson ? profile.ThrowSound1P : profile.ThrowSound3P)
			: (localFirstPerson ? profile.PrepSound1P : profile.PrepSound3P);
		if ( string.IsNullOrWhiteSpace( sound ) ) return;
		Titanfall2SoundEventEmitter.OneShot( Scene, MountIdent, sound, WorldPosition + Vector3.Up * 48f, firstPerson: localFirstPerson );
	}
}
