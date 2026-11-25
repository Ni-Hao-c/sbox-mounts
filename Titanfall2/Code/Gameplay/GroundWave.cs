/// <summary>A host-swept ground wave: Arc Wave hits each receiver once; Firewall lays burning patches.</summary>
[Hide]
public sealed class Titanfall2GroundWave : Component
{
	[Property] public Titanfall2Npc Actor { get; set; }
	[Property] public bool Flame { get; set; }
	GameObject _attacker;
	Vector3 _direction;
	float _distance;
	float _patchDistance;
	readonly HashSet<Component> _hit = new();

	internal static void Launch( Titanfall2Npc actor, Vector3 direction, GameObject attacker, bool flame )
	{
		if ( !Networking.IsHost || actor is not { Dead: false } || !direction.IsFinite ) return;
		var go = new GameObject( actor.Scene, false, flame ? "TF2 Firewall Wave" : "TF2 Arc Wave" );
		go.Flags |= GameObjectFlags.NotSaved;
		go.Tags.Add( "projectile" );
		go.WorldPosition = actor.WorldPosition + Vector3.Up * 12;
		var wave = go.AddComponent<Titanfall2GroundWave>();
		wave.Actor = actor;
		wave.Flame = flame;
		wave._attacker = attacker;
		var planar = direction.WithZ( 0 );
		wave._direction = planar.LengthSquared > 0.01f ? planar.Normal : actor.WorldRotation.Forward;
		go.WorldRotation = Rotation.LookAt( wave._direction );
		if ( Networking.IsActive ) go.NetworkSpawn( true, null ); else go.Enabled = true;
	}

	protected override void OnStart()
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless ) return;
		Titanfall2CombatEffects.Burst( GameObject, Flame ? new Color( 1, 0.3f, 0.05f ) : new Color( 0.2f, 0.65f, 1 ), 36, 100, 14, 2 );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		if ( !Actor.IsValid() || _distance >= 1400 ) { GameObject.Destroy(); return; }
		var travel = 900 * Time.Delta;
		var end = WorldPosition + _direction * travel;
		var wall = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( WorldPosition + Vector3.Up * 20, end + Vector3.Up * 20 ).Radius( 8 )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile", "npc", "player" ), Titanfall2TraceMask.Grenade );
		var ground = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( end + Vector3.Up * 48, end - Vector3.Up * 80 )
			.IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile", "npc", "player" ), Titanfall2TraceMask.Grenade );
		if ( wall.Hit || wall.StartedSolid || !ground.Hit || ground.StartedSolid || ground.Normal.z < 0.5f ) { GameObject.Destroy(); return; }
		WorldPosition = ground.EndPosition + Vector3.Up * 12;
		_distance += travel;
		if ( Flame )
		{
			if ( _distance < _patchDistance ) return;
			_patchDistance = _distance + 120;
			var settings = new Titanfall2ProjectileSettings { Behavior = Titanfall2ProjectileBehavior.ThermiteBurn, SplashRadius = 85, SplashDamage = 30, Tint = new Color( 1, 0.3f, 0.06f ) };
			Titanfall2AreaHazard.Spawn( Scene, WorldPosition, settings, 30, 200, _attacker, Actor.GameObject, Actor.GameObject );
			return;
		}
		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, 110 ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component receiver || _hit.Contains( receiver ) || receiver.GameObject == Actor.GameObject ) continue;
			var npc = receiver.GameObject.GetComponentInParent<Titanfall2Npc>();
			if ( npc.IsValid() && (npc.Dead || !Actor.IsHostileToFaction( npc.Faction )) ) continue;
			if ( receiver.GameObject.GetComponentInParent<PlayerController>() is not null && (!Actor.AttackPlayers || Actor.Friendly) ) continue;
			var point = receiver.GameObject.GetBounds().Center;
			var cover = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( WorldPosition, point ).IgnoreGameObjectHierarchy( Actor.GameObject )
				.WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
			if ( cover.StartedSolid || (cover.Hit && cover.GameObject?.GetComponentInParent<IDamageable>() != receiver) ) continue;
			_hit.Add( receiver );
			var damage = new DamageInfo( npc is { IsTitan: true } ? 400 : 40, _attacker, Actor.GameObject ) { Origin = WorldPosition, Position = point };
			damage.Tags.Add( "shock" ); damage.Tags.Add( "projectile" );
			Titanfall2HostDamage.Deal( (IDamageable)receiver, damage );
		}
	}
}
