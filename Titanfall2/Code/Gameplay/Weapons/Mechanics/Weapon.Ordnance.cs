/// <summary>
/// Composed Pilot ordnance cooking, toss timing, drop-on-cancel, and charge recovery controller.
/// </summary>
internal sealed class Titanfall2WeaponOrdnanceController : ITitanfall2WeaponMechanic
{
	Titanfall2OrdnanceRecovery _recovery;
	Titanfall2OrdnanceProfile _pendingProfile;
	Titanfall2ProjectileSettings _pendingFlight;
	Titanfall2OrdnanceInventory _inventory;
	PlayerController _owner;
	bool _localHeld;
	bool _hostHeld;
	bool _released;
	bool _launched;
	bool _tossPresented;
	int _inputSerial;
	int _lastInputSerial;
	int _cost;
	double _preparedAt;
	double _tossAt;
	double _launchAt;
	double _hostTick = double.NaN;

	internal bool CanBegin( Titanfall2Weapon w ) => w.OrdnanceProfile.Valid && w.Projectile.Enabled && w.Active && w.IsHeld && w.IsActive
		&& !w.IsReloading && !w.OrdnanceBusy && !w.DetonationBusy && !_localHeld && Time.NowDouble >= w.OrdnanceReadyAt && w.Clip1 >= w.Firing.Minimum;
	internal bool AwaitingRelease( Titanfall2Weapon w ) => w.OrdnanceBusy && !_launched;

	public void OnStart( Titanfall2Weapon w ) => RegisterInventory( w );
	public void OnAdded( Titanfall2Weapon w ) => RegisterInventory( w );
	public void OnHolster( Titanfall2Weapon w ) => Cancel( w );
	public void OnDisabled( Titanfall2Weapon w ) => Cancel( w );

	public void OnRemoved( Titanfall2Weapon w )
	{
		Cancel( w );
		_inventory?.Remove( w );
		_inventory = null;
		_recovery.Recovering = false;
	}

	void RegisterInventory( Titanfall2Weapon w )
	{
		if ( !w.UsesOrdnance ) return;
		w.AutoReload = false;
		if ( !Networking.IsHost || !w.Inventory.IsValid() ) return;
		var pump = w.Inventory.GameObject.GetOrAddComponent<Titanfall2OrdnanceInventory>();
		if ( _inventory == pump ) return;
		_inventory?.Remove( w ); _inventory = pump; pump.Add( w );
	}

	internal void BeginAttack( Titanfall2Weapon w )
	{
		if ( w.IsProxy || !CanBegin( w ) ) return;
		BindOwner( w );
		// Base FirePrimary's generic shot delay must not delay prep or cook.
		w.SetNextPrimaryFire( 0 );
		SetInput( w, true );
	}

	internal void SetInput( Titanfall2Weapon w, bool held )
	{
		if ( !w.UsesOrdnance || w.IsProxy || _localHeld == held ) return;
		_localHeld = held;
		_inputSerial = _inputSerial == int.MaxValue ? 1 : _inputSerial + 1;
		if ( Networking.IsHost ) ApplyInput( w, _inputSerial, held );
		else w.SendOrdnanceInputRpc( _inputSerial, held );
	}

	internal void ApplyInput( Titanfall2Weapon w, int serial, bool held )
	{
		if ( !Networking.IsHost || !w.OrdnanceProfile.Valid || !w.Owner.IsValid() ) return;
		BindOwner( w );
		if ( serial <= _lastInputSerial ) return;
		_lastInputSerial = serial;
		if ( !held )
		{
			_hostHeld = false;
			if ( w.OrdnanceBusy && !_released ) ScheduleToss( w, Time.NowDouble );
			return;
		}
		if ( !w.Active || !w.IsHeld || !w.IsActive || w.Inventory?.ActiveItem != w || !w.Owner.Active || w.IsReloading
			|| w.OrdnanceBusy || w.DetonationBusy || _hostHeld || w.NextPrimaryFireTimer > 0 || Time.NowDouble < w.OrdnanceReadyAt || w.Clip1 < w.Firing.Minimum ) return;
		_hostHeld = true; w.SetOrdnanceBusyHost( true );
		_released = _launched = _tossPresented = false;
		_preparedAt = Time.NowDouble;
		_tossAt = _launchAt = double.PositiveInfinity;
		_cost = w.Firing.Cost; _pendingProfile = w.OrdnanceProfile; _pendingFlight = w.Projectile;
		if ( _pendingFlight.Behavior == Titanfall2ProjectileBehavior.RemoteCharge )
			w.BroadcastOrdnancePrep( _pendingProfile.PulloutSeconds );
		w.BroadcastOrdnanceSound( false );
	}

	void BindOwner( Titanfall2Weapon w )
	{
		if ( _owner == w.Owner ) return;
		if ( Networking.IsHost ) CancelHost( w );
		_owner = w.Owner;
		_localHeld = _hostHeld = false;
		_lastInputSerial = _inputSerial = 0;
	}

	void ScheduleToss( Titanfall2Weapon w, double now )
	{
		if ( _released ) return;
		_released = true;
		_tossAt = Math.Max( now, _preparedAt + _pendingProfile.PulloutSeconds );
		_launchAt = _tossAt + _pendingProfile.ThrowSeconds * _pendingProfile.ReleaseCycle;
		w.SetOrdnanceReadyAtHost( _tossAt + Math.Max( w.PrimaryDelay, _pendingProfile.ThrowSeconds * _pendingProfile.ReadyCycle ) );
	}

