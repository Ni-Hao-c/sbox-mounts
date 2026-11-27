/// <summary>
/// Tone's deployable world-space one-way Particle Wall barrier.
/// Blocks hostile hitscan, projectiles, and blast pressure from either side while allowing
/// the owner Titan and allied faction shots to pass straight through unobstructed.
/// </summary>
[Hide]
public sealed class Titanfall2ParticleWall : Component
{
	public const float DefaultHealth = 3000f;
	public const float DefaultDuration = 8.0f;
	public const float HalfWidth = 128f;
	public const float Height = 210f;

	[Property] public Titanfall2Npc OwnerTitan { get; set; }
	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Sync( SyncFlags.FromHost )] public float Health { get; private set; } = DefaultHealth;
	[Sync( SyncFlags.FromHost )] public float Remaining { get; private set; } = DefaultDuration;

	public bool IsActive => Active && !IsProxy && Health > 0f && Remaining > 0f;

	GameObject _visual;

	internal static Titanfall2ParticleWall Deploy( Titanfall2Npc owner, Ray aim )
	{
		if ( !Networking.IsHost || owner is not { IsValid: true, Dead: false } || !owner.Scene.IsValid() ) return null;

		// Replace any previous wall owned by this Tone so a single Titan maintains one active wall.
		foreach ( var existing in owner.Scene.GetAllComponents<Titanfall2ParticleWall>().ToArray() )
		{
			if ( existing.IsValid() && existing.OwnerTitan == owner )
				existing.GameObject.Destroy();
		}

		var flatForward = aim.Forward.WithZ( 0 );
		flatForward = flatForward.LengthSquared > 0.01f ? flatForward.Normal : owner.WorldRotation.Forward.WithZ( 0 ).Normal;
		if ( flatForward.LengthSquared < 0.01f ) flatForward = Vector3.Forward;

		var forwardProbe = Titanfall2CollisionQueries.Run(
			owner.Scene.Trace.Ray( owner.WorldPosition + Vector3.Up * 64f, owner.WorldPosition + Vector3.Up * 64f + flatForward * 140f )
				.IgnoreGameObjectHierarchy( owner.GameObject ).WithoutTags( "trigger", "projectile" ),
			Titanfall2TraceMask.Grenade );
		var deployPos = forwardProbe.Hit
			? forwardProbe.EndPosition - flatForward * 24f
			: owner.WorldPosition + flatForward * 130f;

		var floor = Titanfall2CollisionQueries.Run(
			owner.Scene.Trace.Ray( deployPos + Vector3.Up * 96f, deployPos - Vector3.Up * 320f )
				.IgnoreGameObjectHierarchy( owner.GameObject ).WithoutTags( "trigger", "projectile" ),
			Titanfall2TraceMask.Grenade );
		var finalPos = floor.Hit && !floor.StartedSolid ? floor.EndPosition : owner.WorldPosition + flatForward * 120f;

		using var scope = owner.Scene.Push();
		var go = new GameObject( false, "TF2 Tone Particle Wall" )
		{
			WorldPosition = finalPos,
			WorldRotation = Rotation.LookAt( flatForward, Vector3.Up )
		};
		go.Flags |= GameObjectFlags.NotSaved;
		var wall = go.AddComponent<Titanfall2ParticleWall>();
		wall.OwnerTitan = owner;
		wall.MountIdent = owner.MountIdent ?? "titanfall2";
		wall.Health = DefaultHealth;
		wall.Remaining = DefaultDuration;
		if ( Networking.IsActive ) go.NetworkSpawn( true, null );
		else go.Enabled = true;
		wall.PlayDeployEffectRpc( finalPos );
		return wall;
	}

	internal bool ShouldBlock( GameObject attacker )
	{
		if ( !attacker.IsValid() || !OwnerTitan.IsValid() ) return true;
		if ( attacker == OwnerTitan.GameObject || attacker == OwnerTitan.Seat?.GetOccupant()?.GameObject
			|| OwnerTitan.GameObject.IsDescendant( attacker ) )
			return false;
		if ( attacker.GetComponentInParent<Titanfall2Npc>() is { } npc )
			return npc != OwnerTitan && OwnerTitan.IsHostileToFaction( npc.Faction );
		if ( attacker.GetComponentInParent<PlayerController>() is not null )
			return OwnerTitan.IsHostileToFaction( Sandbox.Npcs.Factions.Player );
		return true;
	}

	/// <summary>Pure geometric segment-vs-oriented-rectangle intersection test for ballistic interception.</summary>
	internal static bool TryIntersectSegment(
		Vector3 start,
		Vector3 end,
		Vector3 wallOrigin,
		Rotation wallRotation,
		float halfWidth,
		float height,
		float extraRadius,
		out float fraction,
		out Vector3 hitPoint,
		out Vector3 hitNormal )
	{
		fraction = 0f;
		hitPoint = default;
		hitNormal = wallRotation.Forward;

		var delta = end - start;
		if ( delta.LengthSquared < 0.0001f ) return false;

		var normal = wallRotation.Forward;
		var denom = Vector3.Dot( delta, normal );
		if ( MathF.Abs( denom ) < 0.0001f ) return false;

		var t = Vector3.Dot( wallOrigin - start, normal ) / denom;
		if ( t < 0f || t > 1f ) return false;

		var point = start + delta * t;
		var local = wallRotation.Inverse * (point - wallOrigin);
		var pad = MathF.Max( 0f, extraRadius );
		if ( MathF.Abs( local.y ) > halfWidth + pad ) return false;
		if ( local.z < -pad || local.z > height + pad ) return false;

		fraction = t;
		hitPoint = point;
		hitNormal = denom < 0f ? normal : -normal;
		return true;
	}

	internal bool TryIntersect( Vector3 start, Vector3 end, float extraRadius, out float fraction, out Vector3 point, out Vector3 normal ) =>
		TryIntersectSegment( start, end, WorldPosition, WorldRotation, HalfWidth, Height, extraRadius, out fraction, out point, out normal );

	internal void ApplyDamage( float amount, Vector3 hitPoint )
	{
		if ( !Networking.IsHost || !IsActive || !float.IsFinite( amount ) || amount <= 0f ) return;
		Health = MathF.Max( 0f, Health - amount );
		var broken = Health <= 0f;
		PlayHitEffectRpc( hitPoint, broken );
		if ( broken )
			GameObject.Destroy();
	}

	protected override void OnStart()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		BuildVisual();
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || IsProxy ) return;
		Remaining = MathF.Max( 0f, Remaining - Time.Delta );
		if ( Remaining <= 0f || Health <= 0f )
			GameObject.Destroy();
	}

	protected override void OnDestroy()
	{
		if ( _visual.IsValid() ) _visual.Destroy();
		_visual = null;
	}

	void BuildVisual()
	{
		_visual = new GameObject( GameObject, false, "Particle Wall Barrier" );
		_visual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;

		// Build glowing cyan grid frame using LineRenderers so the barrier reads clearly from all angles.
		var frameColor = new Color( 0.22f, 0.78f, 1f ) * 3.2f;
		AddLine( _visual, new Vector3( 0, -HalfWidth, 4f ), new Vector3( 0, HalfWidth, 4f ), 5f, frameColor );
		AddLine( _visual, new Vector3( 0, -HalfWidth, Height ), new Vector3( 0, HalfWidth, Height ), 5f, frameColor );
		AddLine( _visual, new Vector3( 0, -HalfWidth, 4f ), new Vector3( 0, -HalfWidth, Height ), 5f, frameColor );
		AddLine( _visual, new Vector3( 0, HalfWidth, 4f ), new Vector3( 0, HalfWidth, Height ), 5f, frameColor );

		var gridColor = new Color( 0.16f, 0.65f, 1f ) * 1.9f;
		for ( var i = 1; i <= 3; i++ )
		{
			var z = Height * (i / 4f);
			AddLine( _visual, new Vector3( 0, -HalfWidth, z ), new Vector3( 0, HalfWidth, z ), 2.5f, gridColor );
		}
		for ( var j = -2; j <= 2; j++ )
		{
			var y = HalfWidth * (j / 3f);
			AddLine( _visual, new Vector3( 0, y, 4f ), new Vector3( 0, y, Height ), 2.5f, gridColor );
		}

		var lightObj = new GameObject( _visual, true, "Barrier Glow" ) { LocalPosition = Vector3.Up * (Height * 0.5f) };
		var light = lightObj.AddComponent<PointLight>();
		light.LightColor = new Color( 0.22f, 0.72f, 1f ) * 2.2f;
		light.Radius = 260f;
		light.Shadows = false;
		_visual.Enabled = true;
	}

	static void AddLine( GameObject parent, Vector3 a, Vector3 b, float width, Color color )
	{
		var seg = new GameObject( parent, true, "Wall Segment" );
		var pa = new GameObject( seg, true, "A" ) { LocalPosition = a };
		var pb = new GameObject( seg, true, "B" ) { LocalPosition = b };
		var line = seg.AddComponent<LineRenderer>();
		line.Points = [pa, pb];
		line.Width = width;
		line.Color = color;
		line.Opaque = false;
		line.Additive = true;
		line.CastShadows = false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayDeployEffectRpc( Vector3 origin )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, "ShieldWall_Deploy", origin );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayHitEffectRpc( Vector3 point, bool broken )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var fx = new GameObject( Scene, false, broken ? "Particle Wall Broken" : "Particle Wall Hit" ) { WorldPosition = point };
		fx.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Titanfall2CombatEffects.Burst( fx, new Color( 0.28f, 0.80f, 1f ), broken ? 32 : 12, broken ? 140 : 60, broken ? 24 : 12, broken ? 0.9f : 0.4f );
		fx.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 1.0f;
		fx.Enabled = true;
		Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent,
			broken ? "titan_energyshield_down" : "Vortex_Shield_AbsorbBulletSmall", point );
	}
}

