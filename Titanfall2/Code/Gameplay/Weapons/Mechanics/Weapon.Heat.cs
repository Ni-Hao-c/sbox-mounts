/// <summary>
/// Composed continuous-heat &amp; overheat-venting mechanic controller (L-STAR).
/// </summary>
internal sealed class Titanfall2WeaponHeatController : ITitanfall2WeaponMechanic
{
	Titanfall2HeatState _hostHeat;
	bool _ownerRun;
	PlayerController _owner;
	double _hostTick = double.NaN;
	Titanfall2HeatInventory _inventory;

	internal bool IsFiringRun( Titanfall2Weapon w ) => Networking.IsHost ? _hostHeat.FiringRun : _ownerRun;

	public void OnStart( Titanfall2Weapon w ) => RegisterInventory( w );
	public void OnAdded( Titanfall2Weapon w ) => RegisterInventory( w );
	public void OnHolster( Titanfall2Weapon w ) => StopRun( w );
	public void OnReload( Titanfall2Weapon w ) => StopRun( w );
	public void OnDisabled( Titanfall2Weapon w ) => StopRun( w );

	public void OnRemoved( Titanfall2Weapon w )
	{
		StopRun( w );
		_inventory?.Remove( w );
		_inventory = null;
		_hostHeat.Recovering = false;
	}

	void RegisterInventory( Titanfall2Weapon w )
	{
		if ( !Networking.IsHost || !w.HeatProfile.Valid || !w.Inventory.IsValid() ) return;
		var pump = w.Inventory.GameObject.GetOrAddComponent<Titanfall2HeatInventory>();
		if ( _inventory == pump ) return;
		_inventory?.Remove( w ); _inventory = pump; pump.Add( w );
	}

	internal void PrepareOwnerShot( Titanfall2Weapon w )
	{
		if ( w.HeatProfile.Valid && !w.IsProxy ) _ownerRun = true;
	}

	internal bool CanAcceptShot( Titanfall2Weapon w )
	{
		if ( !w.HeatProfile.Valid ) return true;
		TickHost( w );
		return w.Active && w.IsHeld && w.IsActive && !w.IsReloading
			&& _hostHeat.CanFire( w.Clip1, w.Firing.Minimum, w.Firing.Cost, Time.NowDouble );
	}

	internal void AcceptShot( Titanfall2Weapon w )
	{
		if ( !w.HeatProfile.Valid ) return;
		if ( w.Clip1 + w.Firing.Cost == w.Firing.Minimum ) w.PlayHeatWarningInternal();
		_hostHeat.AcceptedShot( Time.NowDouble );
	}

	public void Tick( Titanfall2Weapon w )
	{
		if ( !w.HeatProfile.Valid ) return;
		RegisterInventory( w );
		if ( !w.IsProxy && w.Owner is { UseInputControls: true } && !Input.Down( "attack1" ) ) StopRun( w );
		if ( Networking.IsHost ) TickHost( w );
	}

	// The inventory pump runs even when the inventory has disabled this gun.
	internal void TickHost( Titanfall2Weapon w )
	{
		if ( !Networking.IsHost || !w.HeatProfile.Valid || _hostTick == Time.NowDouble ) return;
		_hostTick = Time.NowDouble;
		if ( _owner != w.Owner )
		{
			StopHostRun( w ); _owner = w.Owner; _ownerRun = false;
		}
		if ( !w.IsHeld || !w.Inventory.IsValid() ) { _hostHeat.Recovering = false; return; }
		w.TickHeatReloadInternal();
		if ( !w.Active || !w.IsActive || w.IsReloading ) StopHostRun( w );
		else if ( _hostHeat.FiringRun && (w.IsProxy || w.Owner is { UseInputControls: false })
			&& Time.NowDouble - _hostHeat.LastShot > w.PrimaryDelay + Math.Max( 0.12, w.PrimaryDelay * 0.5 ) )
			StopHostRun( w ); // A missing owner stop cannot retain a firing run indefinitely.
		w.Clip1 = _hostHeat.Recover( w.HeatProfile, w.Clip1, w.ClipMaxSize, w.IsReloading || w.SavedHeatReloadMilestone != 0, Time.NowDouble, Time.Delta );
		// Empty overheat starts automatically, but partial clips never auto reload.
		if ( w.Active && w.IsActive && w.Clip1 == 0 && !w.IsReloading && _hostHeat.HasFired
			&& w.NextPrimaryFireTimer <= 0 && !w.IsProjectileRequestPending ) w.Reload();
	}

	internal void StopRun( Titanfall2Weapon w )
	{
		if ( !w.HeatProfile.Valid ) return;
		var wasFiring = _ownerRun; _ownerRun = false;
		if ( Networking.IsHost ) StopHostRun( w );
		else if ( wasFiring && !w.IsProxy ) w.SendStopHeatRunRpc();
	}

	internal void ApplyStopRun( Titanfall2Weapon w )
	{
		if ( !Networking.IsHost || !w.HeatProfile.Valid || !w.Owner.IsValid() ) return;
		StopHostRun( w );
	}

	void StopHostRun( Titanfall2Weapon w )
	{
		if ( !_hostHeat.Stop( w.HeatProfile, w.Clip1, Time.NowDouble ) ) return;
		w.SetHeatVentUntilHost( _hostHeat.VentUntil );
		if ( w.Active && w.IsHeld && w.IsActive ) w.PlayHeatVentInternal();
	}
}

/// <summary>Retail owner preframe regenerates inactive inventory weapons too.</summary>
[Title( "Titanfall 2 heat recovery" ), Group( "Titanfall 2" )]
public sealed class Titanfall2HeatInventory : Component
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
			weapon.TickHeatHost();
		}
	}
}
