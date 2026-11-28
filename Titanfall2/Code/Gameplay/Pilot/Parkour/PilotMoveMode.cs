using Sandbox.Movement;

/// <summary>
/// Active Sandbox MoveMode implementing Titanfall 2 Pilot parkour via composed state controllers:
/// Wallrun, Wallhang, Mantle, Slide, and Third-Person Citizen Pose.
/// Yields cleanly to SitMoveMode (Titans/chairs), NoclipMoveMode, and swimming.
/// </summary>
[Title( "Titanfall 2 Pilot MoveMode" ), Group( "Titanfall 2" )]
public sealed class Titanfall2PilotMoveMode : MoveMode
{
	const int ActivePriority = 40;
	// Native stance defaults; retail Pilot stand/crouch/sprint do not override them.
	const float GroundAccel = 2500f;
	const float GroundDecel = 1500f;
	const float JumpBufferSeconds = 0.13f;
	const float DoubleJumpCooldownSeconds = 0.14f;

	[Property] public Titanfall2PilotParkour Parkour { get; set; }

	internal Titanfall2PilotSettings Settings => Parkour.IsValid() ? Parkour.Settings : Titanfall2PilotSettings.RetailDefault;
	// PlayerController.BodyRadius is the full box width / capsule diameter; half-width is BodyRadius * 0.5f.
	internal float BodyHalfWidth => Math.Clamp( Controller.BodyRadius * 0.5f, 6f, 24f );

	// Composed movement state controllers.
	internal readonly Titanfall2PilotWallrunController Wallrun = new();
	internal readonly Titanfall2PilotWallhangController Wallhang = new();
	internal readonly Titanfall2PilotMantleController Mantle = new();
	internal readonly Titanfall2PilotSlideController Slide = new();
	readonly Titanfall2PilotThirdPersonPoseController _thirdPersonPose = new();

	// Buffered frame-edge inputs so physics ticks never miss a tap.
	TimeSince _sinceJumpPressed = 10f;
	TimeSince _sinceDuckPressed = 10f;
	bool _jumpBuffered;
	bool _duckBuffered;
	bool _wasGrounded;
	float _peakFallSpeed;

	// Jump & Double Jump coordination with PlayerController.InputJump.
	int _doubleJumpsRemaining = 1;
	TimeSince _sinceGrounded = 10f;
	bool _canCoyoteJump;
	bool _engineGroundJumpFired;
	bool _engineJumpWasSliding;
	TimeUntil _doubleJumpCooldownUntil;

	// Shared wall cooldown / same-wall memory across Wallrun & Wallhang.
	TimeUntil _wallRepelUntil;
	bool _hasPreviousWall;
	Vector3 _prevWallNormal;
	Vector3 _prevWallPos;

	// Cached wish input for current fixed step.
	Vector3 _rawInput;
	Vector3 _wishDir;
	bool _wantsSprint;

	internal Vector3 RawInput => _rawInput;
	internal Vector3 WishDir => _wishDir;
	internal bool HasJumpBuffered => _jumpBuffered;

	public override int Score( PlayerController controller )
	{
		if ( !Enabled || !controller.IsValid() ) return -100;
		Parkour ??= GetComponent<Titanfall2PilotParkour>();
		if ( Parkour is not { Equipped: true } ) return -100;
		if ( controller.IsSwimming ) return -100;
		if ( GetComponentInParent<ISitTarget>( false ) is not null ) return -100;
		return ActivePriority;
	}

	bool IsGrapplingActive => Parkour.IsValid() && Parkour.Tactical is { IsValid: true, IsGrappling: true };
	internal bool WantsWallhang => Controller.IsValid() && Controller.UseInputControls && Input.Down( "attack2" )
		&& !(Parkour.IsValid() && Parkour.YieldedToSandbox);
	public override bool AllowGrounding => !Wallrun.IsActive && !Wallhang.IsActive && !Mantle.IsActive && !IsGrapplingActive;
	public override bool AllowFalling => !Wallrun.IsActive && !Wallhang.IsActive && !Mantle.IsActive;
	protected override bool SmoothInput => false;

