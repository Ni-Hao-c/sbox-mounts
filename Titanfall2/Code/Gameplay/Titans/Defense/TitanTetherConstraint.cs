/// <summary>Host-owned tether endpoints live on the chassis, surviving pilot/AI transitions.</summary>
public sealed class Titanfall2TetherConstraint : Component
{
	readonly List<Titanfall2TetherTrap> _tethers = [];
	NavMeshAgent _agent;
	bool _capturedPosition;
	bool _updatePosition;
	internal bool HasTethers => _tethers.Any( tether => tether.IsValid() && tether.Attached );
	internal bool Attach( Titanfall2TetherTrap trap )
	{
		_tethers.RemoveAll( tether => !tether.IsValid() || !tether.Attached );
		if ( !Networking.IsHost || IsProxy || _tethers.Count >= Titanfall2TetherRules.MaximumAttached ) return false;
		_tethers.Add( trap );
		return true;
	}
	internal void Detach( Titanfall2TetherTrap trap )
	{
		_tethers.Remove( trap );
		if ( !HasTethers ) RestoreNavigation();
	}
	internal Vector3 Constrain( Titanfall2Npc actor, Vector3 velocity, float delta )
	{
		if ( !Networking.IsHost || IsProxy ) return velocity;
		foreach ( var tether in _tethers )
			if ( tether.IsValid() && tether.Attached )
				velocity = Titanfall2TetherRules.ConstrainVelocity( tether.WorldPosition, actor.WorldPosition, actor.BodyHeight, velocity, delta );
		return velocity;
	}
	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		var actor = GetComponent<Titanfall2Npc>();
		if ( !actor.IsValid() || !actor.TitanCanAct || !HasTethers ) { RestoreNavigation(); return; }
		// The seat consumes the same constraint inside its existing hull/ground sweep.
		if ( actor.Seat is { IsOccupied: true } or { IsEmbarking: true }
			|| actor.IsTraversing || actor.GetComponent<Titanfall2TitanKit>() is { AiControlsMovement: true } ) { RestoreNavigation(); return; }
		var agent = actor.Agent;
		if ( agent != _agent ) RestoreNavigation();
		if ( agent.IsValid() && !_capturedPosition )
		{
			_agent = agent; _updatePosition = agent.UpdatePosition; _capturedPosition = true;
			agent.UpdatePosition = false;
		}
		var velocity = agent.IsValid() ? agent.Velocity : Vector3.Zero;
		velocity = Constrain( actor, velocity, Time.Delta );
		var radius = MathF.Max( 4, actor.BodyRadius - 2 );
		var hull = new BBox( new Vector3( -radius, -radius, 2 ), new Vector3( radius, radius, actor.BodyHeight - 1 ) );
		var start = actor.WorldPosition;
		var mask = (Titanfall2TraceMask)((uint)Titanfall2TraceMask.NpcSolid | (uint)Titanfall2Contents.TitanClip);
		var sweep = Scene.Trace.IgnoreGameObjectHierarchy( actor.GameObject ).WithoutTags( "trigger", "projectile", "tether-trap" );
		var step = Titanfall2CollisionQueries.Run( sweep.Box( hull, start, start + velocity * Time.Delta ), mask );
		if ( !step.StartedSolid )
		{
			var ground = Titanfall2CollisionQueries.Run( sweep.Box( hull, step.EndPosition + Vector3.Up * 16, step.EndPosition - Vector3.Up * 32 ), mask );
			if ( ground.Hit && !ground.StartedSolid && ground.Normal.z >= 0.6f ) actor.WorldPosition = ground.EndPosition;
		}
		if ( agent.IsValid() ) { agent.SetAgentPosition( actor.WorldPosition ); agent.Velocity = velocity; }
	}
	void RestoreNavigation()
	{
		if ( _capturedPosition && _agent.IsValid() )
		{
			_agent.SetAgentPosition( WorldPosition );
			_agent.UpdatePosition = _updatePosition;
		}
		_capturedPosition = false; _agent = null;
	}
	protected override void OnDisabled() => RestoreNavigation();
	protected override void OnDestroy() => RestoreNavigation();
}
