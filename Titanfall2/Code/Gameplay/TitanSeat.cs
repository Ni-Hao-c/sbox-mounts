/// <summary>Collision-checked embarkation and local/listen-host Titan controls.</summary>
[Title( "Titanfall 2 Titan Cockpit" ), Group( "Titanfall 2" )]
public sealed class Titanfall2TitanSeat : BaseChair, Component.IPressable, Sandbox.Movement.ISitTarget
{
	const Titanfall2TraceMask DrivingMask = (Titanfall2TraceMask)((uint)Titanfall2TraceMask.NpcSolid | (uint)Titanfall2Contents.TitanClip);
	const Titanfall2TraceMask EmbarkMask = (Titanfall2TraceMask)((uint)Titanfall2TraceMask.PlayerSolid | (uint)Titanfall2TraceMask.ShotHull);
	[Property] public Titanfall2Npc Actor { get; set; }
	[Property] public float DriveSpeed { get; set; } = 180;
	[Property] public float EmbarkSeconds { get; set; } = 0.8f;
	[Property] public float EmbarkRange { get; set; } = 200;
	[Property] public bool AlignWithPilot { get; set; } = true;
	[Property] public string CoreAction { get; set; } = "flashlight";
	[Property] public bool ShowHud { get; set; } = true;
	[Property] public bool ShowCockpit { get; set; } = true;
	[Sync( SyncFlags.FromHost )] public float EmbarkRemaining { get; private set; }
	[Sync( SyncFlags.FromHost )] public int SeatSerial { get; private set; }
	public bool IsDriving { get; private set; }
	public bool IsEmbarking => BoardingPilot.IsValid();
	public string StatusMessage => _messageTime > 0 ? _message : null;
	internal PlayerController LocalPilot => GetOccupant() is { IsProxy: false } pilot ? pilot
		: BoardingPilot is { IsProxy: false } boarding ? boarding : null;
	BaseInventoryComponent _pausedInventory;
	BaseCombatWeapon _pausedWeapon;
	bool _previousPumping;
	bool _previousViewEnabled;
	bool _capturedView;
	[Sync( SyncFlags.FromHost )] PlayerController BoardingPilot { get; set; }
	PlayerController _seatedPilot;
	GameObject _hud;
	GameObject _cockpitView;
	PlayerController _viewPilot;
	bool _previousThirdPerson;
	TimeUntil _dashTime;
	TimeUntil _entryGrace;
	TimeUntil _reentryDelay;
	TimeUntil _messageTime;
	string _message;
	Vector3 _dashDirection;

	public override bool CanEnter( PlayerController player ) => Active && Actor is { Active: true, IsTitan: true, Dead: false }
		&& player.IsValid() && player.Active && player.Scene == Scene && !player.IsProxy && Networking.IsHost && !IsProxy && !Actor.IsProxy
		&& SeatPosition.IsValid() && !BoardingPilot.IsValid() && _reentryDelay <= 0
		&& player.GetComponentInParent<BaseChair>() is null && base.CanEnter( player )
		&& !Scene.GetAllComponents<Titanfall2TitanSeat>().Any( seat => seat != this && seat.BoardingPilot == player )
		&& CanReachCockpit( player );

	Component.IPressable.Tooltip? Component.IPressable.GetTooltip( Component.IPressable.Event e ) => Actor is not { Dead: false } ? null
		: new Component.IPressable.Tooltip( Actor.DisplayName ?? "泰坦", "airline_seat_recline_normal",
			IsOccupied ? "座舱已占用" : IsEmbarking ? "正在建立神经连接" : "登乘泰坦" );
	bool Component.IPressable.CanPress( Component.IPressable.Event e ) => e.Source is PlayerController pilot && CanEnter( pilot );
	bool Component.IPressable.Press( Component.IPressable.Event e ) => e.Source is PlayerController pilot && TryEmbark( pilot );

