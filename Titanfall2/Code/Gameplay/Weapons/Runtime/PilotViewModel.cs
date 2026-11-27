/// <summary>The original TF2 pilot first-person rig drives the gun and the bone-merged pilot arms.</summary>
[Hide]
public sealed class Titanfall2PilotViewModel : BaseWeaponModel
{
	internal const string PilotArms = "models/weapons/arms/pov_pilot_light_ged_m.mdl";
	internal const string Idle = "pilot_fp_idle";
	internal const string Draw = "pilot_fp_draw";
	internal const string Fire = "pilot_fp_fire";
	internal const string Reload = "pilot_fp_reload";
	internal const string EmptyReload = "pilot_fp_reload_empty";
	internal const string ReloadSegLoop = "pilot_fp_reload_seg_loop";
	internal const string ReloadSegEnd = "pilot_fp_reload_seg_end";
	internal const string ReloadEmptySegEnd = "pilot_fp_reload_empty_seg_end";
	internal const string AdsIn = "pilot_fp_ads_in";
	internal const string AdsIdle = "pilot_fp_ads_idle";
	internal const string AdsFire = "pilot_fp_ads_fire";
	internal const string AdsOut = "pilot_fp_ads_out";
	internal const string Sprint = "pilot_fp_sprint";
	internal const string SprintRaise = "pilot_fp_sprint_raise";
	internal const string OneHandedIdle = "pilot_fp_onehanded_idle";
	internal const string Charge = "pilot_fp_charge";
	internal const string Cooldown = "pilot_fp_cooldown";
	internal const string CooldownAds = "pilot_fp_cooldown_ads";
	internal static string EmptyReloadLate( int milestone ) => EmptyReload + "_late" + milestone;
	readonly List<GameObject> _stateParticles = [];

	const float InertiaStiffness = 140f;
	const float InertiaDamping = 18f;
	const float MaxInertiaDegrees = 4.5f;

	Titanfall2Weapon _owner;
	HashSet<string> _clips = new( StringComparer.OrdinalIgnoreCase );
	Dictionary<string, Titanfall2PilotViewData.ClipEvent[]> _eventsByClip = new( StringComparer.OrdinalIgnoreCase );
	string _clipBodyGroup;
	TimeUntil _actionEnds;
	string _action;
	float _lastCycle;
	bool _aiming;
	bool _sprinting;
	bool _oneHanded;
	bool _reloadCancelled;
	Titanfall2PilotAnimationPlayer _animation;
	readonly Titanfall2PilotCameraAnchor _cameraAnchor = new();
	float _zoom;
	internal bool UsesContinuousAds => _animation?.IsReady == true;
	internal float PresentationZoom => _animation?.Zoom ?? _zoom;
	internal string PresentationAction => _action;
	internal float PresentationCycle => _animation is not null
		? _action == Fire ? _animation.FireCycle : _animation.ActionCycle
		: Renderer.IsValid() ? Renderer.Sequence.TimeNormalized : 0;
	internal int FireSerial => _animation?.FireSerial ?? 0;

	// Critically damped look-inertia state in view-local yaw/pitch degrees.
	Angles _lastCameraAngles;
	Vector2 _swayOffset;
	Vector2 _swayVelocity;
	float _wallrunBlend;
	float _slidePoseBlend;
	float _mantlePoseBlend;
	bool _inertiaReady;
	double _inertiaTime;

	internal bool HasAds => _clips.Contains( AdsIn ) && _clips.Contains( AdsIdle );

