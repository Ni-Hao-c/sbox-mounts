public enum Titanfall2ProjectileBehavior
{
	Standard,
	HomingTitan,
	ArcZap,
	ThermiteBurn,
	GravityPull,
	ElectricSmoke,
	RemoteCharge,
	ClusterBurst,
	FragGrenade,
	ArcGrenade
}

/// <summary>Serialized ballistic settings shared by player weapons and integrated NPC armament.</summary>
public record struct Titanfall2ProjectileSettings
{
	public float Speed { get; set; }
	public float GravityScale { get; set; }
	public float Radius { get; set; }
	public float Lifetime { get; set; }
	public float SplashDamage { get; set; }
	public float SplashTitanDamage { get; set; }
	public float SplashRadius { get; set; }
	public bool DamageOwner { get; set; }
	public string ModelPath { get; set; }
	public string TrailEffect { get; set; }
	public string TrailAttachment { get; set; }
	public string ScriptTrailEffect { get; set; }
	public string HazardEffect { get; set; }
	public float FuseSeconds { get; set; }
	public float HazardSeconds { get; set; }
	public string ImpactTable { get; set; }
	public string MountIdent { get; set; }
	public bool ReducedEffects { get; set; }
	public bool VortexAbsorbOnly { get; set; }
	public Color Tint { get; set; }
	public Titanfall2ProjectileBehavior Behavior { get; set; }
	public readonly bool Enabled => Speed > 0;
}

/// <summary>The host sweeps each flight step; other peers see the replicated object, not their own hit simulation.</summary>
[Title( "Titanfall 2 Projectile" ), Group( "Titanfall 2" )]
public sealed partial class Titanfall2Projectile : Component
{
	[Property] public Titanfall2ProjectileSettings Settings { get; set; }
	public Vector3 Velocity { get; private set; }
	GameObject _attacker;
	GameObject _weapon;
	GameObject _ignore;
	float _damage;
	float _heavyDamage;
	Titanfall2DamageProfile _damageProfile;
	bool _npcFired;
	Vector3 _launchOrigin;
	TimeUntil _expiry;
	TimeUntil _nextZap;
	bool _finished;
	bool _stuck;
	GameObject _stuckTo;
	Transform _stuckLocal;
	Vector3 _stuckNormal = Vector3.Up;
	Vector3 _stuckLocalNormal;
	Surface _stuckSurface;
	readonly List<GameObject> _trails = [];

	internal void SetHostVelocity( Vector3 velocity )
	{
		if ( Networking.IsHost && !IsProxy && !_finished && velocity.IsFinite ) Velocity = velocity;
	}

	internal static Titanfall2Projectile Launch( Scene scene, Ray aim, Titanfall2ProjectileSettings settings,
		float damage, float heavyDamage, GameObject attacker, GameObject weapon, GameObject ignore,
		Titanfall2DamageProfile damageProfile = default, bool npcFired = false )
	{
		if ( !Networking.IsHost || !scene.IsValid() || !aim.Position.IsFinite || !aim.Forward.IsFinite
			|| aim.Forward.LengthSquared < 0.25f || !float.IsFinite( settings.Speed ) || settings.Speed <= 0 ) return null;
		// Retail NPC bolts are created on the server. The additional client
		// predicted-bolt callback is not run for an NPC attack.
		if ( npcFired ) settings.ScriptTrailEffect = null;
		using var scope = scene.Push();
		var go = new GameObject( false, "TF2 Projectile" );
		go.Tags.Add( "projectile" );
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = aim.Position;
		go.WorldRotation = Rotation.LookAt( aim.Forward );
		var projectile = go.AddComponent<Titanfall2Projectile>();
		projectile.Settings = settings;
		projectile.Velocity = aim.Forward.Normal * settings.Speed;
		projectile._attacker = attacker;
		projectile._weapon = weapon;
		projectile._ignore = ignore;
		projectile._damage = damage;
		projectile._heavyDamage = heavyDamage;
		projectile._damageProfile = damageProfile;
		projectile._npcFired = npcFired;
		projectile._launchOrigin = aim.Position;
		projectile._expiry = settings.Behavior == Titanfall2ProjectileBehavior.RemoteCharge ? 45f
			: settings.FuseSeconds > 0 ? settings.FuseSeconds : Math.Clamp( settings.Lifetime, 0.05f, 12 );
		if ( Networking.IsActive ) go.NetworkSpawn( true, null );
		else go.Enabled = true;
		return projectile;
	}

