public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2SpinUpProfile SpinUp { get; set; }
	Titanfall2SpinUpState _ownerSpin, _hostSpin;
	PlayerController _spinOwner;
	double _ownerSpinLastShot, _hostSpinLastShot;
	float _ownerSpinLastInterval, _hostSpinLastInterval;
	public float SpinUpFraction => SpinUp.UseAds ? _adsBlend : _ownerSpin.Fraction( SpinUp, Time.NowDouble );

	void PrepareOwnerSpinShot()
	{
		if ( !SpinUp.Enabled ) return;
		RefreshSpinOwner();
		ExpireSpinGap( ref _ownerSpin, _ownerSpinLastShot, _ownerSpinLastInterval );
		_ownerSpin.Start( SpinUp, Time.NowDouble );
		PrimaryDelay = SpinUp.Interval( SpinUpFraction, PrimaryDelay );
		_ownerSpinLastShot = Time.NowDouble; _ownerSpinLastInterval = PrimaryDelay;
		// Base FirePrimary set its timer before calling the virtual PrimaryAttack.
		SetNextPrimaryFire( PrimaryDelay );
	}

	float AcceptHostSpinShot( float zoom )
	{
		if ( !SpinUp.Enabled ) return PrimaryDelay;
		RefreshSpinOwner();
		ExpireSpinGap( ref _hostSpin, _hostSpinLastShot, _hostSpinLastInterval );
		_hostSpin.Start( SpinUp, Time.NowDouble );
		var interval = SpinUp.Interval( SpinUp.UseAds ? zoom : _hostSpin.Fraction( SpinUp, Time.NowDouble ), PrimaryDelay );
		_hostSpinLastShot = Time.NowDouble; _hostSpinLastInterval = interval;
		return interval;
	}

	void ExpireSpinGap( ref Titanfall2SpinUpState state, double lastShot, float interval )
	{
		// A missing stop message cannot bank spin indefinitely. This tolerance is only
		// for detecting a broken firing stream; it never grants an extra shot/deadline.
		if ( state.Firing && Time.NowDouble - lastShot > interval + Math.Max( 0.12, interval * 0.5 ) )
			state.Stop( SpinUp, lastShot + interval );
	}

	void RefreshSpinOwner()
	{
		if ( _spinOwner == Owner ) return;
		_spinOwner = Owner; _ownerSpin = default; _hostSpin = default;
	}

	void TickSpinUp()
	{
		if ( !SpinUp.Enabled ) return;
		RefreshSpinOwner();
		if ( !IsHeld || !IsActive || IsReloading || !HasRoundAmmo() ) { StopSpinUp(); return; }
		if ( !IsProxy && Owner is { UseInputControls: true } && !Input.Down( "attack1" ) ) StopSpinUp();
		if ( !IsProxy ) PrimaryDelay = SpinUp.Interval( SpinUpFraction, PrimaryDelay );
	}

	void StopSpinUp()
	{
		var wasFiring = _ownerSpin.Firing;
		_ownerSpin.Stop( SpinUp, Time.NowDouble );
		if ( Networking.IsHost ) _hostSpin.Stop( SpinUp, Time.NowDouble );
		else if ( wasFiring && !IsProxy ) RequestStopSpinUp();
		if ( SpinUp.Enabled && !IsProxy ) PrimaryDelay = SpinUp.Interval( SpinUpFraction, PrimaryDelay );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestStopSpinUp()
	{
		if ( !Networking.IsHost || !Owner.IsValid() ) return;
		// Only a stop notification crosses the network. Start times and fractions
		// are derived exclusively from accepted shots on the host.
		_hostSpin.Stop( SpinUp, Time.NowDouble );
	}
}
