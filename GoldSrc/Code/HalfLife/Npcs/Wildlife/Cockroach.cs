using Sandbox.Npcs;

namespace GoldSrc.HalfLife;

[Title( "Cockroach" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeCockroach" )]
[NpcDefinition( "monster_cockroach", "roach", MonsterClass.Insect, 1, 1, 2, 44, Blood = BloodColor.Yellow )]
public sealed class Cockroach : Npc, Component.ICollisionListener
{
	internal override float SightRange => 150;

	TimeUntil _nextLook, _ignoreFood, _moveTimeout;
	Vector3? _destination;
	bool _feeding, _escapingLight;
	float? _restingLight;
	Vector3? _food;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		EyeHeight = 1;
		// Let the lights in the spawn area settle before recording the idle light level.
		_nextLook = 1;
	}

	protected override void Think()
	{
		if ( !Movement.IsValid() || _nextLook > 0 ) return;
		_nextLook = 0.1f;
		_restingLight ??= LightLevel();

		if ( _destination is { } goal )
		{
			if ( WorldPosition.Distance( goal ) > 5 && _moveTimeout > 0
				&& (!_escapingLight || LightLevel() > _restingLight + 0.01f) )
			{
				if ( !_escapingLight && !_food.HasValue && Game.Random.Int( 0, 149 ) == 0 ) ScurryAtRandom();
				return;
			}
			_escapingLight = false;
			_restingLight = LightLevel();
			_feeding = _food is { } food && WorldPosition.Distance( food ) <= 8;
			_destination = null;
			Movement.Stop();
		}

		if ( Game.Random.Int( 0, 3 ) == 0 )
		{
			var frightened = HasNearbyThreat();
			if ( frightened || Game.Random.Int( 0, 149 ) == 0 )
			{
				if ( frightened || _feeding ) _ignoreFood = Game.Random.Float( 30, 45 );
				_food = null;
				_feeding = false;
				ScurryAtRandom();
				return;
			}
		}

		if ( _feeding ) return;
		var currentLight = LightLevel();

		if ( currentLight > _restingLight + 0.01f )
		{
			_food = null;
			ScurryAtRandom();
			_escapingLight = _destination.HasValue;
			return;
		}

		if ( _ignoreFood > 0 || Senses?.Disturbance is not { Kind: "meat" or "carcass" } scent
			|| System.MathF.Abs( scent.Position.z - WorldPosition.z ) > 3 ) return;
		_food = scent.Position;
		Scurry( scent.Position + new Vector3( Game.Random.Int( -2, 3 ), Game.Random.Int( -2, 3 ), 0 ) );
	}

	bool HasNearbyThreat()
	{
		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, SightRange ) ) )
		{
			var actor = obj.GetComponentInParent<BaseNpc>()?.GameObject ?? obj.GetComponentInParent<PlayerController>()?.GameObject;
			if ( actor.IsValid() && actor.GetComponent<IDamageable>() is { Health: > 0 } && GetDisposition( actor ) == Disposition.Fearful )
				return true;
		}
		return false;
	}

	void ScurryAtRandom()
	{
		Vector3 offset;
		do
		{
			var direction = new Vector3( Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ), 0 );
			offset = direction * Game.Random.Int( 256, 511 );
		} while ( offset.LengthSquared < 128 * 128 );
		Scurry( WorldPosition + offset );
	}

	// Sample direct lighting on the host so a flashlight can startle the roach on a dedicated server.
	float LightLevel()
	{
		var eye = EyePosition;
		float level = Scene.GetAllComponents<AmbientLight>().Sum( light => light.Color.Luminance );
		foreach ( var light in Scene.GetAllComponents<Light>() )
		{
			var delta = eye - light.WorldPosition;
			var radius = light switch { PointLight point => point.Radius, SpotLight spot => spot.Radius, _ => 0 };
			var sun = light is DirectionalLight;
			if ( !sun && (radius <= 0 || delta.LengthSquared >= radius * radius) ) continue;
			if ( light is SpotLight cone && Vector3.GetAngle( delta, cone.WorldRotation.Forward ) > cone.ConeOuter ) continue;
			var source = sun ? eye - light.WorldRotation.Forward * 8192 : light.WorldPosition;
			if ( light.Shadows && Scene.Trace.Ray( eye, source ).IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( light.GameObject ).WithoutTags( "trigger" ).Run().Hit ) continue;
			level += light.Intensity * light.LightColor.Luminance * (sun ? 1 : 1 - delta.Length / radius);
		}
		return level;
	}

	void Scurry( Vector3 point )
	{
		var destination = Movement.ProjectPoint( point );
		if ( destination is null ) return;
		_destination = destination;
		_moveTimeout = WorldPosition.Distance( destination.Value ) / Definition.Speed + 1;
		Movement.MoveTo( destination.Value, 3, Definition.Speed );
		if ( !_food.HasValue && Game.Random.Int( 0, 9 ) == 0 ) EmitSound( "roach/rch_walk", WorldPosition, 1, Game.Random.Float( 0.8f, 1.19f ) );
	}

	void ICollisionListener.OnCollisionStart( Collision collision ) => TrySquash( collision );
	void ICollisionListener.OnCollisionUpdate( Collision collision ) => TrySquash( collision );

	void TrySquash( Collision collision )
	{
		if ( !Networking.IsHost || Health <= 0 ) return;
		var player = collision.Other.GameObject?.GetComponentInParent<PlayerController>();
		if ( !player.IsValid() || player.Velocity.IsNearlyZero() ) return;
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 8, WorldPosition + Vector3.Down * 16 )
			.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( player.GameObject )
			.WithoutTags( "npc", "player", "playercontroller", "trigger" ).Run();
		if ( floor.Hit ) BloodEffects.Decal( BloodColor.Yellow, floor.EndPosition, floor.Normal, Game.Random.Int( 1, 6 ) );
		var damage = new DamageInfo { Damage = Health, Attacker = player.GameObject, Position = WorldPosition, Origin = player.WorldPosition };
		damage.Tags.Add( "crush" );
		((IDamageable)this).OnDamage( damage );
	}

	protected override void OnDeath( IDamageable.DeathEvent death )
	{
		bool squeak = Game.Random.Int( 0, 4 ) == 0;
		EmitSound( squeak ? "roach/rch_die" : "roach/rch_smash", WorldPosition, squeak ? 0.8f : 0.7f, Game.Random.Float( 0.8f, 1.19f ) );
		// A crushed roach makes a local world sound. Nearby monsters can hear it for a second.
		EmitStimulus( "world", radius: 128, lifetime: 1 );
	}
}
