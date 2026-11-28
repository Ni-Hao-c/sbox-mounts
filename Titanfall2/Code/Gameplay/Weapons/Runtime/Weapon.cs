/// <summary>Spawnable TF2 gun using the engine inventory; projectile shots are simulated by the host.</summary>
[Title( "Titanfall 2 Weapon" ), Group( "Titanfall 2" )]
public sealed partial class Titanfall2Weapon : BaseCombatWeapon
{
	[Property] public string MountIdent { get; set; }
	[Property] public string WorldModelPath { get; set; }
	[Property] public string ViewModelPath { get; set; }
	[Property] public string ViewArmsModelPath { get; set; }
	[Property] public string FireSound { get; set; }
	[Property] public string FireSound1P { get; set; }
	[Property] public Titanfall2WeaponPresentation Presentation { get; set; }
	[Property] public Titanfall2WeaponAudio Audio { get; set; }
	[Property] public float AdsFov { get; set; } = 55f;
	[Property] public SkinnedModelRenderer DroppedModel { get; set; }
	[Property] public Titanfall2ProjectileSettings Projectile { get; set; }
	[Property] public float TitanDamage { get; set; }
	[Property] public int BurstCount { get; set; } = 1;
	[Property] public float BurstRecovery { get; set; }
	[Property] public float RechamberTime { get; set; }
	int _burstRemaining;
	TimeUntil _burstStep;
	TimeUntil _nextProjectileRequest;
	float _adsBlend;
	internal float AdsBlend => _adsBlend;
	internal bool IsFullyAds => _adsBlend >= 1f;

	public bool IsAiming => !IsProxy && IsHeld && IsActive && !IsReloading
		&& (!(IsRechambering && NeedsRechamber) || string.IsNullOrEmpty( Presentation.ScopeReticle ))
		&& (Input.Down( "attack2" ) || (Firing.AttackPressesAds && Input.Down( "attack1" ) && HasRoundAmmo()))
		&& _parkourState is not { Equipped: true, CombatBusy: true }
		&& ViewModel.IsValid() && ViewModel.GetComponent<Titanfall2PilotViewModel>() is { HasAds: true };

	public bool IsSprinting => !IsProxy && IsHeld && IsActive && !IsReloading && !IsAiming
		&& _parkourState is not { Equipped: true, CombatBusy: true }
		&& TimeSinceShoot > 0.25f && Owner.IsValid()
		&& (_parkourPose.IsWallrunning || _parkourPose.IsWallhanging || _parkourPose.IsSliding || _parkourPose.IsMantling ? false
			: _parkourState is { Equipped: true } ? _parkourPose.IsSprinting
			: Owner.IsOnGround && Input.Down( "run" ) && Owner.Velocity.WithZ( 0 ).Length > 180f);

	public override Vector2 CurrentSpread => SpreadProfile.Enabled ? RetailSpread( _adsBlend ) : base.CurrentSpread * float.Lerp( 1f, 0.25f, _adsBlend );

