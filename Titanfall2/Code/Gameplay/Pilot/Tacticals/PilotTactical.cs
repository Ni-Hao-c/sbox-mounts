/// <summary>
/// Pilot Tactical ability controller attached alongside <see cref="Titanfall2PilotParkour"/> and
/// <see cref="Titanfall2PilotCombat"/>. Implements Stim, Grapple, Phase Shift, and Pulse Blade.
/// Triggered via [Mouse3] (toolmenu) and cycled via [T] (spray) or console command tf2_tactical.
/// </summary>
[Title( "Titanfall 2 Pilot Tactical" ), Group( "Titanfall 2" )]
public sealed class Titanfall2PilotTactical : Component
{
	internal const string PhasedTag = "tf2-phased";

	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public Titanfall2PilotParkour Parkour { get; set; }
	[Property] public Titanfall2PilotTacticalSettings Settings { get; set; } = Titanfall2PilotTacticalSettings.RetailDefault;

	[Sync( SyncFlags.FromHost )] public Titanfall2PilotTacticalKind SelectedKind { get; private set; } = Titanfall2PilotTacticalKind.Stim;
	[Sync( SyncFlags.FromHost )] public float ChargeUnits { get; private set; } = Titanfall2PilotTacticalRules.ChargeUnitsPerUse;
	[Sync( SyncFlags.FromHost )] public float MaxChargeUnits { get; private set; } = Titanfall2PilotTacticalRules.ChargeUnitsPerUse;
	[Sync( SyncFlags.FromHost )] public float StimRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float PhaseRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool GrappleActive { get; private set; }
	[Sync( SyncFlags.FromHost )] public Vector3 GrappleAnchor { get; private set; }
	[Sync( SyncFlags.FromHost )] public int ActivationSerial { get; private set; }

	public bool IsStimActive => StimRemaining > 0f;
	public bool IsPhaseActive => PhaseRemaining > 0f;
	public bool IsGrappling => GrappleActive;
	public bool IsActive => IsStimActive || IsPhaseActive || IsGrappling;
	public float SpeedMultiplier => IsStimActive ? Settings.StimSpeedScale : IsPhaseActive ? Settings.PhaseSpeedScale : 1f;
	public float WallrunSpeedMultiplier => IsStimActive ? Settings.StimWallrunSpeedScale : 1f;
	public int ChargesAvailable => (int)MathF.Floor( MathF.Max( 0f, ChargeUnits ) / Titanfall2PilotTacticalRules.ChargeUnitsPerUse );
	public int MaxCharges => Math.Max( 1, (int)MathF.Round( MaxChargeUnits / Titanfall2PilotTacticalRules.ChargeUnitsPerUse ) );
	public float ChargeFillFraction => MaxChargeUnits > 0.01f ? Math.Clamp( ChargeUnits / MaxChargeUnits, 0f, 1f ) : 0f;
	public float GrappleElapsed => _grappleElapsed;
	public string DisplayLabel => Titanfall2PilotTacticalRules.ShortLabel( SelectedKind );

	PlayerController _controller;
	GameObject _grappleTarget;
	Vector3 _grappleTargetLocalOffset;
	float _grappleElapsed;
	float _grappleLowSpeedTime;
	bool _wasPhased;
	float _stimHealRemainder;

	// Cached host Player health properties via whitelist-safe TypeLibrary.
	Component _hostPlayerComponent;
	PropertyDescription _healthProp;
	PropertyDescription _maxHealthProp;
	bool _resolvedHealthProps;

	// Local/3P grapple rope & phase camera isolation state.
	GameObject _ropeVisual;
	GameObject _ropeStartObj;
	GameObject _ropeEndObj;
	LineRenderer _ropeLine;
	CameraComponent _phaseCamera;

	internal static Titanfall2PilotTacticalSettings ResolveSettings( string mountIdent ) =>
		Sandbox.Mounting.Directory.Get( mountIdent ?? "titanfall2" ) is Titanfall2Mount mount
			? mount.GetPilotTacticalSettings()
			: Titanfall2PilotTacticalSettings.RetailDefault;

