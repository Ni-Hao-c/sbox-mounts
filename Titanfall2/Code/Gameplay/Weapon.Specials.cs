using Sandbox.Npcs;

public sealed partial class Titanfall2Weapon
{
	[Property] public bool SmartLockEnabled { get; set; }
	public float SmartLockProgress { get; private set; }
	GameObject _smartTarget;
	static Vector3 LockAimPoint( GameObject target ) => target.GetComponent<BaseNpc>() is { } npc
		? Vector3.Lerp( target.WorldPosition, npc.EyePosition, 0.8f ) : target.GetBounds().Center;

	public override Ray AimRay
	{
		get
		{
			var aim = base.AimRay;
			return SmartLockEnabled && SmartLockProgress >= 1f && CanSmartLock( _smartTarget, aim )
				? new Ray( aim.Position, (LockAimPoint( _smartTarget ) - aim.Position).Normal ) : aim;
		}
	}

	void UpdateSmartLock()
	{
		if ( !SmartLockEnabled || IsProxy || !IsHeld || !IsActive || IsReloading || IsAiming )
		{
			_smartTarget = null;
			SmartLockProgress = 0;
			return;
		}
		var aim = base.AimRay;
		var best = Scene.GetAllComponents<BaseNpc>().Where( n => n.Health > 0 && n.GetDisposition( Owner.GameObject ) == Disposition.Hostile )
			.Select( n => n.GameObject ).Where( go => CanSmartLock( go, aim ) )
			.OrderByDescending( go => Vector3.Dot( aim.Forward, (LockAimPoint( go ) - aim.Position).Normal ) ).FirstOrDefault();
		if ( best != _smartTarget ) SmartLockProgress = 0;
		_smartTarget = best;
		SmartLockProgress = best.IsValid() ? Math.Clamp( SmartLockProgress + Time.Delta / 0.6f, 0, 1 ) : 0;
	}

	bool CanSmartLock( GameObject target, Ray aim )
	{
		if ( !target.IsValid() || target.GetComponent<BaseNpc>() is not { Health: > 0 } npc || !Owner.IsValid()
			|| npc.GetDisposition( Owner.GameObject ) != Disposition.Hostile || IsAiming ) return false;
		var delta = LockAimPoint( target ) - aim.Position;
		if ( delta.LengthSquared > 1500f * 1500f || delta.LengthSquared < 1 || Vector3.Dot( aim.Forward, delta.Normal ) < 0.94f ) return false;
		var hit = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( aim.Position, aim.Position + delta ).UseHitboxes().IgnoreGameObjectHierarchy( Owner.GameObject )
			.WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Shot );
		return !hit.StartedSolid && (!hit.Hit || hit.GameObject == target || target.IsDescendant( hit.GameObject ));
	}

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

