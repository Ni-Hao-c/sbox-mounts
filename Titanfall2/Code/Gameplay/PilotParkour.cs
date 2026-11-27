using Sandbox.Movement;

/// <summary>Snapshot of Pilot parkour kinematics consumed by weapons and first-person viewmodels.</summary>
public readonly record struct Titanfall2PilotViewPoseState(
	float WallrunSide,
	float SlideBlend,
	float MantleProgress,
	float AirborneBlend,
	float HorizontalSpeed,
	float LandingDip,
	bool IsWallrunning,
	bool IsSliding,
	bool IsMantling,
	bool IsSprinting,
	float OffhandDip = 0f,
	float MeleeProgress = 0f,
	Titanfall2PilotMeleeStrikeKind MeleeStrikeKind = Titanfall2PilotMeleeStrikeKind.None );

/// <summary>Typed state surface exposed by the Pilot Jump Kit controller for weapons and HUDs.</summary>
public interface ITitanfall2PilotParkourState
{
	bool Equipped { get; }
	bool CombatBusy { get; }
	float WallrunSide { get; }
	float SlideBlend { get; }
	float MantleProgress { get; }
	bool IsWallrunning { get; }
	bool IsSliding { get; }
	bool IsMantling { get; }
	bool IsSprinting { get; }
	bool IsAirborne { get; }
	float HorizontalSpeed { get; }
	Vector3 WallNormal { get; }
	int DoubleJumpsRemaining { get; }
	Titanfall2PilotViewPoseState ViewPoseState { get; }
}

/// <summary>
/// Persists Jump Kit ownership across Sandbox death and respawn within the active scene.
/// Runs zero work when no player has a Jump Kit equipped.
/// </summary>
public sealed class Titanfall2JumpKitPersistence : GameObjectSystem<Titanfall2JumpKitPersistence>
{
	sealed record EquippedEntry( string MountIdent, Titanfall2PilotSettings Settings, string PackModelPath,
		Titanfall2OffhandOrdnanceConfig? Ordnance, Titanfall2PilotTacticalKind? Tactical );

	readonly Dictionary<Guid, EquippedEntry> _equipped = [];

	public Titanfall2JumpKitPersistence( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 5, ReapplyToRespawnedPlayers, nameof( ReapplyToRespawnedPlayers ) );
	}

	internal static Guid OwnerKey( PlayerController player ) =>
		player?.Network?.Owner?.Id ?? Guid.Empty;

	internal void Remember( PlayerController player, string mountIdent, Titanfall2PilotSettings settings, string packModelPath )
	{
		if ( !player.IsValid() ) return;
		var key = OwnerKey( player );
		var existing = _equipped.TryGetValue( key, out var prev ) ? prev : null;
		_equipped[key] = new( mountIdent ?? "titanfall2", settings, packModelPath, existing?.Ordnance, existing?.Tactical );
	}

	internal void RememberOrdnance( PlayerController player, in Titanfall2OffhandOrdnanceConfig config )
	{
		if ( !player.IsValid() || !config.Valid ) return;
		var key = OwnerKey( player );
		if ( _equipped.TryGetValue( key, out var existing ) )
			_equipped[key] = existing with { Ordnance = config };
	}

	internal void RememberTactical( PlayerController player, Titanfall2PilotTacticalKind tactical )
	{
		if ( !player.IsValid() ) return;
		var key = OwnerKey( player );
		if ( _equipped.TryGetValue( key, out var existing ) )
			_equipped[key] = existing with { Tactical = tactical };
	}

	internal void Forget( PlayerController player )
	{
		if ( !player.IsValid() ) return;
		_equipped.Remove( OwnerKey( player ) );
	}

	internal bool IsRemembered( PlayerController player ) =>
		player.IsValid() && _equipped.ContainsKey( OwnerKey( player ) );

	void ReapplyToRespawnedPlayers()
	{
		if ( _equipped.Count == 0 || !Scene.IsValid() ) return;
		if ( Networking.IsActive && !Networking.IsHost ) return;

		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
		{
			if ( !player.IsValid() || !player.Active ) continue;
			var key = OwnerKey( player );
			if ( !_equipped.TryGetValue( key, out var entry ) ) continue;
			var parkour = player.GameObject.GetComponent<Titanfall2PilotParkour>();
			if ( parkour is { Equipped: true } ) continue;
			Titanfall2PilotParkour.AttachEquipped( player, entry.MountIdent, entry.Settings, entry.PackModelPath, entry.Ordnance, entry.Tactical );
		}
	}
}

/// <summary>
/// Equipable Titanfall 2 Pilot Jump Kit controller. Coordinates camera roll/FOV/landing feedback,
/// 3P Jump Kit visuals, Miles movement audio, and typed parkour state for TF2 weapons.
/// </summary>
[Title( "Titanfall 2 Pilot Parkour" ), Group( "Titanfall 2" )]
public sealed class Titanfall2PilotParkour : Component, ICameraModifier, ITitanfall2PilotParkourState
{
	internal const string DefaultPackModel = "models/weapons/ammoboxes/backpack_single.mdl";
	const float MaxCameraRollDegrees = 22f;

	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public string PackModelPath { get; set; }
	[Property] public Titanfall2PilotSettings Settings { get; set; } = Titanfall2PilotSettings.RetailDefault;

	[Sync( SyncFlags.FromHost )] public bool Equipped { get; internal set; }

	// Synced presentation states so 3P viewers and weapons see live wallrun/slide/mantle state.
	[Sync] public float WallrunSide { get; internal set; }
	[Sync] public float SlideBlend { get; internal set; }
	[Sync] public float MantleProgress { get; internal set; }
	[Sync] public bool IsWallrunning { get; internal set; }
	[Sync] public bool IsSliding { get; internal set; }
	[Sync] public bool IsMantling { get; internal set; }
	[Sync] public bool IsSprinting { get; internal set; }
	[Sync] public bool IsAirborne { get; internal set; }
	[Sync] public float HorizontalSpeed { get; internal set; }
	[Sync] public Vector3 WallNormal { get; internal set; }
	[Sync] public Vector3 SlideDirection { get; internal set; }
	[Sync] public int DoubleJumpsRemaining { get; internal set; } = 1;

	public bool CombatBusy => (_combat.IsValid() && _combat.CombatBusy) || (_tactical.IsValid() && _tactical.IsPhaseActive);

	public Titanfall2PilotViewPoseState ViewPoseState => new(
		WallrunSide, SlideBlend, MantleProgress, _airborneBlend, HorizontalSpeed,
		_landingDip, IsWallrunning, IsSliding, IsMantling, IsSprinting,
		_combat.IsValid() ? _combat.OffhandDip : 0f,
		_combat.IsValid() ? _combat.MeleeProgress : 0f,
		_combat.IsValid() ? _combat.MeleeStrikeKind : Titanfall2PilotMeleeStrikeKind.None );

	// Camera modifier state (local owner only).
	float _cameraRoll;
	float _fovScale = 1f;
	float _landingDip;
	float _landingDipVelocity;
	float _airborneBlend;
	float _thrusterGlow;
	double _lastCameraTime;

