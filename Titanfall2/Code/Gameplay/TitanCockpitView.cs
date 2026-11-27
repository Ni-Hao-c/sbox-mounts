using Sandbox.Npcs;
using Chassis = Titanfall2TitanKit.Chassis;

/// <summary>
/// Renders the retail Titan cockpit canopy, first-person Titan weapon, and bone-merged
/// Titan chassis arms exclusively for the local seated pilot.
/// </summary>
[Title( "Titanfall 2 Cockpit View" ), Group( "Titanfall 2" )]
public sealed class Titanfall2TitanCockpitView : Component, ICameraModifier
{
	internal const string ClosedIdle = "atpov_cockpit_hatch_close_idle";
	internal const float CockpitFieldOfView = 82f;
	const string RetailUiSurface = "models/weapons/arms/titan_atlas_int_screen";
	const string TitanSwordViewModel = "models/weapons/titan_sword/atpov_titan_sword.mdl";
	const float InertiaStiffness = 110f;
	const float InertiaDamping = 17f;
	const float MaxWeaponSwayDegrees = 4.2f;
	const float CockpitParallaxScale = 0.28f;

	static Material _uiSurfaceMaterial;

	[Property] public Titanfall2TitanSeat Seat { get; set; }
	public bool HasRetailIdle { get; private set; }
	public int UiSurfaceOverrideCount { get; private set; }
	public bool IsShowing => _renderer.IsValid() && _renderer.RenderOptions.Overlay;
	public bool IsWeaponShowing => _weaponRenderer.IsValid() && _weaponRenderer.RenderOptions.Overlay;
	public bool IsArmsShowing => _armsRenderer.IsValid() && _armsRenderer.RenderOptions.Overlay;

	GameObject _visual;
	SkinnedModelRenderer _renderer;
	string _requestedPath;
	TimeUntil _resourceRetry;

	GameObject _weaponVisual;
	SkinnedModelRenderer _weaponRenderer;
	GameObject _armsVisual;
	SkinnedModelRenderer _armsRenderer;
	string _requestedWeaponPath;
	string _requestedArmsPath;
	TimeUntil _weaponRetry;
	HashSet<string> _weaponClips = new( StringComparer.OrdinalIgnoreCase );
	string _weaponAction;
	TimeUntil _weaponActionEnds;
	bool _wasReloading;
	bool _wasAiming;
	int _lastShotSerial = -1;

	readonly Titanfall2PilotCameraAnchor _cockpitCameraAnchor = new( "CAMERA" );
	readonly Titanfall2PilotCameraAnchor _weaponCameraAnchor = new( "CAMERA_BASE" );

	// Critically damped cockpit/weapon motion state.
	Angles _lastCameraAngles;
	Vector2 _swayOffset;
	Vector2 _swayVelocity;
	bool _inertiaReady;
	double _motionTime;
	float _stridePhase;
	float _strideBlend;
	float _recoilPitch;
	float _recoilKick;
	float _dashRoll;
	float _currentFov = CockpitFieldOfView;

	// Engine tags are limited to 32 characters.
	readonly string _bodyTag = "tf2-cockpit-" + Guid.NewGuid().ToString( "N" )[..20];
	CameraComponent _hiddenCamera;
	GameObject _hiddenActor;

	// The seated pilot's view replaces the suspended hand weapon's camera effects.
	int ICameraModifier.CameraOrder => 250;

	internal static string SourceModel( Chassis chassis ) => chassis switch
	{
		Chassis.Ronin or Chassis.Northstar => "models/weapons/arms/pov_titan_light_cockpit.mdl",
		Chassis.Legion or Chassis.Scorch => "models/weapons/arms/pov_titan_heavy_cockpit.mdl",
		Chassis.Monarch or Chassis.Ion or Chassis.Tone => "models/weapons/arms/pov_titan_medium_cockpit.mdl",
		_ => null
	};