	internal static bool TryAttach( GameObject view, Titanfall2Weapon owner, string path )
	{
		if ( view.GetComponent<Titanfall2PilotViewModel>() is not { Renderer: { } driver } presentation
			|| !driver.Model.AnimationNames.Contains( Idle )
			|| Titanfall2GameplayVisuals.GetModel( path ) is not { IsError: false } arms
			|| arms.Bones.GetBone( "def_r_wrist" ) is null || arms.Bones.GetBone( "def_l_wrist" ) is null )
			return false;

		var child = new GameObject( view, false, "TF2 pilot arms" );
		child.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var skin = child.AddComponent<SkinnedModelRenderer>();
		skin.Model = arms;
		skin.UseAnimGraph = false;
		skin.BoneMergeTarget = driver;
		skin.RenderOptions.Game = false;
		skin.RenderOptions.Overlay = true;
		skin.RenderType = ModelRenderer.ShadowRenderType.Off;
		child.Enabled = true;

		presentation._owner = owner;
		presentation._clips = new HashSet<string>( driver.Model.AnimationNames, StringComparer.OrdinalIgnoreCase );
		presentation.LoadEvents( driver.Model );
		driver.RenderType = ModelRenderer.ShadowRenderType.Off;
		driver.Sequence.Blending = true;
		presentation._animation?.Dispose();
		presentation._animation = null;
		if ( driver.Model.GetData<Titanfall2PilotViewData>() is { ContinuousAds: true } animationData )
		{
			try { presentation._animation = new( driver, animationData ); }
			catch ( Exception error )
			{
				driver.UseAnimGraph = false;
				Titanfall2Log.Warning( $"TF2 continuous pilot ADS unavailable for '{owner.ViewModelPath}': {error.Message}" );
			}
		}
		presentation.OnDeploy();
		return true;
	}

	void LoadEvents( Model model )
	{
		_eventsByClip.Clear();
		if ( model.GetData<Titanfall2PilotViewData>() is { Events.Length: > 0 } data )
		{
			foreach ( var group in data.Events.GroupBy( e => e.Clip, StringComparer.OrdinalIgnoreCase ) )
				_eventsByClip[group.Key] = group.OrderBy( e => e.Cycle ).ToArray();
		}
		if ( model.GetData<Titanfall2ModelData>() is { BodyGroups.Length: > 0 } meta )
		{
			_clipBodyGroup = meta.BodyGroups
				.FirstOrDefault( g => g.Name.Contains( "mag", StringComparison.OrdinalIgnoreCase )
					|| g.Name.Contains( "clip", StringComparison.OrdinalIgnoreCase ) )?.Name;
		}
	}

	internal void SetLocomotionState( bool aiming, bool sprinting, bool oneHanded )
		=> SetLocomotionState( aiming, sprinting, oneHanded, aiming ? 1 : 0 );

	internal void SetLocomotionState( bool aiming, bool sprinting, bool oneHanded, float zoom )
	{
		if ( !Renderer.IsValid() ) return;
		SetZoom( zoom );
		var locked = _action is Reload or EmptyReload or ReloadSegLoop or Draw or Charge or Cooldown || _action?.StartsWith( EmptyReload + "_late", StringComparison.Ordinal ) == true;
		if ( locked )
		{
			_aiming = false;
			_sprinting = false;
			_oneHanded = oneHanded;
			return;
		}

		aiming &= HasAds;
		sprinting &= !aiming && _clips.Contains( Sprint );
		oneHanded &= !aiming && !sprinting && _clips.Contains( OneHandedIdle );
		if ( _animation is not null )
		{
			// ADS is a sampled base pose, not an action which can replace a shot.
			// Retaining the fire timeline also makes a mid-shot ADS reversal continuous.
			_aiming = aiming;
			if ( sprinting != _sprinting )
			{
				_sprinting = sprinting;
				if ( sprinting ) Play( Sprint );
				else if ( oneHanded ) Play( OneHandedIdle );
				else if ( _clips.Contains( SprintRaise ) ) Play( SprintRaise );
				else Play( Idle );
			}
			if ( oneHanded != _oneHanded && _action is Idle or AdsIdle or OneHandedIdle or SprintRaise )
				Play( oneHanded ? OneHandedIdle : Idle );
			_oneHanded = oneHanded;
			return;
		}

		if ( aiming != _aiming )
		{
			_aiming = aiming;
			if ( aiming )
				Play( AdsIn );
			else if ( _clips.Contains( AdsOut ) )
				Play( AdsOut );
			else
				Play( Idle );
			_sprinting = false;
			_oneHanded = oneHanded;
			return;
		}

		if ( sprinting != _sprinting )
		{
			_sprinting = sprinting;
			if ( sprinting )
				Play( Sprint );
			else if ( oneHanded )
				Play( OneHandedIdle );
			else if ( _clips.Contains( SprintRaise ) )
				Play( SprintRaise );
			else
				Play( Idle );
			_oneHanded = oneHanded;
			return;
		}

		if ( oneHanded != _oneHanded )
		{
			_oneHanded = oneHanded;
			if ( _action is Idle or OneHandedIdle )
				Play( oneHanded ? OneHandedIdle : Idle );
		}
	}

