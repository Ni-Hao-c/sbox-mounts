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
	internal const string AdsIn = "pilot_fp_ads_in";
	internal const string AdsIdle = "pilot_fp_ads_idle";
	internal const string AdsFire = "pilot_fp_ads_fire";
	internal const string AdsOut = "pilot_fp_ads_out";
	internal const string Sprint = "pilot_fp_sprint";
	internal const string SprintRaise = "pilot_fp_sprint_raise";
	internal const string OneHandedIdle = "pilot_fp_onehanded_idle";

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

	// Critically damped look-inertia state in view-local yaw/pitch degrees.
	Angles _lastCameraAngles;
	Vector2 _swayOffset;
	Vector2 _swayVelocity;
	bool _inertiaReady;

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
	{
		if ( !Renderer.IsValid() ) return;
		var locked = _action is Reload or EmptyReload or Draw;
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

	internal void Place( Transform camera, float adsBlend, float wallrunSide = 0f, float mantleProgress = 0f )
	{
		var posedCamera = ApplyInertia( camera, adsBlend, wallrunSide, mantleProgress );

		// Retail action clips move the POV root into view space. Resolve the current
		// camera attachment, since the reference pose has a different origin and axis.
		var attachment = Renderer.Model.Attachments.Get( "CAMERA_BASE" );
		if ( attachment?.Bone is not { } bone || !Renderer.TryGetBoneTransform( bone, out var pose ) )
		{
			WorldTransform = posedCamera;
			return;
		}
		var localCamera = WorldTransform.ToLocal( pose.ToWorld( attachment.LocalTransform ) );
		WorldTransform = posedCamera.ToWorld( localCamera.ToLocal( global::Transform.Zero ) );
	}

	Transform ApplyInertia( Transform camera, float adsBlend, float wallrunSide, float mantleProgress )
	{
		var currentAngles = camera.Rotation.Angles();
		if ( !_inertiaReady )
		{
			_lastCameraAngles = currentAngles;
			_inertiaReady = true;
			return camera;
		}

		var dt = Math.Clamp( Time.Delta, 0.001f, 0.05f );
		var dYaw = MathX.DeltaDegrees( currentAngles.yaw, _lastCameraAngles.yaw );
		var dPitch = MathX.DeltaDegrees( currentAngles.pitch, _lastCameraAngles.pitch );
		_lastCameraAngles = currentAngles;

		_swayOffset.x = Math.Clamp( _swayOffset.x + dYaw * 0.12f, -MaxInertiaDegrees, MaxInertiaDegrees );
		_swayOffset.y = Math.Clamp( _swayOffset.y + dPitch * 0.12f, -MaxInertiaDegrees, MaxInertiaDegrees );

		var accel = -InertiaStiffness * _swayOffset - InertiaDamping * _swayVelocity;
		_swayVelocity += accel * dt;
		_swayOffset += _swayVelocity * dt;

		var scale = float.Lerp( 1f, 0.18f, adsBlend );
		var wallRoll = -Math.Clamp( wallrunSide, -1f, 1f ) * 6.5f * scale;
		var mantlePitch = Math.Clamp( mantleProgress, 0f, 1f ) * 22f;
		var mantleDrop = camera.Rotation * new Vector3( 0, 0, -Math.Clamp( mantleProgress, 0f, 1f ) * 5.5f );
		var swayRot = Rotation.From( new Angles( _swayOffset.y * scale + mantlePitch, _swayOffset.x * scale, _swayOffset.x * 0.35f * scale + wallRoll ) );
		return new Transform( camera.Position + mantleDrop, camera.Rotation * swayRot );
	}

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || !Renderer.SceneModel.IsValid() ) return;
		DispatchEvents();

		var restingClip = _aiming && _clips.Contains( AdsIdle ) ? AdsIdle
			: _sprinting && _clips.Contains( Sprint ) ? Sprint
			: _oneHanded && _clips.Contains( OneHandedIdle ) ? OneHandedIdle
			: Idle;

		if ( _action != restingClip && _actionEnds <= 0 )
			Play( restingClip );
	}

	public override void OnDeploy()
	{
		base.OnDeploy();
		_aiming = false;
		_sprinting = false;
		SetClipVisible( true );
		Play( Draw );
	}

	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		base.OnAttack( hitPoint, origin );
		if ( _owner.IsValid() && _owner.IsReloading ) return;
		_sprinting = false;
		var fireClip = _aiming && _clips.Contains( AdsFire ) ? AdsFire : Fire;
		Play( fireClip );
		if ( !HasEvent( fireClip, Titanfall2PilotViewData.EventKind.MuzzleFlash ) )
			SpawnMuzzleFlash();
	}

	public override void OnReloadStart()
	{
		base.OnReloadStart();
		_aiming = false;
		_sprinting = false;
		var isEmpty = _owner.IsValid() && _owner.Clip1 == 0;
		var reloadTime = _owner.IsValid() ? _owner.ReloadTime : (float?)null;
		Play( isEmpty ? EmptyReload : Reload, reloadTime );
	}

	public override void OnReloadCancel()
	{
		base.OnReloadCancel();
		SetClipVisible( true );
		Play( Idle );
	}

	void Play( string name, float? seconds = null )
	{
		if ( !Renderer.IsValid() ) return;
		var seq = Renderer.Sequence;
		seq.Name = name;
		seq.Looping = name is Idle or AdsIdle or Sprint or OneHandedIdle;
		seq.TimeNormalized = 0;
		seq.PlaybackRate = seconds is > 0.05f ? seq.Duration / seconds.Value : 1f;
		_action = name;
		_lastCycle = -0.001f;
		_actionEnds = seq.Duration / MathF.Max( 0.01f, seq.PlaybackRate );
		DispatchEvents();
	}

	void DispatchEvents()
	{
		if ( string.IsNullOrEmpty( _action ) || !_eventsByClip.TryGetValue( _action, out var events ) ) return;
		var cycle = Math.Clamp( Renderer.Sequence.TimeNormalized, 0f, 1f );
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
			case Titanfall2PilotViewData.EventKind.Sound:
				if ( _owner.IsValid() )
					Titanfall2GameplayVisuals.PlayViewSound( _owner.MountIdent, evt.Options, WorldPosition );
				break;
			case Titanfall2PilotViewData.EventKind.MuzzleFlash:
				SpawnMuzzleFlash();
				break;
			case Titanfall2PilotViewData.EventKind.ShellEject:
				if ( ShellEjectGameObject.IsValid() && (!_owner.IsValid() || !_owner.Projectile.Enabled) )
				{
					if ( _owner.IsValid() ) Titanfall2RetailWeaponEffects.Spawn( ShellEjectGameObject, _owner.MountIdent, _owner.Presentation.ShellView, true, 3 );
				}
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
		if ( _owner.IsValid() ) Titanfall2RetailWeaponEffects.Spawn( MuzzleGameObject, _owner.MountIdent, _owner.Presentation.MuzzleView, true );
	}

	bool HasEvent( string clip, Titanfall2PilotViewData.EventKind kind ) =>
		_eventsByClip.TryGetValue( clip, out var events ) && events.Any( e => e.Kind == kind );

	void SetClipVisible( bool visible )
	{
		if ( string.IsNullOrEmpty( _clipBodyGroup ) || !Renderer.IsValid() ) return;
		Renderer.SetBodyGroup( _clipBodyGroup, visible ? 0 : 1 );
	}
}
