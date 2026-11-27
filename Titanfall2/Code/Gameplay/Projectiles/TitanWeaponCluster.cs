/// <summary>Bounded Northstar cluster bursts with retail impacts and occluded, local blast damage.</summary>
[Hide]
public sealed class Titanfall2TitanCluster : Component
{
	[Property] public Titanfall2ProjectileSettings Settings { get; set; }
	[Property] public Vector3 SurfaceNormal { get; set; } = Vector3.Up;
	GameObject _attacker;
	GameObject _weapon;
	GameObject _ignore;
	int _bursts;
	TimeUntil _nextBurst;
	internal const int BurstCount = 20;
	internal const float Duration = 5;
	internal const float SpreadRadius = 250;

	internal static void Spawn( Scene scene, Vector3 origin, Titanfall2ProjectileSettings settings,
		GameObject attacker, GameObject weapon, GameObject ignore, Vector3 normal )
	{
		if ( !Networking.IsHost || !scene.IsValid() || !origin.IsFinite || !normal.IsFinite ) return;
		using var scope = scene.Push();
		var go = new GameObject( false, "TF2 Northstar Cluster" );
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = origin;
		var cluster = go.AddComponent<Titanfall2TitanCluster>();
		cluster.Settings = settings;
		cluster.SurfaceNormal = normal.LengthSquared > 0.5f ? normal.Normal : Vector3.Up;
		cluster._attacker = attacker; cluster._weapon = weapon; cluster._ignore = ignore;
		cluster._bursts = BurstCount;
		cluster._nextBurst = 0.5f;
		if ( Networking.IsActive ) go.NetworkSpawn( true, null );
		else go.Enabled = true;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy || _nextBurst > 0 ) return;
		if ( _bursts <= 0 ) { GameObject.Destroy(); return; }
		_nextBurst = Duration / BurstCount;
		_bursts--;
		var normal = SurfaceNormal.Normal;
		var tangent = Vector3.Cross( normal, MathF.Abs( normal.z ) > 0.9f ? Vector3.Forward : Vector3.Up ).Normal;
		var bitangent = Vector3.Cross( normal, tangent );
		var angle = Random.Shared.NextSingle() * MathF.PI * 2;
		var radius = MathF.Sqrt( Random.Shared.NextSingle() ) * SpreadRadius;
		var candidate = WorldPosition + (tangent * MathF.Cos( angle ) + bitangent * MathF.Sin( angle )) * radius;
		var trace = Scene.Trace.Ray( WorldPosition, candidate ).WithoutTags( "trigger", "projectile" );
		if ( _ignore.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( _ignore );
		var cover = Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.Grenade );
		if ( cover.StartedSolid ) return;
		if ( cover.Hit ) candidate = cover.EndPosition + cover.Normal * 2;
		var surfaceTrace = Scene.Trace.Ray( candidate + normal * 24, candidate - normal * 80 )
			.UseHitPosition().WithoutTags( "trigger", "projectile" );
		if ( _ignore.IsValid() ) surfaceTrace = surfaceTrace.IgnoreGameObjectHierarchy( _ignore );
		var surface = Titanfall2CollisionQueries.Run( surfaceTrace, Titanfall2TraceMask.Grenade );
		if ( surface.StartedSolid ) return;
		var point = surface.Hit ? surface.HitPosition : candidate;
		var burstNormal = surface.Hit ? surface.Normal : normal;
		DamageBurst( point + burstNormal * 2 );
		PlayBurst( new BaseCombatWeapon.ShotEffect( point, true, burstNormal, surface.Hit ? surface.GameObject : null, surface.Surface ) );
	}

	void DamageBurst( Vector3 origin )
	{
		var radius = Math.Clamp( Settings.SplashRadius, 1, 512 );
		var targets = new Dictionary<Component, (GameObject Object, Vector3 Point, float Distance)>();
		foreach ( var obj in Scene.FindInPhysics( new Sphere( origin, radius ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component receiver ) continue;
			if ( _ignore.IsValid() && (receiver.GameObject == _ignore || receiver.GameObject.IsDescendant( _ignore )) ) continue;
			foreach ( var collider in obj.GetComponents<Collider>() )
			{
				if ( collider.IsTrigger ) continue;
				var point = collider.FindClosestPoint( origin );
				var distance = point.Distance( origin );
				if ( distance >= radius || (targets.TryGetValue( receiver, out var old ) && old.Distance <= distance) ) continue;
				targets[receiver] = (obj, point, distance);
			}
		}
		foreach ( var (receiver, sample) in targets )
		{
			if ( !receiver.IsValid() || receiver is not IDamageable damageable ) continue;
			var sight = Scene.Trace.Ray( origin, sample.Point ).WithoutTags( "trigger", "projectile" );
			if ( _ignore.IsValid() ) sight = sight.IgnoreGameObjectHierarchy( _ignore );
			var hit = Titanfall2CollisionQueries.Run( sight, Titanfall2TraceMask.Grenade );
			if ( hit.StartedSolid || (hit.Hit && hit.GameObject != sample.Object
				&& hit.GameObject?.GetComponentInParent<IDamageable>() != damageable) ) continue;
			var heavy = receiver.GameObject.GetComponentInParent<Titanfall2Npc>() is { IsTitan: true };
			var damage = new DamageInfo( (heavy ? Settings.SplashTitanDamage : Settings.SplashDamage)
				* (1 - sample.Distance / radius), _attacker, _weapon ) { Origin = origin, Position = sample.Point };
			damage.Tags.Add( "explosion" ); damage.Tags.Add( "projectile" );
			Titanfall2HostDamage.Deal( damageable, damage );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayBurst( BaseCombatWeapon.ShotEffect shot ) => Titanfall2CombatEffects.Impact( Scene, shot,
		Settings.MountIdent, "exp_rocket_cluster_secondary", Settings.SplashRadius, Settings.Tint );
}