	// Third-person Jump Kit backpack visual & local Pilot Combat HUD.
	GameObject _packVisual;
	GameObject _combatHud;
	PointLight _thrusterLight;
	SkinnedModelRenderer _packParentBody;
	PlayerController _controller;
	Titanfall2PilotCombat _combat;
	Titanfall2PilotTactical _tactical;

	internal Titanfall2PilotTactical Tactical => _tactical;

	int ICameraModifier.CameraOrder => 65;

	/// <summary>Equips the Jump Kit onto <paramref name="player"/> and persists across respawn.</summary>
	public static Titanfall2PilotParkour Equip( PlayerController player, string mountIdent = "titanfall2",
		Titanfall2PilotSettings? settings = null, string packModelPath = null )
	{
		if ( !player.IsValid() || !player.Scene.IsValid() ) return null;
		var resolved = settings ?? ResolveSettings( mountIdent );
		var pack = string.IsNullOrWhiteSpace( packModelPath ) ? ResolvePackModel( mountIdent ) : packModelPath;
		var parkour = AttachEquipped( player, mountIdent ?? "titanfall2", resolved, pack );
		player.Scene.GetSystem<Titanfall2JumpKitPersistence>()?.Remember( player, mountIdent ?? "titanfall2", resolved, pack );
		return parkour;
	}

	/// <summary>Removes the Jump Kit from <paramref name="player"/> and clears respawn persistence.</summary>
	public static bool Unequip( PlayerController player )
	{
		if ( !player.IsValid() || !player.Scene.IsValid() ) return false;
		player.Scene.GetSystem<Titanfall2JumpKitPersistence>()?.Forget( player );
		var parkour = player.GameObject.GetComponent<Titanfall2PilotParkour>();
		if ( !parkour.IsValid() || !parkour.Equipped ) return false;

		parkour.Equipped = false;
		parkour.ResetTransientState();
		parkour.DestroyPackVisual();
		parkour.DestroyCombatHud();
		if ( player.GameObject.GetComponent<Titanfall2PilotCombat>() is { } combat )
			combat.ResetOnUnequip();
		if ( player.GameObject.GetComponent<Titanfall2PilotTactical>() is { } tactical )
			tactical.ResetOnUnequip();
		if ( player.GameObject.GetComponent<Titanfall2PilotMoveMode>() is { } mode )
			mode.Enabled = false;
		return true;
	}

	internal static Titanfall2PilotParkour AttachEquipped( PlayerController player, string mountIdent,
		Titanfall2PilotSettings settings, string packModelPath, Titanfall2OffhandOrdnanceConfig? ordnance = null,
		Titanfall2PilotTacticalKind? tacticalKind = null )
	{
		var resolvedMount = mountIdent ?? "titanfall2";
		var parkour = player.GameObject.GetOrAddComponent<Titanfall2PilotParkour>();
		parkour.MountIdent = resolvedMount;
		parkour.Settings = settings;
		parkour.PackModelPath = packModelPath;
		parkour.Equipped = true;
		parkour.Enabled = true;

		var mode = player.GameObject.GetOrAddComponent<Titanfall2PilotMoveMode>();
		mode.Parkour = parkour;
		mode.Enabled = true;

		var combat = player.GameObject.GetOrAddComponent<Titanfall2PilotCombat>();
		combat.ConfigureForEquip( parkour, resolvedMount, ordnance );
		parkour._combat = combat;

		var tactical = player.GameObject.GetOrAddComponent<Titanfall2PilotTactical>();
		tactical.ConfigureForEquip( parkour, resolvedMount, tacticalKind );
		parkour._tactical = tactical;
		return parkour;
	}

	[ConCmd( "tf2_jumpkit", Help = "Toggle Titanfall 2 Pilot Jump Kit parkour on the local player." )]
	static void ToggleJumpKitCommand()
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		var player = scene.GetAllComponents<PlayerController>().FirstOrDefault( p => p.IsValid() && p.Active && !p.IsProxy );
		if ( !player.IsValid() ) return;