	public void Tick( Titanfall2Weapon w )
	{
		if ( !w.UsesOrdnance )
		{
			if ( Networking.IsHost && w.DetonationBusy ) w.TickDetonationHost();
			return;
		}
		RegisterInventory( w );
		if ( !w.IsProxy && _localHeld && w.Owner is { UseInputControls: true } && !Input.Down( "attack1" ) ) SetInput( w, false );
		if ( Networking.IsHost ) TickHost( w );
	}

	internal void TickHost( Titanfall2Weapon w )
	{
		if ( !Networking.IsHost || !w.UsesOrdnance || _hostTick == Time.NowDouble ) return;
		_hostTick = Time.NowDouble;
		BindOwner( w );
		w.TickDetonationHost();
		if ( w.OrdnanceBusy )
		{
			if ( !w.Active || !w.IsHeld || !w.IsActive || !w.Owner.IsValid() || !w.Owner.Active || w.IsReloading ) CancelHost( w );
			else
			{
				if ( !_released && _pendingProfile.MaxCookSeconds > 0
					&& Time.NowDouble >= _preparedAt + _pendingProfile.MaxCookSeconds )
					ScheduleToss( w, _preparedAt + _pendingProfile.MaxCookSeconds );
				if ( _released && !_tossPresented && Time.NowDouble >= _tossAt )
				{
					_tossPresented = true;
					w.BroadcastOrdnanceToss( _pendingProfile.ThrowSeconds, _tossAt );
				}
				if ( _released && !_launched && Time.NowDouble >= _launchAt ) Commit( w, false );
				if ( _launched && Time.NowDouble >= w.OrdnanceReadyAt ) w.SetOrdnanceBusyHost( false );
			}
		}
		if ( w.IsHeld && w.Inventory.IsValid() )
			w.Clip1 = _recovery.Recover( w.OrdnanceProfile, w.Clip1, w.ClipMaxSize, Time.NowDouble, Time.Delta );
		else _recovery.Recovering = false;
	}

	void Commit( Titanfall2Weapon w, bool drop )
	{
		if ( !Networking.IsHost || _launched || !_owner.IsValid() || w.Clip1 < _cost ) return;
		var owner = _owner;
		var aim = owner.EyeTransform.ForwardRay;
		if ( !aim.Position.IsFinite || !aim.Forward.IsFinite ) return;
		var direction = _pendingProfile.ThrowDirection( aim.Forward );
		// Satchel's script offsets its release point 15 units forward; other
		// offhands use the attack position. Sweep this offset so it cannot cross cover.
		var origin = aim.Position;
		if ( _pendingFlight.Behavior == Titanfall2ProjectileBehavior.RemoteCharge )
		{
			var clearance = Titanfall2CollisionQueries.Run( w.Scene.Trace.Ray( origin, origin + aim.Forward * 15 )
				.Radius( Math.Clamp( _pendingFlight.Radius, 0, 12 ) ).IgnoreGameObjectHierarchy( owner.GameObject )
				.IgnoreGameObjectHierarchy( w.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.Grenade );
			if ( !clearance.StartedSolid ) origin = clearance.EndPosition;
		}
		var settings = _pendingFlight with { FuseSeconds = _pendingProfile.RemainingFuse( _preparedAt, Time.NowDouble ) };
		var shot = Titanfall2Projectile.Launch( w.Scene, new Ray( origin, direction ), settings,
			w.Ballistics.Damage, w.TitanDamage, owner.GameObject, w.GameObject, owner.GameObject, w.DamageProfile );
		if ( !shot.IsValid() ) return;
		shot.SetHostVelocity( direction * settings.Speed * (drop ? 0.2f : 1) + owner.Velocity * _pendingProfile.InheritVelocity );
		w.Clip1 -= _cost;
		_launched = true;
		_recovery.Used( _released ? _tossAt : Time.NowDouble );
		w.AdvanceOrdnanceThrowSerialHost();
		w.ResetShootTimer();
		if ( !drop ) w.SetNextPrimaryFire( (float)Math.Max( 0, w.OrdnanceReadyAt - Time.NowDouble ) );
		w.BroadcastOrdnanceSound( true );
	}

	internal void Cancel( Titanfall2Weapon w )
	{
		_localHeld = false;
		if ( Networking.IsHost ) CancelHost( w );
		else if ( w.UsesOrdnance && !w.IsProxy ) w.SendCancelOrdnanceRpc();
	}

	internal void CancelHost( Titanfall2Weapon w )
	{
		if ( !Networking.IsHost ) return;
		var wasPlaying = (w.OrdnanceBusy && _pendingFlight.Behavior == Titanfall2ProjectileBehavior.RemoteCharge) || w.DetonationBusy;
		if ( w.OrdnanceBusy && !_launched && _pendingProfile.DropOnCancel ) Commit( w, true );
		w.CancelDetonationHost();
		w.SetOrdnanceBusyHost( false ); _hostHeld = false;
		_released = _launched = _tossPresented = false;
		if ( wasPlaying ) w.BroadcastOrdnanceCancel();
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
