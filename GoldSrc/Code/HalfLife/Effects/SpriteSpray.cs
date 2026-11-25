namespace GoldSrc.HalfLife;

internal static class SpriteSpray
{
	const float Lifetime = 0.85f;
	static readonly ParticleFloat Fade = new()
	{
		Type = ParticleFloat.ValueType.Curve,
		CurveA = new Curve(
			new Curve.Frame( 0, 1 ) { Mode = Curve.HandleMode.Linear },
			new Curve.Frame( 0.35f / Lifetime, 1 ) { Mode = Curve.HandleMode.Linear },
			new Curve.Frame( 1, 0 ) { Mode = Curve.HandleMode.Linear } )
	};

	internal static void Emit( string name, string sprite, Vector3 position, Vector3 direction, int count, float speed, float noise )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ||
			mount.GetIndexedSpriteFrames( sprite ) is not { Length: > 0 } frames ) return;

		// Each GoldSrc spray particle keeps the frame it picked at launch.
		var groups = new ParticleEffect[frames.Length];
		for ( int i = 0; i < count; ++i )
		{
			int frame = Game.Random.Int( 0, frames.Length - 1 );
			var effect = groups[frame] ??= CreateGroup( name, position, frames[frame] );
			if ( effect.Emit( position, 0 ) is not { } particle ) continue;
			var spread = new Vector3( Game.Random.Float( -noise, noise ), Game.Random.Float( -noise, noise ),
				Game.Random.Float( 0, System.Math.Min( noise * 1.5f, 1 ) ) );
			// TE_SPRITE_SPRAY encodes speed in units of two.
			particle.Velocity = (direction + spread) * (2 * speed * Game.Random.Float( 0.8f, 1.2f ));
		}
	}

	static ParticleEffect CreateGroup( string name, Vector3 position, Sprite frame )
	{
		var go = new GameObject( false, name );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var effect = go.AddComponent<ParticleEffect>();
		effect.Lifetime = Lifetime;
		effect.ApplyShape = true;
		effect.InitialScale = frame.Animations[0].Frames[0].Texture.Width * 0.5f;
		effect.ApplyAlpha = true;
		effect.Alpha = Fade;
		effect.Force = true;
		effect.ForceDirection = go.Scene.PhysicsWorld.Gravity * 0.5f;
		go.AddComponent<ParticleSpriteRenderer>().Sprite = frame;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = Lifetime;
		go.Enabled = true;
		return effect;
	}
}
