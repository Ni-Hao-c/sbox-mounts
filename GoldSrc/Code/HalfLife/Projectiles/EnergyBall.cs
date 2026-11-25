namespace GoldSrc.HalfLife;

[Hide]
public class EnergyBall : NpcProjectile
{
	internal override float LaunchSpeed => 800;
	internal override float Lifetime => 5;
	internal override string HitTag => "energybeam";
	internal virtual string SpriteName => "xspark4";
	internal virtual float SpriteSize => 16;
	internal SpriteRenderer Glow { get; private set; }

	protected override void OnStart()
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetIndexedSprite( SpriteName ) is not { } sprite ) return;
		Glow = GameObject.AddComponent<SpriteRenderer>();
		Glow.Sprite = sprite;
		Glow.Size = SpriteSize;
		Glow.Additive = true;
	}

	internal override void OnImpact( IDamageable victim, Vector3 position, Vector3 normal )
	{
		base.OnImpact( victim, position, normal );
		if ( victim is not null && Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( "weapons/electro4.wav", position, volume: 0.3f, pitch: Game.Random.Float( 0.9f, 0.99f ) );
	}
}