	/// <summary>Starts a cancellable boarding transition; remote-client driving is not enabled.</summary>
	public bool TryEmbark( PlayerController pilot )
	{
		if ( !CanEnter( pilot ) ) return false;
		BoardingPilot = pilot;
		SeatSerial++;
		EmbarkRemaining = Math.Clamp( EmbarkSeconds, 0.1f, 3 );
		PauseInventory( pilot );
		SetMessage( "正在登乘 · 建立神经连接", 3 );
		return true;
	}

	bool CanReachCockpit( PlayerController pilot )
	{
		if ( pilot.WorldPosition.Distance( WorldPosition ) > Math.Clamp( EmbarkRange, 96, 400 ) ) return false;
		var trace = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( pilot.EyeTransform.Position,
			WorldPosition + Vector3.Up * (Actor.BodyHeight * 0.5f) ).IgnoreGameObjectHierarchy( pilot.GameObject )
			.WithoutTags( "trigger", "projectile" ), EmbarkMask );
		return !trace.StartedSolid && (!trace.Hit || trace.GameObject == GameObject
			|| trace.GameObject?.GetComponentInParent<Titanfall2Npc>() == Actor);
	}

	void TickEmbark()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		if ( !BoardingPilot.IsValid() )
		{
			if ( EmbarkRemaining > 0 ) { SeatSerial++; RestoreInventory(); }
			BoardingPilot = null;
			EmbarkRemaining = 0;
			return;
		}
		if ( Actor is not { Dead: false } || !BoardingPilot.Active || BoardingPilot.IsProxy || IsOccupied || !CanReachCockpit( BoardingPilot ) )
		{
			BoardingPilot = null;
			SeatSerial++;
			EmbarkRemaining = 0;
			RestoreInventory();
			SetMessage( "登乘已取消", 2 );
			return;
		}
		EmbarkRemaining = MathF.Max( 0, EmbarkRemaining - Time.Delta );
		if ( EmbarkRemaining > 0 ) return;
		var pilot = BoardingPilot;
		BoardingPilot = null;
		SeatSerial++;
		_seatedPilot = pilot;
		SeatPosition.GetOrAddComponent<Titanfall2TitanSeatEscape>().Seat = this;
		// SitMoveMode owns physics transitions. Do not disable a body while Walk still ticks.
		pilot.GameObject.GetOrAddComponent<Sandbox.Movement.SitMoveMode>().Enabled = true;
		pilot.GameObject.SetParent( SeatPosition, false );
		pilot.GameObject.LocalTransform = global::Transform.Zero;
		_entryGrace = 0.6f;
		if ( AlignWithPilot ) { Actor.Friendly = true; Actor.Leader = pilot.GameObject; }
		SetMessage( "神经连接完成 · 泰坦就绪", 3 );
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost ) TickEmbark();
		UpdateHud();
		UpdateCockpitView();
		var pilot = GetOccupant();
		if ( !Networking.IsHost || Actor is not { Dead: false } || !pilot.IsValid() || pilot.IsProxy )
		{
			IsDriving = false;
			if ( !BoardingPilot.IsValid() ) RestoreInventory();
			return;
		}
		_seatedPilot = pilot;
		PauseInventory( pilot );
		if ( pilot.Mode is not Sandbox.Movement.SitMoveMode ) return;
		if ( _entryGrace <= 0 && Input.Pressed( "use" ) ) { TryDisembark( pilot ); return; }
		var aim = pilot.EyeTransform.ForwardRay;
		var core = Actor.GetComponent<Titanfall2TitanCore>();
		core?.SetAim( aim );
		if ( !string.IsNullOrWhiteSpace( CoreAction ) && Input.Pressed( CoreAction ) )
		{
			if ( core?.TryActivate( aim, pilot.GameObject ) != true )
				SetMessage( core is { Supported: true } ? "核心尚未就绪" : "此机体的核心尚未接入", 2 );
			Input.Clear( CoreAction );
		}
		if ( Input.Pressed( "reload" ) ) Actor.StartReload();
		if ( Input.Down( "attack1" ) ) Actor.Fire( aim, pilot.GameObject, piloted: true );
		if ( Actor.GetComponent<Titanfall2TitanKit>() is { } kit )
		{
			if ( Input.Pressed( "attack2" ) && !kit.TryOffense( aim, pilot.GameObject ) ) SetMessage( kit.LastOffenseFailure ?? "攻击技能冷却中", 2 );
			if ( Input.Pressed( "duck" ) && !kit.TryDefense( aim, pilot.GameObject ) ) SetMessage( "防御技能冷却中", 2 );
			if ( Input.Pressed( "run" ) && kit.TryDash() )
			{
				var move = Input.AnalogMove;
				_dashDirection = (pilot.EyeTransform.Rotation * (move.LengthSquared > 0.01f ? move : Vector3.Forward)).WithZ( 0 ).Normal;
				_dashTime = 0.2f;
			}
		}
	}

	protected override void OnFixedUpdate()
	{
		IsDriving = false;
		if ( !Networking.IsHost || Actor is not { Dead: false } || BoardingPilot.IsValid()
			|| GetOccupant() is not { IsProxy: false } pilot || pilot.Mode is not Sandbox.Movement.SitMoveMode ) return;
		if ( _dashTime > 0 ) { if ( !TryDrive( _dashDirection * (800 * Time.Delta) ) ) _dashTime = 0; return; }
		var angles = pilot.EyeAngles;
		var turn = Math.Clamp( angles.yaw, -100 * Time.Delta, 100 * Time.Delta );
		if ( MathF.Abs( turn ) > 0.001f ) { WorldRotation *= Rotation.FromYaw( turn ); pilot.EyeAngles = angles.WithYaw( angles.yaw - turn ); }
		var input = Input.AnalogMove;
		if ( input.LengthSquared < 0.01f ) return;
		var planar = (pilot.EyeTransform.Rotation * input).WithZ( 0 );
		TryDrive( planar.Normal * (MathF.Min( 1, input.Length ) * DriveSpeed * Time.Delta) );
	}

	internal bool TryDrive( Vector3 displacement )
	{
		if ( !Networking.IsHost || !Active || Actor is not { Active: true, Dead: false } || !displacement.IsFinite || displacement.LengthSquared <= 0.001f ) return false;
		var radius = MathF.Max( 4, Actor.BodyRadius - 2 );
		var hull = new BBox( new Vector3( -radius, -radius, 0 ), new Vector3( radius, radius, Actor.BodyHeight - 2 ) );
		var origin = WorldPosition + Vector3.Up * 2;
		var step = TraceHull( hull, origin, origin + displacement.WithZ( 0 ), DrivingMask );
		if ( step.StartedSolid || (step.EndPosition - origin).LengthSquared <= 0.01f ) return false;
		var ground = TraceHull( hull, step.EndPosition, step.EndPosition - Vector3.Up * 32, DrivingMask );
		if ( !ground.Hit || ground.StartedSolid || ground.Normal.z < 0.6f || !HasFootSupport( ground.EndPosition, radius, DrivingMask ) ) return false;
		WorldPosition = ground.EndPosition;
		IsDriving = true;
		return true;
	}

	SceneTraceResult TraceHull( BBox hull, Vector3 start, Vector3 end, Titanfall2TraceMask mask ) =>
		Titanfall2CollisionQueries.Run( Scene.Trace.Box( hull, start, end ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "projectile" ), mask );

	bool HasFootSupport( Vector3 position, float radius, Titanfall2TraceMask mask )
	{
		// A downward box sweep can land with only one corner on a platform.
		// Check the feet independently so a candidate over a ledge cannot pass.
		var edge = radius * 0.7f;
		Vector3[] samples = [Vector3.Zero, new( edge, edge, 0 ), new( edge, -edge, 0 ), new( -edge, edge, 0 ), new( -edge, -edge, 0 )];
		foreach ( var offset in samples )
		{
			var foot = position + offset;
			var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( foot + Vector3.Up * 32, foot - Vector3.Up * 40 )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" ), mask );
			if ( hit.StartedSolid || !hit.Hit || hit.Normal.z < 0.65f || MathF.Abs( hit.EndPosition.z - position.z ) > 28 ) return false;
		}
		return true;
	}

	/// <summary>Finds a grounded exit reachable without crossing world or imported player clips.</summary>
	internal bool TryFindExit( PlayerController pilot, out Vector3 position, bool emergency = false )
	{
		position = default;
		if ( !pilot.IsValid() || !Actor.IsValid() ) return false;
		var radius = Math.Clamp( pilot.BodyRadius, 8, 48 );
		var hull = new BBox( new Vector3( -radius, -radius, 1 ), new Vector3( radius, radius, Math.Clamp( pilot.BodyHeight, 32, 128 ) ) );
		var facing = pilot.EyeTransform.Forward.WithZ( 0 ).Normal;
		if ( facing.LengthSquared < 0.1f ) facing = WorldRotation.Forward;
		var origin = WorldPosition + Vector3.Up * 2;
		var candidates = Enumerable.Range( 0, emergency ? 24 : 12 ).Select( index =>
		{
			var direction = Rotation.FromYaw( index * 30 ) * facing;
			return (Direction: direction, Point: WorldPosition + direction * (Actor.BodyRadius + radius + 26 + (index / 12) * 72));
		} ).OrderByDescending( candidate => Vector3.Dot( candidate.Direction, facing ) );
		foreach ( var candidate in candidates )
		{
			var lateral = TraceHull( hull, origin, candidate.Point + Vector3.Up * 2, Titanfall2TraceMask.PlayerSolid );
			if ( lateral.StartedSolid || lateral.Hit ) continue;
			var floor = TraceHull( hull, lateral.EndPosition + Vector3.Up * 20, lateral.EndPosition - Vector3.Up * 64, Titanfall2TraceMask.PlayerSolid );
			if ( floor.StartedSolid || !floor.Hit || floor.Normal.z < 0.65f ) continue;
			var fit = TraceHull( hull, floor.EndPosition + Vector3.Up, floor.EndPosition + Vector3.Up * 1.1f, Titanfall2TraceMask.PlayerSolid );
			if ( fit.Hit || fit.StartedSolid || !HasFootSupport( floor.EndPosition + Vector3.Up, radius, Titanfall2TraceMask.PlayerSolid ) ) continue;
			position = floor.EndPosition + Vector3.Up;
			return true;
		}
		if ( !emergency ) return false;
		var above = WorldPosition + Vector3.Up * (Actor.BodyHeight + 12);
		var escape = TraceHull( hull, pilot.WorldPosition, above, Titanfall2TraceMask.PlayerSolid );
		if ( escape.StartedSolid || escape.Hit ) return false;
		position = above;
		return true;
	}

	public bool TryDisembark( PlayerController pilot, bool emergency = false )
	{
		if ( !Networking.IsHost || IsProxy || !pilot.IsValid() || !ContainsPilot( pilot ) || (!emergency && _entryGrace > 0) ) return false;
		if ( !TryFindExit( pilot, out var exit, emergency ) )
		{
			if ( !emergency ) { SetMessage( "出口受阻 · 请移动泰坦后重试", 3 ); return false; }
			// Destruction must not delete a child pilot, even inside a sealed room.
			exit = pilot.WorldPosition;
		}
		var look = pilot.EyeTransform.Rotation.Angles();
		pilot.GameObject.SetParent( null, true );
		pilot.WorldPosition = exit;
		pilot.EyeAngles = look;
		RestoreInventory();
		_seatedPilot = null;
		SeatSerial++;
		_reentryDelay = 0.8f;
		IsDriving = false;
		return true;
	}

	bool ContainsPilot( PlayerController pilot )
	{
		for ( var parent = pilot.GameObject.Parent; parent.IsValid(); parent = parent.Parent )
			if ( parent == SeatPosition || parent == GameObject ) return true;
		return false;
	}

	void Sandbox.Movement.ISitTarget.AskToLeave( PlayerController player ) { if ( _entryGrace <= 0 ) TryDisembark( player ); }
	public override bool CanLeave( PlayerController player ) => TryFindExit( player, out _ );
	public new void Eject( PlayerController player ) => TryDisembark( player, emergency: true );

	void PauseInventory( PlayerController pilot )
	{
		var inventory = pilot.GetComponent<BaseInventoryComponent>();
		if ( inventory == _pausedInventory && pilot == _viewPilot ) return;
		RestoreInventory();
		_viewPilot = pilot;
		_previousThirdPerson = pilot.ThirdPerson;
		pilot.ThirdPerson = false;
		_pausedInventory = inventory;
		if ( !inventory.IsValid() ) return;
		_previousPumping = inventory.ManualPumping;
		inventory.ManualPumping = true;
		_pausedWeapon = inventory.ActiveItem as BaseCombatWeapon;
		if ( _pausedWeapon?.ViewModel.IsValid() == true ) { _capturedView = true; _previousViewEnabled = _pausedWeapon.ViewModel.Enabled; _pausedWeapon.ViewModel.Enabled = false; }
		pilot.GetComponentInChildren<Titanfall2WeaponSoundEmitter>()?.StopFiring();
	}
	void RestoreInventory()
	{
		_dashTime = 0;
		if ( _viewPilot.IsValid() ) _viewPilot.ThirdPerson = _previousThirdPerson;
		_viewPilot = null;
		if ( _pausedInventory.IsValid() ) _pausedInventory.ManualPumping = _previousPumping;
		if ( _capturedView && _pausedWeapon?.ViewModel.IsValid() == true ) _pausedWeapon.ViewModel.Enabled = _previousViewEnabled;
		_capturedView = false;
		_pausedInventory = null;
		_pausedWeapon = null;
	}

	void SetMessage( string message, float seconds ) { _message = message; _messageTime = seconds; }
	void UpdateHud()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		if ( !ShowHud || !LocalPilot.IsValid() ) { if ( _hud.IsValid() ) _hud.Destroy(); return; }
		if ( _hud.IsValid() ) return;
		_hud = new GameObject( Scene, false, "Titan Cockpit HUD" );
		_hud.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_hud.AddComponent<ScreenPanel>().ZIndex = 120;
		_hud.AddComponent<Titanfall2TitanCockpitHud>().Seat = this;
		_hud.Enabled = true;
	}
	void UpdateCockpitView()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		if ( !ShowCockpit || !IsOccupied || !LocalPilot.IsValid() || Actor is not { Dead: false } )
		{
			if ( _cockpitView.IsValid() ) _cockpitView.Destroy();
			return;
		}
		if ( _cockpitView.IsValid() ) return;
		_cockpitView = new GameObject( Scene, false, "Titan Cockpit View" );
		_cockpitView.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		_cockpitView.AddComponent<Titanfall2TitanCockpitView>().Seat = this;
		_cockpitView.Enabled = true;
	}
	internal void ReleaseSeat()
	{
		// Default component queries omit disabled children. Keep the actual pilot
		// so disabling a Titan root cannot hide the passenger before we unparent it.
		var pilot = _seatedPilot.IsValid() ? _seatedPilot : GetComponentInChildren<PlayerController>( true );
		if ( Networking.IsHost && pilot.IsValid() ) TryDisembark( pilot, emergency: true );
		_seatedPilot = null;
		if ( Networking.IsHost && !IsProxy )
		{
			if ( BoardingPilot.IsValid() || EmbarkRemaining > 0 ) SeatSerial++;
			BoardingPilot = null;
			EmbarkRemaining = 0;
		}
		RestoreInventory();
		if ( _hud.IsValid() ) _hud.Destroy();
		if ( _cockpitView.IsValid() ) _cockpitView.Destroy();
	}
	protected override void OnDisabled() => ReleaseSeat();
	protected override void OnDestroy() => ReleaseSeat();
	public override void OnDamage( in DamageInfo damage ) { }
}

/// <summary>Detach before the engine starts recursively destroying the cockpit's children.</summary>
[Hide]
public sealed class Titanfall2TitanSeatEscape : Component
{
	[Property] public Titanfall2TitanSeat Seat { get; set; }
	public override void OnParentDestroy() { if ( Seat.IsValid() ) Seat.ReleaseSeat(); }
}
