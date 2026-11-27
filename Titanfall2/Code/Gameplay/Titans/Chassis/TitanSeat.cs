/// <summary>Pure deterministic rules for piloted Titan locomotion, turning, wall sliding, and foot support.</summary>
internal static class Titanfall2TitanDriveRules
{
	internal const float StepHeight = 28f;
	internal const float WalkableNormalZ = 0.58f;
	internal const float Gravity = 1000f;
	internal const float MaxFallSpeed = 1200f;
	internal const float MaxTurnRateDegrees = 280f;

	internal static float ChassisDashSpeed( Titanfall2TitanKit.Chassis kit ) => kit switch
	{
		Titanfall2TitanKit.Chassis.Ronin or Titanfall2TitanKit.Chassis.Northstar => 960f,
		Titanfall2TitanKit.Chassis.Legion or Titanfall2TitanKit.Chassis.Scorch => 700f,
		_ => 820f
	};

	internal static float ComputeTurnStep( float eyeYaw, float deltaTime )
	{
		if ( !float.IsFinite( eyeYaw ) || !float.IsFinite( deltaTime ) || deltaTime <= 0 ) return 0;
		var desired = eyeYaw * Math.Clamp( deltaTime * 16f, 0f, 1f );
		var minStep = MathF.Sign( eyeYaw ) * MathF.Min( MathF.Abs( eyeYaw ), 140f * deltaTime );
		var step = MathF.Abs( desired ) > MathF.Abs( minStep ) ? desired : minStep;
		var maxStep = MaxTurnRateDegrees * deltaTime;
		return Math.Clamp( step, -maxStep, maxStep );
	}

	internal static Vector3 SlideDisplacement( Vector3 remaining, Vector3 hitNormal )
	{
		var flat = remaining.WithZ( 0 );
		var normal = hitNormal.WithZ( 0 );
		if ( normal.LengthSquared < 0.0001f ) return Vector3.Zero;
		normal = normal.Normal;
		var into = Vector3.Dot( flat, normal );
		if ( into >= 0 ) return flat;
		var slid = flat - normal * into;
		return Vector3.Dot( slid, flat ) > 0 ? slid : Vector3.Zero;
	}

	internal static bool HasMajorityFootSupport( bool centerSupported, int outerSupportedCount ) =>
		centerSupported || outerSupportedCount >= 2;
}

/// <summary>Frame input snapshot captured by Titanfall2PilotInputInterceptor in Stage.StartUpdate while piloting a Titan.</summary>
internal readonly record struct Titanfall2TitanCockpitInput(
	bool OffensePressed,
	bool DefensePressed,
	bool DefenseDown,
	bool UtilityPressed,
	bool SmokePressed,
	bool MeleePressed,
	bool CorePressed,
	bool AdsDown );

