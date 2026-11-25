namespace GoldSrc.HalfLife;

[Title( "Mini Turret" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeMiniTurret" )]
[NpcDefinition( "monster_miniturret", "miniturret", MonsterClass.Machine, 40, 16, 32, 0, Attack = NpcAttack.Bullet, Damage = 5, Range = 1200, Movement = NpcMovement.Stationary, AttackSequence = "fire", IdleSequence = "idle_off" )]
public sealed class MiniTurret : AutomatedTurret
{
}
