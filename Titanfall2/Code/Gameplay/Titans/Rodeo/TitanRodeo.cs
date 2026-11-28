/// <summary>Host-owned, single-slot external Titan rider. The cockpit remains independently occupied.</summary>
[Title( "Titanfall 2 Titan Rodeo" ), Group( "Titanfall 2" )]
public sealed partial class Titanfall2TitanRodeo : Component
{
	[Property] public Titanfall2Npc Actor { get; set; }
	[Sync( SyncFlags.FromHost )] public PlayerController Rider { get; private set; }
	[Sync( SyncFlags.FromHost )] public Titanfall2RodeoState State { get; private set; }
	[Sync( SyncFlags.FromHost )] public int SessionSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] public double StateStartedAt { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool IsFriendlyRide { get; private set; }
	public bool IsRiding => Rider.IsValid() && State != Titanfall2RodeoState.None;
	public bool BlockingWeapons => IsRiding && State != Titanfall2RodeoState.FriendlyIdle;
	public bool UsesAuthoredTiming { get; private set; }
	public string Diagnostic { get; private set; }
	public float StateDuration => _duration;
	public float StateElapsed => (float)Math.Max( 0, Time.NowDouble - StateStartedAt );
	public PlayerController LocalRider => Rider is { IsProxy: false } ? Rider : null;
	readonly Titanfall2RodeoCommitGate _commit = new();
	Titanfall2TitanRodeoMoveMode _mode;
	Vector3 _mountStart, _lastSafePosition;
	float _duration, _commitAt, _pointOfNoReturn = float.PositiveInfinity;
	double _mountedAt;
	TimeUntil _reentryDelay;
	BaseInventoryComponent _inventory;
	BaseCombatWeapon _weapon;
	bool _oldPumping, _oldViewEnabled, _capturedView;
	bool _releasing;

	internal static Titanfall2TitanRodeo ForPilot( PlayerController pilot ) =>
		pilot.IsValid() && pilot.GetComponent<Titanfall2TitanRodeoMoveMode>() is { Rodeo: { IsValid: true } rodeo } && rodeo.Rider == pilot ? rodeo : null;
	internal static bool IsAttached( PlayerController pilot ) => ForPilot( pilot ) is { IsRiding: true };
	bool HostReady => Networking.IsHost && !IsProxy && Active && Actor is { Active: true, IsTitan: true, TitanCanAct: true, IsInTermination: false, IsProxy: false };
	bool SamePhase( PlayerController pilot ) => Actor.GameObject.Tags.Has( Titanfall2PhaseDimension.PhasedTag ) == pilot.GameObject.Tags.Has( Titanfall2PhaseDimension.PhasedTag );

	public bool CanMount( PlayerController pilot )
	{
		if ( !HostReady || IsRiding || _reentryDelay > 0 || !Titanfall2PilotBattery.IsPilotAlive( pilot ) || pilot.IsProxy || pilot.Scene != Scene
			|| !Titanfall2TitanSeat.HasPilotCombatState( pilot ) || IsAttached( pilot ) || pilot.GetComponentInParent<BaseChair>() is not null
			|| Actor.Seat is { IsTransitioning: true } || !SamePhase( pilot ) || Titanfall2Npc.IsNeutralFaction( Actor.Faction )
			|| Actor.ModelPath?.Contains( "buddy", StringComparison.OrdinalIgnoreCase ) == true ) return false;
		// Cockpit reservations are not parents until they commit; reject those explicitly.
		if ( Scene.GetAllComponents<Titanfall2TitanSeat>().Any( seat => seat.IsTransitioning && seat.LocalPilot == pilot ) ) return false;
		var delta = pilot.WorldPosition - Actor.WorldPosition;
		if ( delta.WithZ( 0 ).Length > Titanfall2RodeoRules.MountRange || MathF.Abs( delta.z ) > Actor.BodyHeight + 80 ) return false;
		return HasClearPath( pilot, pilot.WorldPosition, RiderAnchor().Position );
	}

	public bool TryMount( PlayerController pilot )
	{
		if ( !CanMount( pilot ) ) return false;
		Rider = pilot;
		pilot.GetComponent<Titanfall2PilotCombat>()?.CancelForTitanInteraction();
		IsFriendlyRide = Actor.Friendly;
		SessionSerial = SessionSerial == int.MaxValue ? 1 : SessionSerial + 1;
		_mountStart = _lastSafePosition = pilot.WorldPosition;
		_mountedAt = Time.NowDouble;
		_mode = pilot.GameObject.GetOrAddComponent<Titanfall2TitanRodeoMoveMode>();
		_mode.Rodeo = this;
		_mode.Enabled = true;
		BeginState( Titanfall2RodeoState.Mounting );
		return IsRiding;
	}

	void BeginState( Titanfall2RodeoState state )
	{
		State = state;
		StateStartedAt = Time.NowDouble;
		UsesAuthoredTiming = false;
		_pointOfNoReturn = float.PositiveInfinity;
		_duration = state switch
		{
			Titanfall2RodeoState.Mounting => 0.65f,
			Titanfall2RodeoState.StealingBattery => 3.2f,
			Titanfall2RodeoState.ThrowingGrenade => 2.2f,
			Titanfall2RodeoState.InsertingBattery => 2.4f,
			_ => float.PositiveInfinity
		};
		_commitAt = state == Titanfall2RodeoState.InsertingBattery ? _duration : _duration * 0.85f;
		_commit.Begin( SessionSerial );
		if ( BlockingWeapons ) PauseInventory(); else RestoreInventory();
		BeginPresentation();
	}

	/// <summary>Only local decoded sequence metadata may configure the host clock; no visual callback delivers damage.</summary>
	internal bool ConfigureAnimationTiming( int serial, Titanfall2RodeoState state, float duration, float commitCycle, float pointOfNoReturn = float.PositiveInfinity )
	{
		if ( !HostReady || serial != SessionSerial || state != State || _commit.Committed || !float.IsFinite( duration ) || duration <= 0 || duration > 30
			|| !float.IsFinite( commitCycle ) || commitCycle < 0 || commitCycle > 1 ) return false;
		_duration = duration;
		_commitAt = duration * commitCycle;
		_pointOfNoReturn = float.IsFinite( pointOfNoReturn ) ? duration * Math.Clamp( pointOfNoReturn, 0, 1 ) : float.PositiveInfinity;
		UsesAuthoredTiming = true;
		return true;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		if ( !IsRiding ) { if ( State != Titanfall2RodeoState.None ) ReleaseRider(); return; }
		if ( !HostReady || !Titanfall2PilotBattery.IsPilotAlive( Rider ) || !Titanfall2TitanSeat.HasPilotCombatState( Rider ) || !SamePhase( Rider )
			|| Actor.Friendly != IsFriendlyRide || _mode is not { IsValid: true, Active: true } ) { ReleaseRider(); return; }
		UpdateRiderPlacement();
		if ( !IsRiding ) return;
		TickPresentation();
		var elapsed = StateElapsed;
		if ( State == Titanfall2RodeoState.Mounting )
		{
			if ( elapsed >= _duration ) BeginState( Titanfall2RodeoRules.SelectAction( IsFriendlyRide,
				Rider.GetComponent<Titanfall2PilotBattery>() is { HasBattery: true } ) );
			return;
		}
		if ( State == Titanfall2RodeoState.FriendlyIdle ) return;
		if ( _commit.TryCommit( SessionSerial, HostReady, elapsed, _commitAt ) ) CommitAction();
		if ( !IsRiding ) return;
		if ( elapsed >= _duration )
		{
			if ( IsFriendlyRide ) BeginState( Titanfall2RodeoState.FriendlyIdle );
			else ReleaseRider( true, -Actor.WorldRotation.Forward * 450 + Vector3.Up * 80 );
		}
	}

	void CommitAction()
	{
		if ( !HostReady || !Titanfall2PilotBattery.IsPilotAlive( Rider ) ) return;
		var battery = Rider.GameObject.GetOrAddComponent<Titanfall2PilotBattery>();
		if ( State == Titanfall2RodeoState.InsertingBattery )
		{
			if ( battery.HasBattery && Actor.ApplyRodeoBattery() ) battery.Consume();
			return;
		}
		var grenade = State == Titanfall2RodeoState.ThrowingGrenade;
		if ( State != Titanfall2RodeoState.StealingBattery && !grenade ) return;
		var pilot = Rider;
		// Give before damage: the native death pipeline can immediately cancel this attachment.
		if ( !grenade ) battery.Give();
		Actor.ApplyRodeoDamage( pilot.GameObject, Titanfall2RodeoRules.Damage( Actor.IsDoomed, Actor.Health, grenade ) );
		// The retail rip/damage callback also throws the rider off; the remainder of the
		// authored action is not an extra opportunity for a second damage delivery.
		if ( IsRiding ) ReleaseRider( true, -Actor.WorldRotation.Forward * 450 + Vector3.Up * 80 );
	}

	public void ConsumeInput( bool jumpPressed, bool usePressed, Vector3 moveInput )
	{
		if ( !HostReady || !IsRiding || (!jumpPressed && !usePressed)
			|| !Titanfall2RodeoRules.CanJumpOff( (float)(Time.NowDouble - _mountedAt), StateElapsed >= _pointOfNoReturn ) ) return;
		var direction = (Rider.EyeTransform.Rotation * moveInput).WithZ( 0 );
		ReleaseRider( false, (direction.LengthSquared > 0.01f ? direction.Normal * 350 : Vector3.Zero) + Vector3.Up * 390 );
	}

	internal void UpdateRiderPlacement()
	{
		if ( !HostReady || !IsRiding || Rider.IsProxy || _mode is not { IsValid: true } || Rider.Mode != _mode ) return;
		var target = RiderAnchor().Position;
		if ( State == Titanfall2RodeoState.Mounting )
		{
			var t = Math.Clamp( StateElapsed / MathF.Max( _duration, 0.01f ), 0, 1 );
			t = t * t * (3 - 2 * t);
			target = _mountStart + (target - _mountStart) * t;
		}
		if ( !HasClearPath( Rider, Rider.WorldPosition, target ) ) { ReleaseRider(); return; }
		Rider.WorldPosition = target;
		_lastSafePosition = target;
	}

	Transform RiderAnchor()
	{
		if ( Actor.Renderer.IsValid() && Actor.Renderer.SceneModel.IsValid() && Actor.Renderer.SceneModel.GetAttachment( "hijack", true ) is { } anchor ) return anchor;
		// Explicit compatibility placement for models without an authored slot. No fabricated attachment metadata.
		return new Transform( Actor.WorldPosition - Actor.WorldRotation.Forward * (Actor.BodyRadius * 0.65f) + Vector3.Up * (Actor.BodyHeight * 0.58f), Actor.WorldRotation );
	}

	bool HasClearPath( PlayerController pilot, Vector3 from, Vector3 to )
	{
		var radius = Math.Clamp( pilot.BodyRadius * 0.5f, 6, 24 );
		var hull = new BBox( new Vector3( -radius, -radius, 1 ), new Vector3( radius, radius, Math.Clamp( pilot.BodyHeight, 32, 128 ) ) );
		var trace = Titanfall2CollisionQueries.Run( Scene.Trace.Box( hull, from, to )
			.IgnoreGameObjectHierarchy( pilot.GameObject ).IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.PlayerSolid );
		return !trace.StartedSolid && !trace.Hit;
	}

	public void ReleaseRider( bool emergency = true ) => ReleaseRider( emergency, Vector3.Zero );
	void ReleaseRider( bool emergency, Vector3 velocity )
	{
		if ( !Networking.IsHost || IsProxy || _releasing ) return;
		_releasing = true;
		try
		{
			var pilot = Rider;
			if ( pilot.IsValid() && Actor.IsValid() )
			{
				var origin = pilot.WorldPosition;
				var away = velocity.WithZ( 0 ).LengthSquared > 0.1f ? velocity.WithZ( 0 ).Normal : -Actor.WorldRotation.Forward;
				Vector3? exit = null;
				foreach ( var direction in new[] { away, -Actor.WorldRotation.Forward, Actor.WorldRotation.Right, -Actor.WorldRotation.Right, Vector3.Up } )
				{
					var candidate = origin + direction * (Actor.BodyRadius + pilot.BodyRadius + 24) + Vector3.Up * 12;
					if ( !HasClearPath( pilot, origin, candidate ) ) continue;
					exit = candidate;
					break;
				}
				if ( exit is null && !emergency ) { Diagnostic = "跳离出口受阻"; return; }
				pilot.WorldPosition = exit ?? _lastSafePosition;
			}
			DisposePresentation();
			RestoreInventory();
			_mode?.Release( velocity );
			_mode = null;
			Rider = null;
			State = Titanfall2RodeoState.None;
			SessionSerial = SessionSerial == int.MaxValue ? 1 : SessionSerial + 1;
			_reentryDelay = 0.6f;
		}
		finally { _releasing = false; }
	}

	void PauseInventory()
	{
		if ( _inventory.IsValid() ) return;
		_inventory = Rider.GetComponent<BaseInventoryComponent>();
		if ( !_inventory.IsValid() ) return;
		_oldPumping = _inventory.ManualPumping;
		_inventory.ManualPumping = true;
		_weapon = _inventory.ActiveItem as BaseCombatWeapon;
		if ( _weapon?.ViewModel.IsValid() == true )
		{
			_capturedView = true;
			_oldViewEnabled = _weapon.ViewModel.Enabled;
			_weapon.ViewModel.Enabled = false;
		}
		Rider.GetComponentInChildren<Titanfall2WeaponSoundEmitter>()?.StopFiring();
	}
	void RestoreInventory()
	{
		if ( _inventory.IsValid() ) _inventory.ManualPumping = _oldPumping;
		if ( _capturedView && _weapon?.ViewModel.IsValid() == true ) _weapon.ViewModel.Enabled = _oldViewEnabled;
		_inventory = null;
		_weapon = null;
		_capturedView = false;
	}
	protected override void OnEnabled() => Scene?.GetSystem<Titanfall2PilotInputInterceptor>()?.RegisterRodeo( this );
	protected override void OnDisabled() { Scene?.GetSystem<Titanfall2PilotInputInterceptor>()?.UnregisterRodeo( this ); ReleaseRider(); }
	protected override void OnDestroy() { Scene?.GetSystem<Titanfall2PilotInputInterceptor>()?.UnregisterRodeo( this ); ReleaseRider(); }
}
