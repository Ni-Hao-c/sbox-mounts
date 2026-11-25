namespace GoldSrc.HalfLife;

[Library]
internal static class BulletEffects
{
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Tracer( Vector3 start, Vector3 end ) => ShowTracer( start, end );

	internal static void ShowTracer( Vector3 start, Vector3 end )
	{
		if ( Application.IsDedicatedServer ) return;
		var go = new GameObject( false, "Bullet tracer" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = start;
		var tracer = go.AddComponent<TracerEffect>();
		tracer.EndPoint = end;
		tracer.DistancePerSecond = 6000;
		tracer.StartDistance = 0;
		tracer.LineWidth = new Curve( new Curve.Frame( 0, 1.5f ), new Curve.Frame( 1, 0 ) );
		tracer.Opaque = false;
		tracer.CastShadows = false;
		var color = new Color( 0.8f, 0.8f, 0.4f, 0.5f );
		tracer.LineColor = new Gradient( new Gradient.ColorFrame( 0, color ), new Gradient.ColorFrame( 1, color.WithAlpha( 0 ) ) );
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0;
		go.Enabled = true;
	}

	internal static void Impact( SceneTraceResult trace, bool flesh, bool decal )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		var sound = mount.GetBulletImpactSound( trace.Surface, flesh );
		Impact( trace.EndPosition, trace.Normal, sound?.ResourcePath, decal ? Game.Random.Int( 1, 5 ) : 0 );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Impact( Vector3 position, Vector3 normal, string soundPath, int decal )
	{
		ShowImpact( position, normal, soundPath is null ? null : GameResource.Load<SoundEvent>( soundPath ), decal );
	}

	internal static void ShowImpact( Vector3 position, Vector3 normal, SoundEvent sound, int decal )
	{
		if ( sound is not null ) Sound.Play( sound, position );
		if ( decal == 0 ) return;
		ImpactDecal.Place( position, normal, $"shot{decal}", 10 );
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ) return;
		if ( Game.Random.Int( 0, 1 ) == 0 )
			NpcAudio.Play( mount.NpcAudio.Find( "weapons/ric" ), position );
		if ( mount.GetIndexedSprite( "wallpuff" ) is not { } sprite ) return;

		var go = new GameObject( false, "Bullet impact dust" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		var dust = go.AddComponent<ParticleEffect>();
		dust.Lifetime = 0.22f;
		dust.InitialScale = 2;
		go.AddComponent<ParticleSpriteRenderer>().Sprite = sprite;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 0.25f;
		go.Enabled = true;
		for ( int i = 0; i < 8; i++ )
		{
			if ( dust.Emit( position + normal, 0 ) is { } particle )
				particle.Velocity = normal * 45 + Vector3.Random * 30;
		}
	}
}
