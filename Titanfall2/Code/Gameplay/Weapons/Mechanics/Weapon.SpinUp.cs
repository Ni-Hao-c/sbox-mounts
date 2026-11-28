/// <summary>
/// Composed progressive fire-rate spin-up mechanic controller (Devotion).
/// </summary>
internal sealed class Titanfall2WeaponSpinUpController : ITitanfall2WeaponMechanic
{
	Titanfall2SpinUpState _ownerSpin;
	internal Titanfall2SpinUpState HostState;
	PlayerController _owner;
	double _ownerLastShot;
	double _hostLastShot;
	float _ownerLastInterval;
	float _hostLastInterval;

	internal float Fraction( Titanfall2Weapon w ) => w.SpinUp.UseAds ? w.AdsBlend : _ownerSpin.Fraction( w.SpinUp, Time.NowDouble );
	internal float HostFraction( Titanfall2Weapon w ) => HostState.Fraction( w.SpinUp, Time.NowDouble );

	public void OnHolster( Titanfall2Weapon w ) => Stop( w );
	public void OnReload( Titanfall2Weapon w ) => Stop( w );
	public void OnDisabled( Titanfall2Weapon w ) => Stop( w );

	internal void PrepareOwnerShot( Titanfall2Weapon w )
	{
		if ( !w.SpinUp.Enabled ) return;
		RefreshOwner( w );
		ExpireGap( w, ref _ownerSpin, _ownerLastShot, _ownerLastInterval );
		_ownerSpin.Start( w.SpinUp, Time.NowDouble );
		w.PrimaryDelay = w.SpinUp.Interval( Fraction( w ), w.PrimaryDelay );
		_ownerLastShot = Time.NowDouble; _ownerLastInterval = w.PrimaryDelay;
		// Base FirePrimary set its timer before calling the virtual PrimaryAttack.
		w.SetNextPrimaryFire( w.PrimaryDelay );
	}

	internal float AcceptHostShot( Titanfall2Weapon w, float zoom )
	{
		if ( !w.SpinUp.Enabled ) return w.PrimaryDelay;
		RefreshOwner( w );
		ExpireGap( w, ref HostState, _hostLastShot, _hostLastInterval );
		HostState.Start( w.SpinUp, Time.NowDouble );
		var interval = w.SpinUp.Interval( w.SpinUp.UseAds ? zoom : HostState.Fraction( w.SpinUp, Time.NowDouble ), w.PrimaryDelay );
		_hostLastShot = Time.NowDouble; _hostLastInterval = interval;
		return interval;
	}

	static void ExpireGap( Titanfall2Weapon w, ref Titanfall2SpinUpState state, double lastShot, float interval )
	{
		// A missing stop message cannot bank spin indefinitely. This tolerance is only
		// for detecting a broken firing stream; it never grants an extra shot/deadline.
		if ( state.Firing && Time.NowDouble - lastShot > interval + Math.Max( 0.12, interval * 0.5 ) )
			state.Stop( w.SpinUp, lastShot + interval );
	}

	void RefreshOwner( Titanfall2Weapon w )
	{
		if ( _owner == w.Owner ) return;
		_owner = w.Owner; _ownerSpin = default; HostState = default;
	}

	public void Tick( Titanfall2Weapon w )
	{
		if ( !w.SpinUp.Enabled ) return;
		RefreshOwner( w );
		if ( !w.IsHeld || !w.IsActive || w.IsReloading || !w.HasRoundAmmoInternal() ) { Stop( w ); return; }
		if ( !w.IsProxy && w.Owner is { UseInputControls: true } && !Input.Down( "attack1" ) ) Stop( w );
		if ( !w.IsProxy ) w.PrimaryDelay = w.SpinUp.Interval( Fraction( w ), w.PrimaryDelay );
	}

	internal void Stop( Titanfall2Weapon w )
	{
		var wasFiring = _ownerSpin.Firing;
		_ownerSpin.Stop( w.SpinUp, Time.NowDouble );
		if ( Networking.IsHost ) HostState.Stop( w.SpinUp, Time.NowDouble );
		else if ( wasFiring && !w.IsProxy ) w.SendStopSpinUpRpc();
		if ( w.SpinUp.Enabled && !w.IsProxy ) w.PrimaryDelay = w.SpinUp.Interval( Fraction( w ), w.PrimaryDelay );
	}

	internal void ApplyStopHost( Titanfall2Weapon w )
	{
		if ( !Networking.IsHost || !w.Owner.IsValid() ) return;
		// Only a stop notification crosses the network. Start times and fractions
		// are derived exclusively from accepted shots on the host.
		HostState.Stop( w.SpinUp, Time.NowDouble );
	}
}