/// <summary>Collision-checked embarkation and local/listen-host Titan controls.</summary>
[Title( "Titanfall 2 Titan Cockpit" ), Group( "Titanfall 2" )]
public sealed class Titanfall2TitanSeat : BaseChair, Component.IPressable, Sandbox.Movement.ISitTarget
{
	const Titanfall2TraceMask DrivingMask = (Titanfall2TraceMask)((uint)Titanfall2TraceMask.NpcSolid | (uint)Titanfall2Contents.TitanClip);
	const Titanfall2TraceMask EmbarkMask = (Titanfall2TraceMask)((uint)Titanfall2TraceMask.PlayerSolid | (uint)Titanfall2TraceMask.ShotHull);
	[Property] public Titanfall2Npc Actor { get; set; }
	[Property] public float DriveSpeed { get; set; } = 180;
	[Property] public float EmbarkSeconds { get; set; } = 0.8f;
	[Property] public float EmbarkRange { get; set; } = 200;
	[Property] public bool AlignWithPilot { get; set; } = true;
	[Property] public string OffenseAction { get; set; } = "spray";
	[Property] public string DefenseAction { get; set; } = "duck";
	[Property] public string DefenseAltAction { get; set; } = "toolmenu";
	[Property] public string UtilityAction { get; set; } = "drop";
	[Property] public string SmokeAction { get; set; } = "inspectmenu";
	[Property] public string MeleeAction { get; set; } = "flashlight";
	[Property] public string CoreAction { get; set; } = "noclip";
	[Property] public string EjectAction { get; set; } = "jump";
	[Property] public string IonLaserAction { get; set; } = "spray";
	[Property] public bool ShowHud { get; set; } = true;
	[Property] public bool ShowCockpit { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public float EmbarkRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public int SeatSerial { get; private set; }
	public bool IsDriving { get; private set; }
	public Vector3 DriveVelocity { get; private set; }
	public float DashRemainingVisual => _dashTime > 0 ? Math.Clamp( _dashTime / 0.24f, 0f, 1f ) : 0f;
	public Vector3 DashDirection => _dashDirection;
	public bool IsEmbarking => BoardingPilot.IsValid();
	public string StatusMessage => _messageTime > 0 ? _message : null;
	internal PlayerController LocalPilot => GetOccupant() is { IsProxy: false } pilot ? pilot
		: BoardingPilot is { IsProxy: false } boarding ? boarding : null;
	BaseInventoryComponent _pausedInventory;
	BaseCombatWeapon _pausedWeapon;
	bool _previousPumping;
	bool _previousViewEnabled;
	bool _capturedView;
	[Sync( SyncFlags.FromHost )] PlayerController BoardingPilot { get; set; }
	PlayerController _seatedPilot;
	GameObject _hud;
	GameObject _cockpitView;
	PlayerController _viewPilot;
	bool _previousThirdPerson;
	TimeUntil _dashTime;
	bool _phaseDashActive;
	float _hoverOriginZ;
	TimeUntil _entryGrace;
	TimeUntil _reentryDelay;
	TimeUntil _messageTime;
	string _message;
	Vector3 _dashDirection;
	float _verticalVelocity;
	bool _pausedAiNavigation;
	bool _hasInterceptedInput;
	bool _cockpitInputYielded;
	Titanfall2TitanCockpitInput _interceptedInput;

	/// <summary>Only players currently in Pilot Combat State (Jump Kit equipped) may embark a Titan.</summary>
	public static bool HasPilotCombatState( PlayerController player ) =>
		player.IsValid() && player.GameObject.GetComponent<Titanfall2PilotParkour>() is { IsValid: true, Equipped: true };

	public override bool CanEnter( PlayerController player ) => Active && Actor is { Active: true, IsTitan: true, TitanCanAct: true }
		&& player.IsValid() && player.Active && player.Scene == Scene && !player.IsProxy && Networking.IsHost && !IsProxy && !Actor.IsProxy
		&& HasPilotCombatState( player )
		&& SeatPosition.IsValid() && !BoardingPilot.IsValid() && _reentryDelay <= 0
		&& player.GetComponentInParent<BaseChair>() is null && base.CanEnter( player )
		&& !Scene.GetAllComponents<Titanfall2TitanSeat>().Any( seat => seat != this && seat.BoardingPilot == player )
		&& CanReachCockpit( player );

	Component.IPressable.Tooltip? Component.IPressable.GetTooltip( Component.IPressable.Event e ) => Actor is not { TitanCanAct: true } ? null
		: new Component.IPressable.Tooltip( Actor.DisplayName ?? "泰坦", "airline_seat_recline_normal",
			IsOccupied ? "座舱已占用" : IsEmbarking ? "正在建立神经连接"
			: e.Source is PlayerController pilot && !HasPilotCombatState( pilot ) ? "需先穿戴铁驭跃升背包 (进入铁驭战斗态)"
			: "登乘泰坦" );
	bool Component.IPressable.CanPress( Component.IPressable.Event e ) => e.Source is PlayerController pilot && CanEnter( pilot );
	bool Component.IPressable.Press( Component.IPressable.Event e ) => e.Source is PlayerController pilot && TryEmbark( pilot );

	/// <summary>Starts a cancellable boarding transition; remote-client driving is not enabled.</summary>
	public bool TryEmbark( PlayerController pilot )
	{
		if ( !CanEnter( pilot ) ) return false;
		BoardingPilot = pilot;
		SeatSerial++;
		EmbarkRemaining = Math.Clamp( EmbarkSeconds, 0.1f, 3 );
		PauseInventory( pilot );
		SetMessage( "正在登乘 · 建立神经连接", 3 );
		return true;
	}

	bool CanReachCockpit( PlayerController pilot )
	{
		if ( pilot.WorldPosition.Distance( WorldPosition ) > Math.Clamp( EmbarkRange, 96, 400 ) ) return false;
		var trace = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( pilot.EyeTransform.Position,
			WorldPosition + Vector3.Up * (Actor.BodyHeight * 0.5f) ).IgnoreGameObjectHierarchy( pilot.GameObject )
			.WithoutTags( "trigger", "projectile" ), EmbarkMask );
		return !trace.StartedSolid && (!trace.Hit || trace.GameObject == GameObject
			|| trace.GameObject?.GetComponentInParent<Titanfall2Npc>() == Actor);
	}

