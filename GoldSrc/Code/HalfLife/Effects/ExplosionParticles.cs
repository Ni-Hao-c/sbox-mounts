namespace GoldSrc.HalfLife;

[Hide]
public sealed class ExplosionParticles : Component
{
	static readonly byte[] SparkRamp = [254, 253, 252, 111, 110, 109, 108, 103, 96];
	ParticleEffect _sparks;
	PointLight _flashLight, _afterglow;
	GameObject _fireball;
	TimeSince _age;
	Color[] _colors;

	internal static void Spawn( Vector3 origin, GameObject fireball )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		var go = new GameObject( false, "Explosion flicker" ) { NetworkMode = NetworkMode.Never, WorldPosition = origin };
		var burst = go.AddComponent<ExplosionParticles>();
		burst._fireball = fireball;
		burst._colors = SparkRamp.Select( i => mount.PaletteColor( i ) ).ToArray();
		burst._flashLight = burst.CreateLight( 200, new Color32( 250, 250, 150, 255 ) );
		burst._afterglow = burst.CreateLight( 150, new Color32( 255, 190, 40, 255 ) );
		var sparks = go.AddComponent<ParticleEffect>();
		sparks.Lifetime = 2;
		sparks.ApplyShape = true;
		sparks.InitialScale = 1;
		sparks.Force = true;
		sparks.ForceDirection = go.Scene.PhysicsWorld.Gravity * 0.25f;
		sparks.Damping = 0.5f;
		go.AddComponent<ParticleSpriteRenderer>().Sprite = PaletteParticle.Sprite;
		burst._sparks = sparks;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 2;
		burst._age = 0;
		go.Enabled = true;
		for ( int i = 0; i < 15; ++i )
		{
			if ( sparks.Emit( origin, 0 ) is not { } spark ) continue;
			spark.Color = burst._colors[0];
			spark.Velocity = new Vector3( Game.Random.Float( -32, 32 ), Game.Random.Float( -32, 32 ), Game.Random.Float( 80, 143 ) );
		}
	}

	PointLight CreateLight( float radius, Color color )
	{
		var light = GameObject.AddComponent<PointLight>();
		light.Radius = radius;
		light.LightColor = color;
		light.Shadows = false;
		return light;
	}

	protected override void OnUpdate()
	{
		float age = _age;
		if ( _fireball.IsValid() ) _fireball.WorldPosition = WorldPosition + Vector3.Up * (10 + 8 * age);
		if ( _flashLight.IsValid() && age >= 0.01f ) _flashLight.Destroy();
		if ( _afterglow.IsValid() )
		{
			if ( age >= 0.75f ) _afterglow.Destroy();
			else _afterglow.Radius = 150 - 200 * age;
		}
		var color = _colors[(int)(age * 10) % SparkRamp.Length];
		foreach ( var particle in _sparks.Particles )
		{
			particle.Color = color;
			particle.Alpha = Game.Random.Int( 0, 3 ) == 0 ? 1 : 0;
		}
	}
}
