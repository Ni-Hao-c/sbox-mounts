public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2HeatProfile HeatProfile { get; set; }
	[Sync( SyncFlags.FromHost )] public double HeatVentUntil { get; private set; }
	public bool HeatVenting => HeatProfile.Valid && Time.NowDouble < HeatVentUntil;
	Titanfall2HeatState _hostHeat;
	bool _ownerHeatRun;
	PlayerController _heatOwner;
	double _heatHostTick = double.NaN;
	Titanfall2HeatInventory _heatInventory;

	void RegisterHeatInventory()
	{
		if ( !Networking.IsHost || !HeatProfile.Valid || !Inventory.IsValid() ) return;
		var pump = Inventory.GameObject.GetOrAddComponent<Titanfall2HeatInventory>();
		if ( _heatInventory == pump ) return;
		_heatInventory?.Remove( this ); _heatInventory = pump; pump.Add( this );
	}

	void PrepareOwnerHeatShot()
	{
		if ( HeatProfile.Valid && !IsProxy ) _ownerHeatRun = true;
	}

	bool CanAcceptHeatShot()
	{
		if ( !HeatProfile.Valid ) return true;
		TickHeatHost();
		return Active && IsHeld && IsActive && !IsReloading
			&& _hostHeat.CanFire( Clip1, Firing.Minimum, Firing.Cost, Time.NowDouble );
	}

	void AcceptHeatShot()
	{
		if ( !HeatProfile.Valid ) return;
		if ( Clip1 + Firing.Cost == Firing.Minimum ) PlayHeatWarning();
		_hostHeat.AcceptedShot( Time.NowDouble );
	}

	public override void DryFire()
	{
		// The minimum-to-start gate waits for recovery; it is not an empty
		// magazine and must not introduce the base dry-fire/reload delay.
		if ( HeatProfile.Valid && Clip1 > 0 ) return;
		base.DryFire();
	}

	void TickHeat()
	{
		if ( !HeatProfile.Valid ) return;
		RegisterHeatInventory();
		if ( !IsProxy && Owner is { UseInputControls: true } && !Input.Down( "attack1" ) ) StopHeatRun();
		if ( Networking.IsHost ) TickHeatHost();
	}

	// The inventory pump runs even when the inventory has disabled this gun.
	internal void TickHeatHost()
	{
		if ( !Networking.IsHost || !HeatProfile.Valid || _heatHostTick == Time.NowDouble ) return;
		_heatHostTick = Time.NowDouble;
		if ( _heatOwner != Owner )
		{
			StopHostHeatRun(); _heatOwner = Owner; _ownerHeatRun = false;
		}
		if ( !IsHeld || !Inventory.IsValid() ) { _hostHeat.Recovering = false; return; }
		TickHeatReload();
		if ( !Active || !IsActive || IsReloading ) StopHostHeatRun();
		else if ( _hostHeat.FiringRun && (IsProxy || Owner is { UseInputControls: false })
			&& Time.NowDouble - _hostHeat.LastShot > PrimaryDelay + Math.Max( 0.12, PrimaryDelay * 0.5 ) )
			StopHostHeatRun(); // A missing owner stop cannot retain a firing run indefinitely.
		Clip1 = _hostHeat.Recover( HeatProfile, Clip1, ClipMaxSize, IsReloading || SavedHeatReloadMilestone != 0, Time.NowDouble, Time.Delta );
		// Empty overheat starts automatically, but partial clips never auto reload.
		if ( Active && IsActive && Clip1 == 0 && !IsReloading && _hostHeat.HasFired
			&& NextPrimaryFire <= 0 && _nextProjectileRequest <= 0 ) Reload();
	}

	void StopHeatRun()
	{
		if ( !HeatProfile.Valid ) return;
		var wasFiring = _ownerHeatRun; _ownerHeatRun = false;
		if ( Networking.IsHost ) StopHostHeatRun();
		else if ( wasFiring && !IsProxy ) RequestStopHeatRun();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestStopHeatRun()
	{
		if ( !Networking.IsHost || !HeatProfile.Valid || !Owner.IsValid() ) return;
		StopHostHeatRun();
	}

	void StopHostHeatRun()
	{
		if ( !_hostHeat.Stop( HeatProfile, Clip1, Time.NowDouble ) ) return;
		HeatVentUntil = _hostHeat.VentUntil;
		if ( Active && IsHeld && IsActive ) PlayHeatVent();
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