	[ConCmd( "tf2_tactical", Help = "Select or cycle Titanfall 2 Pilot Tactical ability (stim | grapple | phase | pulse)." )]
	static void TacticalCommand( string name = null )
	{
		var scene = Game.ActiveScene;
		if ( !scene.IsValid() ) return;
		var player = scene.GetAllComponents<PlayerController>().FirstOrDefault( p => p.IsValid() && p.Active && !p.IsProxy );
		if ( !player.IsValid() ) return;
		var tactical = player.GameObject.GetComponent<Titanfall2PilotTactical>();
		if ( !tactical.IsValid() || tactical.Parkour is not { Equipped: true } )
		{
			Titanfall2Log.Info( "Equip Pilot Jump Kit first (tf2_jumpkit) to use Pilot Tactical abilities." );
			return;
		}
		if ( string.IsNullOrWhiteSpace( name ) )
		{
			tactical.CycleTactical();
			Titanfall2Log.Info( $"TF2 Pilot Tactical -> {tactical.DisplayLabel}" );
			return;
		}
		if ( Titanfall2PilotTacticalRules.TryParseKind( name, out var kind ) )
		{
			tactical.SelectTactical( kind );
			Titanfall2Log.Info( $"TF2 Pilot Tactical -> {Titanfall2PilotTacticalRules.ShortLabel( kind )}" );
		}
		else
		{
			Titanfall2Log.Info( "Unknown tactical. Use: stim | grapple | phase | pulse" );
		}
	}

	internal void ConfigureForEquip( Titanfall2PilotParkour parkour, string mountIdent, Titanfall2PilotTacticalKind? persistedKind = null )
	{
		Parkour = parkour;
		MountIdent = mountIdent ?? "titanfall2";
		Settings = ResolveSettings( MountIdent );
		if ( Networking.IsHost )
		{
			var kind = persistedKind ?? SelectedKind;
			SelectedKind = kind;
			MaxChargeUnits = Titanfall2PilotTacticalRules.MaxChargeUnits( kind );
			ChargeUnits = MaxChargeUnits;
			StimRemaining = 0f;
			PhaseRemaining = 0f;
			GrappleActive = false;
		}
		Enabled = true;
	}

	internal void ResetOnUnequip()
	{
		ClearActiveStates( applyDetachBoost: false );
		DestroyRopeVisual();
		RestorePhaseVisibility();
		Enabled = false;
	}

	protected override void OnAwake()
	{
		_controller = GetComponent<PlayerController>();
		Parkour ??= GetComponent<Titanfall2PilotParkour>();
	}

	protected override void OnUpdate()
	{
		_controller ??= GetComponent<PlayerController>();
		Parkour ??= GetComponent<Titanfall2PilotParkour>();
		if ( Parkour is not { IsValid: true, Equipped: true } || !_controller.IsValid() )
		{
			ClearActiveStates( applyDetachBoost: false );
			DestroyRopeVisual();
			RestorePhaseVisibility();
			return;
		}

		if ( Networking.IsHost )
			TickHost( Math.Clamp( Time.Delta, 0f, 0.05f ) );

		UpdatePhasePresentation();
		UpdateGrappleVisual();
	}

	protected override void OnDisabled()
	{
		ClearActiveStates( applyDetachBoost: false );
		DestroyRopeVisual();
		RestorePhaseVisibility();
	}

	protected override void OnDestroy()
	{
		DestroyRopeVisual();
		RestorePhaseVisibility();
	}

	/// <summary>Triggers the selected Pilot tactical (or detaches active grapple) from local input.</summary>
	public void TriggerTactical()
	{
		if ( IsProxy || Parkour is not { IsValid: true, Equipped: true } || !_controller.IsValid() ) return;
		if ( _controller.Mode is not Titanfall2PilotMoveMode ) return;

		if ( SelectedKind == Titanfall2PilotTacticalKind.Grapple && GrappleActive )
		{
			DetachGrapple( impactJump: false );
			return;
		}

		var aim = _controller.EyeTransform.ForwardRay;
		if ( Networking.IsHost )
			TryActivateOnHost( aim );
		else
			RequestActivateOnHost( aim.Position, aim.Forward );
	}

	/// <summary>Cycles Stim -> Grapple -> Phase Shift -> Pulse Blade and persists across respawn.</summary>
	public void CycleTactical()
	{
		SelectTactical( Titanfall2PilotTacticalRules.NextKind( SelectedKind ) );
	}

	/// <summary>Selects a specific Pilot tactical ability and refills charges for immediate sandbox play.</summary>
	public void SelectTactical( Titanfall2PilotTacticalKind kind )
	{
		if ( IsProxy || Parkour is not { IsValid: true, Equipped: true } ) return;
		if ( Networking.IsHost )
			ApplySelectOnHost( kind );
		else
			RequestSelectOnHost( kind );
	}