	internal void Place( Transform camera, float adsBlend, float wallrunSide = 0f, float mantleProgress = 0f ) =>
		Place( camera, adsBlend, new Titanfall2PilotViewPoseState( wallrunSide, 0f, mantleProgress, 0f, 0f, 0f,
			MathF.Abs( wallrunSide ) > 0.1f, false, mantleProgress > 0.01f, false ) );

	internal void Place( Transform camera, float adsBlend, in Titanfall2PilotViewPoseState pose )
	{
		SetZoom( adsBlend );
		var posedCamera = ApplyInertia( camera, _zoom, pose );
		if ( _owner.IsValid() )
		{
			// Apply authored optic translation in view space before resolving the
			// animated CAMERA_BASE anchor; it must not become part of that inverse.
			posedCamera.Position += camera.Rotation * _owner.Presentation.ViewModelOffset( _zoom );
		}

		// Retail action clips move the POV root into view space. Resolve the current
		// camera attachment, since the reference pose has a different origin and axis.
		if ( !_cameraAnchor.TryRead( Renderer, out var localCamera ) )
		{
			WorldTransform = posedCamera;
			return;
		}
		// Parent-space animation bones share one pose and do not depend on either
		// the current native render root or its previous world-bone cache.
		WorldTransform = posedCamera.ToWorld( localCamera.ToLocal( global::Transform.Zero ) );
	}

