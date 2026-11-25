public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2ChargeProfile ChargeProfile { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public int ChargeReserve { get; private set; } = -1;
	[Sync( SyncFlags.FromHost )] public Titanfall2ChargeSnapshot ChargeSnapshot { get; private set; }
	Titanfall2ChargeState _hostCharge, _predictedCharge;
	PlayerController _chargeOwner;
	bool _chargeLocalHeld, _chargeHostHeld, _dispatchingChargeShot, _chargeShotSpent;
	int _chargeInputSerial, _chargeLastInputSerial, _chargeObservedSerial;
	float _chargeZoom;
	double _chargeTick = double.NaN, _chargeViewUpdate;

	internal bool UsesRetailCharge => ChargeProfile.Valid;
	internal bool IsDispatchingChargeShot => _dispatchingChargeShot;
	internal int AvailableChargeAmmo => Math.Max( 0, ChargeReserve );
	public int ChargeSerial => ChargeSnapshot.Serial;
	public float ChargeFraction => ChargeVisible ? ObservedCharge.Fraction( ChargeProfile, Time.NowDouble ) : 0;
	public Titanfall2ChargePhase ChargePhase => ChargeVisible ? ObservedCharge.Phase( ChargeProfile, Time.NowDouble ) : Titanfall2ChargePhase.Idle;
	bool ChargeVisible => UsesRetailCharge && Active && IsHeld && IsActive && Owner.IsValid();
	Titanfall2ChargeState ObservedCharge => Networking.IsHost ? _hostCharge : !IsProxy ? _predictedCharge
		: Owner.IsValid() && ChargeSnapshot.OwnerId == Owner.GameObject.Id ? ChargeSnapshot.State : default;
	internal bool CanBeginCharge => ChargeVisible && !IsReloading && !_chargeLocalHeld && !ObservedCharge.Charging && !ObservedCharge.FiredOnPress
		&& Time.NowDouble >= ObservedCharge.ReadyAt && AvailableChargeAmmo >= Firing.Cost;

	// The generic weapon HUD obtains Ammo1 through this virtual method. The
	// reserve belongs to this weapon, as in retail, and survives changing holders.
	protected override int GetReserveAmmo( BaseAmmoResource ammoType ) => UsesRetailCharge && ammoType == PrimaryAmmoType
		? AvailableChargeAmmo : base.GetReserveAmmo( ammoType );

	void InitializeCharge()
	{
		if ( !UsesRetailCharge ) return;
		UsesClips = false; ClipMaxSize = 0; AutoReload = false;
		if ( Networking.IsHost && ChargeReserve < 0 ) ChargeReserve = Math.Clamp( ChargeProfile.InitialReserve, 0, ChargeProfile.MaximumReserve );
	}

	void BindChargeOwner()
	{
		InitializeCharge();
		if ( _chargeOwner == Owner ) return;
		_chargeOwner = Owner; _chargeLocalHeld = _chargeHostHeld = false; _chargeLastInputSerial = _chargeInputSerial = 0;
		_hostCharge.Cancel( Time.NowDouble ); _predictedCharge.Cancel( Time.NowDouble );
		if ( Networking.IsHost ) PublishCharge();
	}

	void BeginChargeAttack()
	{
		if ( !UsesRetailCharge || IsProxy || !Active || !IsHeld || !IsActive || IsReloading ) return;
		BindChargeOwner();
		if ( !CanBeginCharge ) return;
		// Base FirePrimary installs the ordinary shot interval before this virtual
		// attack runs. Beginning charge must not create an extra one-second wait.
		SetNextPrimaryFire( 0 );
		SetChargeInput( true );
	}

	void SetChargeInput( bool held )
	{
		if ( _chargeLocalHeld == held ) return;
		_chargeLocalHeld = held;
		_chargeInputSerial = _chargeInputSerial == int.MaxValue ? 1 : _chargeInputSerial + 1;
		if ( Networking.IsHost ) ApplyChargeInput( _chargeInputSerial, held, _adsBlend );
		else
		{
			if ( held ) _predictedCharge.Begin( ChargeProfile, Time.NowDouble );
			else _predictedCharge.Release( ChargeProfile, Time.NowDouble );
			RequestChargeInput( _chargeInputSerial, held, _adsBlend );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestChargeInput( int serial, bool held, float zoom ) => ApplyChargeInput( serial, held, zoom );

	void ApplyChargeInput( int serial, bool held, float zoom )
	{
		if ( !Networking.IsHost || !UsesRetailCharge || !Owner.IsValid() ) return;
		BindChargeOwner();
		if ( serial <= _chargeLastInputSerial || !float.IsFinite( zoom ) || zoom is < 0 or > 1 ) return;
		_chargeLastInputSerial = serial; _chargeZoom = zoom;
		if ( held && (!Active || !IsActive || !IsHeld || !Owner.Active || IsReloading) )
		{
			_chargeHostHeld = false; _hostCharge.Cancel( Time.NowDouble ); PublishCharge(); return;
		}
		_chargeHostHeld = held;
		if ( !held ) _hostCharge.Release( ChargeProfile, Time.NowDouble );
		else if ( Active && IsActive && IsHeld && Owner.Active && !IsReloading && NextPrimaryFire <= 0
			&& AvailableChargeAmmo >= Firing.Cost ) _hostCharge.Begin( ChargeProfile, Time.NowDouble );
		PublishCharge();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestChargeView( int inputSerial, float zoom )
	{
		if ( !Networking.IsHost || !UsesRetailCharge || !Active || !Owner.IsValid() || !IsActive || IsReloading
			|| _chargeOwner != Owner || inputSerial != _chargeLastInputSerial || !_hostCharge.Charging
			|| !float.IsFinite( zoom ) || zoom is < 0 or > 1 ) return;
		// Aim continues to come from the host's current player eye transform. This
		// hint changes only the existing validated zoom/spread input, never time.
		_chargeZoom = zoom;
	}

	void TickCharge()
	{
		if ( !UsesRetailCharge || _chargeTick == Time.NowDouble ) return;
		_chargeTick = Time.NowDouble;
		BindChargeOwner();
		if ( !Active || !IsHeld || !IsActive || !Owner.IsValid() || !Owner.Active || IsReloading ) { CancelCharge(); return; }
		if ( !Networking.IsHost && ChargeSnapshot.Serial != _chargeObservedSerial
			&& ChargeSnapshot.OwnerId == Owner.GameObject.Id && ChargeSnapshot.InputSerial >= _chargeInputSerial )
		{
			_chargeObservedSerial = ChargeSnapshot.Serial; _predictedCharge = ChargeSnapshot.State;
		}
		if ( !IsProxy && Owner.UseInputControls && _chargeLocalHeld != Input.Down( "attack1" ) ) SetChargeInput( Input.Down( "attack1" ) );
		// Retail's semi-auto latch requires a release after every shot, but a new
		// press during recovery can remain held and start charging once ready.
		if ( Networking.IsHost && _chargeHostHeld && NextPrimaryFire <= 0 && AvailableChargeAmmo >= Firing.Cost
			&& _hostCharge.Begin( ChargeProfile, Time.NowDouble ) ) PublishCharge();
		else if ( !Networking.IsHost && !IsProxy && _chargeLocalHeld && NextPrimaryFire <= 0 && AvailableChargeAmmo >= Firing.Cost )
			_predictedCharge.Begin( ChargeProfile, Time.NowDouble );
		if ( !IsProxy && _chargeLocalHeld && Time.NowDouble >= _chargeViewUpdate )
		{
			_chargeViewUpdate = Time.NowDouble + 0.1;
			if ( Networking.IsHost ) _chargeZoom = _adsBlend;
			else RequestChargeView( _chargeInputSerial, _adsBlend );
		}
		if ( !Networking.IsHost || !_hostCharge.Charging || _hostCharge.Fraction( ChargeProfile, Time.NowDouble ) < 1 ) return;
		if ( AvailableChargeAmmo < Firing.Cost ) { CancelCharge(); return; }
		var eye = Owner.EyeTransform;
		if ( !eye.Position.IsFinite || !eye.Forward.IsFinite ) { CancelCharge(); return; }
		_dispatchingChargeShot = true; _chargeShotSpent = false;
		try { ResolveHitscan( new Ray( eye.Position, eye.Forward ), _chargeZoom ); }
		finally
		{
			_dispatchingChargeShot = false;
			if ( _chargeShotSpent )
			{
				_hostCharge.AcceptedShot( Time.NowDouble, PrimaryDelay );
				SetNextPrimaryFire( PrimaryDelay ); PublishCharge();
			}
			else CancelCharge();
		}
	}

	bool TryTakeChargeAmmo( int amount )
	{
		if ( !Networking.IsHost || !_dispatchingChargeShot || !UsesRetailCharge || amount <= 0 || AvailableChargeAmmo < amount ) return false;
		if ( !ChargeProfile.InfiniteReserve ) ChargeReserve -= amount;
		_chargeShotSpent = true;
		return true;
	}

	void CancelCharge()
	{
		if ( !UsesRetailCharge ) return;
		var changed = _hostCharge.Charging || _hostCharge.InitialFraction != 0 || _hostCharge.FiredOnPress;
		if ( !Networking.IsHost && !IsProxy && _chargeLocalHeld ) SetChargeInput( false );
		_chargeLocalHeld = _chargeHostHeld = false;
		_hostCharge.Cancel( Time.NowDouble ); _predictedCharge.Cancel( Time.NowDouble );
		if ( Networking.IsHost && changed ) PublishCharge();
	}

	void PublishCharge()
	{
		ChargeSnapshot = new()
		{
			OwnerId = Owner.IsValid() ? Owner.GameObject.Id : Guid.Empty,
			Serial = ChargeSnapshot.Serial == int.MaxValue ? 1 : ChargeSnapshot.Serial + 1,
			InputSerial = _chargeLastInputSerial, State = _hostCharge
		};
	}
}