		if ( player.GameObject.GetComponent<Titanfall2PilotParkour>() is { Equipped: true } )
		{
			Unequip( player );
			Titanfall2Log.Info( "TF2 Pilot Jump Kit unequipped." );
		}
		else
		{
			Equip( player );
			Titanfall2Log.Info( "TF2 Pilot Jump Kit equipped." );
		}
	}

	static Titanfall2PilotSettings ResolveSettings( string mountIdent ) =>
		Sandbox.Mounting.Directory.Get( mountIdent ?? "titanfall2" ) is Titanfall2Mount mount
			? mount.GetPilotSettings()
			: Titanfall2PilotSettings.RetailDefault;

	static string ResolvePackModel( string mountIdent ) =>
		$"mount://{mountIdent ?? "titanfall2"}/{DefaultPackModel}.visual.vmdl";

	protected override void OnAwake()
	{
		_controller = GetComponent<PlayerController>();
		_combat = GetComponent<Titanfall2PilotCombat>();
		_tactical = GetComponent<Titanfall2PilotTactical>();
	}

	protected override void OnUpdate()
	{
		_controller ??= GetComponent<PlayerController>();
		_combat ??= GetComponent<Titanfall2PilotCombat>();
		_tactical ??= GetComponent<Titanfall2PilotTactical>();
		if ( !Equipped )
		{
			DestroyPackVisual();
			DestroyCombatHud();
			ResetTransientState();
			return;
		}

		// Ensure MoveMode, PilotCombat & PilotTactical stay active on owners after host syncs Equipped = true.
		if ( !IsProxy && _controller.IsValid() )
		{
			var mode = GameObject.GetOrAddComponent<Titanfall2PilotMoveMode>();
			mode.Parkour = this;
			if ( !mode.Enabled ) mode.Enabled = true;

			if ( !_combat.IsValid() || !_combat.Enabled )
			{
				_combat = GameObject.GetOrAddComponent<Titanfall2PilotCombat>();
				_combat.ConfigureForEquip( this, MountIdent );
			}

			if ( !_tactical.IsValid() || !_tactical.Enabled )
			{
				_tactical = GameObject.GetOrAddComponent<Titanfall2PilotTactical>();
				_tactical.ConfigureForEquip( this, MountIdent );
			}
		}

		var dt = Math.Clamp( Time.Delta, 0f, 0.05f );
		_airborneBlend = MathX.Lerp( _airborneBlend, IsAirborne && !IsWallrunning && !IsMantling ? 1f : 0f, dt * 10f, true );
		_thrusterGlow = MathF.Max( 0f, _thrusterGlow - dt * 2.8f );
		if ( IsWallrunning || IsSliding ) _thrusterGlow = MathF.Max( _thrusterGlow, 0.75f );

		UpdateLandingDip( dt );
		UpdatePackVisual();
		UpdateCombatHud();
	}

	protected override void OnDestroy()
	{
		DestroyPackVisual();
		DestroyCombatHud();
	}

	internal void TriggerLandingDip( float impactSpeed )
	{
		var strength = Math.Clamp( (impactSpeed - 180f) / 420f, 0f, 1f );
		if ( strength <= 0.05f ) return;
		_landingDipVelocity -= (18f + strength * 32f);
		_thrusterGlow = MathF.Max( _thrusterGlow, strength * 0.6f );
	}

	internal void TriggerThrusterBurst( float intensity = 1f )
	{
		_thrusterGlow = Math.Clamp( MathF.Max( _thrusterGlow, intensity ), 0f, 1.5f );
	}

	internal void PlayMovementSound( string event1P, string event3P = null )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var localFirstPerson = !IsProxy && _controller is { ThirdPerson: false };
		var name = localFirstPerson ? event1P : (event3P ?? event1P);
		if ( string.IsNullOrWhiteSpace( name ) ) return;
		Titanfall2SoundEventEmitter.OneShot( Scene, MountIdent, name, WorldPosition + Vector3.Up * 36f, firstPerson: localFirstPerson );
	}

	void UpdateLandingDip( float dt )
	{
		// Critically damped vertical landing spring (settles cleanly within ~0.25s).
		var accel = -170f * _landingDip - 24f * _landingDipVelocity;
		_landingDipVelocity += accel * dt;
		_landingDip = Math.Clamp( _landingDip + _landingDipVelocity * dt, -5.5f, 1.5f );
		if ( MathF.Abs( _landingDip ) < 0.01f && MathF.Abs( _landingDipVelocity ) < 0.05f )
		{
			_landingDip = 0f;
			_landingDipVelocity = 0f;
		}
	}

	void ResetTransientState()
	{
		WallrunSide = 0f;
		SlideBlend = 0f;
		MantleProgress = 0f;
		IsWallrunning = false;
		IsSliding = false;
		IsMantling = false;
		IsSprinting = false;
		IsAirborne = false;
		HorizontalSpeed = 0f;
		WallNormal = Vector3.Zero;
		SlideDirection = Vector3.Zero;
		DoubleJumpsRemaining = 1;
		_cameraRoll = 0f;
		_fovScale = 1f;
		_landingDip = 0f;
		_landingDipVelocity = 0f;
		_airborneBlend = 0f;
		_thrusterGlow = 0f;
	}

	float ReadActiveWeaponAdsBlend()
	{
		var weapon = GetComponentInChildren<Titanfall2Weapon>( includeDisabled: false );
		return weapon is { IsValid: true, IsHeld: true, IsActive: true } ? weapon.AdsBlend : 0f;
	}

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( !Equipped || IsProxy || Scene.Camera != camera || !_controller.IsValid() ) return;
		if ( _controller.Mode is not Titanfall2PilotMoveMode )
		{
			_cameraRoll = 0f;
			_fovScale = 1f;
			return;
		}

		var adsBlend = ReadActiveWeaponAdsBlend();
		var dt = _lastCameraTime != Time.NowDouble ? Math.Clamp( Time.Delta, 0f, 0.05f ) : 0f;
		_lastCameraTime = Time.NowDouble;

		// 1. Target camera roll from wallrun & slide (retail 0x1802c1970 projection).
		var targetRoll = 0f;
		var rollRate = Settings.WallrunViewTiltSpeed * 2.2f;
		if ( IsWallrunning && WallNormal.LengthSquared > 0.25f )
		{
			targetRoll = Titanfall2PilotParkourRules.ComputeWallrunCameraRoll( view.Rotation.Right, WallNormal, adsBlend, Settings );
			rollRate = Settings.WallrunViewTiltSpeed * 2.2f;
		}
		else if ( SlideBlend > 0.01f && SlideDirection.LengthSquared > 0.25f )
		{
			targetRoll = Titanfall2PilotParkourRules.ComputeSlideCameraRoll( view.Rotation.Right, SlideDirection, HorizontalSpeed, adsBlend, Settings ) * SlideBlend;
			rollRate = IsSliding ? Settings.SlideViewTiltIncreaseSpeed * 2.4f : Settings.SlideViewTiltDecreaseSpeed * 3.2f;
		}

		targetRoll = Math.Clamp( targetRoll, -MaxCameraRollDegrees, MaxCameraRollDegrees );
		if ( dt > 0f )
			_cameraRoll = MathX.Lerp( _cameraRoll, targetRoll, Math.Clamp( dt * rollRate, 0f, 1f ), true );

		// 2. Dynamic FOV scale (slideFOVScale = 1.1 + subtle speed FOV + Stim/Phase/Grapple boost, suppressed in ADS).
		var targetFovScale = Titanfall2PilotParkourRules.ComputeTargetFovScale( IsSliding, IsWallrunning, HorizontalSpeed, adsBlend, Settings );
		if ( _tactical.IsValid() && _tactical.IsActive )
		{
			var tacticalFov = Titanfall2PilotTacticalRules.ComputeTacticalFovScale(
				_tactical.IsStimActive, _tactical.IsPhaseActive, _tactical.IsGrappling, adsBlend, _tactical.Settings );
			targetFovScale = MathF.Max( targetFovScale, tacticalFov );
		}
		if ( dt > 0f )
		{
			var fovTime = targetFovScale > _fovScale ? Settings.SlideFovLerpInTime : Settings.SlideFovLerpOutTime;
			var fovRate = fovTime > 0.01f ? 1f / fovTime : 6f;
			_fovScale = MathX.Lerp( _fovScale, targetFovScale, Math.Clamp( dt * fovRate, 0f, 1f ), true );
		}

		// 3. Subtle mantle pitch wave, combat melee/throw punch & landing camera dip (first-person only for dip/tilt).
		if ( !_controller.ThirdPerson )
		{
			var mantlePitch = IsMantling ? MathF.Sin( Math.Clamp( MantleProgress, 0f, 1f ) * MathF.PI ) * 3.5f : 0f;
			var combatKick = _combat.IsValid() ? _combat.CameraKickAngles : Angles.Zero;
			var dipScale = 1f - adsBlend * 0.7f;
			view.Position += Vector3.Up * (_landingDip * dipScale);
			var totalPitch = mantlePitch + combatKick.pitch;
			var totalYaw = combatKick.yaw;
			var totalRoll = _cameraRoll + combatKick.roll;
			if ( MathF.Abs( totalPitch ) > 0.01f || MathF.Abs( totalYaw ) > 0.01f || MathF.Abs( totalRoll ) > 0.01f )
				view.Rotation *= Rotation.From( new Angles( totalPitch, totalYaw, totalRoll ) );
		}

		if ( MathF.Abs( _fovScale - 1f ) > 0.001f )
			view.FieldOfView = Math.Clamp( view.FieldOfView * _fovScale, 35f, 120f );
	}

	void UpdatePackVisual()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !_controller.IsValid() ) return;
		var body = _controller.Renderer;
		if ( !body.IsValid() ) return;

		if ( _packVisual.IsValid() && _packParentBody != body )
			DestroyPackVisual();

		if ( !_packVisual.IsValid() )
		{
			var spine = body.GetBoneObject( "spine_2" ) ?? body.GetBoneObject( "spine_1" ) ?? body.GameObject;
			if ( !spine.IsValid() ) return;

			var model = Titanfall2GameplayVisuals.GetModel( PackModelPath );
			_packVisual = new GameObject( spine, false, "TF2 Pilot Jump Kit" );
			_packVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_packVisual.LocalPosition = new Vector3( -7.5f, 0f, 1.5f );
			_packVisual.LocalRotation = Rotation.From( new Angles( 0f, 180f, 0f ) );
			_packVisual.LocalScale = 0.85f;

			if ( model is { IsError: false } )
			{
				var mr = _packVisual.AddComponent<ModelRenderer>();
				mr.Model = model;
				mr.RenderOptions.Game = true;
				mr.RenderOptions.Overlay = false;
				var center = model.Bounds.Center;
				_packVisual.LocalPosition = new Vector3( -7.5f, 0f, 1.5f ) - _packVisual.LocalRotation * (center * _packVisual.LocalScale);
			}

			var lightGo = new GameObject( _packVisual, true, "JumpKit Thruster Glow" );
			lightGo.LocalPosition = new Vector3( -4f, 0f, -4f );
			_thrusterLight = lightGo.AddComponent<PointLight>();
			_thrusterLight.LightColor = new Color( 0.18f, 0.72f, 1f );
			_thrusterLight.Radius = 64f;
			_thrusterLight.Shadows = false;

			_packVisual.Enabled = true;
			_packParentBody = body;
		}

		if ( _packVisual.IsValid() )
		{
			// Hide backpack in local first person so it never clips the camera.
			var show = IsProxy || _controller.ThirdPerson;
			_packVisual.Enabled = show;
			if ( _thrusterLight.IsValid() )
			{
				var baseGlow = 0.25f + _thrusterGlow * 2.2f;
				_thrusterLight.LightColor = new Color( 0.18f, 0.72f, 1f ) * baseGlow;
				_thrusterLight.Radius = 36f + _thrusterGlow * 54f;
			}
		}
	}

	void DestroyPackVisual()
	{
		if ( _packVisual.IsValid() ) _packVisual.Destroy();
		_packVisual = null;
		_thrusterLight = null;
		_packParentBody = null;
	}

	void UpdateCombatHud()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		if ( !Equipped || IsProxy || !Scene.IsValid() || !_combat.IsValid() )
		{
			DestroyCombatHud();
			return;
		}
		if ( _combatHud.IsValid() ) return;

		_combatHud = new GameObject( Scene, false, "TF2 Pilot Combat HUD" );
		_combatHud.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_combatHud.AddComponent<ScreenPanel>().ZIndex = 95;
		var hud = _combatHud.AddComponent<Titanfall2PilotCombatHud>();
		hud.Parkour = this;
		hud.Combat = _combat;
		hud.Tactical = _tactical;
		_combatHud.Enabled = true;
	}

	void DestroyCombatHud()
	{
		if ( _combatHud.IsValid() ) _combatHud.Destroy();
		_combatHud = null;
	}
}

