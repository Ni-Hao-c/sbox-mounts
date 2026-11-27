/// <summary>Lightweight client-side muzzle flash and shell ejection effects for TF2 weapons.</summary>
static class Titanfall2WeaponEffects
{
	static readonly Color DefaultFlashColor = new( 1f, 0.78f, 0.35f );

	internal static void SpawnMuzzleFlash( GameObject muzzle, bool firstPerson, Color? tint = null )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || !muzzle.IsValid() ) return;
		var color = tint is { a: > 0 } c ? c : DefaultFlashColor;
		// Use the engine's textured additive burst, with the same render layer as the gun.
		// A solid sphere here looked like a glowing bullet and obscured the sights.
		var prefab = GameObject.GetPrefab( "prefabs/effects/default_muzzleflash.prefab" );
		if ( !prefab.IsValid() ) return;
		var go = prefab.Clone( new CloneConfig { Parent = muzzle, Transform = global::Transform.Zero, StartEnabled = false } );
		go.Name = "TF2 Muzzle Flash";
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.LocalScale = firstPerson ? 0.5f : 0.65f;
		foreach ( var effect in go.GetComponentsInChildren<ParticleEffect>( true ) )
		{
			effect.Tint = color;
			effect.Brightness = 20f;
			// Start visible on the birth frame; the stock zero-size ramp can vanish
			// entirely when a frame takes longer than this short burst's lifetime.
			effect.InitialScale = 10f;
			effect.Scale = 1f;
		}
		foreach ( var renderer in go.GetComponentsInChildren<ParticleSpriteRenderer>( true ) )
		{
			renderer.RenderOptions.Game = !firstPerson;
			renderer.RenderOptions.Overlay = firstPerson;
		}

		var light = go.AddComponent<PointLight>();
		light.LightColor = color;
		light.Radius = firstPerson ? 64f : 96f;
		light.Shadows = false;

		var transient = go.AddComponent<Titanfall2MuzzleFlashEffect>();
		transient.Lifetime = 0.09f;
		go.Enabled = true;
	}

	internal static void SpawnTracer( Scene scene, Vector3 start, Vector3 end, Color? tint = null )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || !scene.IsValid()
			|| !start.IsFinite || !end.IsFinite || (end - start).LengthSquared < 1f ) return;
		var distance = start.Distance( end );
		var color = tint is { a: > 0 } c ? c : DefaultFlashColor;
		var go = new GameObject( scene, false, "TF2 Bullet Tracer" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.WorldPosition = start;
		var tracer = go.AddComponent<TracerEffect>();
		tracer.SetTarget( end );
		tracer.StartDistance = 0;
		// Keep short-range shots visible for a few frames, without a 200-unit blind spot.
		tracer.DistancePerSecond = MathF.Min( 9000f, distance / 0.04f );
		tracer.Length = Math.Clamp( distance * 0.5f, 8f, 100f );
		tracer.LineColor = new Gradient( new Gradient.ColorFrame( 0, color * 3f ), new Gradient.ColorFrame( 1, color.WithAlpha( 0 ) ) );
		tracer.LineWidth = new Curve( new Curve.Frame( 0, 0.65f ), new Curve.Frame( 1, 0 ) );
		tracer.Opaque = false;
		tracer.CastShadows = false;
		tracer.EnableLight = false;
		var cleanup = go.AddComponent<TemporaryEffect>();
		cleanup.DestroyAfterSeconds = distance / tracer.DistancePerSecond + 0.1f;
		cleanup.WaitForChildEffects = false;
		go.Enabled = true;
	}

	internal static void SpawnShellEject( GameObject port, bool firstPerson, Vector3 inheritedVelocity )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || !port.IsValid() || !port.Scene.IsValid() ) return;
		var prefab = GameObject.GetPrefab( "prefabs/effects/default_brasseject.prefab" );
		if ( !prefab.IsValid() ) return;
		var rot = port.WorldRotation;
		var ejectDir = (rot.Right + rot.Up * 0.5f).Normal;
		var go = prefab.Clone( new CloneConfig
		{
			Transform = new Transform( port.WorldPosition, Rotation.LookAt( ejectDir ) ),
			StartEnabled = false
		} );
		go.Name = "TF2 Ejected Shell";
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		foreach ( var effect in go.GetComponentsInChildren<ParticleEffect>( true ) )
		{
			effect.ConstantMovement = inheritedVelocity * 0.45f;
			effect.Lifetime = 0.65f;
			effect.Collision = false;
		}
		foreach ( var renderer in go.GetComponentsInChildren<ParticleSpriteRenderer>( true ) )
		{
			renderer.RenderOptions.Game = !firstPerson;
			renderer.RenderOptions.Overlay = firstPerson;
			renderer.Shadows = false;
		}
		var cleanup = go.GetComponent<TemporaryEffect>();
		if ( cleanup.IsValid() )
		{
			cleanup.DestroyAfterSeconds = 0.8f;
			cleanup.WaitForChildEffects = false;
		}
		go.Enabled = true;
	}
}

[Hide]
public sealed class Titanfall2MuzzleFlashEffect : Component
{
	public float Lifetime { get; set; } = 0.045f;
	TimeSince _age;

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		if ( _age >= Lifetime )
		{
			GameObject.Destroy();
			return;
		}
		var t = 1f - Math.Clamp( _age / MathF.Max( 0.005f, Lifetime ), 0f, 1f );
		if ( GetComponent<PointLight>() is { } light ) light.Radius *= (0.7f + 0.3f * t);
	}
}

