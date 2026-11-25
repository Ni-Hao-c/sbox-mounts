using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

[Hide]
public abstract class SwimmingNpc : VoicedNpc
{
	internal abstract void SwimAlert();

	internal WaterBody Water { get; private set; }
	internal GameObject Prey { get; private set; }
	TimeUntil _nextDecision, _nextWander;
	Vector3 _dryVelocity;
	Vector3 _home;
	bool _ready;
	[Sync( SyncFlags.FromHost )] protected bool Stranded { get; private set; }

	protected Vector3 SwimVelocity
	{
		get => GetComponent<Rigidbody>() is { MotionEnabled: true } body ? body.Velocity
			: !Stranded && Movement is FlyingLayer swimmer ? swimmer.Velocity : _dryVelocity;
		set
		{
			if ( GetComponent<Rigidbody>() is { MotionEnabled: true } body ) body.Velocity = value;
			else if ( !Stranded && Movement is FlyingLayer swimmer ) swimmer.Velocity = value;
			else _dryVelocity = value;
		}
	}

	public override Relationship GetRelationship( GameObject other )
	{
		var relationship = base.GetRelationship( other );
		if ( relationship.Disposition == Disposition.Hostile && other.IsValid() && !WaterBody.Find( Scene, other.WorldPosition ).IsValid() )
			return new( Disposition.Neutral );
		return relationship;
	}

	internal override void OnModelLoaded( ModelLoader loader )
	{
		_ready = true;
		_home = WorldPosition;
		if ( Movement is FlyingLayer swimmer ) swimmer.HoverHeight = 0;
	}

	protected override void Think()
	{
		if ( !_ready ) return;
		var busy = AdvanceAction();
		if ( _nextDecision > 0 ) return;
		_nextDecision = 0.1f;
		Water = WaterBody.Find( Scene, WorldPosition );
		Stranded = !Water.IsValid();
		if ( Stranded )
		{
			Prey = null;
			Movement.Stop();
			return;
		}
		if ( busy ) return;
		var enemy = SelectPrey();
		if ( enemy != Prey && enemy.IsValid() ) SwimAlert();
		Prey = enemy;
		if ( Prey.IsValid() && Hunt( Prey ) ) return;
		if ( _nextWander > 0 ) return;
		_nextWander = Game.Random.Float( 1, 3 );
		var direction = Rotation.FromYaw( Game.Random.Float( -75, 75 ) ) * WorldRotation.Forward;
		var point = WorldPosition + direction * Definition.Speed * 2 + Vector3.Up * Game.Random.Float( -50, 50 );
		if ( point.Distance( _home ) > 512 || WaterBody.Find( Scene, point ) != Water ) point = _home;
		SwimTo( point, Definition.Speed );
	}

	internal virtual GameObject SelectPrey()
	{
		var enemy = FindVisibleEnemy();
		return CanHunt( enemy ) ? enemy : null;
	}

	internal bool CanHunt( GameObject target ) => IsLiveTarget( target ) && WaterBody.Find( Scene, target.WorldPosition ) == Water;

	internal abstract bool Hunt( GameObject target );

	internal void SwimTo( Vector3 point, float speed, GameObject face = null )
	{
		var local = Water.ToSurface( point );
		var depth = Water.Depth * System.MathF.Abs( Water.WorldScale.z );
		point = Water.FromSurface( local.WithZ( SwimHeight( local.z, depth, face is null ) ) );
		Movement.MoveTo( point, 2, speed, face );
	}

	internal virtual float SwimHeight( float requested, float depth, bool wandering )
	{
		var margin = System.MathF.Min( Definition.Height * 0.5f, depth * 0.25f );
		return requested.Clamp( margin - depth, -margin );
	}

	internal virtual Vector3 StrandedJump() => Vector3.Zero;

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost || !Stranded || Health <= 0 )
		{
			_dryVelocity = Vector3.Zero;
			return;
		}
		_dryVelocity += Scene.PhysicsWorld.Gravity * Time.Delta;
		var to = WorldPosition + _dryVelocity * Time.Delta;
		var trace = Scene.Trace.Ray( WorldPosition + Vector3.Up * 2, to ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		WorldPosition = trace.Hit ? trace.EndPosition : to;
		if ( trace.Hit ) _dryVelocity = trace.Normal.z > 0.5f ? StrandedJump() : Vector3.Zero;
	}
}