	/// <summary>Required by PlayerController.CategorizeGround; base MoveMode returns false for every surface.</summary>
	public override bool IsStandableSurface( in SceneTraceResult result ) =>
		result.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ;

	public override void OnModeBegin()
	{
		_doubleJumpsRemaining = 1;
		_wasGrounded = Controller?.IsOnGround ?? false;
		_canCoyoteJump = _wasGrounded;
		_engineGroundJumpFired = false;
		_engineJumpWasSliding = false;
		_rawInput = Vector3.Zero;
		Wallrun.Reset();
		Wallhang.Reset();
		Mantle.Reset();
		Slide.Reset();
		_thirdPersonPose.Reset();
		SyncParkourState();
	}

	public override void OnModeEnd( MoveMode next )
	{
		Wallrun.End( this );
		Wallhang.End( this, playDetachSound: false );
		Slide.End( this, playStopSound: false );
		Mantle.Reset();
		_engineGroundJumpFired = false;
		_thirdPersonPose.Reset();
		Parkour?.ResetThirdPersonBodyVisual();
		SyncParkourState();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy || !Controller.IsValid() || Controller.Mode != this || !Controller.UseInputControls ) return;
		if ( Input.Pressed( "jump" ) )
		{
			_jumpBuffered = true;
			_sinceJumpPressed = 0f;
		}
		if ( Input.Pressed( Controller.DuckButton ) )
		{
			_duckBuffered = true;
			_sinceDuckPressed = 0f;
		}
	}

	public override void UpdateRigidBody( Rigidbody body )
	{
		if ( !body.IsValid() ) return;
		var dynamicSupport = Controller.IsOnGround && Controller.GroundIsDynamic;
		// Apply retail 600 u/s^2 gravity explicitly in AddVelocity for exact TF2 jump arcs,
		// wallrun gravity ramping, and zero-gravity ledge mantles; keep native gravity on dynamic bodies.
		body.Gravity = dynamicSupport;
		body.LinearDamping = 0f;
		body.AngularDamping = 1f;
		if ( Controller.FeetCollider.IsValid() && !dynamicSupport )
			Controller.FeetCollider.Friction = 0f;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		var settings = Settings;
		var flatInput = input.WithZ( 0 );
		if ( flatInput.LengthSquared > 1f ) flatInput = flatInput.Normal;
		_rawInput = flatInput;

		var eyeFlat = Rotation.FromYaw( eyes.Yaw() );
		var worldWish = (eyeFlat * flatInput).WithZ( 0 );
		_wishDir = worldWish;

		var duckHeld = Controller.UseInputControls && Input.Down( Controller.DuckButton );
		var runHeld = Controller.UseInputControls && Input.Down( Controller.AltMoveButton );
		if ( Controller.RunByDefault ) runHeld = !runHeld;
		var forwardDot = _wishDir.LengthSquared > 0.01f ? Vector3.Dot( _wishDir, eyeFlat.Forward ) : 0f;
		_wantsSprint = runHeld && !duckHeld && forwardDot > 0.5f && !Slide.IsActive && !Mantle.IsActive;

		var tactical = Parkour.IsValid() ? Parkour.Tactical : GetComponent<Titanfall2PilotTactical>();
		var emp = GetComponent<Titanfall2EmpStatus>();
		var empScale = emp is { IsValid: true } ? emp.MoveSpeedScale : 1f;
		var speedScale = (tactical is { IsValid: true } ? tactical.SpeedMultiplier : 1f) * empScale;
		var wallScale = (tactical is { IsValid: true } ? tactical.WallrunSpeedMultiplier : 1f) * empScale;

		var targetSpeed = Wallrun.IsActive ? settings.WallrunMaxSpeedHorizontal * wallScale
			: Slide.IsActive ? MathF.Max( settings.SlideStopSpeed * speedScale, Controller.Velocity.WithZ( 0 ).Length )
			: Controller.IsDucking ? settings.CrouchSpeed * speedScale
			: _wantsSprint ? settings.SprintSpeed * speedScale
			: settings.WalkSpeed * speedScale;

		return worldWish * targetSpeed;
	}

	/// <summary>
	/// PlayerController runs AddVelocity() -> BeginStep() -> PrePhysicsStep() -> physics -> PostPhysicsStep().
	/// Update state transitions at the start of AddVelocity so velocity integration always uses current-tick state.
	/// </summary>
	public override void AddVelocity()
	{
		if ( !Controller.IsValid() || !Controller.Body.IsValid() ) return;
		var body = Controller.Body;
		var settings = Settings;
		var dt = Math.Clamp( Time.Delta, 0.0001f, 0.05f );

		if ( _engineGroundJumpFired )
		{
			_engineGroundJumpFired = false;
			var jumpHeight = _engineJumpWasSliding ? settings.SlideJumpHeight : settings.JumpHeight;
			var jumpZ = Titanfall2PilotParkourRules.JumpSpeedForHeight( jumpHeight, settings.Gravity );
			body.Velocity = Titanfall2PilotParkourRules.ClampHorizontal( body.Velocity.WithZ( 0 ), Titanfall2PilotParkourRules.MaxHorizontalSpeed ).WithZ( jumpZ );
			Controller.PreventGrounding( 0.15f );
			Parkour?.PlayMovementSound( "human_jump_default" );
			SyncParkourState();
			return;
		}

		UpdateParkourState( dt );

		// Active Pilot Grapple drives sling/reel velocity; Jump or Crouch detaches with vertical pop.
		var tactical = Parkour.IsValid() ? Parkour.Tactical : GetComponent<Titanfall2PilotTactical>();
		if ( tactical is { IsValid: true, IsGrappling: true } && TryStepGrapple( tactical, body, settings, dt ) )
			return;

		if ( Mantle.IsActive )
		{
			if ( !Mantle.TryVaultCancel( this, body ) )
				body.Velocity = Vector3.Zero;
			return;
		}

		if ( Wallhang.IsActive )
		{
			if ( _jumpBuffered ) { ConsumeJumpBuffer(); ExecuteWallkick(); return; }
			Wallhang.StepVelocity( this, body );
			return;
		}

		if ( Wallrun.IsActive )
		{
			if ( _jumpBuffered ) { ConsumeJumpBuffer(); ExecuteWallkick(); return; }
			Wallrun.StepVelocity( this, body, tactical, dt );
			return;
		}

		var grounded = Controller.IsOnGround;
		var duckHeld = Controller.UseInputControls && Input.Down( Controller.DuckButton );

		if ( _jumpBuffered && TryExecuteBufferedJump( body, settings, grounded ) )
			return;

		if ( Slide.StepVelocity( this, body, grounded, duckHeld, dt ) )
			return;

		if ( grounded )
		{
			StepGroundVelocity( body, dt );
		}
		else
		{
			var v = Titanfall2PilotParkourRules.ApplyAirAcceleration( body.Velocity, _wishDir, settings.AirSpeed, settings.AirAcceleration, dt );
			v.z -= settings.Gravity * dt;
			body.Velocity = v;
		}
	}

	public override void PrePhysicsStep()
	{
		if ( !Controller.IsValid() || Mantle.IsActive || Wallhang.IsActive || IsGrapplingActive ) return;
		if ( Wallrun.IsActive )
		{
			Wallrun.TryGlideObstacle( this );
			return;
		}
		if ( Controller.IsOnGround )
			TrySteppingUp( Settings.StepHeight );
	}

	public override void PostPhysicsStep()
	{
		if ( !Controller.IsValid() || Mantle.IsActive || Wallhang.IsActive ) return;
		if ( !Wallrun.IsActive && !IsGrapplingActive && Controller.IsOnGround )
			StickToGround( Settings.StepHeight );
		SyncParkourState();
	}

	public override void OnJump()
	{
		// InputJump can fire immediately after CategorizeGround, before our landing-state update runs.
		// Replenish the airborne jump here too so a landing-frame hop cannot retain an exhausted charge.
		if ( Controller.IsValid() && Controller.IsOnGround )
		{
			_doubleJumpsRemaining = 1;
			_hasPreviousWall = false;
			_peakFallSpeed = 0f;
		}
		_engineGroundJumpFired = true;
		_engineJumpWasSliding = Slide.IsActive;
		Slide.End( this, playStopSound: false );
		ConsumeJumpBuffer();
		_canCoyoteJump = false;
		_doubleJumpCooldownUntil = DoubleJumpCooldownSeconds;
	}

	protected override void OnRotateRenderBody( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() || !Controller.IsValid() ) return;
		_thirdPersonPose.RotateRenderBody( this, renderer, base.OnRotateRenderBody );
	}

	protected override void OnUpdateAnimatorVelocity( SkinnedModelRenderer renderer )
	{
		base.OnUpdateAnimatorVelocity( renderer );
		if ( !renderer.IsValid() || !Controller.IsValid() ) return;
		Titanfall2PilotThirdPersonPoseController.UpdateAnimatorVelocity( this, renderer, SetAnimatorParameter );
	}

	protected override void OnUpdateAnimatorState( SkinnedModelRenderer renderer )
	{
		base.OnUpdateAnimatorState( renderer );
		if ( !renderer.IsValid() || !Controller.IsValid() ) return;
		Titanfall2PilotThirdPersonPoseController.UpdateAnimatorState( this, renderer, SetAnimatorParameter, SetAnimatorParameter, SetAnimatorParameter );
	}

	bool TryStepGrapple( Titanfall2PilotTactical tactical, Rigidbody body, in Titanfall2PilotSettings settings, float dt )
	{
		if ( _jumpBuffered || _duckBuffered )
		{
			var isJump = _jumpBuffered;
			ConsumeJumpBuffer();
			_duckBuffered = false;
			tactical.DetachGrapple( impactJump: isJump );
			if ( isJump ) Parkour?.PlayJumpJetBurst();
			SyncParkourState();
			return true;
		}

		if ( !tactical.StepGrappleMovement( body, _wishDir, Controller.EyeTransform.Forward, settings.Gravity, dt ) )
			return false;

		SyncParkourState();
		return true;
	}

	bool TryExecuteBufferedJump( Rigidbody body, in Titanfall2PilotSettings settings, bool grounded )
	{
		var canGroundJump = grounded || (_canCoyoteJump && _sinceGrounded <= settings.JumpGracePeriod);
		if ( Slide.IsActive && canGroundJump )
		{
			ConsumeJumpBuffer();
			_canCoyoteJump = false;
			_doubleJumpCooldownUntil = DoubleJumpCooldownSeconds;
			var hop = Titanfall2PilotParkourRules.ComputeSlideJumpVelocity( body.Velocity, settings );
			Slide.End( this, playStopSound: false );
			body.Velocity = hop;
			Controller.PreventGrounding( 0.15f );
			Controller.OnJumped();
			Parkour?.PlayMovementSound( "human_jump_default" );
			SyncParkourState();
			return true;
		}

		if ( canGroundJump )
		{
			ConsumeJumpBuffer();
			_canCoyoteJump = false;
			_doubleJumpCooldownUntil = DoubleJumpCooldownSeconds;
			var jumpZ = Titanfall2PilotParkourRules.JumpSpeedForHeight( settings.JumpHeight, settings.Gravity );
			body.Velocity = body.Velocity.WithZ( MathF.Max( jumpZ, body.Velocity.z ) );
			Controller.PreventGrounding( 0.15f );
			Controller.OnJumped();
			_sinceGrounded = settings.JumpGracePeriod + 1f;
			Parkour?.PlayMovementSound( "human_jump_default" );
			SyncParkourState();
			return true;
		}

		if ( _doubleJumpsRemaining > 0 && _doubleJumpCooldownUntil <= 0f )
		{
			ConsumeJumpBuffer();
			_doubleJumpsRemaining--;
			_canCoyoteJump = false;
			_doubleJumpCooldownUntil = DoubleJumpCooldownSeconds;
			body.Velocity = Titanfall2PilotParkourRules.ComputeDoubleJumpVelocity( body.Velocity, _wishDir, settings );
			Controller.PreventGrounding( 0.12f );
			Controller.OnJumped();
			Parkour?.TriggerThrusterBurst( 1.2f );
			Parkour?.PlayJumpJetBurst();
			SyncParkourState();
			return true;
		}

		return false;
	}

	void UpdateParkourState( float dt )
	{
		var settings = Settings;
		if ( _jumpBuffered && _sinceJumpPressed > JumpBufferSeconds ) _jumpBuffered = false;
		if ( _duckBuffered && _sinceDuckPressed > JumpBufferSeconds ) _duckBuffered = false;

		var grounded = Controller.IsOnGround && !Wallrun.IsActive && !Wallhang.IsActive && !Mantle.IsActive;
		var duckHeld = Controller.UseInputControls && Input.Down( Controller.DuckButton );

		if ( grounded && !_wasGrounded )
		{
			_doubleJumpsRemaining = 1;
			_canCoyoteJump = true;
			_hasPreviousWall = false;
			if ( _peakFallSpeed > 220f )
			{
				Parkour?.TriggerLandingDip( _peakFallSpeed );
				Parkour?.PlayMovementSound(
					_peakFallSpeed > 420f ? settings.SoundGroundImpact : settings.SoundLandDefault,
					fallbackEvent: settings.SoundLandDefault );
			}
			_peakFallSpeed = 0f;

			if ( duckHeld && !Slide.IsActive && Titanfall2PilotParkourRules.CanStartSlide(
				Controller.Body.Velocity, EyeForwardFlat(), _wishDir, isLanding: true, settings ) )
				Slide.Start( this );
		}

		if ( grounded )
		{
			_sinceGrounded = 0f;
			_canCoyoteJump = true;
			_peakFallSpeed = 0f;
		}
		else if ( -Controller.Body.Velocity.z > _peakFallSpeed )
		{
			_peakFallSpeed = -Controller.Body.Velocity.z;
		}
		_wasGrounded = grounded;

		if ( Slide.IsActive )
		{
			Controller.UpdateDucking( true );
		}
		else if ( grounded && (_duckBuffered || duckHeld)
			&& Titanfall2PilotParkourRules.CanStartSlide( Controller.Body.Velocity, EyeForwardFlat(), _wishDir, isLanding: false, settings ) )
		{
			Slide.Start( this );
			Controller.UpdateDucking( true );
		}

		// Cushion imminent high-speed ground impact before physics resolves touchdown so Jump Kit pilots take no fall damage.
		if ( !grounded && !Wallrun.IsActive && !Wallhang.IsActive && !Mantle.IsActive && Controller.Body.Velocity.z < -Titanfall2PilotParkourRules.FallCushionTriggerSpeed )
		{
			var dropDist = MathF.Abs( Controller.Body.Velocity.z ) * dt + 14f;
			var groundProbe = TracePilotHull( WorldPosition + Vector3.Up * 2f, WorldPosition - Vector3.Up * dropDist );
			if ( groundProbe.Hit && !groundProbe.StartedSolid && groundProbe.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ )
			{
				var v = Controller.Body.Velocity;
				Controller.Body.Velocity = v.WithZ( Titanfall2PilotParkourRules.CushionFallSpeed( v.z ) );
			}
		}

		// State transitions: Mantle -> Wallhang -> Wallrun -> Ground/Air.
		if ( Mantle.IsActive )
			Mantle.Step( this, dt );
		else if ( Wallhang.IsActive )
			Wallhang.UpdateAttachment( this, dt );
		else if ( Wallrun.IsActive )
			Wallrun.UpdateAttachment( this, dt );
		else if ( !grounded && _wallRepelUntil <= 0f )
		{
			if ( !Mantle.TryStart( this ) && !Wallhang.TryStart( this ) )
				Wallrun.TryStart( this );
		}

		Slide.UpdateBlend( dt );
		SyncParkourState();
	}

	internal Vector3 ProjectToGroundPlane( Vector3 velocity )
	{
		if ( Controller.GroundIsDynamic ) return velocity + Controller.GroundVelocity;
		var normal = Controller.GroundNormal;
		if ( normal.z <= 0.001f ) return velocity + Controller.GroundVelocity;
		var flat = velocity.WithZ( 0 );
		var z = -Vector3.Dot( flat, normal ) / normal.z;
		return flat.WithZ( z ) + Controller.GroundVelocity;
	}

	void StepGroundVelocity( Rigidbody body, float dt )
	{
		var relative = body.Velocity - Controller.GroundVelocity;
		var next = Titanfall2PilotParkourRules.StepGroundVelocity( relative, Controller.WishVelocity, GroundAccel, GroundDecel, dt );
		body.Velocity = ProjectToGroundPlane( next );
	}

	void ExecuteWallkick()
	{
		var settings = Settings;
		var wallNormal = Wallhang.IsActive ? Wallhang.WallNormal : Wallrun.WallNormal;
		RecordPreviousWall( wallNormal );
		if ( Wallhang.IsActive ) Wallhang.End( this, playDetachSound: false );
		else Wallrun.End( this );

		_doubleJumpsRemaining = 1;
		_canCoyoteJump = false;
		_doubleJumpCooldownUntil = DoubleJumpCooldownSeconds;
		_wallRepelUntil = settings.WallrunRepelTime;
		Controller.Body.Velocity = Titanfall2PilotParkourRules.ComputeWallkickVelocity(
			Controller.Body.Velocity, wallNormal, _wishDir, EyeForwardFlat(), settings );
		Controller.PreventGrounding( 0.16f );
		Controller.OnJumped();
		Parkour?.TriggerThrusterBurst( 1.25f );
		Parkour?.PlayJumpJetBurst();
		SyncParkourState();
	}

	internal void BreakWallrunForMelee()
	{
		if ( !Wallrun.IsActive && !Wallhang.IsActive ) return;
		var wallNormal = Wallhang.IsActive ? Wallhang.WallNormal : Wallrun.WallNormal;
		RecordPreviousWall( wallNormal );
		if ( Wallhang.IsActive ) Wallhang.End( this, playDetachSound: false );
		else Wallrun.End( this );
		_wallRepelUntil = 0.22f;
		SyncParkourState();
	}

	// Internal controller hooks & thin reflection-compatible forwarders for Validation/PilotParkour.cs.
	bool TryStartWallrun() => Wallrun.TryStart( this );
	void StartSlide() => Slide.Start( this );
	void EndSlide( bool playStopSound ) => Slide.End( this, playStopSound );
	bool TryStartMantle() => Mantle.TryStart( this );

	internal void OnWallAttached() { _doubleJumpsRemaining = 1; _canCoyoteJump = false; }
	internal void OnMantleStarted() { _jumpBuffered = false; _canCoyoteJump = false; }
	internal void OnMantleCompleted() { _doubleJumpsRemaining = 1; _hasPreviousWall = false; }
	internal void ResetAirJumpAfterVault() { _doubleJumpsRemaining = 1; _doubleJumpCooldownUntil = DoubleJumpCooldownSeconds; }
	internal void ClearDuckBuffer() => _duckBuffered = false;
	internal void SetWallRepel( float seconds ) => _wallRepelUntil = seconds;
	internal bool IsSameWallBlocked( Vector3 normal ) =>
		Titanfall2PilotParkourRules.IsSameWallBlocked( _hasPreviousWall, _prevWallNormal, _prevWallPos, normal, WorldPosition, Settings );

	internal void RecordPreviousWall( Vector3 normal )
	{
		_hasPreviousWall = true;
		_prevWallNormal = normal;
		_prevWallPos = WorldPosition;
	}

	internal void ConsumeJumpBuffer()
	{
		_jumpBuffered = false;
		_sinceJumpPressed = 10f;
	}

	internal Vector3 EyeForwardFlat()
	{
		var fwd = Controller.EyeTransform.Forward.WithZ( 0 );
		return fwd.LengthSquared > 0.0001f ? fwd.Normal : WorldRotation.Forward.WithZ( 0 ).Normal;
	}

	internal SceneTraceResult TraceWallRay( Vector3 start, Vector3 end ) =>
		Titanfall2CollisionQueries.Run( Scene.Trace.Ray( start, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile", "player" ), Titanfall2TraceMask.PlayerSolid );

	internal SceneTraceResult TraceWallSphere( Vector3 start, Vector3 end, float radius ) =>
		Titanfall2CollisionQueries.Run( Scene.Trace.Sphere( radius, start, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile", "player" ), Titanfall2TraceMask.PlayerSolid );

	internal SceneTraceResult TracePilotHull( Vector3 start, Vector3 end )
	{
		var halfWidth = MathF.Max( 4f, BodyHalfWidth - 0.5f );
		var height = Math.Clamp( Controller.CurrentHeight - 2f, 28f, 96f );
		var box = new BBox( new Vector3( -halfWidth, -halfWidth, 0f ), new Vector3( halfWidth, halfWidth, height ) );
		var phased = Parkour.IsValid() && Parkour.Tactical is { IsValid: true, IsPhaseActive: true };
		var query = Scene.Trace.Box( box, start, end ).IgnoreGameObjectHierarchy( GameObject );
		query = phased
			? query.WithoutTags( "trigger", "projectile", "player", "npc", "titan", Titanfall2PhaseDimension.RealspaceTag )
			: query.WithoutTags( "trigger", "projectile", "player" );
		return Titanfall2CollisionQueries.Run( query, Titanfall2TraceMask.PlayerSolid );
	}

	internal static bool IsRunnableWallHit( in SceneTraceResult hit ) =>
		hit.Hit && !hit.StartedSolid && Titanfall2PilotParkourRules.IsValidWallNormal( hit.Normal ) && !IsNoClimb( hit );

	internal static bool IsNoClimb( in SceneTraceResult hit )
	{
		if ( hit.GameObject.IsValid() && hit.GameObject.Tags.Has( Titanfall2CollisionQueries.NoWallrunTag ) )
			return true;
		var source = hit.Collider?.Components.Get<Titanfall2CollisionSource>();
		return source is { HasEffectiveContents: true } && (source.EffectiveContents & (uint)Titanfall2Contents.NoClimb) != 0;
	}

	internal void SyncParkourState()
	{
		if ( !Parkour.IsValid() || !Controller.IsValid() ) return;
		var flatSpeed = Controller.Body.IsValid() ? Controller.Body.Velocity.WithZ( 0 ).Length : 0f;
		Parkour.IsWallrunning = Wallrun.IsActive;
		Parkour.IsWallhanging = Wallhang.IsActive;
		Parkour.WallrunSide = Wallrun.IsActive ? Wallrun.WallSide : Wallhang.IsActive ? Wallhang.WallSide : 0f;
		Parkour.WallNormal = Wallrun.IsActive ? Wallrun.WallNormal : Wallhang.IsActive ? Wallhang.WallNormal : Mantle.IsActive ? Mantle.WallNormal : Vector3.Zero;
		Parkour.IsSliding = Slide.IsActive;
		Parkour.SlideBlend = Slide.Blend;
		Parkour.SlideDirection = Slide.IsActive ? Slide.Direction : Parkour.SlideDirection;
		Parkour.IsMantling = Mantle.IsActive;
		Parkour.MantleProgress = Mantle.Progress;
		Parkour.IsSprinting = _wantsSprint && Controller.IsOnGround && flatSpeed > Settings.WalkSpeed + 15f;
		Parkour.IsAirborne = !Controller.IsOnGround && !Wallrun.IsActive && !Wallhang.IsActive && !Mantle.IsActive;
		Parkour.HorizontalSpeed = flatSpeed;
		Parkour.DoubleJumpsRemaining = _doubleJumpsRemaining;
	}
}
