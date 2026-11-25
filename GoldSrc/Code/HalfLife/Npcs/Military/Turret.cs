namespace GoldSrc.HalfLife;

[Title( "Turret" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeTurret" )]
[NpcDefinition( "monster_turret", "turret", MonsterClass.Machine, 50, 32, 48, 0, Attack = NpcAttack.Bullet, Damage = 10, Range = 1200, Movement = NpcMovement.Stationary, AttackSequence = "fire", IdleSequence = "idle_off" )]
public sealed class Turret : AutomatedTurret
{
	internal override bool HasRotaryBarrel => true;
	internal override float ShotAttenuation => 0.6f;
	internal override string ShotSound => "turret/tu_fire1.wav";
}
