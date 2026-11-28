/// <summary>
/// Composed Charge Rifle mechanic controller (hold-to-charge, client prediction, host-verified discharge).
/// </summary>
internal sealed class Titanfall2WeaponChargeController : ITitanfall2WeaponMechanic
{
	internal Titanfall2ChargeState HostState;
	Titanfall2ChargeState _predicted;
	PlayerController _owner;
	internal bool LocalHeld;
	internal bool HostHeld;
	bool _dispatchingShot;
	bool _shotSpent;
	int _inputSerial;
	int _lastInputSerial;
	int _observedSerial;
	float _zoom;
	double _tick = double.NaN;
	double _viewUpdate;

	internal bool IsDispatchingShot => _dispatchingShot;

	static bool IsVisible( Titanfall2Weapon w ) => w.UsesRetailCharge && w.Active && w.IsHeld && w.IsActive && w.Owner.IsValid();

	Titanfall2ChargeState Observed( Titanfall2Weapon w ) => Networking.IsHost ? HostState : !w.IsProxy ? _predicted
		: w.Owner.IsValid() && w.ChargeSnapshot.OwnerId == w.Owner.GameObject.Id ? w.ChargeSnapshot.State : default;

	internal float ChargeFraction( Titanfall2Weapon w ) => IsVisible( w ) ? Observed( w ).Fraction( w.ChargeProfile, Time.NowDouble ) : 0;
	internal Titanfall2ChargePhase ChargePhase( Titanfall2Weapon w ) => IsVisible( w ) ? Observed( w ).Phase( w.ChargeProfile, Time.NowDouble ) : Titanfall2ChargePhase.Idle;

	internal bool CanBegin( Titanfall2Weapon w ) => IsVisible( w ) && !w.IsReloading && !LocalHeld && !Observed( w ).Charging && !Observed( w ).FiredOnPress
		&& Time.NowDouble >= Observed( w ).ReadyAt && w.AvailableChargeAmmo >= w.Firing.Cost;

	public void OnStart( Titanfall2Weapon w ) => Initialize( w );
	public void OnAdded( Titanfall2Weapon w ) => Initialize( w );
	public void OnRemoved( Titanfall2Weapon w ) => Cancel( w );
	public void OnHolster( Titanfall2Weapon w ) => Cancel( w );
	public void OnReload( Titanfall2Weapon w ) => Cancel( w );
	public void OnDisabled( Titanfall2Weapon w ) => Cancel( w );

	void Initialize( Titanfall2Weapon w )
	{
		if ( !w.UsesRetailCharge ) return;
		w.UsesClips = false; w.ClipMaxSize = 0; w.AutoReload = false;
		if ( Networking.IsHost && w.ChargeReserve < 0 )
			w.SetChargeReserveHost( Math.Clamp( w.ChargeProfile.InitialReserve, 0, w.ChargeProfile.MaximumReserve ) );
	}

	void BindOwner( Titanfall2Weapon w )
	{
		Initialize( w );
		if ( _owner == w.Owner ) return;
		_owner = w.Owner; LocalHeld = HostHeld = false; _lastInputSerial = _inputSerial = 0;
		HostState.Cancel( Time.NowDouble ); _predicted.Cancel( Time.NowDouble );
		if ( Networking.IsHost ) Publish( w );
	}

	internal void BeginAttack( Titanfall2Weapon w )
	{
		if ( !w.UsesRetailCharge || w.IsProxy || !w.Active || !w.IsHeld || !w.IsActive || w.IsReloading ) return;
		BindOwner( w );
		if ( !CanBegin( w ) ) return;
		// Base FirePrimary installs the ordinary shot interval before this virtual
		// attack runs. Beginning charge must not create an extra one-second wait.
		w.SetNextPrimaryFire( 0 );
		SetInput( w, true );
	}

	internal void SetInput( Titanfall2Weapon w, bool held )
	{
		if ( LocalHeld == held ) return;
		LocalHeld = held;
		_inputSerial = _inputSerial == int.MaxValue ? 1 : _inputSerial + 1;
		if ( Networking.IsHost ) ApplyInput( w, _inputSerial, held, w.AdsBlend );
		else
		{
			if ( held ) _predicted.Begin( w.ChargeProfile, Time.NowDouble );
			else _predicted.Release( w.ChargeProfile, Time.NowDouble );
			w.SendChargeInputRpc( _inputSerial, held, w.AdsBlend );
		}
	}

	internal void ApplyInput( Titanfall2Weapon w, int serial, bool held, float zoom )
	{
		if ( !Networking.IsHost || !w.UsesRetailCharge || !w.Owner.IsValid() ) return;
		BindOwner( w );
		if ( serial <= _lastInputSerial || !float.IsFinite( zoom ) || zoom is < 0 or > 1 ) return;
		_lastInputSerial = serial; _zoom = zoom;
		if ( held && (!w.Active || !w.IsActive || !w.IsHeld || !w.Owner.Active || w.IsReloading) )
		{
			HostHeld = false; HostState.Cancel( Time.NowDouble ); Publish( w ); return;
		}
		HostHeld = held;
		if ( !held ) HostState.Release( w.ChargeProfile, Time.NowDouble );
		else if ( w.Active && w.IsActive && w.IsHeld && w.Owner.Active && !w.IsReloading && w.NextPrimaryFireTimer <= 0
			&& w.AvailableChargeAmmo >= w.Firing.Cost ) HostState.Begin( w.ChargeProfile, Time.NowDouble );
		Publish( w );
	}