	/// <summary>Detaches an active Grapple (e.g., on Jump/Crouch cancel or wallrun attach).</summary>
	internal void DetachGrapple( bool impactJump )
	{
		if ( !GrappleActive ) return;
		if ( !IsProxy && _controller.IsValid() && _controller.Body.IsValid() )
		{
			_controller.Body.Velocity = Titanfall2PilotTacticalRules.ComputeGrappleDetachVelocity(
				_controller.Body.Velocity, impactJump, Settings );
			_controller.PreventGrounding( 0.12f );
		}

		if ( Networking.IsHost )
			FinishGrappleOnHost( refundPartial: true );
		else
			RequestDetachGrappleOnHost();
	}

	/// <summary>Integrates active Grapple velocity inside <see cref="Titanfall2PilotMoveMode"/>.</summary>
	internal bool StepGrappleMovement( Rigidbody body, Vector3 wishDir, Vector3 eyeForward, float gravity, float dt )
	{
		if ( !GrappleActive || !body.IsValid() || !_controller.IsValid() ) return false;

		var anchor = ResolveCurrentAnchor();
		var pos = WorldPosition + Vector3.Up * 36f;
		_grappleElapsed += dt;

		var speed = body.Velocity.Length;
		if ( speed < Settings.GrappleLowSpeedThreshold )
			_grappleLowSpeedTime += dt;
		else
			_grappleLowSpeedTime = MathF.Max( 0f, _grappleLowSpeedTime - dt * 2f );

		if ( Titanfall2PilotTacticalRules.ShouldDetachGrapple(
			pos, body.Velocity, anchor, eyeForward, _grappleElapsed, _grappleLowSpeedTime, _controller.IsOnGround, Settings ) )
		{
			var closeImpact = pos.Distance( anchor ) <= Settings.GrappleDetachLengthMax + 12f;
			DetachGrapple( impactJump: closeImpact );
			return false;
		}

		body.Velocity = Titanfall2PilotTacticalRules.ComputeGrappleVelocity(
			pos, body.Velocity, anchor, wishDir, eyeForward, _grappleElapsed, Settings, gravity, dt );
		_controller.PreventGrounding( 0.08f );
		return true;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestActivateOnHost( Vector3 origin, Vector3 forward )
	{
		if ( !Networking.IsHost || !origin.IsFinite || !forward.IsFinite || forward.LengthSquared < 0.1f ) return;
		TryActivateOnHost( new Ray( origin, forward.Normal ) );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestSelectOnHost( Titanfall2PilotTacticalKind kind )
	{
		if ( !Networking.IsHost ) return;
		ApplySelectOnHost( kind );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestDetachGrappleOnHost()
	{
		if ( !Networking.IsHost ) return;
		FinishGrappleOnHost( refundPartial: true );
	}

	void ApplySelectOnHost( Titanfall2PilotTacticalKind kind )
	{
		if ( !Networking.IsHost ) return;
		ClearActiveStates( applyDetachBoost: false );
		SelectedKind = kind;
		MaxChargeUnits = Titanfall2PilotTacticalRules.MaxChargeUnits( kind );
		ChargeUnits = MaxChargeUnits;
		if ( _controller.IsValid() )
			Scene.GetSystem<Titanfall2JumpKitPersistence>()?.RememberTactical( _controller, kind );
	}

	internal bool TryActivateOnHost( Ray aim )
	{
		if ( !Networking.IsHost || !_controller.IsValid() || Parkour is not { Equipped: true } ) return false;
		if ( ChargeUnits < Titanfall2PilotTacticalRules.ChargeUnitsPerUse - 0.01f ) return false;

		switch ( SelectedKind )
		{
			case Titanfall2PilotTacticalKind.Stim:
				if ( IsStimActive ) return false;
				ChargeUnits = MathF.Max( 0f, ChargeUnits - Titanfall2PilotTacticalRules.ChargeUnitsPerUse );
				StimRemaining = Settings.StimDuration;
				_stimHealRemainder = 0f;
				ActivationSerial++;
				PlayTacticalSoundRpc( Settings.SoundStim1P, Settings.SoundStim3P, WorldPosition );
				return true;

			case Titanfall2PilotTacticalKind.Grapple:
				if ( GrappleActive )
				{
					FinishGrappleOnHost( refundPartial: true );
					return true;
				}
				return TryStartGrappleOnHost( aim );

			case Titanfall2PilotTacticalKind.PhaseShift:
				if ( IsPhaseActive ) return false;
				ChargeUnits = MathF.Max( 0f, ChargeUnits - Titanfall2PilotTacticalRules.ChargeUnitsPerUse );
				PhaseRemaining = Settings.PhaseDuration;
				ActivationSerial++;
				Titanfall2PhaseDimension.EnsurePhysicsRules( Scene );
				Titanfall2PhaseDimension.MarkRealspace( GameObject, inRealspace: false );
				Titanfall2PhaseDimension.CleanseStuckProjectiles( Scene, GameObject );
				SpawnPhaseRiftRpc( WorldPosition + Vector3.Up * 36f );
				PlayTacticalSoundRpc( "Jumpjet_Wallrun_Start_1P", "Jumpjet_Wallrun_Start_3P", WorldPosition );
				return true;

			case Titanfall2PilotTacticalKind.PulseBlade:
				ChargeUnits = MathF.Max( 0f, ChargeUnits - Titanfall2PilotTacticalRules.ChargeUnitsPerUse );
				ActivationSerial++;
				LaunchPulseBladeHost( aim );
				PlayTacticalSoundRpc( Settings.SoundPulseThrow1P, Settings.SoundPulseThrow3P, aim.Position );
				return true;

			default:
				return false;
		}
	}

	bool TryStartGrappleOnHost( Ray aim )
	{
		if ( !aim.Position.IsFinite || !aim.Forward.IsFinite || aim.Forward.LengthSquared < 0.25f ) return false;
		var eye = _controller.EyeTransform.Position;
		var origin = aim.Position.Distance( eye ) < 96f ? aim.Position : eye;
		var dir = aim.Forward.Normal;

		var trace = Scene.Trace.Ray( origin, origin + dir * Settings.GrappleMaxRange )
			.Radius( 4f )
			.UseHitPosition()
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile" );
		var hit = Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.ShotHull );
		if ( !hit.Hit || hit.StartedSolid || !Titanfall2CollisionQueries.CanGrapple( hit ) )
			return false;

		if ( hit.Distance < Settings.GrappleDetachLengthMin + 12f )
			return false;

		ChargeUnits = MathF.Max( 0f, ChargeUnits - Titanfall2PilotTacticalRules.ChargeUnitsPerUse );
		GrappleActive = true;
		GrappleAnchor = hit.HitPosition;
		_grappleTarget = hit.GameObject;
		_grappleTargetLocalOffset = hit.GameObject.IsValid()
			? hit.GameObject.WorldTransform.PointToLocal( hit.HitPosition )
			: Vector3.Zero;
		_grappleElapsed = 0f;
		_grappleLowSpeedTime = 0f;
		ActivationSerial++;
		if ( !IsProxy && _controller.Body.IsValid() && GrappleAnchor.z > WorldPosition.z + 36f )
		{
			_controller.PreventGrounding( 0.2f );
			var v = _controller.Body.Velocity;
			if ( v.z < 140f ) _controller.Body.Velocity = v.WithZ( 140f );
		}
		PlayTacticalSoundRpc( "Jumpjet_Jump_Start_1P", "Jumpjet_Jump_Start_3P", hit.HitPosition );
		return true;
	}

	void FinishGrappleOnHost( bool refundPartial )
	{
		if ( !GrappleActive ) return;
		if ( refundPartial && SelectedKind == Titanfall2PilotTacticalKind.Grapple )
		{
			var refund = Titanfall2PilotTacticalRules.ComputeGrappleEarlyDetachRefund( _grappleElapsed, Settings.GrappleMaxActiveSeconds );
			ChargeUnits = MathF.Min( MaxChargeUnits, ChargeUnits + refund );
		}
		GrappleActive = false;
		_grappleTarget = null;
		_grappleElapsed = 0f;
		_grappleLowSpeedTime = 0f;
	}

	void LaunchPulseBladeHost( Ray aim )
	{
		var eye = _controller.EyeTransform.Position;
		var origin = aim.Position.Distance( eye ) < 96f ? aim.Position : eye;
		var dir = aim.Forward.LengthSquared > 0.25f ? aim.Forward.Normal : _controller.EyeTransform.Forward;
		using var scope = Scene.Push();
		var go = new GameObject( false, "TF2 Pulse Blade" )
		{
			WorldPosition = origin + dir * 18f,
			WorldRotation = Rotation.LookAt( dir )
		};
		go.Flags |= GameObjectFlags.NotSaved;
		go.Tags.Add( "projectile" );
		var blade = go.AddComponent<Titanfall2PulseBlade>();
		blade.OwnerPlayer = _controller;
		blade.MountIdent = MountIdent ?? "titanfall2";
		blade.Settings = Settings;
		blade.Velocity = dir * Settings.PulseThrowSpeed;
		if ( Networking.IsActive ) go.NetworkSpawn( true, null );
		else go.Enabled = true;
	}

	void TickHost( float dt )
	{
		if ( StimRemaining > 0f )
		{
			StimRemaining = MathF.Max( 0f, StimRemaining - dt );
			ApplyStimHealingHost( dt );
		}

		if ( PhaseRemaining > 0f )
		{
			PhaseRemaining = MathF.Max( 0f, PhaseRemaining - dt );
			if ( PhaseRemaining <= 0f )
				ResolvePhaseExitSafetyHost();
		}

		if ( GrappleActive )
		{
			if ( _controller.Mode is not Titanfall2PilotMoveMode )
			{
				FinishGrappleOnHost( refundPartial: false );
			}
			else
			{
				if ( _grappleTarget.IsValid() )
					GrappleAnchor = _grappleTarget.WorldTransform.PointToWorld( _grappleTargetLocalOffset );
				if ( IsProxy ) _grappleElapsed += dt;
				if ( _grappleElapsed >= Settings.GrappleMaxActiveSeconds )
					FinishGrappleOnHost( refundPartial: false );
			}
		}

		// Recharge tactical meter when ability is not actively draining.
		if ( !IsActive && ChargeUnits < MaxChargeUnits )
		{
			var rate = Titanfall2PilotTacticalRules.RechargeUnitsPerSecond( SelectedKind, Settings );
			ChargeUnits = MathF.Min( MaxChargeUnits, ChargeUnits + rate * dt );
		}
	}

	void ApplyStimHealingHost( float dt )
	{
		if ( !_resolvedHealthProps )
		{
			_resolvedHealthProps = true;
			foreach ( var comp in GameObject.Components.GetAll() )
			{
				if ( !comp.IsValid() || comp == this ) continue;
				var desc = Game.TypeLibrary?.GetType( comp.GetType() );
				var hp = desc?.GetProperty( "Health" );
				var maxHp = desc?.GetProperty( "MaxHealth" );
				if ( hp is not null && maxHp is not null
					&& hp.PropertyType == typeof( float ) && maxHp.PropertyType == typeof( float )
					&& hp.CanRead && hp.CanWrite )
				{
					_hostPlayerComponent = comp;
					_healthProp = hp;
					_maxHealthProp = maxHp;
					break;
				}
			}
		}

		if ( !_hostPlayerComponent.IsValid() || _healthProp is null || _maxHealthProp is null ) return;
		var cur = (float)_healthProp.GetValue( _hostPlayerComponent );
		var max = (float)_maxHealthProp.GetValue( _hostPlayerComponent );
		if ( cur <= 0f || cur >= max ) return;

		_stimHealRemainder += Settings.StimHealPerSecond * dt;
		if ( _stimHealRemainder < 0.5f ) return;
		var apply = _stimHealRemainder;
		_stimHealRemainder = 0f;
		_healthProp.SetValue( _hostPlayerComponent, MathF.Min( max, cur + apply ) );
	}

	void ResolvePhaseExitSafetyHost()
	{
		if ( !_controller.IsValid() ) return;
		Titanfall2PhaseDimension.MarkRealspace( GameObject, inRealspace: true );
		Titanfall2PhaseDimension.ResolvePilotExitHost( _controller, MountIdent );
		SpawnPhaseRiftRpc( WorldPosition + Vector3.Up * 36f );
	}

	Vector3 ResolveCurrentAnchor()
	{
		if ( _grappleTarget.IsValid() )
			return _grappleTarget.WorldTransform.PointToWorld( _grappleTargetLocalOffset );
		return GrappleAnchor;
	}

	void ClearActiveStates( bool applyDetachBoost )
	{
		if ( applyDetachBoost && GrappleActive )
			DetachGrapple( impactJump: false );
		if ( Networking.IsHost )
		{
			StimRemaining = 0f;
			PhaseRemaining = 0f;
			GrappleActive = false;
		}
		_grappleTarget = null;
		_grappleElapsed = 0f;
		_grappleLowSpeedTime = 0f;
	}

	void UpdatePhasePresentation()
	{
		var phased = IsPhaseActive;
		if ( phased == _wasPhased ) return;
		_wasPhased = phased;

		if ( phased )
		{
			Titanfall2PhaseDimension.EnsurePhysicsRules( Scene );
			Titanfall2PhaseDimension.MarkRealspace( GameObject, inRealspace: false );
			if ( !Application.IsHeadless && _controller.IsValid() && _controller.Renderer.IsValid() && (IsProxy || _controller.ThirdPerson) )
				_controller.Renderer.RenderOptions.Game = false;
			if ( !IsProxy )
				Titanfall2PhaseDimension.SetCameraPhaseIsolation( Scene, ref _phaseCamera, active: true );
		}
		else
		{
			RestorePhaseVisibility();
		}
	}

	void RestorePhaseVisibility()
	{
		_wasPhased = false;
		if ( GameObject.IsValid() )
		{
			GameObject.Tags.Remove( PhasedTag );
			GameObject.Tags.Remove( Titanfall2PhaseDimension.RealspaceTag );
		}
		if ( _controller.IsValid() && _controller.Renderer.IsValid() )
			_controller.Renderer.RenderOptions.Game = true;
		Titanfall2PhaseDimension.SetCameraPhaseIsolation( Scene, ref _phaseCamera, active: false );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnPhaseRiftRpc( Vector3 origin ) =>
		Titanfall2PhaseDimension.SpawnPhaseRiftVisual( Scene, origin, large: false );

	void UpdateGrappleVisual()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		if ( !GrappleActive || !_controller.IsValid() )
		{
			DestroyRopeVisual();
			return;
		}

		if ( !_ropeVisual.IsValid() )
		{
			_ropeVisual = new GameObject( Scene, false, "TF2 Pilot Grapple Cable" );
			_ropeVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_ropeStartObj = new GameObject( _ropeVisual, true, "Start" );
			_ropeEndObj = new GameObject( _ropeVisual, true, "End" );
			_ropeLine = _ropeVisual.AddComponent<LineRenderer>();
			_ropeLine.Points = [_ropeStartObj, _ropeEndObj];
			_ropeLine.Width = new Curve( new Curve.Frame( 0f, 1.4f ), new Curve.Frame( 1f, 0.9f ) );
			_ropeLine.Color = new Gradient(
				new Gradient.ColorFrame( 0f, new Color( 0.45f, 0.90f, 1f ) * 2.5f ),
				new Gradient.ColorFrame( 1f, new Color( 0.95f, 0.98f, 1f ) * 3f ) );
			_ropeLine.Opaque = false;
			_ropeLine.Additive = true;
			_ropeLine.CastShadows = false;
			var light = _ropeEndObj.AddComponent<PointLight>();
			light.LightColor = new Color( 0.35f, 0.85f, 1f ) * 2f;
			light.Radius = 72f;
			light.Shadows = false;
			_ropeVisual.Enabled = true;
		}

		var eye = _controller.EyeTransform;
		var handOffset = !IsProxy && !_controller.ThirdPerson
			? eye.Rotation * new Vector3( 14f, -10f, -8f )
			: eye.Rotation * new Vector3( 6f, -8f, -12f );
		_ropeStartObj.WorldPosition = eye.Position + handOffset;
		_ropeEndObj.WorldPosition = ResolveCurrentAnchor();
	}

	void DestroyRopeVisual()
	{
		if ( _ropeVisual.IsValid() ) _ropeVisual.Destroy();
		_ropeVisual = null;
		_ropeStartObj = null;
		_ropeEndObj = null;
		_ropeLine = null;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayTacticalSoundRpc( string sound1P, string sound3P, Vector3 worldPos )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var localFirstPerson = !IsProxy && _controller.IsValid() && !_controller.ThirdPerson;
		var chosen = localFirstPerson && !string.IsNullOrWhiteSpace( sound1P ) ? sound1P : sound3P;
		if ( !string.IsNullOrWhiteSpace( chosen ) )
			Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent ?? "titanfall2", chosen, worldPos );
	}
}