/// <summary>
/// Scorch's deployable Incendiary Trap (Gas Canister): bounces to rest, disperses a flammable gas pool,
/// and erupts into a wide roaring thermite inferno when ignited by any Scorch fire source (T-203, Firewall,
/// Thermal Shield, Flame Core, or chain-ignited canisters).
/// </summary>
[Hide]
public sealed class Titanfall2GasCanister : Component
{
	public const float GasRadius = 240f;
	public const float IgniteTriggerRadius = 270f;
	public const float UnignitedLifetime = 14f;
	public const float BurnDuration = 6.5f;

	[Property] public Titanfall2Npc OwnerTitan { get; set; }
	[Property] public GameObject Attacker { get; set; }
	[Property] public string MountIdent { get; set; } = "titanfall2";
	public Vector3 Velocity { get; set; }

	[Sync( SyncFlags.FromHost )] public bool Deployed { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Ignited { get; private set; }

	TimeUntil _expiry;
	int _bounces;
	GameObject _gasVisual;

	internal static Titanfall2GasCanister Launch( Titanfall2Npc owner, Ray aim, GameObject attacker )
	{
		if ( !Networking.IsHost || owner is not { IsValid: true, Dead: false } || !owner.Scene.IsValid() ) return null;
		var dir = (aim.Forward.Normal + Vector3.Up * 0.20f).Normal;
		var origin = owner.WorldPosition + Vector3.Up * (owner.BodyHeight * 0.78f) + dir * 24f;

		using var scope = owner.Scene.Push();
		var go = new GameObject( false, "TF2 Scorch Gas Canister" )
		{
			WorldPosition = origin,
			WorldRotation = Rotation.LookAt( dir )
		};
		go.Flags |= GameObjectFlags.NotSaved;
		go.Tags.Add( "projectile" );
		var canister = go.AddComponent<Titanfall2GasCanister>();
		canister.OwnerTitan = owner;
		canister.Attacker = attacker.IsValid() ? attacker : owner.GameObject;
		canister.MountIdent = owner.MountIdent ?? "titanfall2";
		canister.Velocity = dir * 1150f;
		if ( Networking.IsActive ) go.NetworkSpawn( true, null );
		else go.Enabled = true;
		return canister;
	}

	/// <summary>Ignites all deployed flammable Scorch Gas Canisters within radius, supporting chain ignition.</summary>
	internal static int IgniteInRadius( Scene scene, Vector3 origin, float radius, GameObject igniter = null )
	{
		if ( !Networking.IsHost || !scene.IsValid() || !origin.IsFinite || radius <= 0f ) return 0;
		var ignitedCount = 0;
		var queue = new Queue<Vector3>();
		queue.Enqueue( origin );
		var checkRadius = MathF.Max( radius, IgniteTriggerRadius );

		while ( queue.Count > 0 )
		{
			var center = queue.Dequeue();
			foreach ( var canister in scene.GetAllComponents<Titanfall2GasCanister>().ToArray() )
			{
				if ( !canister.IsValid() || canister.Ignited ) continue;
				if ( canister.WorldPosition.Distance( center ) > checkRadius ) continue;
				if ( canister.IgniteHost( igniter ) )
				{
					ignitedCount++;
					queue.Enqueue( canister.WorldPosition );
				}
			}
		}
		return ignitedCount;
	}

	protected override void OnStart()
	{
		_expiry = UnignitedLifetime;
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;

		var modelPath = $"mount://{MountIdent ?? "titanfall2"}/models/weapons/titan_incendiary_trap/w_titan_incendiary_trap.mdl.visual.vmdl";
		var model = Titanfall2GameplayVisuals.GetModel( modelPath );
		if ( model is { IsError: false } )
		{
			var mr = GameObject.AddComponent<ModelRenderer>();
			mr.Model = model;
			mr.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || IsProxy || Ignited ) return;
		if ( _expiry <= 0f )
		{
			GameObject.Destroy();
			return;
		}

		if ( Deployed ) return;

		var dt = Time.Delta;
		var gravity = Vector3.Down * 920f;
		var start = WorldPosition;
		var end = start + Velocity * dt + gravity * (0.5f * dt * dt);

		var trace = Scene.Trace.Ray( start, end )
			.Radius( 8f )
			.WithoutTags( "trigger", "projectile" );
		if ( OwnerTitan.IsValid() )
			trace = trace.IgnoreGameObjectHierarchy( OwnerTitan.GameObject );
		var hit = Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.Grenade );

		if ( hit.Hit || hit.StartedSolid )
		{
			var normal = hit.Normal.LengthSquared > 0.25f ? hit.Normal : Vector3.Up;
			WorldPosition = hit.EndPosition + normal * 4f;
			if ( normal.z >= 0.65f && (_bounces >= 1 || Velocity.Length < 260f) )
			{
				Velocity = Vector3.Zero;
				Deployed = true;
				_expiry = UnignitedLifetime;
				SpawnGasCloudRpc( WorldPosition );
				return;
			}
			_bounces++;
			var along = Vector3.Dot( Velocity, normal );
			Velocity = (Velocity - normal * along) * 0.45f - normal * along * 0.35f;
			if ( _bounces >= 3 )
			{
				Velocity = Vector3.Zero;
				Deployed = true;
				_expiry = UnignitedLifetime;
				SpawnGasCloudRpc( WorldPosition );
			}
			return;
		}

		WorldPosition = end;
		Velocity += gravity * dt;
	}

