namespace GoldSrc.HalfLife;

[Hide]
public sealed class TurretWreck : NpcCorpse
{
	[Property] public float Radius { get; set; }
	[Property] public float Height { get; set; }
	TimeUntil _nextEffect;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Age >= 8 || _nextEffect > 0 ) return;
		_nextEffect = 0.1f;
		var offset = new Vector3( Game.Random.Float( -Radius, Radius ), Game.Random.Float( -Radius, Radius ), 0 );
		if ( Game.Random.Float( 0, 2 ) > Age ) Spray( "steam1", offset, 1, 26, 1.2f );
		if ( Game.Random.Float( 0, Height > 48 ? 8 : 5 ) > Age )
			Spray( "spark1", offset.WithZ( Game.Random.Float( 0, Height ) ), 5, 2, 0.35f );
	}

	void Spray( string name, Vector3 offset, int count, float size, float lifetime )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetIndexedSprite( name ) is not { } sprite ) return;
		var go = new GameObject( false, name );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = WorldTransform.PointToWorld( offset );
		var effect = go.AddComponent<ParticleEffect>();
		effect.Lifetime = lifetime;
		effect.InitialScale = size;
		go.AddComponent<ParticleSpriteRenderer>().Sprite = sprite;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = lifetime + 0.1f;
		go.Enabled = true;
		for ( int i = 0; i < count; i++ )
		{
			var particle = effect.Emit( go.WorldPosition, 0 );
			if ( particle is not null )
				particle.Velocity = Vector3.Up * 28 + Vector3.Random * (count == 1 ? 6 : 65);
		}
	}
}
