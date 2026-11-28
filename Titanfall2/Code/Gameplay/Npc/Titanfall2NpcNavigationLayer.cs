using Sandbox.Engine.Resources;
using Sandbox.Npcs.Layers;
using System.Runtime.CompilerServices;

/// <summary>Owns validated link crossings; ordinary route planning remains the engine NavigationLayer's responsibility.</summary>
[Title( "Titanfall 2 NPC Navigation" ), Group( "Titanfall 2" )]
public sealed class Titanfall2NpcNavigationLayer : NavigationLayer
{
	Titanfall2Npc Actor => Npc as Titanfall2Npc;
	NavMeshAgent _configured;
	Titanfall2TraversalProfile _configuredProfile;
	NavMeshLinkType _configuredJumpType, _configuredClimbType;
	int _configuredTypesVersion = -1;
	bool _active;
	float _elapsed, _duration, _arc, _fallSpeed;
	Vector3 _start, _end;
	Vector3[] _rootCurve;
	Titanfall2TraversalKind _kind;
	Titanfall2TraversalPhase _phase;
	string _role;
	readonly static List<Titanfall2NpcNavigationLayer> Reservations = new();
	static readonly ConditionalWeakTable<Scene, RoofConnections> Roofs = new();
	sealed class RoofConnections
	{
		public double NextAttempt;
		public List<Titanfall2TraversalLink> Links = new();
	}

	protected override void OnEnabled()
	{
		// The crossing owns swept movement. Do not let the base layer start a second physics flight.
		MovementMode = MovementModes.Kinematic;
		base.OnEnabled();
		ConfigureAgent();
	}
	protected override void OnStart() { base.OnStart(); ConfigureAgent(); }
	void ConfigureAgent()
	{
		_ = Titanfall2TraversalLink.EnsureTypesAsync();
		var jumpType = Titanfall2TraversalLink.JumpType;
		var climbType = Titanfall2TraversalLink.ClimbType;
		if ( !Agent.IsValid() || !Actor.IsValid() || _configured == Agent && _configuredProfile == Actor.TraversalProfile
			&& _configuredJumpType == jumpType && _configuredClimbType == climbType
			&& _configuredTypesVersion == Titanfall2TraversalLink.TypesVersion ) return;
		_configured = Agent;
		_configuredProfile = Actor.TraversalProfile;
		_configuredJumpType = jumpType;
		_configuredClimbType = climbType;
		_configuredTypesVersion = Titanfall2TraversalLink.TypesVersion;
		Agent.AllowAllLinkTypes = false;
		var allowed = new HashSet<NavMeshLinkType>();
		var profile = Actor.TraversalProfile;
		if ( profile.Enabled )
		{
			Add( NavMeshLinkType.Jump ); Add( NavMeshLinkType.Drop );
			if ( profile.Rise >= 256 ) Add( Titanfall2TraversalLink.JumpType );
			if ( profile.Climb ) { Add( NavMeshLinkType.Climb ); Add( NavMeshLinkType.Ladder ); Add( Titanfall2TraversalLink.ClimbType ); }
		}
		Agent.AllowedLinkTypes = allowed;
		void Add( NavMeshLinkType type ) { if ( type is not null ) allowed.Add( type ); }
	}
	protected override bool OnCustomLinkTraversal( LinkTraversalState traversal )
	{
		StartTraversal( traversal );
		return true; // Reject unknown custom types explicitly; never let them become plain teleport-like links.
	}
	protected override void OnUpdate()
	{
		ConfigureAgent();
		if ( !Networking.IsHost || IsProxy || !Actor.IsValid() ) { base.OnUpdate(); return; }
		if ( !CanOwnMovement() )
		{
			if ( _active || IsTraversingLink ) CancelTraversal( "Actor control changed", immediate: true );
			return;
		}
		if ( !_active && LinkTraversal is { } traversal ) StartTraversal( traversal );
		if ( _active ) { TickTraversal(); return; }
		TryPublishRoofConnection();
		base.OnUpdate();
	}
	protected override void OnFixedUpdate()
	{
		if ( _active || IsTraversingLink ) return;
		base.OnFixedUpdate();
	}
	bool CanOwnMovement() => Actor is { Dead: false, IsInTermination: false, NavigationMode: Titanfall2Npc.Navigation.Ground }
		&& Actor.Seat is not { IsOccupied: true } and not { IsEmbarking: true }
		&& Actor.GetComponent<Titanfall2TitanKit>() is not { AiControlsMovement: true };

