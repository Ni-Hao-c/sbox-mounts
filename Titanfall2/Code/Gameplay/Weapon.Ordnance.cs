public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2OrdnanceProfile OrdnanceProfile { get; set; }
	[Sync( SyncFlags.FromHost )] public bool OrdnanceBusy { get; private set; }
	[Sync( SyncFlags.FromHost )] public int OrdnanceThrowSerial { get; private set; }
	[Sync( SyncFlags.FromHost )] public double OrdnanceReadyAt { get; private set; }
	internal bool UsesOrdnance => OrdnanceProfile.Enabled;
	internal bool CanBeginOrdnance => OrdnanceProfile.Valid && Projectile.Enabled && Active && IsHeld && IsActive
		&& !IsReloading && !OrdnanceBusy && !_ordnanceLocalHeld && Time.NowDouble >= OrdnanceReadyAt && Clip1 >= Firing.Minimum;
	internal int OrdnanceCharges => Math.Max( 0, Clip1 ) / Firing.Cost;
	internal int OrdnanceMaximumCharges => Math.Max( 0, ClipMaxSize ) / Firing.Cost;
	Titanfall2OrdnanceRecovery _ordnanceRecovery;
	Titanfall2OrdnanceProfile _pendingOrdnance;
	Titanfall2ProjectileSettings _pendingOrdnanceFlight;
	Titanfall2OrdnanceInventory _ordnanceInventory;
	PlayerController _ordnanceOwner;
	bool _ordnanceLocalHeld, _ordnanceHostHeld, _ordnanceReleased, _ordnanceLaunched, _ordnanceTossPresented;
	int _ordnanceInputSerial, _ordnanceLastInputSerial, _ordnanceCost;
	double _ordnancePreparedAt, _ordnanceTossAt, _ordnanceLaunchAt, _ordnanceHostTick = double.NaN;

	void RegisterOrdnanceInventory()
	{
		if ( !UsesOrdnance ) return;
		AutoReload = false;
		if ( !Networking.IsHost || !Inventory.IsValid() ) return;
		var pump = Inventory.GameObject.GetOrAddComponent<Titanfall2OrdnanceInventory>();
		if ( _ordnanceInventory == pump ) return;
		_ordnanceInventory?.Remove( this ); _ordnanceInventory = pump; pump.Add( this );
	}

	void BeginOrdnanceAttack()
	{
		if ( IsProxy || !CanBeginOrdnance ) return;
		BindOrdnanceOwner();
		// Base FirePrimary's generic shot delay must not delay prep or cook.
		SetNextPrimaryFire( 0 );
		SetOrdnanceInput( true );
	}

	internal void ReleaseOrdnance() => SetOrdnanceInput( false );
	void SetOrdnanceInput( bool held )
	{
		if ( !UsesOrdnance || IsProxy || _ordnanceLocalHeld == held ) return;
		_ordnanceLocalHeld = held;
		_ordnanceInputSerial = _ordnanceInputSerial == int.MaxValue ? 1 : _ordnanceInputSerial + 1;
		if ( Networking.IsHost ) ApplyOrdnanceInput( _ordnanceInputSerial, held );
		else RequestOrdnanceInput( _ordnanceInputSerial, held );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestOrdnanceInput( int serial, bool held ) => ApplyOrdnanceInput( serial, held );
	void ApplyOrdnanceInput( int serial, bool held )
	{
		if ( !Networking.IsHost || !OrdnanceProfile.Valid || !Owner.IsValid() ) return;
		BindOrdnanceOwner();
		if ( serial <= _ordnanceLastInputSerial ) return;
		_ordnanceLastInputSerial = serial;
		if ( !held )
		{
			_ordnanceHostHeld = false;
			if ( OrdnanceBusy && !_ordnanceReleased ) ScheduleOrdnanceToss( Time.NowDouble );
			return;
		}
		if ( !Active || !IsHeld || !IsActive || Inventory?.ActiveItem != this || !Owner.Active || IsReloading
			|| OrdnanceBusy || _ordnanceHostHeld || NextPrimaryFire > 0 || Time.NowDouble < OrdnanceReadyAt || Clip1 < Firing.Minimum ) return;
		_ordnanceHostHeld = true; OrdnanceBusy = true;
		_ordnanceReleased = _ordnanceLaunched = _ordnanceTossPresented = false;
		_ordnancePreparedAt = Time.NowDouble;
		_ordnanceTossAt = _ordnanceLaunchAt = double.PositiveInfinity;
		_ordnanceCost = Firing.Cost; _pendingOrdnance = OrdnanceProfile; _pendingOrdnanceFlight = Projectile;
		PlayOrdnanceSound( false );
	}

	void BindOrdnanceOwner()
	{
		if ( _ordnanceOwner == Owner ) return;
		if ( Networking.IsHost ) CancelHostOrdnance();
		_ordnanceOwner = Owner;
		_ordnanceLocalHeld = _ordnanceHostHeld = false;
		_ordnanceLastInputSerial = _ordnanceInputSerial = 0;
	}

	void ScheduleOrdnanceToss( double now )
	{
		if ( _ordnanceReleased ) return;
		_ordnanceReleased = true;
		_ordnanceTossAt = Math.Max( now, _ordnancePreparedAt + _pendingOrdnance.PulloutSeconds );
		_ordnanceLaunchAt = _ordnanceTossAt + _pendingOrdnance.ThrowSeconds * _pendingOrdnance.ReleaseCycle;
		OrdnanceReadyAt = _ordnanceTossAt + Math.Max( PrimaryDelay, _pendingOrdnance.ThrowSeconds * _pendingOrdnance.ReadyCycle );
	}

	void TickOrdnance()
	{
		if ( !UsesOrdnance ) return;
		RegisterOrdnanceInventory();
		if ( !IsProxy && _ordnanceLocalHeld && Owner is { UseInputControls: true } && !Input.Down( "attack1" ) ) ReleaseOrdnance();
		if ( Networking.IsHost ) TickOrdnanceHost();
	}

	internal void TickOrdnanceHost()
	{
		if ( !Networking.IsHost || !UsesOrdnance || _ordnanceHostTick == Time.NowDouble ) return;
		_ordnanceHostTick = Time.NowDouble;
		BindOrdnanceOwner();
		if ( OrdnanceBusy )
		{
			if ( !Active || !IsHeld || !IsActive || !Owner.IsValid() || !Owner.Active || IsReloading ) CancelHostOrdnance();
			else
			{
				if ( !_ordnanceReleased && _pendingOrdnance.MaxCookSeconds > 0
					&& Time.NowDouble >= _ordnancePreparedAt + _pendingOrdnance.MaxCookSeconds )
					ScheduleOrdnanceToss( _ordnancePreparedAt + _pendingOrdnance.MaxCookSeconds );
				if ( _ordnanceReleased && !_ordnanceTossPresented && Time.NowDouble >= _ordnanceTossAt )
				{
					_ordnanceTossPresented = true;
					PlayOrdnanceToss();
				}
				if ( _ordnanceReleased && !_ordnanceLaunched && Time.NowDouble >= _ordnanceLaunchAt ) CommitOrdnance( false );
				if ( _ordnanceLaunched && Time.NowDouble >= OrdnanceReadyAt ) OrdnanceBusy = false;
			}
		}
		if ( IsHeld && Inventory.IsValid() )
			Clip1 = _ordnanceRecovery.Recover( OrdnanceProfile, Clip1, ClipMaxSize, Time.NowDouble, Time.Delta );
		else _ordnanceRecovery.Recovering = false;
	}

	void CommitOrdnance( bool drop )
	{
		if ( !Networking.IsHost || _ordnanceLaunched || !_ordnanceOwner.IsValid() || Clip1 < _ordnanceCost ) return;
		var owner = _ordnanceOwner;
		var aim = owner.EyeTransform.ForwardRay;
		if ( !aim.Position.IsFinite || !aim.Forward.IsFinite ) return;
		var direction = _pendingOrdnance.ThrowDirection( aim.Forward );
		// Satchel's script offsets its release point 15 units forward; other
		// offhands use the attack position. Sweep this offset so it cannot cross cover.
		var origin = aim.Position;
		if ( _pendingOrdnanceFlight.Behavior == Titanfall2ProjectileBehavior.RemoteCharge )
		{
			var clearance = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( origin, origin + aim.Forward * 15 )
				.Radius( Math.Clamp( _pendingOrdnanceFlight.Radius, 0, 12 ) ).IgnoreGameObjectHierarchy( owner.GameObject )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
			if ( !clearance.StartedSolid ) origin = clearance.EndPosition;
		}
		var settings = _pendingOrdnanceFlight with { FuseSeconds = _pendingOrdnance.RemainingFuse( _ordnancePreparedAt, Time.NowDouble ) };
		var shot = Titanfall2Projectile.Launch( Scene, new Ray( origin, direction ), settings,
			Ballistics.Damage, TitanDamage, owner.GameObject, GameObject, owner.GameObject, DamageProfile );
		if ( !shot.IsValid() ) return;
		shot.SetHostVelocity( direction * settings.Speed * (drop ? 0.2f : 1) + owner.Velocity * _pendingOrdnance.InheritVelocity );
		Clip1 -= _ordnanceCost;
		_ordnanceLaunched = true;
		_ordnanceRecovery.Used( _ordnanceReleased ? _ordnanceTossAt : Time.NowDouble );
		OrdnanceThrowSerial = OrdnanceThrowSerial == int.MaxValue ? 1 : OrdnanceThrowSerial + 1;
		TimeSinceShoot = 0;
		if ( !drop ) SetNextPrimaryFire( (float)Math.Max( 0, OrdnanceReadyAt - Time.NowDouble ) );
		PlayOrdnanceSound( true );
	}

	void CancelOrdnance()
	{
		_ordnanceLocalHeld = false;
		if ( Networking.IsHost ) CancelHostOrdnance();
		else if ( UsesOrdnance && !IsProxy ) RequestCancelOrdnance();
	}
	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestCancelOrdnance() { if ( Networking.IsHost ) CancelHostOrdnance(); }
	void CancelHostOrdnance()
	{
		if ( !Networking.IsHost ) return;
		if ( OrdnanceBusy && !_ordnanceLaunched && _pendingOrdnance.DropOnCancel ) CommitOrdnance( true );
		OrdnanceBusy = false; _ordnanceHostHeld = false;
		_ordnanceReleased = _ordnanceLaunched = _ordnanceTossPresented = false;
	}
	void RemoveOrdnanceInventory()
	{
		CancelOrdnance(); _ordnanceInventory?.Remove( this ); _ordnanceInventory = null;
		_ordnanceRecovery.Recovering = false;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayOrdnanceToss() => WeaponModel?.OnAttack();
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayOrdnanceSound( bool thrown )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer ) return;
		var firstPerson = StateEffectsFirstPerson;
		var sound = thrown ? firstPerson ? OrdnanceProfile.ThrowSound1P : OrdnanceProfile.ThrowSound3P
			: firstPerson ? OrdnanceProfile.PrepSound1P : OrdnanceProfile.PrepSound3P;
		if ( firstPerson ) Titanfall2GameplayVisuals.PlayViewSound( Scene, MountIdent, sound, WorldPosition );
		else Titanfall2GameplayVisuals.PlayFireSound( Scene, MountIdent, sound, WorldPosition );
	}
}

[Title( "Titanfall 2 ordnance recovery" ), Group( "Titanfall 2" )]
public sealed class Titanfall2OrdnanceInventory : Component
{
	readonly List<Titanfall2Weapon> _weapons = [];
	internal void Add( Titanfall2Weapon weapon ) { if ( !_weapons.Contains( weapon ) ) _weapons.Add( weapon ); }
	internal void Remove( Titanfall2Weapon weapon ) => _weapons.Remove( weapon );
	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;
		for ( var i = _weapons.Count - 1; i >= 0; i-- )
		{
			var weapon = _weapons[i];
			if ( !weapon.IsValid() || weapon.Inventory?.GameObject != GameObject ) { _weapons.RemoveAt( i ); continue; }
			weapon.TickOrdnanceHost();
		}
	}
}
