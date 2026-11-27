using Sandbox.Movement;

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