/// <summary>
/// Active Sandbox MoveMode implementing Titanfall 2 Pilot parkour:
/// sprint & air-strafe momentum, double jump, wallrun/wallkick, slide/slide-hop, and ledge mantle.
/// Yields cleanly to SitMoveMode (Titans/chairs), NoclipMoveMode, and swimming.
/// </summary>
[Title( "Titanfall 2 Pilot MoveMode" ), Group( "Titanfall 2" )]
public sealed class Titanfall2PilotMoveMode : MoveMode
{
	const int ActivePriority = 40;
	const float GroundAccel = 12f;
	const float GroundFriction = 5.5f;
	const float StopSpeed = 100f;
	const float JumpBufferSeconds = 0.13f;
	const float DoubleJumpCooldownSeconds = 0.14f;
	const float WallrunFootstepInterval = 0.22f;
	const float WallrunMinEntrySpeed = 95f;
	const float WallrunMinFloorClearance = 8f;

	[Property] public Titanfall2PilotParkour Parkour { get; set; }

	Titanfall2PilotSettings Settings => Parkour.IsValid() ? Parkour.Settings : Titanfall2PilotSettings.RetailDefault;
	// PlayerController.BodyRadius is the full box width / capsule diameter; half-width is BodyRadius * 0.5f.
	float BodyHalfWidth => Math.Clamp( Controller.BodyRadius * 0.5f, 6f, 24f );

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

	// Wallrun state.
	bool _isWallrunning;
	Vector3 _wallNormal;
	Vector3 _wallHugVelocity;
	float _wallSide;
	float _wallrunElapsed;
	float _wallPushAwayTime;
	TimeSince _sinceWallFootstep = 10f;
	TimeUntil _wallRepelUntil;
	bool _hasPreviousWall;
	Vector3 _prevWallNormal;
	Vector3 _prevWallPos;

	// Slide state.
	bool _isSliding;
	Vector3 _slideDirection;
	float _slideBlend;
	TimeSince _sinceSlideBoost = 10f;

	// Mantle state.
	bool _isMantling;
	Vector3 _mantleStart;
	Vector3 _mantleEnd;
	float _mantleDuration;
	float _mantleElapsed;
	TimeUntil _mantleCooldownUntil;