	internal static string SourceArmsModel( Chassis chassis ) => chassis switch
	{
		Chassis.Monarch => "models/weapons/arms/pov_titan_medium_vanguard.mdl",
		Chassis.Ion => "models/weapons/arms/pov_titan_medium_wraith.mdl",
		Chassis.Tone => "models/weapons/arms/pov_titan_medium_ajax.mdl",
		Chassis.Legion => "models/weapons/arms/pov_titan_heavy_deadbolt.mdl",
		Chassis.Scorch => "models/weapons/arms/pov_titan_heavy_ogre.mdl",
		Chassis.Northstar => "models/weapons/arms/pov_titan_light_raptor.mdl",
		Chassis.Ronin => "models/weapons/arms/pov_titan_light_locust.mdl",
		_ => null
	};

	internal static string ResolveWeaponViewModelPath( string mountIdent, string explicitViewModel, string gunModelPath, bool swordCore )
	{
		var ident = string.IsNullOrWhiteSpace( mountIdent ) ? "titanfall2" : mountIdent;
		if ( swordCore ) return $"mount://{ident}/{TitanSwordViewModel}.titanpov.vmdl";
		if ( !string.IsNullOrWhiteSpace( explicitViewModel ) ) return explicitViewModel;

		var source = gunModelPath?.ToLowerInvariant() switch
		{
			{ } p when p.Contains( "xo16" ) => "models/weapons/titan_xo16_shorty/atpov_xo16shorty.mdl",
			{ } p when p.Contains( "predator" ) => "models/weapons/titan_predator/atpov_titan_predator.mdl",
			{ } p when p.Contains( "particle_accelerator" ) => "models/weapons/titan_particle_accelerator/atpov_titan_particle_accelerator.mdl",
			{ } p when p.Contains( "leadwall" ) || p.Contains( "triple_threat" ) => "models/weapons/titan_triple_threat/atpov_titan_triple_threat.mdl",
			{ } p when p.Contains( "sniper" ) => "models/weapons/atpov_titan_sniper_rifle/atpov_titan_sniper_rifle.mdl",
			{ } p when p.Contains( "40mm" ) => "models/weapons/thr_40mm/atpov_thr_40mm.mdl",
			{ } p when p.Contains( "meteor" ) || p.Contains( "thermite_launcher" ) => "models/weapons/titan_thermite_launcher/atpov_titan_thermite_launcher.mdl",
			{ } p when p.Contains( "sword" ) => TitanSwordViewModel,
			_ => null
		};
		return source is null ? null : $"mount://{ident}/{source}.titanpov.vmdl";
	}