	static Titanfall2TraversalKind Kind( NavMeshLinkType type )
	{
		if ( type is null ) return Titanfall2TraversalKind.None;
		if ( type == NavMeshLinkType.Jump || type == Titanfall2TraversalLink.JumpType ) return Titanfall2TraversalKind.Jump;
		if ( type == NavMeshLinkType.Drop ) return Titanfall2TraversalKind.Drop;
		if ( type == NavMeshLinkType.Climb || type == Titanfall2TraversalLink.ClimbType ) return Titanfall2TraversalKind.Climb;
		return type == NavMeshLinkType.Ladder ? Titanfall2TraversalKind.Ladder : Titanfall2TraversalKind.None;
	}
	void StartTraversal( LinkTraversalState traversal )
	{
		if ( _active || !Actor.IsValid() ) return;
		var profile = Actor.TraversalProfile;
		_kind = Kind( traversal.Type );
		if ( !CanOwnMovement() ) { Reject( "Actor movement is controlled by another action" ); return; }
		if ( !Titanfall2NpcTraversalRules.Allows( profile, _kind, traversal.Start, traversal.End ) )
		{
			Reject( $"Crossing exceeds actor capability: kind={_kind}, type={traversal.Type?.ResourcePath ?? "null"}, start={traversal.Start}, end={traversal.End}, profile={profile}, model={Actor.ModelPath}" );
			return;
		}
		_start = Actor.WorldPosition;
		_end = traversal.End;
		if ( !TryLanding( _end, out _end ) ) { Reject( "Landing has no clear supported hull" ); return; }
		Reservations.RemoveAll( x => !x.IsValid() || !x._active || !x.Actor.IsValid() );
		if ( Reservations.Any( x => x != this && x.Scene == Scene && x._end.Distance( _end ) < Actor.BodyRadius + x.Actor.BodyRadius + 16 ) )
		{ Reject( "Landing is reserved" ); return; }
		_arc = profile.ArcHeight;
		_role = Titanfall2NpcTraversalRules.Role( _kind );
		var clip = Actor.AnimationData?.Clips?.FirstOrDefault( x => x.Role == _role );
		_rootCurve = clip?.RootMotion;
		var clear = Preflight();
		for ( var retry = 0; !clear && retry < 2 && _kind == Titanfall2TraversalKind.Jump; retry++ ) { _arc += 48; clear = Preflight(); }
		if ( !clear ) { Reject( "Crossing hull is obstructed" ); return; }
		_duration = Titanfall2NpcTraversalRules.Duration( profile, _kind, _start, _end );
		if ( clip is { Duration: > 0.2f } ) _duration = Math.Clamp( clip.Duration, _duration * 0.6f, _duration * 1.6f );
		_phase = Titanfall2TraversalPhase.Align;
		_elapsed = 0;
		_active = true;
		Reservations.Add( this );
		Actor.CancelNpcOrdnance();
		Actor.SetTraversalHost( _phase, _kind, 0, clip is null ? null : _role, begin: true );
		var facing = (_end - _start).WithZ( 0 );
		if ( facing.LengthSquared > 1 ) Actor.WorldRotation = Rotation.LookAt( facing.Normal );
	}
	void TryPublishRoofConnection()
	{
		// This is a bounded, local link discovery pass, not retail AIN import. It only uses a requested route or known contact.
		var profile = Actor.TraversalProfile;
		if ( profile.Rise < 256 || Titanfall2TraversalLink.JumpType is null || Scene.NavMesh is not { IsEnabled: true, IsGenerating: false } nav ) return;
		var goal = Agent.TargetPosition ?? Actor.LastContactPoint;
		if ( !goal.HasValue || goal.Value.z - Actor.WorldPosition.z < 64 || goal.Value.z - Actor.WorldPosition.z > profile.Rise ) return;
		if ( (goal.Value - Actor.WorldPosition).WithZ( 0 ).Length > profile.Distance ) return;
		var state = Roofs.GetValue( Scene, _ => new RoofConnections() );
		if ( Time.NowDouble < state.NextAttempt ) return;
		state.NextAttempt = Time.NowDouble + 0.5;
		state.Links.RemoveAll( x => !x.IsValid() );
		if ( state.Links.Count >= 64 ) return;
		var from = nav.GetClosestPoint( Actor.WorldPosition );
		var to = nav.GetClosestPoint( goal.Value );
		if ( !from.HasValue || !to.HasValue || from.Value.Distance( Actor.WorldPosition ) > 32 || to.Value.Distance( goal.Value ) > 48 )
		{
			nav.RequestTilesGeneration( new BBox( goal.Value - new Vector3( 96, 96, 128 ), goal.Value + new Vector3( 96, 96, 128 ) ) );
			return;
		}
		if ( !Titanfall2NpcTraversalRules.Allows( profile, Titanfall2TraversalKind.Jump, from.Value, to.Value ) ) return;
		if ( state.Links.Any( x => x.WorldTransform.PointToWorld( x.LocalStart ).Distance( from.Value ) < 96
			&& x.WorldTransform.PointToWorld( x.LocalEnd ).Distance( to.Value ) < 96 ) ) return;
		// Publish only after a supported destination and whole hull trajectory have been checked.
		if ( !TryLanding( to.Value, out var landing ) ) return;
		var previous = from.Value;
		for ( var i = 1; i <= 24; i++ )
		{
			var next = Titanfall2NpcTraversalRules.Sample( Titanfall2TraversalKind.Jump, from.Value, landing, i / 24f, profile.ArcHeight );
			var hit = Sweep( previous, next );
			if ( hit.StartedSolid || hit.Hit ) return;
			previous = next;
		}
		var go = new GameObject( Scene, false, "TF2 discovered roof link" ) { WorldPosition = from.Value };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var link = go.AddComponent<Titanfall2TraversalLink>();
		link.LocalEnd = landing - from.Value;
		link.ConnectionRadius = 40;
		link.Bidirectional = false;
		state.Links.Add( link );
		go.Enabled = true;
	}
	BBox Hull => new( new Vector3( -MathF.Max( 4, Actor.BodyRadius - 2 ), -MathF.Max( 4, Actor.BodyRadius - 2 ), 2 ),
		new Vector3( MathF.Max( 4, Actor.BodyRadius - 2 ), MathF.Max( 4, Actor.BodyRadius - 2 ), MathF.Max( 24, Actor.BodyHeight - 2 ) ) );
	SceneTraceResult Sweep( Vector3 from, Vector3 to ) => Titanfall2CollisionQueries.Run(
		Scene.Trace.Box( Hull, from, to ).IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.NpcSolid );
	bool TryLanding( Vector3 point, out Vector3 landing )
	{
		landing = point;
		var floor = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( point + Vector3.Up * 24, point - Vector3.Up * 48 )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.NpcSolid );
		if ( !floor.Hit || floor.StartedSolid || floor.Normal.z < 0.65f ) return false;
		landing = floor.EndPosition + Vector3.Up;
		var clearance = Sweep( landing, landing + Vector3.Up );
		return !clearance.StartedSolid && !clearance.Hit;
	}
	bool Preflight()
	{
		var previous = _start;
		for ( var i = 1; i <= 24; i++ )
		{
			var next = Titanfall2NpcTraversalRules.Sample( _kind, _start, _end, i / 24f, _arc, _rootCurve );
			var hit = Sweep( previous, next );
			if ( hit.StartedSolid || hit.Hit ) return false;
			previous = next;
		}
		return true;
	}
	void TickTraversal()
	{
		if ( !Agent.IsValid() ) { CancelTraversal( "Navigation agent unavailable" ); return; }
		if ( _phase == Titanfall2TraversalPhase.Falling ) { TickFall(); return; }
		var dt = Math.Clamp( Time.Delta, 0, 0.25f );
		_elapsed += dt;
		if ( _phase == Titanfall2TraversalPhase.Align )
		{
			if ( _elapsed < 0.12f ) return;
			_phase = Titanfall2TraversalPhase.Traverse; _elapsed = 0;
		}
		if ( _phase == Titanfall2TraversalPhase.Recover )
		{
			Actor.SetTraversalHost( _phase, _kind, Math.Clamp( _elapsed / 0.25f, 0, 1 ), Actor.HasActorClip( ModelLoader.LandRole ) ? ModelLoader.LandRole : _role );
			if ( _elapsed >= 0.25f ) Finish();
			return;
		}
		var targetCycle = Math.Clamp( _elapsed / _duration, 0, 1 );
		var oldCycle = Math.Clamp( (_elapsed - dt) / _duration, 0, 1 );
		var steps = Math.Max( 1, (int)MathF.Ceiling( dt / 0.025f ) );
		for ( var i = 1; i <= steps; i++ )
		{
			var cycle = oldCycle + (targetCycle - oldCycle) * (i / (float)steps);
			var point = Titanfall2NpcTraversalRules.Sample( _kind, _start, _end, cycle, _arc, _rootCurve );
			var trace = Sweep( Actor.WorldPosition, point );
			if ( trace.StartedSolid || trace.Hit ) { BeginFall( "Crossing blocked after takeoff" ); return; }
			Agent.SetAgentPosition( point ); Actor.WorldPosition = point;
		}
		Actor.SetTraversalHost( _phase, _kind, targetCycle, _role );
		if ( targetCycle < 1 ) return;
		if ( !TryLanding( _end, out var landing ) || landing.Distance( _end ) > 8 ) { BeginFall( "Landing changed during crossing" ); return; }
		_phase = Titanfall2TraversalPhase.Recover; _elapsed = 0;
	}
	void BeginFall( string reason )
	{
		Actor.SetTraversalFailureHost( reason );
		_phase = Titanfall2TraversalPhase.Falling; _elapsed = 0; _fallSpeed = 0;
		Actor.SetTraversalHost( _phase, Titanfall2TraversalKind.Drop, 0, Actor.HasActorClip( ModelLoader.DropRole ) ? ModelLoader.DropRole : _role );
	}
	void TickFall()
	{
		var dt = Math.Clamp( Time.Delta, 0, 0.05f );
		_elapsed += dt;
		_fallSpeed = MathF.Min( 900, _fallSpeed + 800 * dt );
		var trace = Sweep( Actor.WorldPosition, Actor.WorldPosition - Vector3.Up * (_fallSpeed * dt) );
		if ( !trace.StartedSolid ) { Agent.SetAgentPosition( trace.EndPosition ); Actor.WorldPosition = trace.EndPosition; }
		if ( trace.Hit && trace.Normal.z >= 0.65f || trace.StartedSolid || _elapsed > 5 )
		{
			CancelTraversal( Actor.LastTraversalFailure ?? "Crossing aborted", immediate: true );
		}
	}
	void Finish()
	{
		_active = false; _phase = Titanfall2TraversalPhase.None;
		Reservations.Remove( this );
		CompleteLinkTraversal();
		Actor.SetTraversalHost( Titanfall2TraversalPhase.None, Titanfall2TraversalKind.None, 0 );
	}
	void Reject( string reason )
	{
		Actor?.SetTraversalFailureHost( reason );
		AbortLinkTraversal();
	}
	public void CancelTraversal( string reason, bool immediate = false )
	{
		if ( !Networking.IsHost ) return;
		if ( !immediate && _active && _phase == Titanfall2TraversalPhase.Falling && CanOwnMovement() ) return;
		if ( !immediate && _active && _phase == Titanfall2TraversalPhase.Traverse && CanOwnMovement() )
		{
			BeginFall( reason );
			return;
		}
		_active = false; _phase = Titanfall2TraversalPhase.None;
		Reservations.Remove( this );
		AbortLinkTraversal();
		if ( Actor.IsValid() ) { Actor.SetTraversalFailureHost( reason ); Actor.SetTraversalHost( Titanfall2TraversalPhase.None, Titanfall2TraversalKind.None, 0 ); }
	}
	protected override void OnDisabled() { CancelTraversal( "Navigation disabled", immediate: true ); _configured = null; base.OnDisabled(); }
	protected override void OnDestroy() { Reservations.Remove( this ); base.OnDestroy(); }
}