	const float RetailFovAspectRatio = 4f / 3f;

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		base.ModifyCamera( camera, ref view );
		if ( _adsBlend > 0.001f )
		{
			var adsFov = camera.IsValid() ? ResolveAdsFieldOfView( AdsFov, camera.FovAxis ) : AdsFov;
			var targetFov = Math.Clamp( adsFov, 10f, view.FieldOfView );
			view.FieldOfView = float.Lerp( view.FieldOfView, targetFov, _adsBlend );
		}
		ApplyViewKickCamera( ref view );
	}

	// Retail zoom_fov (e.g. Kraber 26.26 = 3x of 70° 4:3, HCOG 35°) is authored as 4:3 horizontal FOV.
	internal static float ResolveAdsFieldOfView( float retailHorizontalFov, CameraComponent.Axis axis )
	{
		var clamped = Math.Clamp( float.IsFinite( retailHorizontalFov ) ? retailHorizontalFov : 55f, 10f, 120f );
		if ( axis != CameraComponent.Axis.Vertical ) return clamped;
		var halfRad = clamped * (MathF.PI / 360f);
		return MathF.Atan( MathF.Tan( halfRad ) / RetailFovAspectRatio ) * (360f / MathF.PI);
	}

	internal static float ResolveHorizontalFieldOfView( float fov, CameraComponent.Axis axis, Vector2 screenSize )
	{
		var clamped = Math.Clamp( float.IsFinite( fov ) ? fov : 90f, 10f, 150f );
		if ( axis != CameraComponent.Axis.Vertical ) return clamped;
		var aspect = screenSize.y > 1f && screenSize.x > 1f && float.IsFinite( screenSize.x / screenSize.y )
			? screenSize.x / screenSize.y : 16f / 9f;
		var halfRad = clamped * (MathF.PI / 360f);
		return Math.Clamp( MathF.Atan( MathF.Tan( halfRad ) * aspect ) * (360f / MathF.PI), 15f, 150f );
	}

	protected override bool WantsPrimaryAttack() => _burstRemaining == 0
		&& (!Firing.RequireFullAdsToFire || _adsBlend >= 0.98f)
		&& base.WantsPrimaryAttack();
	// Right mouse is ADS for mounted guns. The base secondary-fire path would
	// cancel reloads because an unused secondary ammo pool is considered infinite.
	protected override bool WantsSecondaryAttack() => Projectile.Behavior == Titanfall2ProjectileBehavior.RemoteCharge && base.WantsSecondaryAttack();

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius )
		=> Titanfall2CollisionQueries.SelectTrace( base.BulletTrace( ray, distance, radius )
			.UseHitPosition().WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );

	public override void PrimaryAttack()
	{
		if ( !CanAcceptRechamberShot() ) return;
		if ( UsesOrdnance ) { _ordnance.BeginAttack( this ); return; }
		if ( UsesRetailCharge ) { _charge.BeginAttack( this ); return; }
		if ( UsesRetailSmartAmmo ) { _smartAmmo.BeginAttack( this ); return; }
		_heat.PrepareOwnerShot( this );
		_spinUp.PrepareOwnerShot( this );
		FireRound();
		if ( BurstCount <= 1 || !IsHeld || !HasRoundAmmo() ) return;
		_burstRemaining = Math.Min( BurstCount - 1, 7 );
		_burstStep = MathF.Max( PrimaryDelay, 0.01f );
		SetNextPrimaryFire( (MathF.Max( PrimaryDelay, 0.01f ) * _burstRemaining) + BurstRecovery );
	}

	void FireRound()
	{
		if ( !Projectile.Enabled ) { FireHitscan(); return; }
		if ( IsProxy || !IsHeld ) return;
		if ( Networking.IsHost ) LaunchProjectile( AimRay.Forward, _adsBlend, validateClientAim: false );
		else RequestProjectile( AimRay.Forward, _adsBlend );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestProjectile( Vector3 direction, float zoom ) => LaunchProjectile( direction, zoom, validateClientAim: true );

	void LaunchProjectile( Vector3 direction, float zoom, bool validateClientAim )
	{
		if ( UsesOrdnance ) return;
		var owner = Owner;
		if ( !Networking.IsHost || !Projectile.Enabled || !owner.IsValid() || Inventory?.ActiveItem != this
			|| IsReloading || !CanAcceptRechamberShot() || _nextProjectileRequest > 0 || !direction.IsFinite || direction.LengthSquared is < 0.25f or > 4f
			|| !float.IsFinite( zoom ) || zoom is < 0 or > 1
			|| (Firing.RequireFullAdsToFire && zoom < 0.95f) ) return;
		direction = direction.Normal;
		if ( validateClientAim && Vector3.Dot( owner.EyeTransform.Forward, direction ) < 0.35f ) return;
		if ( !_heat.CanAcceptShot( this ) || !TakePrimaryAmmo( Firing.Cost ) ) return;
		_heat.AcceptShot( this );
		_nextProjectileRequest = MathF.Max( _spinUp.AcceptHostShot( this, zoom ), 0.015f );
		if ( Clip1 > 0 ) RecordRechamberShot( _nextProjectileRequest );
		var spread = SpreadProfile.Enabled ? RetailSpread( zoom ) : CurrentSpread;
		TimeSinceShoot = 0;
		var pellets = Firing.Count( Ballistics.Pellets );
		var origin = GetMuzzleTransform().Position;
		if ( !origin.IsFinite || origin.Distance( owner.EyePosition ) > 96f ) origin = owner.EyePosition;
		// Keep the real projectile at the visible barrel while preserving camera aim.
		// Sweep the eye-to-barrel segment too, so nearby cover cannot be bypassed.
		var clearance = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( owner.EyePosition, origin )
			.Radius( Math.Clamp( Projectile.Radius, 0, 12 ) ).IgnoreGameObjectHierarchy( owner.GameObject )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
		if ( clearance.StartedSolid ) origin = owner.EyePosition;
		else if ( clearance.Hit ) origin = clearance.EndPosition + clearance.Normal * 0.1f;
		// Scripted bolt offsets share one attack direction; independent aim cones
		// per bolt destroy the Double Take/Mastiff pattern.
		var patternDirection = Firing.HasPattern ? direction.WithAimCone( spread.x, spread.y ) : direction;
		for ( var i = 0; i < pellets; i++ )
		{
			var bearing = Firing.HasPattern ? Firing.Bearing( patternDirection, i, zoom ) : direction.WithAimCone( spread.x, spread.y );
			var target = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( new Ray( owner.EyePosition, bearing ), Ballistics.Range )
				.IgnoreGameObjectHierarchy( owner.GameObject ).IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "trigger", "projectile" ).UseHitboxes(), Titanfall2TraceMask.Grenade ).EndPosition;
			var flightDirection = (target - origin).LengthSquared > 1f ? (target - origin).Normal : bearing;
			Titanfall2Projectile.Launch( Scene, new Ray( origin, flightDirection ), Firing.Flight( Projectile, zoom, i ),
				Ballistics.Damage, TitanDamage, owner.GameObject, GameObject, owner.GameObject, DamageProfile );
		}
		PlayProjectileFire( zoom );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayProjectileFire( float zoom )
	{
		TimeSinceShoot = 0;
		RecordSpreadShot( zoom );
		OnShootEffects( default );
	}

	protected override void Think()
	{
		TickRechamber();
		TickSpreadRun();
		TickMechanics();
		if ( _burstRemaining == 0 ) return;
		if ( !IsHeld || !HasRoundAmmo() || IsReloading || Input.Pressed( "reload" ) )
		{
			_burstRemaining = 0;
			return;
		}
		if ( _burstStep > 0 ) return;
		FireRound();
		_burstRemaining--;
		_burstStep = MathF.Max( PrimaryDelay, 0.01f );
		if ( _burstRemaining == 0 || !HasRoundAmmo() )
		{
			_burstRemaining = 0;
			SetNextPrimaryFire( MathF.Max( PrimaryDelay, BurstRecovery ) );
		}
	}

	public override bool CanPrimaryAttack() => _burstRemaining == 0
		&& !NeedsRechamber
		&& _parkourState is not { Equipped: true, CombatBusy: true }
		&& (!UsesOrdnance || CanBeginOrdnance)
		&& !HeatVenting && (!UsesRetailCharge || CanBeginCharge)
		&& (!UsesRetailSmartAmmo || _smartAmmo.CanStartAttack( this )) && base.CanPrimaryAttack();

	protected override void OnHolstered()
	{
		InterruptRechamber();
		HolsterMechanics();
		StopChargePresentation();
		StopHeatPresentation();
		ResetViewKick();
		GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
		_burstRemaining = 0;
		_adsBlend = 0;
		base.OnHolstered();
	}

	/// <summary>Use the original FP rig instead of the world-model fallback. A matching arms asset can follow the ViewModel skeleton.</summary>
	[Property] public bool UseOriginalViewModel { get; set; }

	BoxCollider _pickupCollider;
	Rigidbody _pickupBody;
	bool _inInventory;

	protected override void OnStart()
	{
		base.OnStart();
		_pickupCollider = GetComponent<BoxCollider>();
		_pickupBody = GetComponent<Rigidbody>();
		if ( DroppedModel.IsValid() )
		{
			DroppedModel.Model = Titanfall2GameplayVisuals.GetModel( WorldModelPath );
			DroppedModel.LocalRotation = PlayerWeaponCorrection( WorldModelPath );
		}
		UpdatePickup( Inventory.IsValid() );
		UpdateAttachments();
		BeginWeaponPrewarm();
		StartMechanics();
	}

	protected override void OnUpdate()
	{
		TickRechamber();
		TickSpreadRun();
		UpdateAttachments();
		TickWeaponPrewarm();
		var stored = Inventory.IsValid();
		if ( stored != _inInventory ) UpdatePickup( stored );
		base.OnUpdate();
		TickMechanics();
		UpdateChargePresentation();
		TickRetailReloadPresentation();
		if ( IsReloading || !IsActive ) GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
		else if ( !IsProxy && Owner is { UseInputControls: true } && _burstRemaining == 0 && SmartBurstRemaining == 0 && !Input.Down( "attack1" ) )
			GetComponent<Titanfall2WeaponSoundEmitter>()?.StopFiring();
		UpdatePilotViewState();
		TickViewKick();
		UpdateScopePresentation();
		UpdateWeaponHud();
	}

	Titanfall2PilotViewPoseState _parkourPose;
	ITitanfall2PilotParkourState _parkourState;
	PlayerController _firstDrawOwner;

	internal bool BeginPilotDraw()
	{
		if ( !Owner.IsValid() || _firstDrawOwner == Owner ) return false;
		_firstDrawOwner = Owner;
		return true;
	}

	void UpdatePilotViewState()
	{
		if ( IsProxy || !IsHeld || !IsActive || !ViewModel.IsValid() )
		{
			_adsBlend = 0;
			_parkourPose = default;
			return;
		}
		ReadPilotParkourState( out _parkourPose );
		var aiming = IsAiming;
		var sprinting = IsSprinting;
		var oneHanded = _parkourPose.IsWallrunning || _parkourPose.IsWallhanging || MathF.Abs( _parkourPose.WallrunSide ) > 0.1f;
		var rate = aiming ? 9f : 11f;
		_adsBlend = SpreadProfile.Enabled ? SpreadProfile.AdvanceZoom( _adsBlend, aiming, Time.Delta )
			: Math.Clamp( _adsBlend + (aiming ? 1f : -1f) * rate * Time.Delta, 0f, 1f );
		if ( ViewModel.GetComponent<Titanfall2PilotViewModel>() is { } pilotView )
			pilotView.SetLocomotionState( aiming, sprinting, oneHanded, _adsBlend, Owner.IsValid() && Owner.IsDucking, _parkourPose.IsSliding );
	}

	void ReadPilotParkourState( out Titanfall2PilotViewPoseState pose )
	{
		pose = default;
		var ownerGo = Owner?.GameObject;
		if ( !ownerGo.IsValid() )
		{
			_parkourState = null;
			return;
		}

		// Use the typed Pilot Jump Kit component; re-query so mid-hold equips take effect immediately.
		var typed = ownerGo.GetComponent<ITitanfall2PilotParkourState>();
		_parkourState = typed;
		if ( typed is { Equipped: true } )
			pose = typed.ViewPoseState;
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		_charge.OnAdded( this );
		base.OnAdded( inventory );
		_heat.OnAdded( this );
		_ordnance.OnAdded( this );
		UpdatePickup( true );
		BeginWeaponPrewarm();
	}

	protected override void OnRemoved( BaseInventoryComponent inventory )
	{
		RemoveMechanics();
		base.OnRemoved( inventory );
		UpdatePickup( false );
	}

	void UpdatePickup( bool stored )
	{
		_inInventory = stored;
		if ( DroppedModel.IsValid() ) DroppedModel.Enabled = !stored;
		if ( _pickupCollider.IsValid() ) _pickupCollider.Enabled = !stored;
		if ( _pickupBody.IsValid() ) _pickupBody.Enabled = !stored;
	}

	// Older saves still point at the unrotated .visual model.
	static Rotation PlayerWeaponCorrection( string path ) =>
		path?.EndsWith( ".held.vmdl", StringComparison.OrdinalIgnoreCase ) == true
			? Rotation.Identity : Titanfall2GameplayVisuals.WeaponAxisCorrection;

	protected override void CreateWorldModel()
	{
		DestroyWorldModel();
		if ( !IsHeld || HolderRenderer?.GetBoneObject( HoldBone ) is not { } hand || !hand.IsValid() ) return;
		WorldModel = Titanfall2GameplayVisuals.CreateGun( hand, WorldModelPath, firstPerson: false, presentation: Presentation );
		if ( WorldModel.IsValid() ) WorldModel.LocalRotation = PlayerWeaponCorrection( WorldModelPath );
	}

	protected override void CreateViewModel()
	{
		if ( IsProxy || !IsHeld || ViewModel.IsValid() ) return;
		_originalView = UseOriginalViewModel && !string.IsNullOrEmpty( ViewModelPath );
		ViewModel = Titanfall2GameplayVisuals.CreateGun( GameObject, _originalView ? ViewModelPath : WorldModelPath, firstPerson: true,
			pilotView: _originalView && !string.IsNullOrEmpty( ViewArmsModelPath ), presentation: Presentation );
		if ( !ViewModel.IsValid() && _originalView )
		{
			_originalView = false;
			ViewModel = Titanfall2GameplayVisuals.CreateGun( GameObject, WorldModelPath, firstPerson: true, presentation: Presentation );
		}
		if ( ViewModel.IsValid() && !_originalView ) CreateViewArms();
		else if ( ViewModel.IsValid() && !string.IsNullOrEmpty( ViewArmsModelPath ) )
		{
			if ( Titanfall2PilotViewModel.TryAttach( ViewModel, this, ViewArmsModelPath ) ) return;
			DestroyViewModel();
			_originalView = false;
			ViewModel = Titanfall2GameplayVisuals.CreateGun( GameObject, WorldModelPath, firstPerson: true, presentation: Presentation );
			if ( ViewModel.IsValid() ) CreateViewArms();
		}
	}

	bool _originalView;
	SkinnedModelRenderer _viewArms;
	Model _armSourceModel;
	(BoneCollection.Bone Source, int Target)[] _armBones = [];

	void CreateViewArms()
	{
		if ( HolderRenderer?.Model is not { } body || body.Bones.GetBone( "hand_R" ) is null ) return;
		var arms = Model.Load( "weapons/shared/first_person/v_first_person_arms_human.vmdl" );
		if ( arms is not { IsError: false } ) return;
		var child = new GameObject( ViewModel, false, "TF2 first-person arms" );
		child.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_viewArms = child.AddComponent<SkinnedModelRenderer>();
		_viewArms.Model = arms;
		_viewArms.UseAnimGraph = false;
		// Match Sandbox's native weapon arms (sleeves and bare hands).
		_viewArms.BodyGroups = 21;
		_viewArms.RenderOptions.Game = false;
		_viewArms.RenderOptions.Overlay = true;
		_viewArms.RenderType = ModelRenderer.ShadowRenderType.Off;
		child.Enabled = true;
	}

	protected override void DestroyViewModel()
	{
		DestroyWeaponHud();
		DestroyScopePresentation();
		base.DestroyViewModel();
		_viewArms = null;
		_armSourceModel = null;
		_armBones = [];
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		if ( !ViewModel.IsValid() ) return;
		var weaponView = view;
		weaponView.Rotation *= ViewKickWeaponRotation;
		if ( ViewModel.GetComponent<Titanfall2PilotViewModel>() is { } pilotView )
		{
			pilotView.Place( new Transform( weaponView.Position, weaponView.Rotation ), _adsBlend, _parkourPose );
			return;
		}
		if ( _originalView && ViewModel.GetComponent<BaseWeaponModel>()?.Renderer?.Model?.Attachments.GetTransform( "CAMERA_BASE" ) is { } cameraBase )
		{
			ViewModel.WorldTransform = new Transform( weaponView.Position, weaponView.Rotation ).ToWorld( cameraBase.ToLocal( global::Transform.Zero ) );
			return;
		}

		var grip = new Transform( weaponView.Position + weaponView.Rotation * new Vector3( 13, -6, -7 ), weaponView.Rotation );
		ViewModel.WorldTransform = grip.ToWorld( new Transform( Vector3.Zero, PlayerWeaponCorrection( WorldModelPath ) ) );
		var body = HolderRenderer;
		if ( !_viewArms.IsValid() || !body.IsValid() || _viewArms.SceneModel is not { } arms
			|| body.GetBoneObject( HoldBone ) is not { } hand || !hand.IsValid() ) return;

		if ( _armSourceModel != body.Model )
		{
			_armSourceModel = body.Model;
			_armBones = _viewArms.Model.Bones.AllBones
				.Select( bone => (Source: _armSourceModel.Bones.GetBone( bone.Name ), Target: bone.Index) )
				.Where( pair => pair.Source is not null ).ToArray();
		}
		// Move the hands and the gun into view space together, using the same animated grip.
		var holderGrip = hand.WorldTransform.WithScale( 1 );
		// Keep each wrist on the weapon, but start the shoulders outside the
		// first-person frustum. The third-person shoulder pose is framed from behind.
		var left = GetViewArmPose( body, holderGrip, grip, weaponView, "L", 1 );
		var right = GetViewArmPose( body, holderGrip, grip, weaponView, "R", -1 );
		var armSpace = _viewArms.WorldTransform;
		foreach ( var (source, target) in _armBones )
		{
			if ( !body.TryGetBoneTransform( source, out var pose ) ) continue;
			var projected = grip.ToWorld( holderGrip.ToLocal( pose ) );
			projected = source.Name.EndsWith( "_L", StringComparison.OrdinalIgnoreCase )
				? left.Apply( source.Name, projected ) : right.Apply( source.Name, projected );
			// The render bone is for this camera pass. Keep the local override for
			// the next animation evaluation too, so it cannot restore the bind pose.
			arms.SetBoneOverride( target, armSpace.ToLocal( projected ) );
			arms.SetBoneWorldTransform( target, projected );
		}
	}

	readonly record struct ViewArmPose( bool Valid, Vector3 ShoulderOffset, Transform Upper, Transform Lower )
	{
		internal Transform Apply( string bone, Transform original )
		{
			if ( !Valid ) return original;
			if ( bone.StartsWith( "arm_upper_", StringComparison.OrdinalIgnoreCase ) ) return Upper;
			if ( bone.StartsWith( "arm_lower_", StringComparison.OrdinalIgnoreCase ) ) return Lower;
			return bone.StartsWith( "clavicle_", StringComparison.OrdinalIgnoreCase ) ? original.WithPosition( original.Position + ShoulderOffset ) : original;
		}
	}

	static ViewArmPose GetViewArmPose( SkinnedModelRenderer body, Transform holderGrip, Transform grip, in CameraView view, string side, float lateral )
	{
		if ( !body.TryGetBoneTransform( "arm_upper_" + side, out var upper )
			|| !body.TryGetBoneTransform( "arm_lower_" + side, out var lower )
			|| !body.TryGetBoneTransform( "hand_" + side, out var hand ) ) return default;
		upper = grip.ToWorld( holderGrip.ToLocal( upper ) );
		lower = grip.ToWorld( holderGrip.ToLocal( lower ) );
		hand = grip.ToWorld( holderGrip.ToLocal( hand ) );

		var upperSegment = lower.Position - upper.Position;
		var lowerSegment = hand.Position - lower.Position;
		var upperLength = upperSegment.Length;
		var lowerLength = lowerSegment.Length;
		var shoulder = view.Position + view.Rotation * new Vector3( 0, 9 * lateral, -13 );
		var reach = hand.Position - shoulder;
		if ( upperLength < 1 || lowerLength < 1 || reach.Length < 1 ) return default;
		var axis = reach.Normal;
		var distance = Math.Clamp( reach.Length, MathF.Abs( upperLength - lowerLength ) + 0.01f, upperLength + lowerLength - 0.01f );
		var along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2 * distance);
		var bend = view.Rotation * new Vector3( 0, lateral * 0.35f, -1 );
		bend = (bend - axis * Vector3.Dot( bend, axis )).Normal;
		var elbow = shoulder + axis * along + bend * MathF.Sqrt( MathF.Max( 0, upperLength * upperLength - along * along ) );
		return new ViewArmPose( true, shoulder - upper.Position,
			new Transform( shoulder, Rotation.FromToRotation( upperSegment, elbow - shoulder ) * upper.Rotation, upper.Scale ),
			new Transform( elbow, Rotation.FromToRotation( lowerSegment, hand.Position - elbow ) * lower.Rotation, lower.Scale ) );
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		Titanfall2CombatEffects.Impact( Scene, shot, MountIdent, Presentation.ImpactTable );
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		base.OnShootEffects( shot );
		var isLocalFirstPerson = !IsProxy && IsHeld && ViewModel.IsValid()
			&& WeaponModel == ViewModel.GetComponent<BaseWeaponModel>();
		// Ballistics still traces from the eye; presentation always leaves the visible muzzle.
		if ( !Projectile.Enabled && (shot.Hit || shot.HitPosition != default) && WeaponModel?.MuzzleGameObject is { } port )
			Titanfall2RetailWeaponEffects.Spawn( port, MountIdent, isLocalFirstPerson && !string.IsNullOrWhiteSpace( Presentation.TracerView ) ? Presentation.TracerView : Presentation.TracerWorld, false, 0.5f, shot.HitPosition );
		if ( shot.NoEvents ) return;
		FireViewKick();
		GameObject.GetOrAddComponent<Titanfall2WeaponSoundEmitter>().Shot( MountIdent, Audio, isLocalFirstPerson,
			GetMuzzleTransform().Position, PrimaryDelay, ammoPercent: ClipMaxSize > 0 ? Clip1 * 100f / ClipMaxSize : 100 );

		// The pilot rig emits the first-person burst through its animation event or fallback.
		if ( HoldType != "grenade" && WeaponModel is { } shown
			&& shown is not Titanfall2PilotViewModel
			&& shown.MuzzleGameObject is { } muzzle && muzzle.IsValid() )
		{
			Titanfall2RetailWeaponEffects.MuzzleFlash( shown, MountIdent, Presentation, isLocalFirstPerson, fullyAds: IsFullyAds );
			Titanfall2RetailWeaponEffects.Shells( shown, MountIdent, Presentation, isLocalFirstPerson, soundOwner: GameObject );
		}
	}
}

