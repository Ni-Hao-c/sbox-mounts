namespace GoldSrc.HalfLife;

[Title( "Sentry" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeSentry" )]
[NpcDefinition( "monster_sentry", "sentry", MonsterClass.Machine, 40, 16, 64, 0, Attack = NpcAttack.Bullet, Damage = 4, Range = 1200, Movement = NpcMovement.Stationary, AttackSequence = "fire", IdleSequence = "idle_off" )]
public sealed class Sentry : AutomatedTurret, Component.ICollisionListener
{
	internal override float SearchDuration => float.PositiveInfinity;
	internal override float MinimumElevation => -60;
	internal override float RetractedHeight => 64;
	internal override float DeployedHeight => 64;
	internal override float GunHeight => 48;
	internal override bool CanBerserk => false;
	internal override float RetractedDamageScale => 1;

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		base.OnDamaged( damage );
		if ( Health > 0 ) Deploy();
	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		var other = collision.Other.GameObject;
		if ( Networking.IsHost && Health > 0 && other.IsValid() && (other.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is not null
			|| other.GetComponentInParent<PlayerController>() is not null) ) Deploy();
	}
}
