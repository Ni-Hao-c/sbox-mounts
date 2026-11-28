/// <summary>
/// Pilot Tactical ability controller attached alongside <see cref="Titanfall2PilotParkour"/> and
/// <see cref="Titanfall2PilotCombat"/>. Implements Stim, Grapple, Phase Shift, and Pulse Blade.
/// A single picked-up tactical is triggered via [T] (spray).
/// </summary>
[Title( "Titanfall 2 Pilot Tactical" ), Group( "Titanfall 2" )]
public sealed partial class Titanfall2PilotTactical : Component
{
	internal const string PhasedTag = "tf2-phased";

	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public Titanfall2PilotParkour Parkour { get; set; }
	[Property] public Titanfall2PilotTacticalSettings Settings { get; set; } = Titanfall2PilotTacticalSettings.RetailDefault;

	[Sync( SyncFlags.FromHost )] public Titanfall2PilotTacticalKind SelectedKind { get; private set; } = Titanfall2PilotTacticalKind.Stim;
	[Sync( SyncFlags.FromHost )] public bool HasEquippedTactical { get; private set; }
	[Sync( SyncFlags.FromHost )] public float ChargeUnits { get; private set; }
	[Sync( SyncFlags.FromHost )] public float MaxChargeUnits { get; private set; }
	[Sync( SyncFlags.FromHost )] public float StimRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float StimSpeedRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public float PhaseRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool GrappleActive { get; private set; }
	[Sync( SyncFlags.FromHost )] public Vector3 GrappleAnchor { get; private set; }
	[Sync( SyncFlags.FromHost )] public int ActivationSerial { get; private set; }

	public bool IsStimActive => StimRemaining > 0f;
	public bool IsPhaseActive => PhaseRemaining > 0f;
	public bool IsGrappling => GrappleActive;
	public bool IsActive => IsStimActive || StimSpeedRemaining > 0f || IsPhaseActive || IsGrappling;
	public float SpeedMultiplier => StimSpeedRemaining > 0f
		? Titanfall2PilotTacticalRules.ComputeStimSpeedScale( StimSpeedRemaining, Settings.StimSpeedScale )
		: IsPhaseActive ? Settings.PhaseSpeedScale : 1f;
	public float WallrunSpeedMultiplier => Titanfall2PilotTacticalRules.ComputeStimSpeedScale( StimSpeedRemaining, Settings.StimWallrunSpeedScale );
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
	int _grappleMovementSerial = -1;
	TimeUntil _nextGrappleAttempt;
	string _grappleSurfaceMaterial;
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
	SkinnedModelRenderer _phaseHiddenRenderer;
	bool _phaseOriginalGameVisibility;

	internal static Titanfall2PilotTacticalSettings ResolveSettings( string mountIdent ) =>
		Sandbox.Mounting.Directory.Get( mountIdent ?? "titanfall2" ) is Titanfall2Mount mount
			? mount.GetPilotTacticalSettings()
			: Titanfall2PilotTacticalSettings.RetailDefault;

