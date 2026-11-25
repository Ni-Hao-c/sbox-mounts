using Sandbox.Npcs;

public sealed partial class Titanfall2Weapon
{
	[Property] public bool SmartLockEnabled { get; set; }
	public override Ray AimRay => ViewKickAimRay;

	public override void SecondaryAttack()
	{
		if ( Projectile.Behavior != Titanfall2ProjectileBehavior.RemoteCharge ) { base.SecondaryAttack(); return; }
		if ( Networking.IsHost ) DetonateCharges();
		else RequestDetonation();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestDetonation() => DetonateCharges();

	void DetonateCharges()
	{
		if ( !Networking.IsHost || !IsHeld || !IsActive || Inventory?.ActiveItem != this ) return;
		foreach ( var charge in Scene.GetAllComponents<Titanfall2Projectile>().ToArray() )
			charge.DetonateOwned( GameObject, Owner.GameObject );
	}
}