	internal void ApplyView( Titanfall2Weapon w, int inputSerial, float zoom )
	{
		if ( !Networking.IsHost || !w.UsesRetailCharge || !w.Active || !w.Owner.IsValid() || !w.IsActive || w.IsReloading
			|| _owner != w.Owner || inputSerial != _lastInputSerial || !HostState.Charging
			|| !float.IsFinite( zoom ) || zoom is < 0 or > 1 ) return;
		// Aim continues to come from the host's current player eye transform. This
		// hint changes only the existing validated zoom/spread input, never time.
		_zoom = zoom;
	}

	public void Tick( Titanfall2Weapon w )
	{
		if ( !w.UsesRetailCharge || _tick == Time.NowDouble ) return;
		_tick = Time.NowDouble;
		BindOwner( w );
		if ( !w.Active || !w.IsHeld || !w.IsActive || !w.Owner.IsValid() || !w.Owner.Active || w.IsReloading ) { Cancel( w ); return; }
		if ( !Networking.IsHost && w.ChargeSnapshot.Serial != _observedSerial
			&& w.ChargeSnapshot.OwnerId == w.Owner.GameObject.Id && w.ChargeSnapshot.InputSerial >= _inputSerial )
		{
			_observedSerial = w.ChargeSnapshot.Serial; _predicted = w.ChargeSnapshot.State;
		}
		if ( !w.IsProxy && w.Owner.UseInputControls && LocalHeld != Input.Down( "attack1" ) ) SetInput( w, Input.Down( "attack1" ) );
		// Retail's semi-auto latch requires a release after every shot, but a new
		// press during recovery can remain held and start charging once ready.
		if ( Networking.IsHost && HostHeld && w.NextPrimaryFireTimer <= 0 && w.AvailableChargeAmmo >= w.Firing.Cost
			&& HostState.Begin( w.ChargeProfile, Time.NowDouble ) ) Publish( w );
		else if ( !Networking.IsHost && !w.IsProxy && LocalHeld && w.NextPrimaryFireTimer <= 0 && w.AvailableChargeAmmo >= w.Firing.Cost )
			_predicted.Begin( w.ChargeProfile, Time.NowDouble );
		if ( !w.IsProxy && LocalHeld && Time.NowDouble >= _viewUpdate )
		{
			_viewUpdate = Time.NowDouble + 0.1;
			if ( Networking.IsHost ) _zoom = w.AdsBlend;
			else w.SendChargeViewRpc( _inputSerial, w.AdsBlend );
		}
		if ( !Networking.IsHost || !HostState.Charging || HostState.Fraction( w.ChargeProfile, Time.NowDouble ) < 1 ) return;
		if ( w.AvailableChargeAmmo < w.Firing.Cost ) { Cancel( w ); return; }
		var eye = w.Owner.EyeTransform;
		if ( !eye.Position.IsFinite || !eye.Forward.IsFinite ) { Cancel( w ); return; }
		_dispatchingShot = true; _shotSpent = false;
		try { w.ResolveHitscanInternal( new Ray( eye.Position, eye.Forward ), _zoom ); }
		finally
		{
			_dispatchingShot = false;
			if ( _shotSpent )
			{
				HostState.AcceptedShot( Time.NowDouble, w.PrimaryDelay );
				w.SetNextPrimaryFire( w.PrimaryDelay ); Publish( w );
			}
			else Cancel( w );
		}
	}

	internal bool TryTakeAmmo( Titanfall2Weapon w, int amount )
	{
		if ( !Networking.IsHost || !_dispatchingShot || !w.UsesRetailCharge || amount <= 0 || w.AvailableChargeAmmo < amount ) return false;
		if ( !w.ChargeProfile.InfiniteReserve ) w.SetChargeReserveHost( w.ChargeReserve - amount );
		_shotSpent = true;
		return true;
	}

	internal void Cancel( Titanfall2Weapon w )
	{
		if ( !w.UsesRetailCharge ) return;
		var changed = HostState.Charging || HostState.InitialFraction != 0 || HostState.FiredOnPress;
		if ( !Networking.IsHost && !w.IsProxy && LocalHeld ) SetInput( w, false );
		LocalHeld = HostHeld = false;
		HostState.Cancel( Time.NowDouble ); _predicted.Cancel( Time.NowDouble );
		if ( Networking.IsHost && changed ) Publish( w );
	}

	void Publish( Titanfall2Weapon w )
	{
		w.SetChargeSnapshotHost( new()
		{
			OwnerId = w.Owner.IsValid() ? w.Owner.GameObject.Id : Guid.Empty,
			Serial = w.ChargeSnapshot.Serial == int.MaxValue ? 1 : w.ChargeSnapshot.Serial + 1,
			InputSerial = _lastInputSerial, State = HostState
		} );
	}
}