	[ConCmd( "tf2_tactical", Help = "Activate the picked-up Pilot tactical. Equip one from Entities > Titanfall 2 first." )]
	static void TacticalCommand()
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
		tactical.TriggerTactical();
	}

	internal void ConfigureForEquip( Titanfall2PilotParkour parkour, string mountIdent, Titanfall2PilotTacticalKind? persistedKind = null )
	{
		Parkour = parkour;
		MountIdent = mountIdent ?? "titanfall2";
		Settings = ResolveSettings( MountIdent );
		if ( Networking.IsHost && persistedKind.HasValue )
		{
			var kind = persistedKind.Value;
			HasEquippedTactical = true;
			SelectedKind = kind;
			MaxChargeUnits = Titanfall2PilotTacticalRules.MaxChargeUnits( kind );
			ChargeUnits = MaxChargeUnits;
			StimRemaining = 0f;
			StimSpeedRemaining = 0f;
			PhaseRemaining = 0f;
			GrappleActive = false;
		}
		Enabled = true;
	}

	internal void ResetOnUnequip()
	{
		ClearActiveStates( applyDetachBoost: false );
		if ( Networking.IsHost )
		{
			HasEquippedTactical = false;
			ChargeUnits = 0f;
			MaxChargeUnits = 0f;
		}
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
		if ( !_controller.Active || _controller.Mode is not Titanfall2PilotMoveMode )
		{
			ClearActiveStates( applyDetachBoost: false );
			DestroyRopeVisual();
			if ( Networking.IsHost && _controller.Active )
				TickHost( float.IsFinite( Time.Delta ) ? MathF.Max( Time.Delta, 0f ) : 0f );
			return;
		}
		RefreshGrappleMovementState();

		if ( Networking.IsHost )
			TickHost( float.IsFinite( Time.Delta ) ? MathF.Max( Time.Delta, 0f ) : 0f );

		UpdatePhasePresentation();
		UpdateGrappleVisual();
		UpdateRetailPresentation();
		UpdateRetailScreenEffects();
	}

	protected override void OnDisabled()
	{
		StopRetailPresentation( playEndSound: true );
		ClearActiveStates( applyDetachBoost: false );
		DestroyRopeVisual();
		RestorePhaseVisibility();
	}

	protected override void OnDestroy()
	{
		StopRetailPresentation( playEndSound: true );
		StopRetailScreenEffects();
		DestroyRopeVisual();
		RestorePhaseVisibility();
	}

	/// <summary>Triggers the selected Pilot tactical (or detaches active grapple) from local input.</summary>
	public void TriggerTactical()
	{
		if ( IsProxy || !HasEquippedTactical || Parkour is not { IsValid: true, Equipped: true } || !_controller.IsValid() ) return;
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
		RefreshGrappleMovementState();

		var anchor = ResolveCurrentAnchor();
		var pos = WorldPosition + Vector3.Up * 36f;
		// Host timers follow elapsed frame time, independent of the movement substep count.
		if ( !Networking.IsHost ) _grappleElapsed += dt;

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

	void RefreshGrappleMovementState()
	{
		// A remote owner's predicted movement counters belong to one host activation.
		// They must not leak into the next hook after a replicated detach/re-attach.
		if ( !Networking.IsHost && (!GrappleActive || _grappleMovementSerial != ActivationSerial) )
		{
			_grappleElapsed = 0f;
			_grappleLowSpeedTime = 0f;
		}
		_grappleMovementSerial = ActivationSerial;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestActivateOnHost( Vector3 origin, Vector3 forward )
	{
		if ( !Networking.IsHost || !origin.IsFinite || !forward.IsFinite || forward.LengthSquared < 0.1f ) return;
		TryActivateOnHost( new Ray( origin, forward.Normal ) );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestDetachGrappleOnHost()
	{
		if ( !Networking.IsHost ) return;
		FinishGrappleOnHost( refundPartial: true );
	}

	/// <summary>Host-only pickup transaction. Replacing an item cancels the previous active effect.</summary>
	internal bool TryEquipOnHost( Titanfall2PilotTacticalKind kind )
	{
		if ( !Networking.IsHost || Parkour is not { IsValid: true, Equipped: true } ) return false;
		if ( !Titanfall2PilotTacticalRules.CanEquip( HasEquippedTactical, SelectedKind, kind ) ) return false;
		_controller ??= GetComponent<PlayerController>();
		if ( !_controller.IsValid() || !_controller.Active ) return false;
		ClearActiveStates( applyDetachBoost: false );
		DestroyRopeVisual();
		RestorePhaseVisibility();
		HasEquippedTactical = true;
		SelectedKind = kind;
		MaxChargeUnits = Titanfall2PilotTacticalRules.MaxChargeUnits( kind );
		ChargeUnits = MaxChargeUnits;
		if ( _controller.IsValid() )
			Scene.GetSystem<Titanfall2JumpKitPersistence>()?.RememberTactical( _controller, kind );
		return true;
	}

	internal bool TryActivateOnHost( Ray aim )
	{
		if ( !Networking.IsHost || !HasEquippedTactical || !_controller.IsValid() || Parkour is not { Equipped: true }
			|| _controller.Mode is not Titanfall2PilotMoveMode || IsActive ) return false;
		if ( !aim.Position.IsFinite || !aim.Forward.IsFinite || aim.Forward.LengthSquared < 0.1f ) return false;
		aim = new Ray( _controller.EyeTransform.Position, aim.Forward.Normal );
		if ( ChargeUnits < Titanfall2PilotTacticalRules.ChargeUnitsPerUse - 0.01f ) return false;

		switch ( SelectedKind )
		{
			case Titanfall2PilotTacticalKind.Stim:
				if ( IsStimActive ) return false;
				ChargeUnits = MathF.Max( 0f, ChargeUnits - Titanfall2PilotTacticalRules.ChargeUnitsPerUse );
				StimRemaining = Settings.StimDuration;
				// sh_stim.gnut keeps the movement status for another 0.5s, fading its last 0.25s.
				StimSpeedRemaining = Settings.StimDuration + Titanfall2PilotTacticalRules.StimSpeedTailSeconds;
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
				PlayTacticalSoundRpc( "Pilot_PhaseShift_Activate_1P", "Pilot_PhaseShift_Activate_3P", WorldPosition );
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
		if ( _nextGrappleAttempt > 0f ) return false;
		_nextGrappleAttempt = 0.2f;
		PlayTacticalSoundRpc( "pilot_grapple_fire", "pilot_grapple_fire_3p", WorldPosition );
		var eye = _controller.EyeTransform.Position;
		var origin = aim.Position.Distance( eye ) < 96f ? aim.Position : eye;
		var dir = aim.Forward.Normal;

		var trace = Scene.Trace.Ray( origin, origin + dir * Settings.GrappleMaxRange )
			.Radius( 4f )
			.UseHitPosition()
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile" );
		var hit = Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.ShotHull );
		if ( !hit.Hit || hit.StartedSolid || !Titanfall2CollisionQueries.CanGrapple( hit )
			|| hit.Distance < Settings.GrappleDetachLengthMin + 12f )
		{
			if ( hit.Hit && !hit.StartedSolid ) PlayGrappleContactRpc( hit.HitPosition, GrappleSurfaceMaterial( hit ), "grapple_disallowed" );
			PlayGrappleResetRpc( retract: false );
			return false;
		}

		ChargeUnits = MathF.Max( 0f, ChargeUnits - Titanfall2PilotTacticalRules.ChargeUnitsPerUse );
		GrappleActive = true;
		GrappleAnchor = hit.HitPosition;
		_grappleSurfaceMaterial = GrappleSurfaceMaterial( hit );
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
		PlayGrappleContactRpc( GrappleAnchor, _grappleSurfaceMaterial, "grapple" );
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
		PlayGrappleContactRpc( ResolveCurrentAnchor(), _grappleSurfaceMaterial ?? "C", "grapple_extract" );
		PlayGrappleResetRpc( retract: true );
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
		blade.Velocity = dir * Settings.PulseThrowSpeed + (_controller.Body.IsValid() ? _controller.Body.Velocity : Vector3.Zero);
		if ( Networking.IsActive ) go.NetworkSpawn( true, null );
		else go.Enabled = true;
	}

	void TickHost( float dt )
	{
		var activeSecondsAtStart = MathF.Max( StimRemaining, PhaseRemaining );
		var wasGrappling = GrappleActive;
		var grappleRechargeSeconds = 0f;
		if ( StimRemaining > 0f )
		{
			var healSeconds = MathF.Min( StimRemaining, dt );
			StimRemaining = MathF.Max( 0f, StimRemaining - dt );
			ApplyStimHealingHost( healSeconds );
		}
		StimSpeedRemaining = MathF.Max( 0f, StimSpeedRemaining - dt );

		if ( PhaseRemaining > 0f )
		{
			PhaseRemaining = MathF.Max( 0f, PhaseRemaining - dt );
			if ( PhaseRemaining <= 0f )
				ResolvePhaseExitSafetyHost();
		}

		if ( GrappleActive )
		{
			if ( _controller.Mode is not Titanfall2PilotMoveMode
				|| (_grappleTarget is not null && !_grappleTarget.IsValid()) )
			{
				FinishGrappleOnHost( refundPartial: false );
			}
			else
			{
				if ( _grappleTarget.IsValid() )
					GrappleAnchor = _grappleTarget.WorldTransform.PointToWorld( _grappleTargetLocalOffset );
				var grappleSecondsRemaining = MathF.Max( 0f, Settings.GrappleMaxActiveSeconds - _grappleElapsed );
				_grappleElapsed += dt;
				if ( _grappleElapsed >= Settings.GrappleMaxActiveSeconds )
				{
					// Only an automatic timeout has a known boundary within this frame.
					grappleRechargeSeconds = MathF.Max( 0f, dt - grappleSecondsRemaining );
					FinishGrappleOnHost( refundPartial: false );
				}
			}
		}

		// Recharge tactical meter when ability is not actively draining.
		if ( HasEquippedTactical && !IsStimActive && !IsPhaseActive && !GrappleActive && ChargeUnits < MaxChargeUnits )
		{
			// A long frame may cross the activation boundary; only its inactive portion recharges.
			var rechargeSeconds = wasGrappling ? grappleRechargeSeconds : MathF.Max( 0f, dt - activeSecondsAtStart );
			var rate = Titanfall2PilotTacticalRules.RechargeUnitsPerSecond( SelectedKind, Settings );
			ChargeUnits = MathF.Min( MaxChargeUnits, ChargeUnits + rate * rechargeSeconds );
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
		StopRetailPresentation( playEndSound: true );
		StopRetailScreenEffects();
		if ( Networking.IsHost && IsPhaseActive && _controller.IsValid() && _controller.Active )
			ResolvePhaseExitSafetyHost();
		if ( applyDetachBoost && GrappleActive )
			DetachGrapple( impactJump: false );
		if ( Networking.IsHost )
		{
			if ( GrappleActive ) FinishGrappleOnHost( refundPartial: false );
			StimRemaining = 0f;
			StimSpeedRemaining = 0f;
			PhaseRemaining = 0f;
			GrappleActive = false;
		}
		_grappleTarget = null;
		_grappleElapsed = 0f;
		_grappleLowSpeedTime = 0f;
		RestorePhaseVisibility();
	}

	void UpdatePhasePresentation()
	{
		var phased = IsPhaseActive;
		if ( phased != _wasPhased )
		{
			_wasPhased = phased;
			if ( phased )
			{
				Titanfall2PhaseDimension.EnsurePhysicsRules( Scene );
				Titanfall2PhaseDimension.MarkRealspace( GameObject, inRealspace: false );
			}
			else
			{
				RestorePhaseVisibility();
			}
		}
		UpdatePhaseBodyVisibility( phased );
		UpdatePhaseCameraIsolation();
	}

	void UpdatePhaseBodyVisibility( bool phased )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		var renderer = phased && _controller.IsValid() && (IsProxy || !LocalTacticalCamera().IsValid())
			? _controller.Renderer : null;
		if ( renderer != _phaseHiddenRenderer ) RestorePhaseBodyVisibility();
		if ( !renderer.IsValid() ) return;
		if ( !_phaseHiddenRenderer.IsValid() )
		{
			// A host system may already own invisibility (seat, rodeo, death).
			// Take ownership only when this component actually changes visibility.
			if ( !renderer.RenderOptions.Game ) return;
			_phaseHiddenRenderer = renderer;
			_phaseOriginalGameVisibility = renderer.RenderOptions.Game;
		}
		renderer.RenderOptions.Game = false;
	}

	void RestorePhaseBodyVisibility()
	{
		if ( _phaseHiddenRenderer.IsValid() )
			_phaseHiddenRenderer.RenderOptions.Game = _phaseOriginalGameVisibility;
		_phaseHiddenRenderer = null;
	}

	void UpdatePhaseCameraIsolation() => Titanfall2PhaseDimension.SetCameraPhaseIsolation(
		Scene, ref _phaseCamera, active: IsPhaseActive && LocalTacticalCamera().IsValid() );

	void RestorePhaseVisibility()
	{
		_wasPhased = false;
		if ( GameObject.IsValid() )
		{
			GameObject.Tags.Remove( PhasedTag );
			GameObject.Tags.Remove( Titanfall2PhaseDimension.RealspaceTag );
		}
		RestorePhaseBodyVisibility();
		Titanfall2PhaseDimension.SetCameraPhaseIsolation( Scene, ref _phaseCamera, active: false );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnPhaseRiftRpc( Vector3 origin ) =>
		Titanfall2PilotTacticalEffects.Spawn( Scene, MountIdent, "P_phase_shift_main", origin, false, 2f );

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
			// Rope integration is still an s&box adapter; do not invent an energy beam or light.
			_ropeLine.Color = new Gradient( new Gradient.ColorFrame( 0f, new Color( 0.32f, 0.33f, 0.34f ) ),
				new Gradient.ColorFrame( 1f, new Color( 0.32f, 0.33f, 0.34f ) ) );
			_ropeLine.Opaque = false;
			_ropeLine.Additive = false;
			_ropeLine.CastShadows = false;
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
		var localFirstPerson = LocalTacticalCamera().IsValid();
		var chosen = localFirstPerson && !string.IsNullOrWhiteSpace( sound1P ) ? sound1P : sound3P;
		if ( !string.IsNullOrWhiteSpace( chosen ) )
			Titanfall2SoundEventEmitter.OneShot( Scene, MountIdent ?? "titanfall2", chosen, worldPos, localFirstPerson );
	}
}

