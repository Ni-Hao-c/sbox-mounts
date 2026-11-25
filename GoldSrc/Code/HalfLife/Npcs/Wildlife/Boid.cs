using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

[Title( "Boid" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeBoid" )]
[NpcDefinition( "monster_flyer", "boid", MonsterClass.None, 1, 5, 2, 125, Movement = NpcMovement.Flying )]
public sealed class Boid : Npc
{
	internal override float ViewDot => 0.2f;

	internal override NpcSounds Sounds { get; } = new( "boid/boid_idle", "boid/boid_alert" );
	BoidFlock _flock;
	TimeUntil _nextSteer, _nextCall, _alert, _turnUntil;
	TimeSince _sinceBlocked;
	float _speed = 125, _goalSpeed = 125;
	[Sync( SyncFlags.FromHost )] int TurnDirection { get; set; }
	const float TurnRate = 75;
	float _averageSpeed;
	[Sync( SyncFlags.FromHost )] float FlapRate { get; set; } = 1;
	internal override float MovementPlaybackRate( StudioSequence sequence, float speed ) => FlapRate;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( !Networking.IsHost ) return;
		_flock = GameObject.Parent?.GetComponent<BoidFlock>();
		_flock?.Join( this );
		_sinceBlocked = 0;
	}

	protected override void OnDisabled()
	{
		_flock?.Leave( this );
		_flock = null;
		base.OnDisabled();
	}

	internal override void OnModelLoaded( ModelLoader loader )
	{
		if ( Movement is FlyingLayer flight ) flight.HoverHeight = 0;
	}

	protected override void Think()
	{
		if ( !Movement.IsValid() || _nextSteer > 0 ) return;
		_nextSteer = 0.1f;
		var leader = _flock?.Members.FirstOrDefault() ?? this;
		var heading = leader.WorldRotation.Forward.WithZ( 0 ).Normal;
		if ( leader == this )
		{
			var side = WorldRotation.Right * 12;
			var blocked = _turnUntil > 0 || Clearance( heading ) < 1 || Clearance( heading, side ) < 1 || Clearance( heading, -side ) < 1;
			if ( blocked )
			{
				_sinceBlocked = 0;
				if ( TurnDirection == 0 )
				{
					var difference = Clearance( WorldRotation.Left ) - Clearance( WorldRotation.Right );
					TurnDirection = difference == 0 ? (Game.Random.Int( 0, 1 ) == 0 ? -1 : 1) : System.Math.Sign( difference );
				}
				heading = Rotation.FromYaw( TurnDirection * TurnRate * 0.1f ) * heading;
				WorldRotation = Rotation.LookAt( heading );
				if ( _nextCall <= 0 )
				{
					EmitSound( _alert > 0 ? Sounds.Alert : Sounds.Idle, WorldPosition );
					_nextCall = Game.Random.Float( 1, 3 );
				}
			}
			else
			{
				TurnDirection = 0;
				if ( _sinceBlocked > 6 ) _turnUntil = Game.Random.Int( 1, 3 );
				if ( _speed <= Definition.Speed ) _speed += 5;
			}
		}
		else
		{
			TurnDirection = leader.TurnDirection;
			var toward = leader.WorldPosition - WorldPosition;
			var distance = toward.Length;
			if ( heading.Dot( toward.WithZ( 0 ).Normal ) <= 0.2f || distance < 100 ) _goalSpeed = leader._speed * 0.5f;
			else if ( distance > 256 ) _goalSpeed = leader._speed * 1.5f;
			foreach ( var other in _flock.Members )
			{
				if ( other == this ) continue;
				var away = WorldPosition - other.WorldPosition;
				if ( away.LengthSquared <= 100 * 100 ) heading += away.Normal;
			}
			if ( distance > 256 ) heading = (heading.Normal + toward.Normal) * 0.5f;
			_speed += System.Math.Clamp( _goalSpeed - _speed, -10, 10 );
		}
		FlapRate = System.Math.Clamp( System.MathF.Abs( _speed - _averageSpeed ) / 10, 0.25f, 1.9f );
		_averageSpeed = _averageSpeed * 0.8f + _speed * 0.2f;
		Movement.MoveTo( WorldPosition + heading.Normal * 192, 4, _speed );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Renderer.IsValid() || Health <= 0 ) return;
		var target = Rotation.From( -FlapRate * 5, 0, -TurnDirection * TurnRate );
		Renderer.LocalRotation = Rotation.Slerp( Renderer.LocalRotation, target, 1 - System.MathF.Exp( -Time.Delta ) );
	}

	float Clearance( Vector3 direction, Vector3 offset = default )
	{
		var origin = WorldPosition + offset;
		return Scene.Trace.Ray( origin, origin + direction * 192 ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "npc", "player", "playercontroller", "trigger" ).Run().Fraction;
	}

	internal override void ModifyDamage( Component.IDamageable.DamageEvent damage ) => damage.Cancelled = true;

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		if ( _flock.IsValid() )
			foreach ( var member in _flock.Members ) member._alert = 15;
		base.OnDeath( death );
	}

	protected override GameObject CreateCorpse() => RestSequence is { } flight ? NpcCorpse.Spawn( this, flight ) : null;

	internal override NpcCorpse AddCorpseComponent( GameObject go )
	{
		var corpse = go.AddComponent<DeadBoid>();
		Renderer.Animation.Parameters.TryGetFloat( RestSequence.TimeParameter, out var time );
		corpse.FlightFrame = time;
		return corpse;
	}
}
