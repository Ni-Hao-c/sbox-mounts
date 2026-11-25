namespace GoldSrc.HalfLife;

[Title( "Baby Headcrab" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeBabyHeadcrab" )]
[NpcDefinition( "monster_babycrab", "baby_headcrab", MonsterClass.AlienPrey, 2.5f, 12, 24, 110, Attack = NpcAttack.Leap, Damage = 3, Range = 180, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class BabyHeadcrab : Headcrab
{
	[Property, Hide] public Gonarch Mother { get; set; }

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		if ( Mother.IsValid() ) Mother.OnChildDied( this );
		base.OnDeath( death );
	}

	internal override float VoicePitch => Game.Random.Int( 140, 150 ) / 100f;
	internal override float VoiceVolume => 0.8f;
	internal override bool PerchedOnActor => GroundObject.IsValid() &&
		(GroundObject.GetComponentInParent<PlayerController>().IsValid() || GroundObject.GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid());
	internal override float LeapRecovery => 0;
	internal override float LeapTurnSpeed => 240;
	internal override float FacingThreshold => 0.55f;
	internal override void OnPursuitFailed( GameObject target ) => PrepareLeap( target );
	internal override void ApplyEquipment() => Renderer.Tint = Renderer.Tint.WithAlpha( 192f / 255f );
}