	PlayerController GetPilot()
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || !Seat.IsValid() || !Seat.Active
			|| Seat.Actor is not { Active: true, IsTitan: true, Dead: false, IsInTermination: false } || Seat.IsEmbarking ) return null;
		var pilot = Seat.GetOccupant();
		return pilot is { Active: true, IsProxy: false } && pilot.Mode is Sandbox.Movement.SitMoveMode
			&& pilot.GetComponent<BaseNpc>() is not { Health: <= 0 } ? pilot : null;
	}

	protected override void OnUpdate()
	{
		if ( !GetPilot().IsValid() ) { Clear(); return; }
		var actor = Seat.Actor;
		var kit = actor.GetComponent<Titanfall2TitanKit>();
		var core = actor.GetComponent<Titanfall2TitanCore>();
		var chassis = kit?.Kit ?? Chassis.None;
		if ( string.IsNullOrWhiteSpace( actor.MountIdent ) ) { Clear(); return; }

		UpdateCockpitResource( actor.MountIdent, chassis );
		UpdateWeaponAndArmsResource( actor, chassis, core is { SwordCoreActive: true } );
		UpdateWeaponAnimation( actor, kit, core );
	}

	void UpdateCockpitResource( string mountIdent, Chassis chassis )
	{
		var source = SourceModel( chassis );
		if ( source is null ) { ClearCockpitVisual(); return; }
		// The full resource imports the authored two-frame closed-hatch pose.
		var path = $"mount://{mountIdent}/{source}.vmdl";
		if ( path != _requestedPath ) { ClearCockpitVisual(); _requestedPath = path; }
		if ( _renderer.IsValid() || _resourceRetry > 0 ) return;
		_resourceRetry = 5;
		CreateVisual( path );
	}

	void UpdateWeaponAndArmsResource( Titanfall2Npc actor, Chassis chassis, bool swordCore )
	{
		var weaponPath = ResolveWeaponViewModelPath( actor.MountIdent, actor.ViewModelPath, actor.GunModelPath, swordCore );
		var armsSource = SourceArmsModel( chassis );
		var armsPath = armsSource is null ? null : $"mount://{actor.MountIdent}/{armsSource}.visual.vmdl";
		if ( weaponPath != _requestedWeaponPath || armsPath != _requestedArmsPath )
		{
			ClearWeaponVisual();
			_requestedWeaponPath = weaponPath;
			_requestedArmsPath = armsPath;
			_weaponRetry = 0;
		}
		if ( weaponPath is null || _weaponRenderer.IsValid() || _weaponRetry > 0 ) return;
		_weaponRetry = 3;
		CreateWeaponVisual( weaponPath, armsPath, actor );
	}

	void CreateVisual( string path )
	{
		var model = Titanfall2GameplayVisuals.GetModel( path );
		if ( model is null || model.IsError || model.Attachments.Get( "CAMERA" )?.Bone is null ) return;
		_visual = new GameObject( Scene, false, "TF2 cockpit POV" );
		_visual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
		_visual.Tags.Add( "firstperson", "viewmodel" );
		_renderer = _visual.AddComponent<SkinnedModelRenderer>();
		_renderer.Model = model;
		_renderer.UseAnimGraph = false;
		_renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		_renderer.RenderOptions.Game = false;
		_renderer.RenderOptions.Overlay = false;
		OverrideRetailUiSurface();
		HasRetailIdle = model.AnimationNames.Contains( ClosedIdle, StringComparer.OrdinalIgnoreCase );
		_renderer.Sequence.Name = HasRetailIdle ? ClosedIdle : "bindpose";
		_renderer.Sequence.Looping = true;
		_renderer.Sequence.Blending = false;
		_visual.Enabled = true;
	}

	void CreateWeaponVisual( string weaponPath, string armsPath, Titanfall2Npc actor )
	{
		var weaponModel = Titanfall2GameplayVisuals.GetModel( weaponPath );
		if ( weaponModel is null || weaponModel.IsError ) return;

		_weaponVisual = new GameObject( Scene, false, "TF2 Titan weapon POV" );
		_weaponVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
		_weaponVisual.Tags.Add( "firstperson", "viewmodel" );
		_weaponRenderer = _weaponVisual.AddComponent<SkinnedModelRenderer>();
		_weaponRenderer.Model = weaponModel;
		_weaponRenderer.UseAnimGraph = false;
		_weaponRenderer.CreateAttachments = true;
		_weaponRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		_weaponRenderer.RenderOptions.Game = false;
		_weaponRenderer.RenderOptions.Overlay = false;
		_weaponRenderer.Sequence.Blending = true;

		_weaponClips = new HashSet<string>( weaponModel.AnimationNames, StringComparer.OrdinalIgnoreCase );
		_weaponAction = null;
		_wasReloading = actor.Reloading;
		_wasAiming = false;
		_lastShotSerial = actor.ShotSerial;

		if ( !string.IsNullOrWhiteSpace( armsPath )
			&& Titanfall2GameplayVisuals.GetModel( armsPath ) is { IsError: false } armsModel )
		{
			_armsVisual = new GameObject( _weaponVisual, false, "TF2 Titan arms POV" );
			_armsVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_armsVisual.Tags.Add( "firstperson", "viewmodel" );
			_armsRenderer = _armsVisual.AddComponent<SkinnedModelRenderer>();
			_armsRenderer.Model = armsModel;
			_armsRenderer.UseAnimGraph = false;
			_armsRenderer.BoneMergeTarget = _weaponRenderer;
			_armsRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			_armsRenderer.RenderOptions.Game = false;
			_armsRenderer.RenderOptions.Overlay = false;
			_armsVisual.Enabled = true;
		}

		_weaponVisual.Enabled = true;
		PlayWeaponClip( _weaponClips.Contains( Titanfall2PilotViewModel.Draw )
			? Titanfall2PilotViewModel.Draw
			: Titanfall2PilotViewModel.Idle, restart: true );
	}

	void UpdateWeaponAnimation( Titanfall2Npc actor, Titanfall2TitanKit kit, Titanfall2TitanCore core )
	{
		if ( !_weaponRenderer.IsValid() || !_weaponRenderer.SceneModel.IsValid() ) return;

		var shotSerial = actor.ShotSerial;
		var fired = _lastShotSerial >= 0 && shotSerial != _lastShotSerial;
		_lastShotSerial = shotSerial;

		var aiming = kit is { Kit: Chassis.Ion, IonZoomFraction: > 0.25f };
		var reloading = actor.Reloading;
		var movingFast = Seat.DriveVelocity.WithZ( 0 ).Length > 120f;

		if ( fired )
		{
			var fireClip = aiming && _weaponClips.Contains( Titanfall2PilotViewModel.AdsFire )
				? Titanfall2PilotViewModel.AdsFire
				: Titanfall2PilotViewModel.Fire;
			PlayWeaponClip( fireClip, restart: true );
			_recoilPitch = MathF.Min( 3.2f, _recoilPitch + 1.15f );
			_recoilKick = MathF.Min( 3.6f, _recoilKick + 1.4f );
			if ( core is not { SwordCoreActive: true } ) SpawnFirstPersonShotEffects( actor );
		}
		else if ( reloading && !_wasReloading )
		{
			var reloadClip = actor.Rounds <= 0 && _weaponClips.Contains( Titanfall2PilotViewModel.EmptyReload )
				? Titanfall2PilotViewModel.EmptyReload
				: Titanfall2PilotViewModel.Reload;
			var duration = MathF.Max( 0.2f, actor.ReloadSeconds * (core?.ReloadMultiplier ?? 1f) );
			PlayWeaponClip( reloadClip, restart: true, targetDuration: duration );
		}
		else if ( !reloading && _wasReloading && _weaponAction is Titanfall2PilotViewModel.Reload or Titanfall2PilotViewModel.EmptyReload )
		{
			PlayWeaponClip( Titanfall2PilotViewModel.Idle, restart: false );
		}
		else if ( !reloading && _weaponAction != Titanfall2PilotViewModel.Draw && _weaponAction != Titanfall2PilotViewModel.Fire && _weaponAction != Titanfall2PilotViewModel.AdsFire )
		{
			if ( aiming != _wasAiming )
			{
				PlayWeaponClip( aiming ? Titanfall2PilotViewModel.AdsIn : Titanfall2PilotViewModel.AdsOut, restart: false );
			}
			else if ( _weaponActionEnds <= 0 )
			{
				var steady = aiming && _weaponClips.Contains( Titanfall2PilotViewModel.AdsIdle )
					? Titanfall2PilotViewModel.AdsIdle
					: movingFast && !aiming && _weaponClips.Contains( Titanfall2PilotViewModel.Sprint )
						? Titanfall2PilotViewModel.Sprint
						: Titanfall2PilotViewModel.Idle;
				if ( _weaponAction != steady ) PlayWeaponClip( steady, restart: false );
			}
		}
		else if ( _weaponActionEnds <= 0 && !reloading )
		{
			var steady = aiming && _weaponClips.Contains( Titanfall2PilotViewModel.AdsIdle )
				? Titanfall2PilotViewModel.AdsIdle
				: Titanfall2PilotViewModel.Idle;
			PlayWeaponClip( steady, restart: false );
		}

		_wasReloading = reloading;
		_wasAiming = aiming;
	}

	void PlayWeaponClip( string clip, bool restart, float targetDuration = 0f )
	{
		if ( !_weaponRenderer.IsValid() || !_weaponRenderer.SceneModel.IsValid() ) return;
		if ( !_weaponClips.Contains( clip ) ) clip = Titanfall2PilotViewModel.Idle;
		if ( !_weaponClips.Contains( clip ) ) return;

		var seq = _weaponRenderer.Sequence;
		var looping = clip is Titanfall2PilotViewModel.Idle or Titanfall2PilotViewModel.AdsIdle or Titanfall2PilotViewModel.Sprint;
		if ( !restart && _weaponAction == clip && seq.Name == clip ) return;

		seq.Name = clip;
		seq.Looping = looping;
		seq.Time = 0;
		var rate = targetDuration > 0.05f && seq.Duration > 0.05f
			? Math.Clamp( seq.Duration / targetDuration, 0.35f, 3.5f )
			: 1f;
		seq.PlaybackRate = rate;
		_weaponAction = clip;
		_weaponActionEnds = looping ? 0f : MathF.Max( 0.05f, seq.Duration / MathF.Max( 0.1f, rate ) );
	}

	void SpawnFirstPersonShotEffects( Titanfall2Npc actor )
	{
		if ( !_weaponRenderer.IsValid() ) return;
		var presentation = actor.WeaponPresentation;
		var muzzleName = string.IsNullOrEmpty( presentation.MuzzleAttachment ) ? "muzzle_flash" : presentation.MuzzleAttachment;
		var muzzle = _weaponRenderer.GetAttachmentObject( muzzleName ) ?? _weaponRenderer.GetAttachmentObject( "muzzle_flash" )
			?? _weaponRenderer.GetAttachmentObject( "muzzle" );
		if ( muzzle.IsValid() )
		{
			var effectName = !string.IsNullOrEmpty( presentation.MuzzleView )
				? presentation.MuzzleView
				: presentation.MuzzleWorld;
			if ( !string.IsNullOrEmpty( effectName ) )
				Titanfall2RetailWeaponEffects.Spawn( muzzle, actor.MountIdent, effectName, firstPerson: true, lifetime: 0.45f );
		}

		var shellName = string.IsNullOrEmpty( presentation.ShellAttachment ) ? "shell" : presentation.ShellAttachment;
		var shellPort = _weaponRenderer.GetAttachmentObject( shellName ) ?? _weaponRenderer.GetAttachmentObject( "shell" );
		if ( shellPort.IsValid() && !string.IsNullOrEmpty( presentation.ShellView ) )
			Titanfall2RetailWeaponEffects.Spawn( shellPort, actor.MountIdent, presentation.ShellView, firstPerson: true, lifetime: 0.9f );
	}

	void OverrideRetailUiSurface()
	{
		// Retail common.rpak uses ubercockpit3samp2222_fix on this curved UI
		// carrier. The generic opaque material hides the outside view. Its UI is
		// provided by our native cockpit HUD; leave the physical frame unchanged.
		for ( var slot = 0; slot < _renderer.Materials.Count; slot++ )
		{
			var name = _renderer.Materials.GetOriginal( slot )?.Name?.Replace( '\\', '/' );
			if ( name is null ) continue;
			while ( name.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase )
				|| name.EndsWith( ".vmt", StringComparison.OrdinalIgnoreCase ) )
				name = name[..name.LastIndexOf( '.' )];
			if ( !name.Equals( RetailUiSurface, StringComparison.OrdinalIgnoreCase )
				&& !name.EndsWith( "/" + RetailUiSurface, StringComparison.OrdinalIgnoreCase ) ) continue;
			_uiSurfaceMaterial ??= MaterialLoader.CreateRuntimeMaterial( "titanfall2/cockpit/native_ui_surface",
				new Titanfall2MaterialMetadata( Titanfall2MaterialMode.AlphaBlend, IsUnlit: true ) );
			// titanfall2_alpha discards opacity < 0.0001 and never writes depth.
			_uiSurfaceMaterial.Set( "g_flT2MaterialOpacity", 0f );
			_renderer.Materials.SetOverride( slot, _uiSurfaceMaterial );
			UiSurfaceOverrideCount++;
		}
	}

	void StepCockpitMotion( Transform eye )
	{
		var currentAngles = eye.Rotation.Angles();
		if ( !_inertiaReady )
		{
			_lastCameraAngles = currentAngles;
			_motionTime = Time.NowDouble;
			_inertiaReady = true;
			return;
		}
		if ( _motionTime == Time.NowDouble ) return;
		_motionTime = Time.NowDouble;

		var dt = Math.Clamp( Time.Delta, 0f, 0.05f );
		var dYaw = MathX.DeltaDegrees( currentAngles.yaw, _lastCameraAngles.yaw );
		var dPitch = MathX.DeltaDegrees( currentAngles.pitch, _lastCameraAngles.pitch );
		_lastCameraAngles = currentAngles;

		_swayOffset.x = Math.Clamp( _swayOffset.x + dYaw * 0.11f, -MaxWeaponSwayDegrees, MaxWeaponSwayDegrees );
		_swayOffset.y = Math.Clamp( _swayOffset.y + dPitch * 0.11f, -MaxWeaponSwayDegrees, MaxWeaponSwayDegrees );

		var accel = -InertiaStiffness * _swayOffset - InertiaDamping * _swayVelocity;
		_swayVelocity += accel * dt;
		_swayOffset += _swayVelocity * dt;

		var planarSpeed = Seat.IsValid() ? Seat.DriveVelocity.WithZ( 0 ).Length : 0f;
		var targetStride = Math.Clamp( planarSpeed / MathF.Max( 120f, Seat?.DriveSpeed ?? 180f ), 0f, 1.25f );
		_strideBlend = MathX.Lerp( _strideBlend, targetStride, Math.Clamp( dt * 8f, 0f, 1f ) );
		if ( _strideBlend > 0.02f ) _stridePhase = (_stridePhase + dt * 5.2f * (0.65f + 0.35f * _strideBlend)) % (MathF.PI * 2f);

		_recoilPitch = MathX.Lerp( _recoilPitch, 0f, Math.Clamp( dt * 14f, 0f, 1f ) );
		_recoilKick = MathX.Lerp( _recoilKick, 0f, Math.Clamp( dt * 16f, 0f, 1f ) );

		var dashVisual = Seat.IsValid() ? Seat.DashRemainingVisual : 0f;
		var lateralDash = dashVisual > 0f && Seat.DashDirection.LengthSquared > 0.01f
			? Vector3.Dot( eye.Rotation.Right, Seat.DashDirection ) * dashVisual
			: 0f;
		_dashRoll = MathX.Lerp( _dashRoll, -lateralDash * 2.6f, Math.Clamp( dt * 14f, 0f, 1f ) );

		var kit = Seat?.Actor?.GetComponent<Titanfall2TitanKit>();
		var core = Seat?.Actor?.GetComponent<Titanfall2TitanCore>();
		var ionZoom = kit is { Kit: Chassis.Ion } ? kit.IonZoomFraction : 0f;
		var phaseFov = kit is { IsPhasing: true } ? 10f : 0f;
		var flightFov = core is { FlightCoreActive: true } ? 6f : 0f;
		var targetFov = CockpitFieldOfView + dashVisual * 6f + phaseFov + flightFov - ionZoom * 10f;
		_currentFov = MathX.Lerp( _currentFov, targetFov, Math.Clamp( dt * 12f, 0f, 1f ) );
	}

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		var pilot = GetPilot();
		if ( camera != Scene.Camera || !pilot.IsValid() ) return;
		var hasAnyVisual = TryGetLocalCockpitCamera( out _ ) || TryGetLocalWeaponCamera( out _ );
		if ( !hasAnyVisual ) return;

		var eye = pilot.EyeTransform;
		StepCockpitMotion( eye );

		var strideVertical = -MathF.Abs( MathF.Sin( _stridePhase ) ) * 1.15f * _strideBlend;
		var strideRoll = MathF.Sin( _stridePhase ) * 0.45f * _strideBlend;
		var cameraAngles = new Angles( -_recoilPitch * 0.35f, 0f, _dashRoll + strideRoll );

		view.Position = eye.Position + Vector3.Up * strideVertical;
		view.Rotation = eye.Rotation * Rotation.From( cameraAngles );
		view.FieldOfView = _currentFov;
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view )
	{
		if ( camera != Scene.Camera ) return;
		var pilot = GetPilot();
		var active = pilot.IsValid();

		Transform localCockpit = default;
		Transform localWeapon = default;
		var cockpitReady = active && TryGetLocalCockpitCamera( out localCockpit );
		var weaponReady = active && TryGetLocalWeaponCamera( out localWeapon );

		if ( !cockpitReady && !weaponReady )
		{
			SetOverlayVisible( false, false );
			RestoreExterior();
			return;
		}

		if ( cockpitReady )
		{
			var cockpitSway = Rotation.From( new Angles(
				_swayOffset.y * CockpitParallaxScale,
				_swayOffset.x * CockpitParallaxScale,
				_swayOffset.x * 0.12f ) );
			var cockpitCamera = new Transform( view.Position, view.Rotation * cockpitSway );
			_visual.WorldTransform = cockpitCamera.ToWorld( localCockpit.ToLocal( global::Transform.Zero ) );
		}

		if ( weaponReady )
		{
			var weaponBob = view.Rotation * new Vector3(
				-_recoilKick * 0.9f,
				MathF.Cos( _stridePhase ) * 0.75f * _strideBlend,
				MathF.Sin( _stridePhase * 2f ) * 0.55f * _strideBlend );
			var weaponSway = Rotation.From( new Angles(
				_swayOffset.y - _recoilPitch * 0.65f,
				_swayOffset.x,
				_swayOffset.x * 0.35f ) );
			var weaponCamera = new Transform( view.Position + weaponBob, view.Rotation * weaponSway );
			_weaponVisual.WorldTransform = weaponCamera.ToWorld( localWeapon.ToLocal( global::Transform.Zero ) );
		}

		SetOverlayVisible( cockpitReady, weaponReady );
		HideExterior( camera );
	}

	void SetOverlayVisible( bool cockpit, bool weapon )
	{
		if ( _renderer.IsValid() ) _renderer.RenderOptions.Overlay = cockpit;
		if ( _weaponRenderer.IsValid() ) _weaponRenderer.RenderOptions.Overlay = weapon;
		if ( _armsRenderer.IsValid() ) _armsRenderer.RenderOptions.Overlay = weapon;
	}

	void HideExterior( CameraComponent camera )
	{
		var actor = Seat.Actor.GameObject;
		if ( _hiddenCamera == camera && _hiddenActor == actor ) return;
		RestoreExterior();
		_hiddenActor = actor;
		_hiddenCamera = camera;
		// Exclude only from this pilot's camera, retaining world rendering/shadows
		// for other cameras. The cockpit and weapon visuals are separate scene roots.
		actor.Tags.Add( _bodyTag );
		camera.RenderExcludeTags.Add( _bodyTag );
	}

	void RestoreExterior()
	{
		if ( _hiddenCamera.IsValid() ) _hiddenCamera.RenderExcludeTags.Remove( _bodyTag );
		if ( _hiddenActor.IsValid() ) _hiddenActor.Tags.Remove( _bodyTag );
		_hiddenCamera = null;
		_hiddenActor = null;
	}

	bool TryGetLocalCockpitCamera( out Transform camera )
	{
		camera = default;
		return _visual.IsValid() && _renderer.IsValid() && _cockpitCameraAnchor.TryRead( _renderer, out camera ) && camera.Position.IsFinite;
	}

	bool TryGetLocalWeaponCamera( out Transform camera )
	{
		camera = default;
		return _weaponVisual.IsValid() && _weaponRenderer.IsValid() && _weaponCameraAnchor.TryRead( _weaponRenderer, out camera ) && camera.Position.IsFinite;
	}

	void ClearCockpitVisual()
	{
		if ( _visual.IsValid() ) _visual.Destroy();
		_visual = null;
		_renderer = null;
		_requestedPath = null;
		HasRetailIdle = false;
		UiSurfaceOverrideCount = 0;
		_resourceRetry = 0;
	}

	void ClearWeaponVisual()
	{
		if ( _armsVisual.IsValid() ) _armsVisual.Destroy();
		if ( _weaponVisual.IsValid() ) _weaponVisual.Destroy();
		_armsVisual = null;
		_armsRenderer = null;
		_weaponVisual = null;
		_weaponRenderer = null;
		_requestedWeaponPath = null;
		_requestedArmsPath = null;
		_weaponClips.Clear();
		_weaponAction = null;
		_weaponRetry = 0;
		_lastShotSerial = -1;
	}

	void Clear()
	{
		RestoreExterior();
		ClearCockpitVisual();
		ClearWeaponVisual();
		_inertiaReady = false;
		_swayOffset = Vector2.Zero;
		_swayVelocity = Vector2.Zero;
		_recoilPitch = 0;
		_recoilKick = 0;
		_dashRoll = 0;
		_strideBlend = 0;
		_currentFov = CockpitFieldOfView;
	}

	protected override void OnDisabled() => Clear();
	protected override void OnDestroy() => Clear();
}
