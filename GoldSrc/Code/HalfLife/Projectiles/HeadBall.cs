namespace GoldSrc.HalfLife;

[Hide]
public sealed class HeadBall : EnergyBall
{
	internal override float SpriteSize => 64;
	internal override float LaunchSpeed => 32;
	internal override float Lifetime => 3.9f;
	internal override string HitTag => "shock";
	internal override void PrepareLaunch()
	{
		Damage = 25;
		_nextHunt = 0.1f;
	}
	TimeUntil _nextHunt;
	bool _discharged;
	PointLight _light;

	protected override void OnStart()
	{
		base.OnStart();
		_light = GameObject.AddComponent<PointLight>();
		_light.Shadows = false;
	}

	protected override void OnUpdate()
	{
		var brightness = (255 - (float)Age * 50).Clamp( 0, 255 );
		if ( Glow.IsValid() ) Glow.Color = Color.White.WithAlpha( brightness / 255 );
		if ( _light.IsValid() ) _light.Radius = brightness / 16;
	}

	internal override void Guide()
	{
		if ( _nextHunt > 0 ) return;
		if ( _discharged )
		{
			GameObject.Destroy();
			return;
		}
		_nextHunt = 0.1f;
		if ( !Target.IsValid() || !Shooter.IsValid() )
		{
			GameObject.Destroy();
			return;
		}
		var center = Npc.TargetHullCenter( Target );
		var offset = center - WorldPosition;
		Velocity = Velocity.Normal * System.MathF.Min( Velocity.Length, 400 ) + offset.Normal * 100;
		if ( offset.Length >= 64 ) return;
		var trace = Scene.Trace.Ray( WorldPosition, center ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		var victim = trace.GameObject?.GetComponentInParent<Component.IDamageable>();
		DamageVictim( victim, trace.EndPosition, trace.Hitbox );
		EnergyBeam.Flash( WorldPosition, trace.EndPosition, Color.White, "laserbeam", "Controller zap" );
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( "weapons/electro4.wav", trace.EndPosition, 0.5f, Game.Random.Int( 140, 160 ) / 100f );
		_discharged = true;
		_nextHunt = 0.3f;
	}

	internal override bool Bounce( SceneTraceResult hit )
	{
		Velocity -= hit.Normal * (2 * Vector3.Dot( hit.Normal, Velocity ));
		WorldPosition = hit.EndPosition + hit.Normal;
		return true;
	}
}