	internal bool IgniteHost( GameObject igniter )
	{
		if ( !Networking.IsHost || Ignited || !Scene.IsValid() ) return false;
		Ignited = true;
		Deployed = true;

		var attacker = Attacker.IsValid() ? Attacker : igniter.IsValid() ? igniter : OwnerTitan?.GameObject;
		var ignore = OwnerTitan.IsValid() ? OwnerTitan.GameObject : null;
		var settings = new Titanfall2ProjectileSettings
		{
			Speed = 1f,
			SplashRadius = 260f,
			HazardSeconds = BurnDuration,
			HazardEffect = "P_grenade_thermite",
			DamageOwner = false,
			MountIdent = MountIdent ?? "titanfall2",
			Tint = new Color( 1f, 0.34f, 0.06f ),
			Behavior = Titanfall2ProjectileBehavior.ThermiteBurn
		};
		Titanfall2AreaHazard.Spawn( Scene, WorldPosition + Vector3.Up * 8f, settings, 140f, 520f, attacker, ignore ?? GameObject, ignore );
		PlayIgniteEffectRpc( WorldPosition );
		GameObject.Destroy();
		return true;
	}

	protected override void OnDestroy()
	{
		if ( _gasVisual.IsValid() ) _gasVisual.Destroy();
		_gasVisual = null;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SpawnGasCloudRpc( Vector3 origin )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		if ( _gasVisual.IsValid() ) return;
		_gasVisual = new GameObject( GameObject, false, "Scorch Gas Cloud" ) { WorldPosition = origin + Vector3.Up * 10f };
		_gasVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Titanfall2CombatEffects.Burst( _gasVisual, new Color( 0.65f, 0.78f, 0.25f ), 20, 90f, 36f, 2.2f, smoke: true );
		var light = _gasVisual.AddComponent<PointLight>();
		light.LightColor = new Color( 0.85f, 0.72f, 0.18f ) * 1.4f;
		light.Radius = 180f;
		light.Shadows = false;
		_gasVisual.Enabled = true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayIgniteEffectRpc( Vector3 origin )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var fx = new GameObject( Scene, false, "Scorch Canister Ignition" ) { WorldPosition = origin };
		fx.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Titanfall2CombatEffects.Burst( fx, new Color( 1f, 0.38f, 0.06f ), 44, 220f, 34f, 1.3f, smoke: true );
		fx.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 1.5f;
		fx.Enabled = true;
		Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, "flamewave_blast_middle", origin );
	}
}
