using Sandbox.Npcs;

public sealed partial class Titanfall2Weapon
{
	[Sync( SyncFlags.FromHost )] internal bool DetonationBusy { get; private set; }
	PlayerController _detonationOwner;
	double _detonationAt;
	double _detonationEndsAt;
	bool _detonationPending;

	[Property] public bool SmartLockEnabled { get; set; }
	public override Ray AimRay => ViewKickAimRay;

	public override bool CanSecondaryAttack() => base.CanSecondaryAttack()
		&& (Projectile.Behavior != Titanfall2ProjectileBehavior.RemoteCharge || !DetonationBusy);

	public override void SecondaryAttack()
	{
		if ( Projectile.Behavior != Titanfall2ProjectileBehavior.RemoteCharge ) { base.SecondaryAttack(); return; }
		if ( IsProxy ) return;
		if ( Networking.IsHost ) BeginDetonationHost();
		else RequestDetonation();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestDetonation() => BeginDetonationHost();

	void BeginDetonationHost()
	{
		var owner = Owner;
		if ( !Networking.IsHost || !CanDetonate( owner ) || DetonationBusy || _ordnance.AwaitingRelease( this ) ) return;
		if ( !OrdnanceProfile.HasDetonationTiming )
		{
			// An older serialized weapon may not have imported model timing.
			DetonateCharges( owner );
			return;
		}

		var start = Time.NowDouble;
		_detonationOwner = owner;
		_detonationPending = DetonationBusy = true;
		_detonationAt = start + OrdnanceProfile.DetonateSeconds * OrdnanceProfile.DetonateCycle;
		_detonationEndsAt = start + OrdnanceProfile.DetonateSeconds;
		PlayDetonation( OrdnanceProfile.DetonateSeconds, start );
	}

	internal void TickDetonationHost()
	{
		if ( !Networking.IsHost || !DetonationBusy ) return;
		if ( !CanDetonate( _detonationOwner ) )
		{
			CancelDetonationHost();
			BroadcastOrdnanceCancel();
			return;
		}
		if ( _detonationPending && Time.NowDouble >= _detonationAt )
		{
			_detonationPending = false;
			DetonateCharges( _detonationOwner );
		}
		if ( Time.NowDouble >= _detonationEndsAt ) CancelDetonationHost();
	}

	internal void CancelDetonationHost()
	{
		DetonationBusy = _detonationPending = false;
		_detonationOwner = null;
	}

	bool CanDetonate( PlayerController owner ) => Projectile.Behavior == Titanfall2ProjectileBehavior.RemoteCharge
		&& Active && IsActive && !IsReloading && owner.IsValid() && owner.Active && owner == Owner;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayDetonation( float seconds, double startedAt )
	{
		if ( IsActive && WeaponModel is Titanfall2PilotViewModel view )
			view.PlayOrdnanceAction( Titanfall2PilotViewModel.Detonate, seconds, (float)Math.Max( 0, Time.NowDouble - startedAt ) );
	}

	void DetonateCharges( PlayerController owner )
	{
		foreach ( var charge in Scene.GetAllComponents<Titanfall2Projectile>().ToArray() )
			charge.DetonateOwned( GameObject, owner.GameObject );
	}
}