static class Titanfall2GameplayVisuals
{
	// The retail world guns use +Z down the barrel and +Y as up. Sandbox's hold socket is +X/+Z.
	internal static readonly Rotation WeaponAxisCorrection = Rotation.LookAt( Vector3.Up, Vector3.Left ).Inverse;

	internal static Model GetModel( string path ) => string.IsNullOrEmpty( path ) ? null : Model.Load( path );

	internal static GameObject CreateGun( GameObject parent, string path, bool firstPerson, bool pilotView = false, Titanfall2WeaponPresentation presentation = default )
	{
		var model = GetModel( path );
		if ( model is null || model.IsError ) return null;
		var visual = new GameObject( parent, false, "TF2 Held Weapon" );
		visual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var renderer = visual.AddComponent<SkinnedModelRenderer>();
		renderer.UseAnimGraph = false;
		renderer.CreateAttachments = true;
		renderer.Model = model;
		presentation.ApplyBodyGroups( renderer );
		if ( firstPerson )
		{
			visual.Flags |= GameObjectFlags.Absolute;
			visual.Tags.Add( "firstperson", "viewmodel" );
			renderer.RenderOptions.Game = false;
			renderer.RenderOptions.Overlay = true;
		}
		var weapon = pilotView ? visual.AddComponent<Titanfall2PilotViewModel>() : visual.AddComponent<BaseWeaponModel>();
		// TF2 owns these effects. Leaving the stock prefabs set duplicates muzzle/brass
		// bursts and uses the stock tracer's long near-distance skip.
		weapon.MuzzleEffect = null;
		weapon.EjectBrass = null;
		weapon.TracerEffect = null;
		weapon.Renderer = renderer;
		visual.Enabled = true;
		weapon.MuzzleGameObject = (!string.IsNullOrEmpty( presentation.MuzzleAttachment ) ? renderer.GetAttachmentObject( presentation.MuzzleAttachment ) : null)
			?? renderer.GetAttachmentObject( "muzzle_flash" ) ?? renderer.GetAttachmentObject( "muzzle" ) ?? visual;
		weapon.ShellEjectGameObject = renderer.GetAttachmentObject( string.IsNullOrEmpty( presentation.ShellAttachment ) ? "shell" : presentation.ShellAttachment );
		return visual;
	}

	internal static void PlayFireSound( Scene scene, string mountIdent, string name, Vector3 position )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || string.IsNullOrWhiteSpace( name ) || string.IsNullOrWhiteSpace( mountIdent ) ) return;
		Titanfall2SoundEventEmitter.OneShot( scene, mountIdent, name, position );
	}

	internal static void PlayViewSound( Scene scene, string mountIdent, string name, Vector3 position )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || string.IsNullOrWhiteSpace( name ) || string.IsNullOrWhiteSpace( mountIdent ) ) return;
		Titanfall2SoundEventEmitter.OneShot( scene, mountIdent, name, position, firstPerson: true );
	}
}