	// Cached wish input for current fixed step.
	Vector3 _wishDir;
	bool _wantsSprint;

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
	public override bool AllowGrounding => !_isWallrunning && !_isMantling && !IsGrapplingActive;
	public override bool AllowFalling => !_isWallrunning && !_isMantling;
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
		_isWallrunning = false;
		_wallHugVelocity = Vector3.Zero;
		_isSliding = false;
		_isMantling = false;
		_slideBlend = 0f;
		SyncParkourState();
	}

	public override void OnModeEnd( MoveMode next )
	{
		if ( _isWallrunning ) EndWallrun( playEndSound: false );
		if ( _isSliding ) EndSlide( playStopSound: false );
		_isMantling = false;
		_engineGroundJumpFired = false;
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

		var eyeFlat = Rotation.FromYaw( eyes.Yaw() );
		var worldWish = (eyeFlat * flatInput).WithZ( 0 );
		_wishDir = worldWish.LengthSquared > 0.0001f ? worldWish.Normal : Vector3.Zero;

		var duckHeld = Controller.UseInputControls && Input.Down( Controller.DuckButton );
		var runHeld = Controller.UseInputControls && Input.Down( Controller.AltMoveButton );
		if ( Controller.RunByDefault ) runHeld = !runHeld;
		var forwardDot = _wishDir.LengthSquared > 0.01f ? Vector3.Dot( _wishDir, eyeFlat.Forward ) : 0f;
		_wantsSprint = runHeld && !duckHeld && forwardDot > 0.5f && !_isSliding && !_isMantling;

		var tactical = Parkour.IsValid() ? Parkour.Tactical : GetComponent<Titanfall2PilotTactical>();
		var speedScale = tactical is { IsValid: true } ? tactical.SpeedMultiplier : 1f;
		var wallScale = tactical is { IsValid: true } ? tactical.WallrunSpeedMultiplier : 1f;

		var targetSpeed = _isWallrunning ? settings.WallrunMaxSpeedHorizontal * wallScale
			: _isSliding ? MathF.Max( settings.SlideStopSpeed * speedScale, Controller.Velocity.WithZ( 0 ).Length )
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

		// Handle ground/slide jump initiated by PlayerController.InputJump -> OnJump on this tick.
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
		if ( tactical is { IsValid: true, IsGrappling: true } )
		{
			if ( _jumpBuffered || _duckBuffered )
			{
				var isJump = _jumpBuffered;
				ConsumeJumpBuffer();
				_duckBuffered = false;
				tactical.DetachGrapple( impactJump: isJump );
				if ( isJump )
					Parkour?.PlayMovementSound( settings.SoundJumpJetStart1P, settings.SoundJumpJetStart3P );
				SyncParkourState();
				return;
			}

			if ( tactical.StepGrappleMovement( body, _wishDir, Controller.EyeTransform.Forward, settings.Gravity, dt ) )
			{
				SyncParkourState();
				return;
			}
		}

		if ( _isMantling )
		{
			// Allow vault-jump cancel after clearing the initial lift.
			if ( _jumpBuffered && _mantleDuration > 0f && (_mantleElapsed / _mantleDuration) >= 0.25f )
			{
				ConsumeJumpBuffer();
				_isMantling = false;
				_doubleJumpsRemaining = 1;
				_doubleJumpCooldownUntil = DoubleJumpCooldownSeconds;
				var dir = EyeForwardFlat();
				var launch = dir * MathF.Max( settings.SprintSpeed, 280f )
					+ Vector3.Up * Titanfall2PilotParkourRules.JumpSpeedForHeight( settings.JumpHeight, settings.Gravity );
				body.Velocity = launch;
				Controller.PreventGrounding( 0.15f );
				Parkour?.TriggerThrusterBurst( 0.9f );
				Parkour?.PlayMovementSound( settings.SoundJumpJetStart1P, settings.SoundJumpJetStart3P );
				SyncParkourState();
				return;
			}
			body.Velocity = Vector3.Zero;
			return;
		}

		if ( _isWallrunning )
		{
			if ( _jumpBuffered )
			{
				ConsumeJumpBuffer();
				ExecuteWallkick();
				return;
			}

			var wallSettings = tactical is { IsValid: true, IsStimActive: true }
				? settings with { WallrunMaxSpeedHorizontal = settings.WallrunMaxSpeedHorizontal * tactical.WallrunSpeedMultiplier }
				: settings;
			var next = Titanfall2PilotParkourRules.StepWallrunVelocity(
				body.Velocity, _wallNormal, _wishDir, EyeForwardFlat(), _wallrunElapsed, wallSettings, dt );
			body.Velocity = next + _wallHugVelocity;
			return;
		}

		var grounded = Controller.IsOnGround;
		var duckHeld = Controller.UseInputControls && Input.Down( Controller.DuckButton );

		// Jump handling: buffered ground/slide-hop jump, coyote jump, or mid-air double jump.
		if ( _jumpBuffered )
		{
			if ( _isSliding && (grounded || (_canCoyoteJump && _sinceGrounded <= settings.JumpGracePeriod)) )
			{
				ConsumeJumpBuffer();
				_canCoyoteJump = false;
				_doubleJumpCooldownUntil = DoubleJumpCooldownSeconds;
				var hop = Titanfall2PilotParkourRules.ComputeSlideJumpVelocity( body.Velocity, settings );
				EndSlide( playStopSound: false );
				body.Velocity = hop;
				Controller.PreventGrounding( 0.15f );
				Controller.OnJumped();
				Parkour?.PlayMovementSound( "human_jump_default" );
				SyncParkourState();
				return;
			}

			if ( grounded || (_canCoyoteJump && _sinceGrounded <= settings.JumpGracePeriod) )
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
				return;
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
				Parkour?.PlayMovementSound( settings.SoundJumpJetStart1P, settings.SoundJumpJetStart3P );
				SyncParkourState();
				return;
			}
		}

		if ( _isSliding )
		{
			if ( !grounded )
			{
				EndSlide( playStopSound: false );
			}
			else
			{
				var wantsStop = !duckHeld;
				var (nextVel, keepSliding) = Titanfall2PilotParkourRules.StepSlideVelocity(
					body.Velocity, Controller.GroundNormal, _wishDir, wantsStop, settings, dt );
				body.Velocity = ProjectToGroundPlane( nextVel );
				if ( nextVel.WithZ( 0 ).LengthSquared > 1f )
					_slideDirection = nextVel.WithZ( 0 ).Normal;
				if ( !keepSliding )
					EndSlide( playStopSound: true );
				return;
			}
		}

		if ( grounded )
		{
			StepGroundVelocity( body, settings, dt );
		}
		else
		{
			// Airborne: Source/TF2 air strafing + exact retail 600 u/s^2 gravity.
			var v = Titanfall2PilotParkourRules.ApplyAirAcceleration( body.Velocity, _wishDir, settings.AirSpeed, settings.AirAcceleration, dt );
			v.z -= settings.Gravity * dt;
			body.Velocity = v;
		}
	}

	public override void PrePhysicsStep()
	{
		if ( !Controller.IsValid() || _isMantling || IsGrapplingActive ) return;
		if ( !_isWallrunning && Controller.IsOnGround )
			TrySteppingUp( Settings.StepHeight );
	}

	public override void PostPhysicsStep()
	{
		if ( !Controller.IsValid() || _isMantling ) return;
		if ( !_isWallrunning && !IsGrapplingActive && Controller.IsOnGround )
			StickToGround( Settings.StepHeight );
		SyncParkourState();
	}

	public override void OnJump()
	{
		// PlayerController.InputJump calls Jump() -> OnJump() BEFORE overwriting Body.Velocity.
		// Record the jump so AddVelocity applies exact TF2 jump/slide-hop speed without burning double jump.
		_engineGroundJumpFired = true;
		_engineJumpWasSliding = _isSliding;
		if ( _isSliding ) EndSlide( playStopSound: false );
		_jumpBuffered = false;
		_sinceJumpPressed = 10f;
		_canCoyoteJump = false;
		_doubleJumpCooldownUntil = DoubleJumpCooldownSeconds;
	}

	protected override void OnUpdateAnimatorState( SkinnedModelRenderer renderer )
	{
		base.OnUpdateAnimatorState( renderer );
		if ( !renderer.IsValid() || !Controller.IsValid() ) return;
		SetAnimatorParameter( renderer, "b_grounded", Controller.IsOnGround || _isWallrunning );
		SetAnimatorParameter( renderer, "duck", _isSliding ? 1.0f : Controller.IsDucking ? 0.85f : 0f );
		SetAnimatorParameter( renderer, "b_jump", !Controller.IsOnGround && !_isWallrunning && Controller.Velocity.z > 40f );
		SetAnimatorParameter( renderer, "b_noclip", false );
	}

	void UpdateParkourState( float dt )
	{
		var settings = Settings;
		if ( _jumpBuffered && _sinceJumpPressed > JumpBufferSeconds ) _jumpBuffered = false;
		if ( _duckBuffered && _sinceDuckPressed > JumpBufferSeconds ) _duckBuffered = false;

		var grounded = Controller.IsOnGround && !_isWallrunning && !_isMantling;
		var duckHeld = Controller.UseInputControls && Input.Down( Controller.DuckButton );

		// Handle landing transition from air.
		if ( grounded && !_wasGrounded )
		{
			_doubleJumpsRemaining = 1;
			_canCoyoteJump = true;
			_hasPreviousWall = false;
			if ( _peakFallSpeed > 220f )
			{
				Parkour?.TriggerLandingDip( _peakFallSpeed );
				Parkour?.PlayMovementSound( _peakFallSpeed > 420f ? settings.SoundGroundImpact : settings.SoundLandDefault );
			}
			_peakFallSpeed = 0f;

			// Landing directly into a slide-hop when holding crouch at speed.
			if ( duckHeld && !_isSliding && Titanfall2PilotParkourRules.CanStartSlide( Controller.Body.Velocity, EyeForwardFlat(), settings ) )
				StartSlide();
		}

		if ( grounded )
		{
			_sinceGrounded = 0f;
			_canCoyoteJump = true;
			_peakFallSpeed = 0f;
		}
		else
		{
			if ( -Controller.Body.Velocity.z > _peakFallSpeed )
				_peakFallSpeed = -Controller.Body.Velocity.z;
		}
		_wasGrounded = grounded;

		// Keep ducking enforced while sliding so collision hull and camera stay low.
		if ( _isSliding )
		{
			Controller.UpdateDucking( true );
		}
		else if ( grounded && (_duckBuffered || duckHeld) && !Controller.IsDucking
			&& Titanfall2PilotParkourRules.CanStartSlide( Controller.Body.Velocity, EyeForwardFlat(), settings ) )
		{
			StartSlide();
			Controller.UpdateDucking( true );
		}

		// Cushion imminent high-speed ground impact before physics resolves touchdown so Jump Kit pilots take no fall damage.
		if ( !grounded && !_isWallrunning && !_isMantling && Controller.Body.Velocity.z < -Titanfall2PilotParkourRules.FallCushionTriggerSpeed )
		{
			var dropDist = MathF.Abs( Controller.Body.Velocity.z ) * dt + 14f;
			var groundProbe = TracePilotHull( WorldPosition + Vector3.Up * 2f, WorldPosition - Vector3.Up * dropDist );
			if ( groundProbe.Hit && !groundProbe.StartedSolid && groundProbe.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ )
			{
				var v = Controller.Body.Velocity;
				Controller.Body.Velocity = v.WithZ( Titanfall2PilotParkourRules.CushionFallSpeed( v.z ) );
			}
		}

		// State transitions: Mantle -> Wallrun -> Ground/Air.
		if ( _isMantling )
		{
			StepMantle( dt );
		}
		else if ( _isWallrunning )
		{
			UpdateWallrunAttachment( dt );
		}
		else if ( !grounded && _wallRepelUntil <= 0f )
		{
			if ( !TryStartMantle() )
				TryStartWallrun();
		}

		// Smooth SlideBlend for camera & viewmodel transitions.
		var slideTarget = _isSliding ? 1f : 0f;
		_slideBlend = MathX.Lerp( _slideBlend, slideTarget, Math.Clamp( dt * (_isSliding ? 14f : 10f), 0f, 1f ), true );
		SyncParkourState();
	}

	Vector3 ProjectToGroundPlane( Vector3 velocity )
	{
		if ( Controller.GroundIsDynamic ) return velocity;
		var normal = Controller.GroundNormal;
		if ( normal.z <= 0.001f ) return velocity;
		var flat = velocity.WithZ( 0 );
		var z = -Vector3.Dot( flat, normal ) / normal.z;
		return flat.WithZ( z ) + Controller.GroundVelocity;
	}

	void StepGroundVelocity( Rigidbody body, in Titanfall2PilotSettings settings, float dt )
	{
		var relative = body.Velocity - Controller.GroundVelocity;
		var flat = relative.WithZ( 0 );
		var speed = flat.Length;

		// Apply ground friction (reduced during brief jump buffer window so slide-hops keep speed cleanly).
		if ( speed > 0.01f && !_jumpBuffered )
		{
			var control = MathF.Max( speed, StopSpeed );
			var drop = control * GroundFriction * dt;
			var newSpeed = MathF.Max( 0f, speed - drop );
			flat *= newSpeed / speed;
		}

		var wish = Controller.WishVelocity.WithZ( 0 );
		var wishSpeed = wish.Length;
		if ( wishSpeed > 0.01f )
		{
			var wishDir = wish / wishSpeed;
			var currentInWish = Vector3.Dot( flat, wishDir );
			var addSpeed = wishSpeed - currentInWish;
			if ( addSpeed > 0f )
			{
				var accelSpeed = MathF.Min( GroundAccel * wishSpeed * dt, addSpeed );
				flat += wishDir * accelSpeed;
			}
		}

		flat = Titanfall2PilotParkourRules.ClampHorizontal( flat, Titanfall2PilotParkourRules.MaxHorizontalSpeed );
		if ( flat.LengthSquared < 0.25f && wishSpeed < 0.01f ) flat = Vector3.Zero;
		body.Velocity = ProjectToGroundPlane( flat.WithZ( relative.z ) );
	}

	void StartSlide()
	{
		var settings = Settings;
		var body = Controller.Body;
		var canBoost = _sinceSlideBoost >= settings.SlideBoostCooldown;
		var (nextVel, boosted) = Titanfall2PilotParkourRules.ApplySlideBoost( body.Velocity, canBoost, settings );
		body.Velocity = nextVel;
		_isSliding = true;
		_duckBuffered = false;
		_slideDirection = nextVel.WithZ( 0 ).LengthSquared > 1f ? nextVel.WithZ( 0 ).Normal : EyeForwardFlat();
		if ( boosted )
		{
			_sinceSlideBoost = 0f;
			Parkour?.TriggerThrusterBurst( 1.0f );
			Parkour?.PlayMovementSound( settings.SoundSlideBoost1P, settings.SoundSlideBoost3P );
		}
		Parkour?.PlayMovementSound( settings.SoundSlideStart1P );
	}

	void EndSlide( bool playStopSound )
	{
		if ( !_isSliding ) return;
		_isSliding = false;
		if ( playStopSound ) Parkour?.PlayMovementSound( Settings.SoundSlideStop1P );
	}

	bool TryStartWallrun()
	{
		var settings = Settings;
		var body = Controller.Body;
		var flatVel = body.Velocity.WithZ( 0 );
		var refDir = flatVel.Length >= 40f ? flatVel : (_wishDir.LengthSquared > 0.01f ? _wishDir * settings.WalkSpeed : Vector3.Zero);
		if ( flatVel.Length < WallrunMinEntrySpeed && refDir.Length < WallrunMinEntrySpeed ) return false;

		// Require separation from floor when descending/flat so normal walking beside a wall never attaches.
		if ( body.Velocity.z <= 40f )
		{
			var floorProbe = TracePilotHull( WorldPosition + Vector3.Up * 2f, WorldPosition - Vector3.Up * WallrunMinFloorClearance );
			if ( floorProbe.Hit && !floorProbe.StartedSolid && floorProbe.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ )
				return false;
		}

		var eyeForward = EyeForwardFlat();
		var eyeRight = Vector3.Cross( eyeForward, Vector3.Up ).Normal;
		var probeDist = BodyHalfWidth + settings.WallrunAllowedWallDist + 6f;
		var chest = WorldPosition + Vector3.Up * (Controller.CurrentHeight * 0.55f);

		// Probe pure sides and 45-degree forward diagonals so diagonal jump-ins attach effortlessly.
		ReadOnlySpan<Vector3> probeDirs =
		[
			eyeRight,
			-eyeRight,
			(eyeForward + eyeRight).Normal,
			(eyeForward - eyeRight).Normal,
			(-eyeForward * 0.35f + eyeRight).Normal,
			(-eyeForward * 0.35f - eyeRight).Normal
		];

		SceneTraceResult best = default;
		var found = false;
		foreach ( var dir in probeDirs )
		{
			var hit = TraceWallRay( chest, chest + dir * probeDist );
			if ( !IsRunnableWallHit( hit ) ) continue;
			if ( Titanfall2PilotParkourRules.IsSameWallBlocked( _hasPreviousWall, _prevWallNormal, _prevWallPos, hit.Normal, WorldPosition, settings ) )
				continue;

			// Allow diagonal approaches up to ~80 degrees into the wall while rejecting head-on perpendicular slams.
			var tangent = Titanfall2PilotParkourRules.WallTangent( hit.Normal, refDir.LengthSquared > 900f ? refDir : eyeForward );
			if ( Vector3.Dot( eyeForward, tangent ) < 0.05f ) continue;
			if ( flatVel.LengthSquared > 900f && Vector3.Dot( flatVel.Normal, tangent ) < 0.15f ) continue;

			if ( !found || hit.Distance < best.Distance )
			{
				best = hit;
				found = true;
			}
		}

		if ( !found ) return false;

		EndSlide( playStopSound: false );
		_isWallrunning = true;
		_wallNormal = best.Normal.WithZ( 0 ).Normal;
		_wallHugVelocity = Vector3.Zero;
		_wallSide = Titanfall2PilotParkourRules.WallSide( eyeForward, _wallNormal );
		_wallrunElapsed = 0f;
		_wallPushAwayTime = 0f;
		_sinceWallFootstep = 0f;
		_doubleJumpsRemaining = 1;
		_canCoyoteJump = false;
		body.Velocity = Titanfall2PilotParkourRules.ComputeWallrunStartVelocity( body.Velocity, _wallNormal, eyeForward, settings );
		Controller.PreventGrounding( 0.15f );
		Parkour?.TriggerThrusterBurst( 1.0f );
		Parkour?.PlayMovementSound( settings.SoundWallrunImpact );
		Parkour?.PlayMovementSound( settings.SoundWallrunStart1P, settings.SoundWallrunStart3P );
		SyncParkourState();
		return true;
	}

	void UpdateWallrunAttachment( float dt )
	{
		var settings = Settings;
		_wallrunElapsed += dt;
		_wallHugVelocity = Vector3.Zero;

		// Crouch immediately drops cleanly off the wall.
		if ( Controller.UseInputControls && Input.Down( Controller.DuckButton ) )
		{
			DetachWallrun( pushAway: true );
			return;
		}

		// Pushing directly away from the wall for > wallrun_pushAwayFallOffTime detaches.
		if ( _wishDir.LengthSquared > 0.01f && Vector3.Dot( _wishDir, _wallNormal ) > 0.65f )
		{
			_wallPushAwayTime += dt;
			if ( _wallPushAwayTime >= settings.WallrunPushAwayFallOffTime )
			{
				DetachWallrun( pushAway: true );
				return;
			}
		}
		else
		{
			_wallPushAwayTime = 0f;
		}

		if ( _wallrunElapsed >= settings.WallrunTimeLimit + settings.WallrunSlipDuration )
		{
			DetachWallrun( pushAway: true );
			return;
		}

		// Check floor contact below feet to transition cleanly back to ground.
		if ( Controller.Body.Velocity.z <= 10f )
		{
			var floorHit = TracePilotHull( WorldPosition + Vector3.Up * 2f, WorldPosition - Vector3.Up * 6f );
			if ( floorHit.Hit && !floorHit.StartedSolid && floorHit.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ )
			{
				EndWallrun( playEndSound: true );
				return;
			}
		}

		// Re-probe the current wall along -_wallNormal to stay attached on flat or smoothly curved walls.
		var chest = WorldPosition + Vector3.Up * (Controller.CurrentHeight * 0.55f);
		var probeDist = BodyHalfWidth + settings.WallrunAllowedWallDist + 10f;
		var hit = TraceWallRay( chest, chest - _wallNormal * probeDist );
		if ( !IsRunnableWallHit( hit ) )
		{
			DetachWallrun( pushAway: false );
			return;
		}

		var nextNormal = hit.Normal.WithZ( 0 ).Normal;
		if ( Vector3.Dot( _wallNormal, nextNormal ) < settings.WallrunAngleChangeMinCos )
		{
			DetachWallrun( pushAway: false );
			return;
		}

		_wallNormal = nextNormal;
		_wallSide = Titanfall2PilotParkourRules.WallSide( EyeForwardFlat(), _wallNormal );

		// Apply smooth corrective velocity toward the ideal wall offset instead of teleporting WorldPosition.
		var desiredDist = BodyHalfWidth + 1.5f;
		var error = hit.Distance - desiredDist;
		if ( MathF.Abs( error ) > 0.5f && MathF.Abs( error ) < 18f )
			_wallHugVelocity = -_wallNormal * Math.Clamp( error * 10f, -120f, 120f );

		// Periodic wallrun footsteps while moving along the wall.
		if ( _sinceWallFootstep >= WallrunFootstepInterval && Controller.Body.Velocity.WithZ( 0 ).Length > 120f )
		{
			_sinceWallFootstep = 0f;
			Parkour?.PlayMovementSound( settings.SoundWallrunFootstep );
		}
	}

	void ExecuteWallkick()
	{
		var settings = Settings;
		var wallNormal = _wallNormal;
		RecordPreviousWall( wallNormal );
		EndWallrun( playEndSound: true );

		_doubleJumpsRemaining = 1;
		_canCoyoteJump = false;
		_doubleJumpCooldownUntil = DoubleJumpCooldownSeconds;
		_wallRepelUntil = settings.WallrunRepelTime;
		Controller.Body.Velocity = Titanfall2PilotParkourRules.ComputeWallkickVelocity(
			Controller.Body.Velocity, wallNormal, _wishDir, settings );
		Controller.PreventGrounding( 0.16f );
		Controller.OnJumped();
		Parkour?.TriggerThrusterBurst( 1.25f );
		Parkour?.PlayMovementSound( settings.SoundJumpJetStart1P, settings.SoundJumpJetStart3P );
		SyncParkourState();
	}

	internal void BreakWallrunForMelee()
	{
		if ( !_isWallrunning ) return;
		RecordPreviousWall( _wallNormal );
		EndWallrun( playEndSound: true );
		_wallRepelUntil = 0.22f;
		SyncParkourState();
	}

	void DetachWallrun( bool pushAway )
	{
		var wallNormal = _wallNormal;
		RecordPreviousWall( wallNormal );
		EndWallrun( playEndSound: true );
		_wallRepelUntil = 0.16f;
		if ( pushAway )
			Controller.Body.Velocity += wallNormal * Settings.WallrunFallAwaySpeed;
	}

	void EndWallrun( bool playEndSound )
	{
		if ( !_isWallrunning ) return;
		_isWallrunning = false;
		_wallHugVelocity = Vector3.Zero;
		_wallrunElapsed = 0f;
		_wallPushAwayTime = 0f;
		if ( playEndSound )
			Parkour?.PlayMovementSound( Settings.SoundWallrunEnd1P, Settings.SoundWallrunEnd3P );
	}

	void RecordPreviousWall( Vector3 normal )
	{
		_hasPreviousWall = true;
		_prevWallNormal = normal;
		_prevWallPos = WorldPosition;
	}

	bool TryStartMantle()
	{
		if ( _mantleCooldownUntil > 0f || _isSliding ) return false;
		var eyeForward = EyeForwardFlat();
		// Require pushing toward the ledge or actively pressing jump against it.
		var pushingForward = _wishDir.LengthSquared > 0.01f && Vector3.Dot( _wishDir, eyeForward ) > 0.35f;
		if ( !pushingForward && !_jumpBuffered ) return false;

		var halfWidth = BodyHalfWidth;
		var reach = halfWidth + 18f;
		var wallProbeOrigin = WorldPosition + Vector3.Up * 38f;
		var wallHit = TraceWallRay( wallProbeOrigin, wallProbeOrigin + eyeForward * reach );
		if ( !wallHit.Hit || wallHit.StartedSolid || wallHit.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ || IsNoClimb( wallHit ) )
			return false;

		// Must be facing reasonably into the ledge wall.
		var wallNormalFlat = wallHit.Normal.WithZ( 0 );
		if ( wallNormalFlat.LengthSquared < 0.01f || Vector3.Dot( eyeForward, -wallNormalFlat.Normal ) < 0.55f )
			return false;

		// Probe downward onto the ledge top from above head height down to waist height.
		var ledgePointXY = wallHit.HitPosition - wallNormalFlat.Normal * (halfWidth + 6f);
		var topStart = new Vector3( ledgePointXY.x, ledgePointXY.y, WorldPosition.z + 92f );
		var topEnd = new Vector3( ledgePointXY.x, ledgePointXY.y, WorldPosition.z + 22f );
		var ledgeHit = TraceWallRay( topStart, topEnd );
		if ( !ledgeHit.Hit || ledgeHit.StartedSolid || ledgeHit.Normal.z < Titanfall2PilotParkourRules.WalkableNormalZ || IsNoClimb( ledgeHit ) )
			return false;

		var climbHeight = ledgeHit.HitPosition.z - WorldPosition.z;
		if ( climbHeight < 22f || climbHeight > 88f ) return false;

		// Verify standing headroom at the destination so we never mantle into a solid ceiling.
		var destPos = new Vector3( ledgePointXY.x, ledgePointXY.y, ledgeHit.HitPosition.z + 2f );
		var hullCheck = TracePilotHull( destPos + Vector3.Up * 2f, destPos + Vector3.Up * 2f );
		if ( hullCheck.StartedSolid || hullCheck.Hit ) return false;

		EndWallrun( playEndSound: false );
		EndSlide( playStopSound: false );
		_isMantling = true;
		_mantleStart = WorldPosition;
		_mantleEnd = destPos;
		_mantleDuration = Titanfall2PilotParkourRules.ComputeMantleDuration( climbHeight );
		_mantleElapsed = 0f;
		_jumpBuffered = false;
		_canCoyoteJump = false;
		Controller.Body.Velocity = Vector3.Zero;
		Controller.PreventGrounding( _mantleDuration + 0.05f );
		Parkour?.PlayMovementSound( climbHeight > 54f ? Settings.SoundMantleMedium : Settings.SoundMantleLow, Settings.SoundMantle3P );
		SyncParkourState();
		return true;
	}

	void StepMantle( float dt )
	{
		_mantleElapsed += dt;
		var progress = _mantleDuration > 0.01f ? Math.Clamp( _mantleElapsed / _mantleDuration, 0f, 1f ) : 1f;
		var nextPos = Titanfall2PilotParkourRules.EvaluateMantlePosition( _mantleStart, _mantleEnd, progress );
		Controller.Body.WorldPosition = nextPos;
		WorldPosition = nextPos;
		Controller.Body.Velocity = Vector3.Zero;

		if ( progress >= 1f )
		{
			_isMantling = false;
			_mantleCooldownUntil = Settings.TraversalCooldown;
			_doubleJumpsRemaining = 1;
			_hasPreviousWall = false;
			// Preserve snappy forward momentum cresting the ledge.
			var forward = (_mantleEnd - _mantleStart).WithZ( 0 );
			if ( forward.LengthSquared > 0.01f )
				Controller.Body.Velocity = forward.Normal * MathF.Min( Settings.SprintSpeed, 210f );
		}
	}

	void ConsumeJumpBuffer()
	{
		_jumpBuffered = false;
		_sinceJumpPressed = 10f;
	}

	Vector3 EyeForwardFlat()
	{
		var fwd = Controller.EyeTransform.Forward.WithZ( 0 );
		return fwd.LengthSquared > 0.0001f ? fwd.Normal : WorldRotation.Forward.WithZ( 0 ).Normal;
	}

	SceneTraceResult TraceWallRay( Vector3 start, Vector3 end ) =>
		Titanfall2CollisionQueries.Run( Scene.Trace.Ray( start, end )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile", "player" ), Titanfall2TraceMask.PlayerSolid );

	SceneTraceResult TracePilotHull( Vector3 start, Vector3 end )
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

	static bool IsRunnableWallHit( in SceneTraceResult hit ) =>
		hit.Hit && !hit.StartedSolid && Titanfall2PilotParkourRules.IsValidWallNormal( hit.Normal ) && !IsNoClimb( hit );

	static bool IsNoClimb( in SceneTraceResult hit )
	{
		if ( hit.GameObject.IsValid() && hit.GameObject.Tags.Has( Titanfall2CollisionQueries.NoWallrunTag ) )
			return true;
		var source = hit.Collider?.Components.Get<Titanfall2CollisionSource>();
		return source is { HasEffectiveContents: true } && (source.EffectiveContents & (uint)Titanfall2Contents.NoClimb) != 0;
	}

	void SyncParkourState()
	{
		if ( !Parkour.IsValid() || !Controller.IsValid() ) return;
		var flatSpeed = Controller.Body.IsValid() ? Controller.Body.Velocity.WithZ( 0 ).Length : 0f;
		Parkour.IsWallrunning = _isWallrunning;
		Parkour.WallrunSide = _isWallrunning ? _wallSide : 0f;
		Parkour.WallNormal = _isWallrunning ? _wallNormal : Vector3.Zero;
		Parkour.IsSliding = _isSliding;
		Parkour.SlideBlend = _slideBlend;
		Parkour.SlideDirection = _isSliding ? _slideDirection : Parkour.SlideDirection;
		Parkour.IsMantling = _isMantling;
		Parkour.MantleProgress = _isMantling && _mantleDuration > 0.01f ? Math.Clamp( _mantleElapsed / _mantleDuration, 0f, 1f ) : 0f;
		Parkour.IsSprinting = _wantsSprint && Controller.IsOnGround && flatSpeed > Settings.WalkSpeed + 15f;
		Parkour.IsAirborne = !Controller.IsOnGround && !_isWallrunning && !_isMantling;
		Parkour.HorizontalSpeed = flatSpeed;
		Parkour.DoubleJumpsRemaining = _doubleJumpsRemaining;
	}
}