	void TickEmbark()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		if ( !BoardingPilot.IsValid() )
		{
			if ( EmbarkRemaining > 0 ) { SeatSerial++; RestoreInventory(); }
			BoardingPilot = null;
			EmbarkRemaining = 0;
			return;
		}
		if ( Actor is not { TitanCanAct: true } || !BoardingPilot.Active || BoardingPilot.IsProxy || IsOccupied || !CanReachCockpit( BoardingPilot ) )
		{
			BoardingPilot = null;
			SeatSerial++;
			EmbarkRemaining = 0;
			RestoreInventory();
			SetMessage( "登乘已取消", 2 );
			return;
		}
		EmbarkRemaining = MathF.Max( 0, EmbarkRemaining - Time.Delta );
		if ( EmbarkRemaining > 0 ) return;
		var pilot = BoardingPilot;
		BoardingPilot = null;
		SeatSerial++;
		_seatedPilot = pilot;
		PauseAiNavigation();
		SeatPosition.GetOrAddComponent<Titanfall2TitanSeatEscape>().Seat = this;
		// SitMoveMode owns physics transitions. Do not disable a body while Walk still ticks.
		pilot.GameObject.GetOrAddComponent<Sandbox.Movement.SitMoveMode>().Enabled = true;
		pilot.GameObject.SetParent( SeatPosition, false );
		pilot.GameObject.LocalTransform = global::Transform.Zero;
		_entryGrace = 0.6f;
		_verticalVelocity = 0;
		if ( AlignWithPilot ) { Actor.Friendly = true; Actor.Leader = pilot.GameObject; }
		SetMessage( "神经连接完成 · 使用键离舱 · 跳跃键弹射", 4 );
	}

	void PauseAiNavigation()
	{
		if ( !Actor.IsValid() ) return;
		if ( Actor.Movement.IsValid() ) { Actor.Movement.Stop(); Actor.Movement.Enabled = false; }
		if ( Actor.Agent.IsValid() ) { Actor.Agent.Stop(); Actor.Agent.Enabled = false; }
		_pausedAiNavigation = true;
	}

	void RestoreAiNavigation()
	{
		if ( !_pausedAiNavigation ) return;
		_pausedAiNavigation = false;
		if ( !Actor.IsValid() || Actor.Dead ) return;
		if ( Actor.Agent.IsValid() && Actor.NavigationMode == Titanfall2Npc.Navigation.Ground && Actor.MoveSpeed > 0 )
			Actor.Agent.Enabled = true;
		if ( Actor.Movement.IsValid() && Actor.UsesEngineSchedules )
			Actor.Movement.Enabled = true;
	}

	internal void BeginFlightCoreHover()
	{
		_hoverOriginZ = WorldPosition.z;
		_verticalVelocity = 0f;
	}

	internal void ConsumeCockpitInput( in Titanfall2TitanCockpitInput input )
	{
		_interceptedInput = input;
		_hasInterceptedInput = true;
		_cockpitInputYielded = false;
	}

	internal void OnCockpitInputYielded()
	{
		_cockpitInputYielded = true;
		_hasInterceptedInput = false;
		_interceptedInput = default;
		var kit = Actor?.GetComponent<Titanfall2TitanKit>();
		kit?.ClearIonInput();
		kit?.ClearHoldDefenseInput();
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost ) TickEmbark();
		UpdateHud();
		UpdateCockpitView();
		var pilot = GetOccupant();
		if ( !Networking.IsHost || Actor is not { TitanCanAct: true } || !pilot.IsValid() || pilot.IsProxy )
		{
			IsDriving = false;
			_hasInterceptedInput = false;
			_cockpitInputYielded = false;
			if ( !BoardingPilot.IsValid() ) RestoreInventory();
			return;
		}
		_seatedPilot = pilot;
		PauseInventory( pilot );
		if ( pilot.Mode is not Sandbox.Movement.SitMoveMode ) return;
		if ( Actor.IsInTermination )
		{
			IsDriving = false;
			DriveVelocity = Vector3.Zero;
			_hasInterceptedInput = false;
			_cockpitInputYielded = false;
			return;
		}

		var yielded = _cockpitInputYielded;
		var hasInput = _hasInterceptedInput;
		var frameInput = _interceptedInput;
		_hasInterceptedInput = false;
		_cockpitInputYielded = false;
		if ( yielded ) return;

		if ( _entryGrace <= 0 && !string.IsNullOrWhiteSpace( EjectAction ) && Input.Pressed( EjectAction ) )
		{
			Input.Clear( EjectAction );
			TryEjectPilot( pilot );
			return;
		}
		if ( _entryGrace <= 0 && Input.Pressed( "use" ) ) { TryDisembark( pilot ); return; }

		var aim = pilot.EyeTransform.ForwardRay;
		Actor.SetAnimationAimDirection( aim.Forward );
		var core = Actor.GetComponent<Titanfall2TitanCore>();
		core?.SetAim( aim );

		var corePressed = hasInput ? frameInput.CorePressed : (!string.IsNullOrWhiteSpace( CoreAction ) && Input.Pressed( CoreAction ));
		if ( corePressed )
		{
			if ( core?.TryActivate( aim, pilot.GameObject ) != true )
				SetMessage( core is { Supported: true } ? "核心尚未就绪" : "此机体的核心尚未接入", 2 );
			if ( !string.IsNullOrWhiteSpace( CoreAction ) ) Input.Clear( CoreAction );
		}

		if ( Actor.GetComponent<Titanfall2TitanKit>() is { } kit )
		{
			var isIon = kit.Kit == Titanfall2TitanKit.Chassis.Ion;
			var offensePressed = hasInput ? frameInput.OffensePressed : (!string.IsNullOrWhiteSpace( OffenseAction ) && Input.Pressed( OffenseAction ));
			var defensePressed = hasInput ? frameInput.DefensePressed
				: ((!string.IsNullOrWhiteSpace( DefenseAction ) && Input.Pressed( DefenseAction ))
					|| (!string.IsNullOrWhiteSpace( DefenseAltAction ) && Input.Pressed( DefenseAltAction ))
					|| (!isIon && Input.Pressed( "attack2" )));
			var defenseDown = hasInput ? frameInput.DefenseDown
				: ((!string.IsNullOrWhiteSpace( DefenseAction ) && Input.Down( DefenseAction ))
					|| (!string.IsNullOrWhiteSpace( DefenseAltAction ) && Input.Down( DefenseAltAction ))
					|| (!isIon && Input.Down( "attack2" )));
			var utilityPressed = hasInput ? frameInput.UtilityPressed : (!string.IsNullOrWhiteSpace( UtilityAction ) && Input.Pressed( UtilityAction ));
			var smokePressed = hasInput ? frameInput.SmokePressed : (!string.IsNullOrWhiteSpace( SmokeAction ) && Input.Pressed( SmokeAction ));
			var meleePressed = hasInput ? frameInput.MeleePressed : (!string.IsNullOrWhiteSpace( MeleeAction ) && Input.Pressed( MeleeAction ));
			var adsDown = hasInput ? frameInput.AdsDown : Input.Down( "attack2" );

			if ( isIon )
			{
				kit.SetIonInput( pilot, aim, adsDown, defenseDown );
				if ( offensePressed && !kit.TryOffense( aim, pilot.GameObject ) )
					SetMessage( kit.LastOffenseFailure ?? "激光肩炮未就绪", 2 );
			}
			else if ( kit.Kit is Titanfall2TitanKit.Chassis.Scorch or Titanfall2TitanKit.Chassis.Ronin )
			{
				kit.SetHoldDefenseInput( pilot, aim, defenseDown );
				if ( offensePressed && !kit.TryOffense( aim, pilot.GameObject ) )
					SetMessage( kit.LastOffenseFailure ?? "战术技能冷却中", 2 );
			}
			else
			{
				if ( offensePressed && !kit.TryOffense( aim, pilot.GameObject ) )
					SetMessage( kit.LastOffenseFailure ?? "战术技能冷却中", 2 );
				if ( defensePressed && !kit.TryDefense( aim, pilot.GameObject ) )
					SetMessage( "防御技能冷却中", 2 );
			}

			if ( utilityPressed )
			{
				if ( kit.TryUtility( aim, pilot.GameObject ) )
				{
					if ( kit.Kit == Titanfall2TitanKit.Chassis.Ronin )
					{
						var move = Input.AnalogMove;
						_dashDirection = (pilot.EyeTransform.Rotation * (move.LengthSquared > 0.01f ? move : Vector3.Forward)).WithZ( 0 ).Normal;
						_dashTime = 0.28f;
						_phaseDashActive = true;
					}
					else if ( kit.Kit == Titanfall2TitanKit.Chassis.Northstar )
					{
						_hoverOriginZ = WorldPosition.z;
						_verticalVelocity = 0f;
					}
				}
				else
				{
					SetMessage( kit.LastUtilityFailure ?? "辅助技能冷却中", 2 );
				}
			}

			if ( smokePressed && !kit.TryElectricSmoke( pilot.GameObject ) )
				SetMessage( "反铁驭电子烟幕冷却中", 2 );

			if ( meleePressed && !kit.TryTitanMelee( aim, pilot.GameObject ) )
				SetMessage( "泰坦近战冷却中", 1 );

			if ( Input.Pressed( "run" ) && kit.TryDash() )
			{
				var move = Input.AnalogMove;
				_dashDirection = (pilot.EyeTransform.Rotation * (move.LengthSquared > 0.01f ? move : Vector3.Forward)).WithZ( 0 ).Normal;
				_dashTime = 0.24f;
				_phaseDashActive = false;
			}
		}
		if ( Input.Pressed( "reload" ) ) Actor.StartReload();
		if ( Input.Down( "attack1" ) ) Actor.Fire( aim, pilot.GameObject, piloted: true );
	}

	protected override void OnFixedUpdate()
	{
		IsDriving = false;
		DriveVelocity = Vector3.Zero;
		if ( !Networking.IsHost || Actor is not { TitanCanAct: true, IsInTermination: false } || BoardingPilot.IsValid()
			|| GetOccupant() is not { IsProxy: false } pilot || pilot.Mode is not Sandbox.Movement.SitMoveMode ) return;
		PauseAiNavigation();
		var angles = pilot.EyeAngles;
		var turn = Titanfall2TitanDriveRules.ComputeTurnStep( angles.yaw, Time.Delta );
		if ( MathF.Abs( turn ) > 0.001f )
		{
			WorldRotation *= Rotation.FromYaw( turn );
			pilot.EyeAngles = angles.WithYaw( angles.yaw - turn );
		}
		var kit = Actor.GetComponent<Titanfall2TitanKit>();
		if ( _dashTime > 0 )
		{
			var chassis = kit?.Kit ?? Titanfall2TitanKit.Chassis.None;
			var dashSpeed = _phaseDashActive ? 1250f : Titanfall2TitanDriveRules.ChassisDashSpeed( chassis );
			if ( !TryDrive( _dashDirection * (dashSpeed * Time.Delta) ) ) { _dashTime = 0; _phaseDashActive = false; }
			return;
		}
		_phaseDashActive = false;
		var input = Input.AnalogMove;
		var core = Actor.GetComponent<Titanfall2TitanCore>();
		var hovering = kit is { IsHovering: true } || core is { FlightCoreActive: true };
		if ( input.LengthSquared < 0.01f )
		{
			if ( hovering || _verticalVelocity < 0 ) TryDrive( Vector3.Zero );
			return;
		}
		var planar = (pilot.EyeTransform.Rotation * input).WithZ( 0 );
		if ( planar.LengthSquared < 0.0001f )
		{
			if ( hovering ) TryDrive( Vector3.Zero );
			return;
		}
		var speed = DriveSpeed > 1 ? DriveSpeed : MathF.Max( 120f, Actor.MoveSpeed );
		if ( kit is { IsValid: true } ) speed *= kit.CurrentMoveSpeedMultiplier;
		TryDrive( planar.Normal * (MathF.Min( 1, input.Length ) * speed * Time.Delta) );
	}

	internal bool TryDrive( Vector3 displacement )
	{
		if ( !Networking.IsHost || !Active || Actor is not { Active: true, TitanCanAct: true } || !displacement.IsFinite ) return false;
		var dt = MathF.Max( Time.Delta, 0.001f );
		if ( displacement.LengthSquared > 0.001f && Actor.GetComponent<Titanfall2TetherConstraint>() is { HasTethers: true } tether )
			displacement = tether.Constrain( Actor, displacement / dt, dt ) * dt;
		var kit = Actor.GetComponent<Titanfall2TitanKit>();
		var core = Actor.GetComponent<Titanfall2TitanCore>();
		var flightCore = core is { FlightCoreActive: true };
		var hovering = kit is { IsHovering: true } || flightCore;
		var phasing = kit is { IsPhasing: true };
		var horizontal = displacement.WithZ( 0 );
		var hasHorizontal = horizontal.LengthSquared > 0.0004f;
		if ( !hasHorizontal && !hovering && _verticalVelocity >= 0 ) return false;

		var radius = MathF.Max( 4, Actor.BodyRadius - 2 );
		var hull = new BBox( new Vector3( -radius, -radius, 0 ), new Vector3( radius, radius, MathF.Max( 24, Actor.BodyHeight - 2 ) ) );
		var startPosition = WorldPosition;
		var current = startPosition + Vector3.Up * 4f;

		// Recover cleanly if the chassis began slightly embedded in terrain or a curb.
		var startCheck = TraceHull( hull, current, current, DrivingMask, phasing );
		if ( startCheck.StartedSolid )
		{
			var lifted = startPosition + Vector3.Up * Titanfall2TitanDriveRules.StepHeight;
			var liftCheck = TraceHull( hull, lifted, lifted, DrivingMask, phasing );
			if ( liftCheck.StartedSolid ) return false;
			current = lifted;
		}

		var remaining = horizontal;
		for ( var slide = 0; slide < 2 && remaining.LengthSquared > 0.0004f; slide++ )
		{
			var step = TraceHull( hull, current, current + remaining, DrivingMask, phasing );
			if ( step.StartedSolid ) break;
			if ( !step.Hit || step.Fraction >= 0.999f )
			{
				current = step.EndPosition;
				remaining = Vector3.Zero;
				break;
			}

			// Attempt stepping over curbs, stairs, and low rubble before sliding along the obstacle.
			if ( TryStepUp( hull, current, remaining, step.EndPosition, out var steppedPosition ) )
			{
				current = steppedPosition;
				remaining = Vector3.Zero;
				break;
			}

			current = step.EndPosition;
			remaining = Titanfall2TitanDriveRules.SlideDisplacement( remaining * (1f - step.Fraction), step.Normal );
		}

		if ( hovering )
		{
			var targetZ = _hoverOriginZ + (flightCore ? 220f : 160f);
			var desiredStep = Math.Clamp( (targetZ - current.z) * 3.8f, -120f, 280f ) * dt;
			var lift = TraceHull( hull, current, current + Vector3.Up * desiredStep, DrivingMask, phasing );
			WorldPosition = lift.StartedSolid ? current : lift.EndPosition;
			_verticalVelocity = 0;
		}
		else
		{
			var snapDrop = Titanfall2TitanDriveRules.StepHeight + 36f;
			var ground = TraceHull( hull, current, current - Vector3.Up * snapDrop, DrivingMask );
			if ( ground.Hit && !ground.StartedSolid && ground.Normal.z >= Titanfall2TitanDriveRules.WalkableNormalZ
				&& HasFootSupport( ground.EndPosition, radius, DrivingMask ) )
			{
				WorldPosition = ground.EndPosition;
				_verticalVelocity = 0;
			}
			else
			{
				// Allow smooth drops off ledges/ramps instead of locking the Titan in place.
				_verticalVelocity = MathF.Max( -Titanfall2TitanDriveRules.MaxFallSpeed,
					_verticalVelocity - Titanfall2TitanDriveRules.Gravity * dt );
				var fallDistance = MathF.Max( 4f, -_verticalVelocity * dt );
				var fall = TraceHull( hull, current, current - Vector3.Up * fallDistance, DrivingMask );
				if ( fall.StartedSolid ) return false;
				WorldPosition = fall.EndPosition;
				if ( fall.Hit && fall.Normal.z >= Titanfall2TitanDriveRules.WalkableNormalZ ) _verticalVelocity = 0;
			}
		}

		var moved = WorldPosition - startPosition;
		if ( moved.LengthSquared <= 0.0004f ) return false;
		DriveVelocity = moved / dt;
		IsDriving = moved.WithZ( 0 ).LengthSquared > 0.0004f;
		return true;
	}

	bool TryStepUp( BBox hull, Vector3 current, Vector3 remaining, Vector3 unsteppedEnd, out Vector3 result )
	{
		result = default;
		var up = TraceHull( hull, current, current + Vector3.Up * Titanfall2TitanDriveRules.StepHeight, DrivingMask );
		if ( up.StartedSolid || (up.EndPosition.z - current.z) < 4f ) return false;
		var forward = TraceHull( hull, up.EndPosition, up.EndPosition + remaining, DrivingMask );
		if ( forward.StartedSolid ) return false;
		var unsteppedDist = (unsteppedEnd - current).WithZ( 0 ).LengthSquared;
		var steppedDist = (forward.EndPosition - up.EndPosition).WithZ( 0 ).LengthSquared;
		if ( steppedDist <= unsteppedDist + 0.25f ) return false;
		var down = TraceHull( hull, forward.EndPosition, forward.EndPosition - Vector3.Up * (Titanfall2TitanDriveRules.StepHeight + 8f), DrivingMask );
		if ( down.StartedSolid || !down.Hit || down.Normal.z < Titanfall2TitanDriveRules.WalkableNormalZ ) return false;
		result = down.EndPosition + Vector3.Up * 2f;
		return true;
	}

	SceneTraceResult TraceHull( BBox hull, Vector3 start, Vector3 end, Titanfall2TraceMask mask, bool ignoreActors = false )
	{
		var trace = Scene.Trace.Box( hull, start, end ).IgnoreGameObjectHierarchy( GameObject );
		trace = ignoreActors
			? trace.WithoutTags( "trigger", "projectile", "tether-trap", "npc", "player" )
			: trace.WithoutTags( "trigger", "projectile", "tether-trap" );
		return Titanfall2CollisionQueries.Run( trace, mask );
	}

	bool HasFootSupport( Vector3 position, float radius, Titanfall2TraceMask mask )
	{
		var edge = radius * 0.55f;
		var centerHit = ProbeFoot( position, position.z, mask );
		if ( centerHit ) return true;
		var outerHits = 0;
		Vector3[] outer = [new( edge, edge, 0 ), new( edge, -edge, 0 ), new( -edge, edge, 0 ), new( -edge, -edge, 0 )];
		foreach ( var offset in outer )
		{
			if ( ProbeFoot( position + offset, position.z, mask ) ) outerHits++;
		}
		return Titanfall2TitanDriveRules.HasMajorityFootSupport( centerHit, outerHits );
	}

	bool ProbeFoot( Vector3 foot, float referenceZ, Titanfall2TraceMask mask )
	{
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( foot + Vector3.Up * (Titanfall2TitanDriveRules.StepHeight + 12f),
			foot - Vector3.Up * (Titanfall2TitanDriveRules.StepHeight + 16f) )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile", "tether-trap" ), mask );
		return !hit.StartedSolid && hit.Hit && hit.Normal.z >= Titanfall2TitanDriveRules.WalkableNormalZ
			&& MathF.Abs( hit.EndPosition.z - referenceZ ) <= Titanfall2TitanDriveRules.StepHeight + 16f;
	}

	/// <summary>Finds a grounded exit reachable without crossing world or imported player clips.</summary>
	internal bool TryFindExit( PlayerController pilot, out Vector3 position, bool emergency = false )
	{
		position = default;
		if ( !pilot.IsValid() || !Actor.IsValid() ) return false;
		var radius = Math.Clamp( pilot.BodyRadius, 8, 48 );
		var hull = new BBox( new Vector3( -radius, -radius, 1 ), new Vector3( radius, radius, Math.Clamp( pilot.BodyHeight, 32, 128 ) ) );
		var facing = pilot.EyeTransform.Forward.WithZ( 0 ).Normal;
		if ( facing.LengthSquared < 0.1f ) facing = WorldRotation.Forward;
		var origin = WorldPosition + Vector3.Up * 2;
		var candidates = Enumerable.Range( 0, emergency ? 24 : 12 ).Select( index =>
		{
			var direction = Rotation.FromYaw( index * 30 ) * facing;
			return (Direction: direction, Point: WorldPosition + direction * (Actor.BodyRadius + radius + 26 + (index / 12) * 72));
		} ).OrderByDescending( candidate => Vector3.Dot( candidate.Direction, facing ) );
		foreach ( var candidate in candidates )
		{
			var lateral = TraceHull( hull, origin, candidate.Point + Vector3.Up * 2, Titanfall2TraceMask.PlayerSolid );
			if ( lateral.StartedSolid || lateral.Hit ) continue;
			var floor = TraceHull( hull, lateral.EndPosition + Vector3.Up * 20, lateral.EndPosition - Vector3.Up * 64, Titanfall2TraceMask.PlayerSolid );
			if ( floor.StartedSolid || !floor.Hit || floor.Normal.z < 0.65f ) continue;
			var fit = TraceHull( hull, floor.EndPosition + Vector3.Up, floor.EndPosition + Vector3.Up * 1.1f, Titanfall2TraceMask.PlayerSolid );
			if ( fit.Hit || fit.StartedSolid || !HasFootSupport( floor.EndPosition + Vector3.Up, radius, Titanfall2TraceMask.PlayerSolid ) ) continue;
			position = floor.EndPosition + Vector3.Up;
			return true;
		}
		if ( !emergency ) return false;
		var above = WorldPosition + Vector3.Up * (Actor.BodyHeight + 12);
		var escape = TraceHull( hull, pilot.WorldPosition, above, Titanfall2TraceMask.PlayerSolid );
		if ( escape.StartedSolid || escape.Hit ) return false;
		position = above;
		return true;
	}

	public bool TryDisembark( PlayerController pilot, bool emergency = false )
	{
		if ( !Networking.IsHost || IsProxy || !pilot.IsValid() || !ContainsPilot( pilot ) || (!emergency && _entryGrace > 0) ) return false;
		if ( !TryFindExit( pilot, out var exit, emergency ) )
		{
			if ( !emergency ) { SetMessage( "出口受阻 · 请移动泰坦后重试", 3 ); return false; }
			// Destruction must not delete a child pilot, even inside a sealed room.
			exit = pilot.WorldPosition;
		}
		var look = pilot.EyeTransform.Rotation.Angles();
		pilot.GameObject.SetParent( null, true );
		pilot.WorldPosition = exit;
		pilot.EyeAngles = look;
		RestoreInventory();
		_seatedPilot = null;
		SeatSerial++;
		_reentryDelay = 0.8f;
		IsDriving = false;
		DriveVelocity = Vector3.Zero;
		_verticalVelocity = 0;
		RestoreAiNavigation();
		return true;
	}

	/// <summary>Host-authoritative active ejection. Ordinary disembarkation does not destroy the chassis.</summary>
	public bool TryEjectPilot( PlayerController pilot )
	{
		if ( !Networking.IsHost || IsProxy || !Active || Actor is not { Active: true, IsTitan: true, TitanCanAct: true }
			|| !pilot.IsValid() || pilot.IsProxy || !pilot.Active || !ContainsPilot( pilot ) || GetOccupant() != pilot ) return false;
		var radius = Math.Clamp( pilot.BodyRadius, 8, 48 );
		var hull = new BBox( new Vector3( -radius, -radius, 1 ), new Vector3( radius, radius, Math.Clamp( pilot.BodyHeight, 32, 128 ) ) );
		var above = WorldPosition + Vector3.Up * (Actor.BodyHeight + 12);
		var clearance = TraceHull( hull, pilot.WorldPosition, above, Titanfall2TraceMask.PlayerSolid );
		Vector3 exit;
		if ( !clearance.StartedSolid && !clearance.Hit ) exit = above;
		else if ( !TryFindExit( pilot, out exit, emergency: true ) )
		{
			SetMessage( "弹射出口受阻 · 请移动泰坦后重试", 3 );
			return false;
		}
		if ( !Actor.BeginTitanEjection() ) return false;
		var look = pilot.EyeTransform.Rotation.Angles();
		pilot.GameObject.SetParent( null, true );
		pilot.WorldPosition = exit;
		pilot.EyeAngles = look;
		RestoreInventory();
		_seatedPilot = null;
		SeatSerial++;
		IsDriving = false;
		DriveVelocity = Vector3.Zero;
		_verticalVelocity = 0;
		RestoreAiNavigation();
		var speed = 1500f + Random.Shared.NextSingle() * 200f;
		if ( Actor.Role == Titanfall2Npc.FrontierRole.NukeTitan ) speed += 400;
		var gravityScale = pilot.Body.IsValid() && float.IsFinite( pilot.Body.GravityScale ) ? MathF.Max( 0, pilot.Body.GravityScale ) : 1;
		// sh_titan.gnut: 270 degrees pitch, then -5 degrees, speed * sqrt(gravityscale).
		var velocity = (Vector3.Up * MathF.Cos( 5 * MathF.PI / 180 ) - WorldRotation.Forward * MathF.Sin( 5 * MathF.PI / 180 ))
			* speed * MathF.Sqrt( gravityScale );
		pilot.GameObject.GetOrAddComponent<Titanfall2EjectionImpulse>().Launch( pilot, velocity );
		SetMessage( "驾驶员弹射 · 泰坦自毁", 3 );
		return true;
	}

	bool ContainsPilot( PlayerController pilot )
	{
		for ( var parent = pilot.GameObject.Parent; parent.IsValid(); parent = parent.Parent )
			if ( parent == SeatPosition || parent == GameObject ) return true;
		return false;
	}

	void Sandbox.Movement.ISitTarget.AskToLeave( PlayerController player ) { if ( _entryGrace <= 0 ) TryDisembark( player ); }
	public override bool CanLeave( PlayerController player ) => TryFindExit( player, out _ );
	public new void Eject( PlayerController player ) => TryDisembark( player, emergency: true );

	void PauseInventory( PlayerController pilot )
	{
		var inventory = pilot.GetComponent<BaseInventoryComponent>();
		if ( inventory == _pausedInventory && pilot == _viewPilot ) return;
		RestoreInventory();
		_viewPilot = pilot;
		_previousThirdPerson = pilot.ThirdPerson;
		pilot.ThirdPerson = false;
		_pausedInventory = inventory;
		if ( !inventory.IsValid() ) return;
		_previousPumping = inventory.ManualPumping;
		inventory.ManualPumping = true;
		_pausedWeapon = inventory.ActiveItem as BaseCombatWeapon;
		if ( _pausedWeapon?.ViewModel.IsValid() == true ) { _capturedView = true; _previousViewEnabled = _pausedWeapon.ViewModel.Enabled; _pausedWeapon.ViewModel.Enabled = false; }
		pilot.GetComponentInChildren<Titanfall2WeaponSoundEmitter>()?.StopFiring();
	}
	void RestoreInventory()
	{
		var kit = Actor?.GetComponent<Titanfall2TitanKit>();
		kit?.ClearIonInput();
		kit?.ClearHoldDefenseInput();
		_dashTime = 0;
		if ( _viewPilot.IsValid() ) _viewPilot.ThirdPerson = _previousThirdPerson;
		_viewPilot = null;
		if ( _pausedInventory.IsValid() ) _pausedInventory.ManualPumping = _previousPumping;
		if ( _capturedView && _pausedWeapon?.ViewModel.IsValid() == true ) _pausedWeapon.ViewModel.Enabled = _previousViewEnabled;
		_capturedView = false;
		_pausedInventory = null;
		_pausedWeapon = null;
	}

	void SetMessage( string message, float seconds ) { _message = message; _messageTime = seconds; }
	void UpdateHud()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		if ( !ShowHud || !LocalPilot.IsValid() ) { if ( _hud.IsValid() ) _hud.Destroy(); return; }
		if ( _hud.IsValid() ) return;
		_hud = new GameObject( Scene, false, "Titan Cockpit HUD" );
		_hud.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_hud.AddComponent<ScreenPanel>().ZIndex = 120;
		_hud.AddComponent<Titanfall2TitanCockpitHud>().Seat = this;
		_hud.Enabled = true;
	}
	void UpdateCockpitView()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		if ( !ShowCockpit || !IsOccupied || !LocalPilot.IsValid() || Actor is not { Dead: false } )
		{
			if ( _cockpitView.IsValid() ) _cockpitView.Destroy();
			return;
		}
		if ( _cockpitView.IsValid() ) return;
		_cockpitView = new GameObject( Scene, false, "Titan Cockpit View" );
		_cockpitView.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_cockpitView.AddComponent<Titanfall2TitanCockpitView>().Seat = this;
		_cockpitView.Enabled = true;
	}
	internal void ReleaseSeat()
	{
		// Default component queries omit disabled children. Keep the actual pilot
		// so disabling a Titan root cannot hide the passenger before we unparent it.
		var pilot = _seatedPilot.IsValid() ? _seatedPilot : GetComponentInChildren<PlayerController>( true );
		if ( Networking.IsHost && pilot.IsValid() ) TryDisembark( pilot, emergency: true );
		_seatedPilot = null;
		if ( Networking.IsHost && !IsProxy )
		{
			if ( BoardingPilot.IsValid() || EmbarkRemaining > 0 ) SeatSerial++;
			BoardingPilot = null;
			EmbarkRemaining = 0;
		}
		RestoreInventory();
		IsDriving = false;
		DriveVelocity = Vector3.Zero;
		_verticalVelocity = 0;
		RestoreAiNavigation();
		if ( _hud.IsValid() ) _hud.Destroy();
		if ( _cockpitView.IsValid() ) _cockpitView.Destroy();
	}
	protected override void OnEnabled() => Scene?.GetSystem<Titanfall2PilotInputInterceptor>()?.RegisterSeat( this );
	protected override void OnDisabled()
	{
		Scene?.GetSystem<Titanfall2PilotInputInterceptor>()?.UnregisterSeat( this );
		ReleaseSeat();
	}
	protected override void OnDestroy()
	{
		Scene?.GetSystem<Titanfall2PilotInputInterceptor>()?.UnregisterSeat( this );
		ReleaseSeat();
	}
	public override void OnDamage( in DamageInfo damage ) { }
}

/// <summary>Detach before the engine starts recursively destroying the cockpit's children.</summary>
[Hide]
public sealed class Titanfall2TitanSeatEscape : Component
{
	[Property] public Titanfall2TitanSeat Seat { get; set; }
	public override void OnParentDestroy() { if ( Seat.IsValid() ) Seat.ReleaseSeat(); }
}
