namespace GoldSrc.HalfLife;

[Hide]
public sealed class NihilanthZap : EnergyBall
{
	internal override string SpriteName => "nhth1";
	internal override string HitTag => "shock";
	internal override float SpriteSize => 128;
	internal override float LaunchSpeed => 200;
	TimeUntil _nextThink;

	internal override void PrepareLaunch()
	{
		_nextThink = 0.1f;
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( "debris/zap4.wav", WorldPosition );
	}

	protected override void OnStart()
	{
		base.OnStart();
		if ( Glow.IsValid() ) Glow.PlaybackSpeed = 2;
		var light = GameObject.AddComponent<PointLight>();
		light.Radius = 128;
		light.LightColor = new Color32( 128, 128, 255 );
		light.Shadows = false;
	}

	internal override void Guide()
	{
		if ( _nextThink > 0 ) return;
		_nextThink = 0.05f;
		if ( !Target.IsValid() )
		{
			GameObject.Destroy();
			return;
		}
		if ( Velocity.Length < 2000 ) Velocity *= 1.2f;
		var destination = Npc.TargetHullCenter( Target );
		if ( WorldPosition.Distance( destination ) >= 256 ) return;
		var trace = Scene.Trace.Ray( WorldPosition, destination ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "playercontroller" ).UseHitboxes().Run();
		DamageVictim( trace.GameObject?.GetComponentInParent<Component.IDamageable>(), trace.EndPosition, trace.Hitbox );
		EnergyBeam.Flash( WorldPosition, trace.EndPosition, new Color32( 64, 196, 255 ), "laserbeam", "Nihilanth zap" );
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( "weapons/electro4.wav", trace.EndPosition, volume: 0.5f, pitch: Game.Random.Float( 1.4f, 1.6f ) );
		GameObject.Destroy();
	}

	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
	{
		Blast.DamageInRadius( GameObject, Shooter, position + normal, 50, 125, "shock" );
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( "weapons/electro4.wav", position, pitch: Game.Random.Float( 0.9f, 0.95f ) );
	}
}