	protected override void OnStart()
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || Settings.ReducedEffects ) return;
		var model = Titanfall2GameplayVisuals.GetModel( Settings.ModelPath );
		var render = new GameObject( GameObject, false, "Projectile Visual" );
		render.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		if ( model is { IsError: false } )
		{
			// These retail missiles are authored nose-first on +Z in their bind pose.
			// Keep flight in +X; rotate the model and its exhaust attachment together.
			render.LocalRotation = ModelRotation( Settings.ModelPath );
			var renderer = render.AddComponent<ModelRenderer>();
			renderer.Model = model;
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
		else if ( string.IsNullOrWhiteSpace( Settings.TrailEffect ) && string.IsNullOrWhiteSpace( Settings.ScriptTrailEffect ) )
		{
			// Model-less plasma/bullets need an emissive streak, not a lit error sphere.
			// This local segment follows the actual host-simulated projectile on all peers.
			var length = Math.Clamp( Settings.Speed * 0.012f, 8f, 42f );
			var width = Math.Clamp( Settings.Radius * 0.7f, 0.7f, 3f );
			var line = render.AddComponent<LineRenderer>();
			// LineRenderer consumes world-space points. Attachment objects keep both
			// endpoints moving with the projectile, including replicated transforms.
			var tail = new GameObject( render, true, "Plasma tail" );
			tail.LocalPosition = Vector3.Backward * length;
			line.Points = [render, tail];
			line.Width = new Curve( new Curve.Frame( 0, width ), new Curve.Frame( 1, 0 ) );
			line.Color = new Gradient( new Gradient.ColorFrame( 0, Settings.Tint * 4f ), new Gradient.ColorFrame( 1, Settings.Tint.WithAlpha( 0 ) ) );
			line.Opaque = false;
			line.Additive = true;
			line.CastShadows = false;
		}
		if ( !string.IsNullOrWhiteSpace( Settings.TrailEffect ) )
		{
			var trail = CreateTrail( render, Settings.TrailEffect );
			if ( model is { IsError: false } && !string.IsNullOrEmpty( Settings.TrailAttachment )
				&& model.Attachments.GetTransform( Settings.TrailAttachment ) is { } exhaust ) trail.LocalTransform = exhaust;
		}
		if ( !string.IsNullOrWhiteSpace( Settings.ScriptTrailEffect ) ) CreateTrail( GameObject, Settings.ScriptTrailEffect );
		render.Enabled = true;
	}

	GameObject CreateTrail( GameObject parent, string effectName )
	{
		var trail = new GameObject( parent, false, "Retail projectile trail" );
		trail.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var plasma = trail.AddComponent<Titanfall2PlasmaFlight>();
		plasma.MountIdent = Settings.MountIdent;
		plasma.EffectName = effectName;
		_trails.Add( trail );
		trail.Enabled = true;
		return trail;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy || _finished ) return;
		if ( _expiry <= 0 )
		{
			if ( Settings.Behavior == Titanfall2ProjectileBehavior.FragGrenade ) Detonate( WorldPosition, Vector3.Up, null, null );
			else { ReleaseTrail(); GameObject.Destroy(); }
			return;
		}
		if ( _stuck )
		{
			if ( _stuckTo.IsValid() )
			{
				WorldTransform = _stuckTo.WorldTransform.ToWorld( _stuckLocal );
				_stuckNormal = _stuckTo.WorldRotation * _stuckLocalNormal;
			}
			return;
		}
		var dt = Time.Delta;
		var gravity = Vector3.Down * (800 * Settings.GravityScale);
		var start = WorldPosition;
		var end = start + Velocity * dt + gravity * (0.5f * dt * dt);
		var trace = Scene.Trace.Ray( start, end ).Radius( Math.Clamp( Settings.Radius, 0, 12 ) ).UseHitboxes().UseHitPosition().WithoutTags( "trigger", "projectile" );
		if ( _ignore.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( _ignore );
		if ( _weapon.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( _weapon );
		var hit = Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.Grenade );
		// Resolve the nearest shield before actor/world impact, fuse bounce or detonation.
		// Consume this entity exactly once. No damage callback or explosion runs on capture.
		if ( !hit.StartedSolid && Titanfall2TitanDefense.ValidSource( Scene, _attacker, _weapon )
			&& Titanfall2TitanDefense.TryFind( Scene, start, hit.EndPosition, Math.Clamp( Settings.Radius, 0, 12 ), _attacker,
				out var shield, out var shieldPoint, out _ ) )
		{
			_finished = true;
			WorldPosition = shieldPoint;
			var captured = new Titanfall2CapturedProjectile( Settings, _damage, _heavyDamage, _damageProfile, _npcFired, _expiry );
			shield.AbsorbProjectile( captured, _launchOrigin.Distance( shieldPoint ), _attacker, _weapon, shieldPoint );
			ReleaseTrail();
			GameObject.Destroy();
			return;
		}
		if ( hit.Hit || hit.StartedSolid )
		{
			if ( Settings.Behavior == Titanfall2ProjectileBehavior.FragGrenade )
			{
				var normal = hit.Normal.LengthSquared > 0.5f ? hit.Normal : -Velocity.Normal;
				WorldPosition = hit.EndPosition + normal * 0.5f;
				Velocity = BounceVelocity( Velocity, normal );
				if ( Velocity.Length < 25f ) { Velocity = Vector3.Zero; _stuck = true; }
				return;
			}
			// Stop on the swept centre before detaching the smoke; it must not remain
			// a whole physics step behind the actual point of contact.
			WorldPosition = hit.EndPosition;
			Impact( hit );
			return;
		}
		WorldPosition = end;
		Velocity += gravity * dt;
		if ( Settings.Behavior == Titanfall2ProjectileBehavior.HomingTitan ) SteerTowardTitan( dt );
		else if ( Settings.Behavior == Titanfall2ProjectileBehavior.ArcZap && _nextZap <= 0 ) { _nextZap = 0.18f; ZapNearby(); }
		if ( Velocity.LengthSquared > 1 ) WorldRotation = Rotation.LookAt( Velocity );
	}

	internal static Rotation ModelRotation( string path )
	{
		var name = path?.Replace( '\\', '/' );
		return name?.Contains( "/weapons/bullets/projectile_arl.mdl", StringComparison.OrdinalIgnoreCase ) == true
			|| name?.Contains( "/weapons/bullets/projectile_rocket_launcher_sram.mdl", StringComparison.OrdinalIgnoreCase ) == true
			|| name?.Contains( "/weapons/bullets/projectile_rocket.mdl", StringComparison.OrdinalIgnoreCase ) == true
			|| name?.Contains( "/weapons/bullets/projectile_rocket_large.mdl", StringComparison.OrdinalIgnoreCase ) == true
			? Rotation.FromToRotation( Vector3.Up, Vector3.Forward ) : Rotation.Identity;
	}

	internal static Vector3 BounceVelocity( Vector3 velocity, Vector3 normal )
	{
		var along = Vector3.Dot( velocity, normal );
		return (velocity - normal * along) * 0.5f - normal * along * 0.5f;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ReleaseTrail()
	{
		foreach ( var trail in _trails )
		{
			if ( !trail.IsValid() ) continue;
			trail.Parent = null;
			if ( trail.GetComponent<Titanfall2PlasmaFlight>() is { } flight ) flight.Enabled = false;
			foreach ( var emitter in trail.GetComponentsInChildren<ParticleEmitter>( true ) ) emitter.Enabled = false;
			var cleanup = trail.GetOrAddComponent<TemporaryEffect>();
			cleanup.DestroyAfterSeconds = 2;
			cleanup.WaitForChildEffects = false;
		}
		_trails.Clear();
	}

	void SteerTowardTitan( float dt )
	{
		if ( TrySteerDesignatedTarget( dt ) ) return;
		var speed = Velocity.Length;
		if ( speed < 10 ) return;
		var forward = Velocity / speed;
		Titanfall2Npc best = null;
		var bestDist = 2800f * 2800f;
		foreach ( var npc in Scene.GetAllComponents<Titanfall2Npc>() )
		{
			if ( !npc.IsTitan || npc.Dead || (_ignore.IsValid() && npc.GameObject == _ignore) ) continue;
			var source = _attacker?.GetComponentInParent<Titanfall2Npc>();
			if ( source.IsValid() ? !source.IsHostileToFaction( npc.Faction )
				: _attacker?.GetComponentInParent<PlayerController>() is not null && !npc.IsHostileToFaction( Sandbox.Npcs.Factions.Player ) ) continue;
			var center = npc.WorldPosition + Vector3.Up * (npc.BodyHeight * 0.55f);
			var delta = center - WorldPosition;
			var distSq = delta.LengthSquared;
			if ( distSq >= bestDist || distSq < 100 ) continue;
			if ( Vector3.Dot( forward, delta.Normal ) < 0.7f ) continue;
			bestDist = distSq;
			best = npc;
		}
		if ( best is null ) return;
		var desired = (best.WorldPosition + Vector3.Up * (best.BodyHeight * 0.55f) - WorldPosition).Normal;
		Velocity = Vector3.Lerp( forward, desired, Math.Clamp( dt * 5.5f, 0f, 1f ) ).Normal * speed;
	}

	void ZapNearby()
	{
		var receivers = new HashSet<Component>();
		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, 220f ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component receiver || !receivers.Add( receiver ) ) continue;
			if ( _ignore.IsValid() && (_ignore == receiver.GameObject || _ignore.IsDescendant( receiver.GameObject )) ) continue;
			if ( receiver is not IDamageable damageable ) continue;
			var sight = Scene.Trace.Ray( WorldPosition, receiver.GameObject.GetBounds().Center ).WithoutTags( "trigger", "projectile" );
			if ( _ignore.IsValid() ) sight = sight.IgnoreGameObjectHierarchy( _ignore );
			var cover = Titanfall2CollisionQueries.Run( sight, Titanfall2TraceMask.Shot );
			if ( cover.StartedSolid || (cover.Hit && cover.GameObject?.GetComponentInParent<IDamageable>() != damageable) ) continue;
			var isTitan = receiver.GameObject.GetComponentInParent<Titanfall2Npc>() is { IsTitan: true };
			var info = new DamageInfo( isTitan ? _heavyDamage * 0.25f : _damage * 0.35f, _attacker, _weapon )
			{
				Origin = WorldPosition,
				Position = obj.WorldPosition
			};
			info.Tags.Add( "shock" );
			Titanfall2HostDamage.Deal( damageable, info );
		}
	}

	void Impact( in SceneTraceResult hit )
	{
		// A swept sphere stops its centre one radius before the surface. Decals and
		// attached effects need the actual contact; initial overlaps have no contact.
		var contact = hit.StartedSolid ? hit.EndPosition : hit.HitPosition;
		if ( Settings.Behavior == Titanfall2ProjectileBehavior.RemoteCharge )
		{
			_stuck = true;
			Velocity = Vector3.Zero;
			WorldPosition = contact + hit.Normal * 2f;
			_stuckTo = hit.GameObject;
			_stuckNormal = hit.Normal;
			_stuckSurface = hit.Surface;
			if ( _stuckTo.IsValid() )
			{
				_stuckLocal = _stuckTo.WorldTransform.ToLocal( WorldTransform );
				_stuckLocalNormal = _stuckTo.WorldRotation.Inverse * hit.Normal;
			}
			return;
		}
		if ( Settings.Behavior is Titanfall2ProjectileBehavior.ThermiteBurn or Titanfall2ProjectileBehavior.GravityPull or Titanfall2ProjectileBehavior.ElectricSmoke )
		{
			_finished = true;
			ReleaseTrail();
			Titanfall2AreaHazard.Spawn( Scene, contact + hit.Normal * 2, Settings, _damage, _heavyDamage, _attacker, _weapon, _ignore,
				hit.Normal, hit.GameObject );
			GameObject.Destroy();
			return;
		}
		Detonate( contact, hit.Normal, hit.GameObject, hit.Surface, hit );
	}

	void Detonate( Vector3 point, Vector3 normal, GameObject hitObject, Surface surface, SceneTraceResult? collision = null )
	{
		if ( _finished ) return;
		_finished = true;
		ReleaseTrail();
		var direct = hitObject?.GetComponentInParent<IDamageable>();
		// Grenades apply their area damage, rather than a second bullet impact hit.
		if ( hitObject.IsValid() && Settings.Behavior is not (Titanfall2ProjectileBehavior.FragGrenade or Titanfall2ProjectileBehavior.ArcGrenade) )
		{
			var damage = _damageProfile.Evaluate( _launchOrigin.Distance( point ),
				hitObject.GetComponentInParent<Titanfall2Npc>() is { IsTitan: true }, _npcFired, _damage, _heavyDamage );
			var tags = new TagSet();
			tags.Add( "projectile" );
			TagTitanWeaponDamage( tags );
			if ( collision is { } hit ) Titanfall2HostDamage.ShootBullet( hit, damage, 0, _attacker, _weapon, tags );
		}
		else direct = null;
		var blastOrigin = point + (normal.LengthSquared > 0.5f ? normal : -Velocity.Normal);
		if ( Settings.Behavior == Titanfall2ProjectileBehavior.ClusterBurst )
			Titanfall2TitanCluster.Spawn( Scene, blastOrigin, Settings, _attacker, _weapon, _ignore, normal );
		else if ( Settings.SplashRadius > 0 && Settings.SplashDamage > 0 )
			Blast( blastOrigin, direct );
		PlayImpact( new BaseCombatWeapon.ShotEffect( point, true, normal, hitObject, surface ), Settings.Tint, Settings.SplashRadius );
		GameObject.Destroy();
	}

	internal void DetonateOwned( GameObject weapon, GameObject attacker )
	{
		if ( !Networking.IsHost || IsProxy || _finished || Settings.Behavior != Titanfall2ProjectileBehavior.RemoteCharge
			|| _weapon != weapon || _attacker != attacker ) return;
		_finished = true;
		ReleaseTrail();
		Blast( WorldPosition, null );
		PlayImpact( new BaseCombatWeapon.ShotEffect( WorldPosition, true, _stuckNormal, _stuckTo, _stuckSurface ), Settings.Tint, Settings.SplashRadius );
		GameObject.Destroy();
	}

	void Blast( Vector3 origin, IDamageable direct )
	{
		var radius = Settings.SplashRadius;
		// Blast pressure is not a bullet/projectile capture. It damages the independent
		// Legion shield once per explosion and still uses normal actor splash/LOS below.
		Titanfall2TitanDefense.DamageGunShieldsInBlast( Scene, origin, radius,
			Settings.SplashTitanDamage > 0 ? Settings.SplashTitanDamage : Settings.SplashDamage, _attacker, _weapon );
		// A physics overlap bounds the work. Pick the closest positive collider sample per
		// damage receiver and require a clear ray to that collider before applying falloff.
		var targets = new Dictionary<Component, (GameObject Object, Vector3 Point, float Distance)>();
		foreach ( var obj in Scene.FindInPhysics( new Sphere( origin, radius ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component receiver || ReferenceEquals( direct, receiver ) ) continue;
			if ( !Settings.DamageOwner && _ignore.IsValid() && (_ignore == receiver.GameObject || _ignore.IsDescendant( receiver.GameObject )) ) continue;
			foreach ( var collider in obj.GetComponents<Collider>() )
			{
				if ( collider.IsTrigger ) continue;
				var point = collider.FindClosestPoint( origin );
				var distance = (point - origin).Length;
				if ( distance >= radius || (targets.TryGetValue( receiver, out var existing ) && distance >= existing.Distance) ) continue;
				targets[receiver] = (obj, point, distance);
			}
		}
		foreach ( var (receiver, sample) in targets )
		{
			if ( !receiver.IsValid() || receiver is not IDamageable damageable ) continue;
			var sight = Scene.Trace.Ray( origin, sample.Point ).WithoutTags( "trigger", "projectile" );
			if ( !Settings.DamageOwner && _ignore.IsValid() ) sight = sight.IgnoreGameObjectHierarchy( _ignore );
			var trace = Titanfall2CollisionQueries.Run( sight, Titanfall2TraceMask.Grenade );
			if ( trace.StartedSolid ) continue;
			if ( trace.Hit && trace.GameObject != sample.Object && trace.GameObject?.GetComponentInParent<IDamageable>() != damageable ) continue;
			var heavy = receiver.GameObject.GetComponentInParent<Titanfall2Npc>() is { IsTitan: true };
			var amount = heavy && Settings.SplashTitanDamage > 0 ? Settings.SplashTitanDamage : Settings.SplashDamage;
			var damage = new DamageInfo( amount * (1 - sample.Distance / radius), _attacker, _weapon )
			{
				Origin = origin,
				Position = sample.Point
			};
			damage.Tags.Add( "explosion" );
			damage.Tags.Add( "projectile" );
			TagTitanWeaponDamage( damage.Tags );
			Titanfall2HostDamage.Deal( damageable, damage );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayImpact( BaseCombatWeapon.ShotEffect shot, Color color, float splash )
	{
		// Retail SetReducedEffects sets m_reducedEffects and clears the authored
		// impact table. The central Double Take bolt still applies full damage.
		if ( Settings.ReducedEffects ) return;
		Titanfall2CombatEffects.Impact( Scene, shot, Settings.MountIdent, Settings.ImpactTable ?? "exp_frag_grenade", splash, color );
	}
}

sealed class Titanfall2ImpactFlash : Component
{
	TimeUntil _expires;
	protected override void OnStart() => _expires = 0.08f;
	protected override void OnUpdate()
	{
		if ( _expires <= 0 ) GameObject.Destroy();
	}
}

/// <summary>Host-simulated lingering hazard for Firestar thermite burn and Gravity Star vortex.</summary>
[Hide]
public sealed class Titanfall2AreaHazard : Component
{
	[Property] public Titanfall2ProjectileSettings Settings { get; set; }
	[Property] public GameObject FollowTarget { get; set; }
	[Property] public Transform AttachmentTransform { get; set; }
	float _damage;
	float _heavyDamage;
	GameObject _attacker;
	GameObject _weapon;
	GameObject _ignore;
	TimeUntil _expires;
	TimeUntil _nextTick;

	internal static void Spawn( Scene scene, Vector3 origin, Titanfall2ProjectileSettings settings,
		float damage, float heavyDamage, GameObject attacker, GameObject weapon, GameObject ignore,
		Vector3? surfaceNormal = null, GameObject attachedTo = null )
	{
		if ( !Networking.IsHost || !scene.IsValid() ) return;
		using var scope = scene.Push();
		var go = new GameObject( false, settings.Behavior == Titanfall2ProjectileBehavior.GravityPull ? "TF2 Gravity Star" : "TF2 Thermite" );
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = origin;
		if ( settings.Behavior == Titanfall2ProjectileBehavior.ThermiteBurn && surfaceNormal is { } normal && normal.LengthSquared > 0.5f )
			go.WorldRotation = Rotation.FromToRotation( Vector3.Up, normal );
		var hazard = go.AddComponent<Titanfall2AreaHazard>();
		hazard.Settings = settings;
		hazard._damage = damage;
		hazard._heavyDamage = heavyDamage;
		hazard._attacker = attacker;
		hazard._weapon = weapon;
		hazard._ignore = ignore;
		hazard._expires = settings.HazardSeconds > 0 ? settings.HazardSeconds : settings.Behavior == Titanfall2ProjectileBehavior.GravityPull ? 2.9f
			: settings.Behavior == Titanfall2ProjectileBehavior.ElectricSmoke ? 5f : 3.5f;
		hazard._nextTick = settings.Behavior == Titanfall2ProjectileBehavior.ElectricSmoke ? 1f : 0.05f;
		if ( settings.Behavior == Titanfall2ProjectileBehavior.ThermiteBurn && attachedTo.IsValid() )
		{
			hazard.FollowTarget = attachedTo;
			hazard.AttachmentTransform = attachedTo.WorldTransform.ToLocal( go.WorldTransform );
		}
		if ( Networking.IsActive ) go.NetworkSpawn( true, null );
		else go.Enabled = true;
	}

	protected override void OnStart()
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless ) return;
		var effect = Settings.HazardEffect ?? (Settings.Behavior switch
		{
			Titanfall2ProjectileBehavior.ThermiteBurn => "P_grenade_thermite",
			Titanfall2ProjectileBehavior.GravityPull => "P_wpn_grenade_gravity",
			Titanfall2ProjectileBehavior.ElectricSmoke => "P_wpn_smk_electric_pilot",
			_ => null
		});
		if ( string.IsNullOrEmpty( effect ) )
		{
			if ( Settings.Behavior == Titanfall2ProjectileBehavior.ClusterBurst )
				Titanfall2CombatEffects.Burst( GameObject, Settings.Tint, 32, 45, 18, 2.5f );
			return;
		}
		var visual = new GameObject( GameObject, false, effect );
		visual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var retail = visual.AddComponent<Titanfall2PlasmaFlight>();
		retail.MountIdent = Settings.MountIdent;
		retail.EffectName = effect;
		retail.CombatImpact = true;
		visual.Enabled = true;
		Titanfall2OrdnanceSounds.Play( Settings.MountIdent, Settings.Behavior switch
		{
			Titanfall2ProjectileBehavior.ThermiteBurn => "firestar",
			Titanfall2ProjectileBehavior.GravityPull => "gravity_start",
			_ => "smoke"
		}, WorldPosition );
	}

	protected override void OnUpdate()
	{
		if ( FollowTarget.IsValid() ) WorldTransform = FollowTarget.WorldTransform.ToWorld( AttachmentTransform );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		var radius = MathF.Max( 80f, Settings.SplashRadius );

		if ( Settings.Behavior == Titanfall2ProjectileBehavior.GravityPull )
		{
			var pulled = new HashSet<Titanfall2Npc>();
			foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, radius ) ) )
			{
				if ( obj.GetComponentInParent<Titanfall2Npc>() is { IsTitan: false, Dead: false } npc && pulled.Add( npc ) )
				{
					var pull = (WorldPosition - npc.WorldPosition);
					if ( pull.Length <= 24f ) continue;
					var sight = Scene.Trace.Ray( WorldPosition, npc.GameObject.GetBounds().Center ).WithoutTags( "trigger", "projectile" );
					if ( _ignore.IsValid() && _ignore != npc.GameObject && !_ignore.IsDescendant( npc.GameObject ) ) sight = sight.IgnoreGameObjectHierarchy( _ignore );
					var cover = Titanfall2CollisionQueries.Run( sight, Titanfall2TraceMask.Grenade );
					if ( cover.StartedSolid || (cover.Hit && cover.GameObject?.GetComponentInParent<Titanfall2Npc>() != npc) ) continue;
					var radiusXY = MathF.Max( 2, npc.BodyRadius - 1 );
					var hull = new BBox( new Vector3( -radiusXY, -radiusXY, 1 ), new Vector3( radiusXY, radiusXY, npc.BodyHeight - 1 ) );
					var step = Titanfall2CollisionQueries.Run( Scene.Trace.Box( hull, npc.WorldPosition, npc.WorldPosition + pull.Normal * (280f * Time.Delta) )
						.IgnoreGameObjectHierarchy( npc.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.NpcSolid );
					if ( !step.StartedSolid ) npc.WorldPosition = step.EndPosition;
				}
			}
			if ( _expires <= 0 )
			{
				PulseDamage( radius, MathF.Max( _damage, Settings.SplashDamage ), "explosion" );
				PlayGravityExplosion();
				GameObject.Destroy();
			}
			return;
		}

		if ( _expires <= 0 ) { GameObject.Destroy(); return; }
		if ( _nextTick > 0 ) return;
		_nextTick = 0.25f;
		var smoke = Settings.Behavior == Titanfall2ProjectileBehavior.ElectricSmoke;
		PulseDamage( radius, smoke ? 10f : MathF.Max( 12f, Settings.SplashDamage * 0.6f ),
			smoke ? "shock" : Settings.Behavior == Titanfall2ProjectileBehavior.ClusterBurst ? "explosion" : "burn" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayGravityExplosion() => Titanfall2CombatEffects.Impact( Scene,
		new BaseCombatWeapon.ShotEffect( WorldPosition, true, Vector3.Up, null, null ), Settings.MountIdent,
		Settings.ImpactTable ?? "exp_gravity_grenade", Settings.SplashRadius );

	void PulseDamage( float radius, float amount, string tag )
	{
		var hitReceivers = new HashSet<Component>();
		foreach ( var obj in Scene.FindInPhysics( new Sphere( WorldPosition, radius ) ) )
		{
			if ( obj.GetComponentInParent<IDamageable>() is not Component receiver || !hitReceivers.Add( receiver ) ) continue;
			if ( !Settings.DamageOwner && _ignore.IsValid() && (_ignore == receiver.GameObject || _ignore.IsDescendant( receiver.GameObject )) ) continue;
			if ( receiver is not IDamageable damageable ) continue;
			var sight = Scene.Trace.Ray( WorldPosition, receiver.GameObject.GetBounds().Center ).WithoutTags( "trigger", "projectile" );
			if ( _ignore.IsValid() && receiver.GameObject != _ignore && !receiver.GameObject.IsDescendant( _ignore ) )
				sight = sight.IgnoreGameObjectHierarchy( _ignore );
			var cover = Titanfall2CollisionQueries.Run( sight, Titanfall2TraceMask.Grenade );
			if ( cover.StartedSolid || (cover.Hit && cover.GameObject != receiver.GameObject && cover.GameObject?.GetComponentInParent<IDamageable>() != damageable) ) continue;
			var isTitan = receiver.GameObject.GetComponentInParent<Titanfall2Npc>() is { IsTitan: true };
			var info = new DamageInfo( isTitan ? MathF.Max( amount, _heavyDamage * 0.35f ) : amount, _attacker, _weapon )
			{
				Origin = WorldPosition,
				Position = receiver.WorldPosition
			};
			info.Tags.Add( tag );
			Titanfall2HostDamage.Deal( damageable, info );
		}
	}
}