	Transform ApplyInertia( Transform camera, float adsBlend, in Titanfall2PilotViewPoseState pose )
	{
		var currentAngles = camera.Rotation.Angles();
		if ( !_inertiaReady )
		{
			_lastCameraAngles = currentAngles;
			_inertiaTime = Time.NowDouble;
			_inertiaReady = true;
			return camera;
		}

		// Camera composition can run again for another render in the same tick.
		// Placement must not advance the spring again or depend on render count.
		if ( _inertiaTime != Time.NowDouble )
		{
			_inertiaTime = Time.NowDouble;
			var dt = Math.Clamp( Time.Delta, 0f, 0.05f );
			var dYaw = MathX.DeltaDegrees( currentAngles.yaw, _lastCameraAngles.yaw );
			var dPitch = MathX.DeltaDegrees( currentAngles.pitch, _lastCameraAngles.pitch );
			_lastCameraAngles = currentAngles;

			_swayOffset.x = Math.Clamp( _swayOffset.x + dYaw * 0.12f, -MaxInertiaDegrees, MaxInertiaDegrees );
			_swayOffset.y = Math.Clamp( _swayOffset.y + dPitch * 0.12f, -MaxInertiaDegrees, MaxInertiaDegrees );

			var accel = -InertiaStiffness * _swayOffset - InertiaDamping * _swayVelocity;
			_swayVelocity += accel * dt;
			_swayOffset += _swayVelocity * dt;

			var targetWall = Math.Clamp( pose.WallrunSide, -1f, 1f );
			var targetSlide = Math.Clamp( pose.SlideBlend, 0f, 1f );
			var targetMantle = Math.Clamp( pose.MantleProgress, 0f, 1f );
			_wallrunBlend = MathX.Lerp( _wallrunBlend, targetWall, Math.Clamp( dt * 12f, 0f, 1f ), true );
			_slidePoseBlend = MathX.Lerp( _slidePoseBlend, targetSlide, Math.Clamp( dt * 14f, 0f, 1f ), true );
			_mantlePoseBlend = MathX.Lerp( _mantlePoseBlend, targetMantle, Math.Clamp( dt * 18f, 0f, 1f ), true );
		}

		var scale = float.Lerp( 1f, 0.18f, adsBlend );
		// Suppress procedural hipfire cant almost completely while ADSing so optics stay aligned.
		var hipScale = float.Lerp( 1f, 0.06f, adsBlend );

		var absWall = MathF.Abs( _wallrunBlend );
		var wallRoll = -_wallrunBlend * 10.5f * hipScale;
		var wallYaw = _wallrunBlend * 3.5f * hipScale;
		var wallLocalOffset = new Vector3( -0.6f * absWall, _wallrunBlend * 1.15f, -0.75f * absWall ) * hipScale;

		// Iconic Titanfall 2 tactical canted slide hipfire stance.
		var slideRoll = -26f * _slidePoseBlend * hipScale;
		var slidePitch = 3.5f * _slidePoseBlend * hipScale;
		var slideYaw = 4.5f * _slidePoseBlend * hipScale;
		var slideLocalOffset = new Vector3( -1.2f, 1.85f, -1.9f ) * (_slidePoseBlend * hipScale);

		// Smooth arch dip while mantling over a ledge.
		var mantleArc = MathF.Sin( _mantlePoseBlend * MathF.PI );
		var mantlePitch = mantleArc * 28f;
		var mantleRoll = -mantleArc * 8f;
		var mantleLocalOffset = new Vector3( -1.5f, 0.6f, -6.2f ) * mantleArc;

		// Landing spring follow & subtle airborne float.
		var landingPitch = -pose.LandingDip * 0.8f * hipScale;
		var airLocalOffset = new Vector3( 0f, 0f, pose.LandingDip * 0.45f - pose.AirborneBlend * 0.45f ) * hipScale;

		// Offhand [G] ordnance tuck and [F] melee strike sweep.
		var offhandDip = Math.Clamp( pose.OffhandDip, 0f, 1f );
		var offhandOffset = new Vector3( -2.2f, 2.6f, -7.8f ) * offhandDip;
		var offhandAngles = new Angles( 26f * offhandDip, -14f * offhandDip, -16f * offhandDip );
		var (meleeOffset, meleeAngles) = Titanfall2PilotCombatRules.ComputeMeleeViewModelPose( pose.MeleeStrikeKind, pose.MeleeProgress );

		var totalOffset = camera.Rotation * (wallLocalOffset + slideLocalOffset + mantleLocalOffset + airLocalOffset + offhandOffset + meleeOffset);
		var swayRot = Rotation.From( new Angles(
			_swayOffset.y * scale + slidePitch + mantlePitch + landingPitch + offhandAngles.pitch + meleeAngles.pitch,
			_swayOffset.x * scale + wallYaw + slideYaw + offhandAngles.yaw + meleeAngles.yaw,
			_swayOffset.x * 0.35f * scale + wallRoll + slideRoll + mantleRoll + offhandAngles.roll + meleeAngles.roll ) );
		return new Transform( camera.Position + totalOffset, camera.Rotation * swayRot );
	}

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || !Renderer.SceneModel.IsValid() ) return;
		if ( _owner.IsValid() && _owner.ChargeProfile.Valid )
		{
			if ( _owner.ChargePhase == Titanfall2ChargePhase.Charging && _action != Charge && _clips.Contains( Charge ) ) Play( Charge );
			else if ( _owner.ChargePhase != Titanfall2ChargePhase.Charging && _action == Charge ) Play( Idle );
		}
		_animation?.ApplyParameters();
		DispatchEvents();

		var restingClip = _aiming && _clips.Contains( AdsIdle ) ? AdsIdle
			: _sprinting && _clips.Contains( Sprint ) ? Sprint
			: _oneHanded && _clips.Contains( OneHandedIdle ) ? OneHandedIdle
			: Idle;

		if ( _action != Charge && _action != restingClip && _actionEnds <= 0 )
			Play( restingClip );
	}

	public override void OnDeploy()
	{
		base.OnDeploy();
		_aiming = false;
		_sprinting = false;
		_oneHanded = false;
		SetZoom( 0 );
		SetClipVisible( true );
		Play( Draw );
	}

	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		base.OnAttack( hitPoint, origin );
		if ( _owner.IsValid() && _owner.IsReloading ) return;
		_sprinting = false;
		var fireClip = _animation is not null ? Fire : _aiming && _clips.Contains( AdsFire ) ? AdsFire : Fire;
		Play( fireClip );
		if ( !HasEvent( fireClip, Titanfall2PilotViewData.EventKind.MuzzleFlash ) )
			SpawnMuzzleFlash();
	}

	public override void OnReloadStart()
	{
		base.OnReloadStart();
		_aiming = false;
		_sprinting = false;
		_reloadCancelled = false;
		var isEmpty = _owner.IsValid() && _owner.ReloadStartedEmpty;
		var reloadTime = _owner.IsValid() ? _owner.ReloadDuration : (float?)null;
		var elapsed = _owner.IsValid() ? _owner.ReloadElapsed : 0;
		var role = isEmpty ? EmptyReload : Reload;
		if ( isEmpty && _owner.IsValid() && _owner.ReloadMilestone > 0 && _clips.Contains( EmptyReloadLate( _owner.ReloadMilestone ) ) ) role = EmptyReloadLate( _owner.ReloadMilestone );
		Play( role, reloadTime, elapsed );
	}

	public override void OnIncrementalReload()
	{
		base.OnIncrementalReload();
		if ( !_owner.IsValid() || !_owner.IsReloading || !_owner.ReloadIsSegmented ) return;
		if ( _owner.Clip1 >= _owner.ClipMaxSize || _owner.Ammo1 <= 0 ) return;
		if ( !_clips.Contains( ReloadSegLoop ) ) return;
		Play( ReloadSegLoop, _owner.ReloadLoopDuration );
	}

	public override void OnReloadFinish()
	{
		base.OnReloadFinish();
		if ( _reloadCancelled || !_owner.IsValid() || !_owner.ReloadIsSegmented ) return;
		var endClip = _owner.ReloadStartedEmpty && _clips.Contains( ReloadEmptySegEnd )
			? ReloadEmptySegEnd
			: ReloadSegEnd;
		if ( !_clips.Contains( endClip ) ) return;
		Play( endClip, _owner.ReloadEndDuration );
	}

	public override void OnReloadCancel()
	{
		base.OnReloadCancel();
		_reloadCancelled = true;
		SetClipVisible( true );
		StopStateParticles();
		Play( Idle );
	}
	internal void PlayHeatVent()
	{
		if ( !_clips.Contains( Cooldown ) || !_owner.IsValid() ) return;
		Play( Cooldown, MathF.Max( 0.01f, (float)(_owner.HeatVentUntil - Time.NowDouble) ) );
	}

	void Play( string name, float? seconds = null, float elapsed = 0 )
	{
		if ( !Renderer.IsValid() ) return;
		DispatchEvents();
		if ( _animation is not null )
		{
			_animation.Play( name, seconds, elapsed );
			_action = name;
			var duration = seconds is > 0.05f && float.IsFinite( seconds.Value ) ? seconds.Value : _animation.Duration( name );
			_actionEnds = MathF.Max( 0, duration - MathF.Max( 0, elapsed ) );
			_lastCycle = elapsed > 0 ? PresentationCycle : -0.001f;
			DispatchEvents();
			return;
		}
		var seq = Renderer.Sequence;
		seq.Name = name;
		seq.Looping = name is Idle or AdsIdle or Sprint or OneHandedIdle or Charge;
		seq.PlaybackRate = seconds is > 0.05f ? seq.Duration / seconds.Value : 1f;
		var realDuration = seq.Duration / MathF.Max( 0.01f, seq.PlaybackRate );
		seq.TimeNormalized = elapsed > 0 && realDuration > 0 ? Math.Clamp( elapsed / realDuration, 0, 1 ) : 0;
		_action = name;
		_lastCycle = elapsed > 0 ? seq.TimeNormalized : -0.001f;
		_actionEnds = MathF.Max( 0, realDuration - MathF.Max( 0, elapsed ) );
		DispatchEvents();
	}

	void DispatchEvents()
	{
		if ( string.IsNullOrEmpty( _action ) || !_eventsByClip.TryGetValue( _action, out var events ) ) return;
		var cycle = Math.Clamp( PresentationCycle, 0f, 1f );
		if ( _action == Charge && cycle < _lastCycle ) _lastCycle = -0.001f;
		foreach ( var evt in events )
		{
			if ( evt.Cycle <= _lastCycle || evt.Cycle > cycle ) continue;
			ExecuteEvent( evt );
		}
		_lastCycle = cycle;
	}

	void ExecuteEvent( Titanfall2PilotViewData.ClipEvent evt )
	{
		switch ( evt.Kind )
		{
			case Titanfall2PilotViewData.EventKind.CreateParticle:
			case Titanfall2PilotViewData.EventKind.StopParticle:
			case Titanfall2PilotViewData.EventKind.AddParticleControlPoint:
				ParticleEvent( evt ); break;
			case Titanfall2PilotViewData.EventKind.Sound:
				if ( _owner.IsValid() )
					Titanfall2GameplayVisuals.PlayViewSound( Scene, _owner.MountIdent, evt.Options, WorldPosition );
				break;
			case Titanfall2PilotViewData.EventKind.MuzzleFlash:
				SpawnMuzzleFlash();
				break;
			case Titanfall2PilotViewData.EventKind.ShellEject:
				// Kraber and other projectile guns still eject authored casings.
				if ( _owner.IsValid() ) Titanfall2RetailWeaponEffects.Shells( this, _owner.MountIdent, _owner.Presentation, true, _aiming, _owner.GameObject );
				break;
			case Titanfall2PilotViewData.EventKind.HideClip:
				SetClipVisible( false );
				break;
			case Titanfall2PilotViewData.EventKind.ShowClip:
				SetClipVisible( true );
				break;
		}
	}

	void SpawnMuzzleFlash()
	{
		if ( !MuzzleGameObject.IsValid() || (_owner.IsValid() && _owner.HoldType == "grenade") ) return;
		if ( _owner.IsValid() ) Titanfall2RetailWeaponEffects.MuzzleFlash( this, _owner.MountIdent, _owner.Presentation,
			true, _aiming, _owner.IsFullyAds );
	}

	bool HasEvent( string clip, Titanfall2PilotViewData.EventKind kind ) =>
		_eventsByClip.TryGetValue( clip, out var events ) && events.Any( e => e.Kind == kind );

	void SetClipVisible( bool visible )
	{
		if ( string.IsNullOrEmpty( _clipBodyGroup ) || !Renderer.IsValid() ) return;
		Renderer.SetBodyGroup( _clipBodyGroup, visible ? 0 : 1 );
	}

	void SetZoom( float zoom )
	{
		_zoom = float.IsFinite( zoom ) ? Math.Clamp( zoom, 0, 1 ) : 0;
		_animation?.SetZoom( _zoom );
	}
	void ParticleEvent( Titanfall2PilotViewData.ClipEvent evt )
	{
		if ( !_owner.IsValid() ) return;
		_stateParticles.RemoveAll( x => !x.IsValid() );
		var parts = evt.Options.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		if ( parts.Length == 0 ) return;
		if ( evt.Kind == Titanfall2PilotViewData.EventKind.StopParticle )
		{
			foreach ( var particle in _stateParticles.Where( x => x.Name == parts[0] ) ) particle.Destroy();
			_stateParticles.RemoveAll( x => !x.IsValid() ); return;
		}
		if ( evt.Kind == Titanfall2PilotViewData.EventKind.CreateParticle && parts.Length == 3 && parts[1] == "follow_attachment" )
		{
			var particle = Titanfall2WeaponStateEffects.Spawn( this, _owner.MountIdent, parts[0], parts[2], true, 15 );
			if ( particle.IsValid() ) _stateParticles.Add( particle );
			return;
		}
		if ( evt.Kind == Titanfall2PilotViewData.EventKind.AddParticleControlPoint && parts.Length == 4 && parts[2] == "follow_attachment"
			&& int.TryParse( parts[1], out var index ) && index is >= 0 and < 64 )
		{
			var anchor = Renderer.GetAttachmentObject( parts[3] );
			if ( !anchor.IsValid() ) return;
			foreach ( var particle in _stateParticles.Where( x => x.Name == parts[0] ) )
				particle.GetOrAddComponent<Titanfall2WeaponParticleFollow>().Set( index, anchor );
		}
	}
	internal void StopStateParticles()
	{
		foreach ( var particle in _stateParticles ) if ( particle.IsValid() ) particle.Destroy();
		_stateParticles.Clear();
	}
	protected override void OnDestroy()
	{
		StopStateParticles();
		_animation?.Dispose();
		_animation = null;
		base.OnDestroy();
	}
}
