public enum Titanfall2BoltPattern { Default, DoubleTake, Mastiff }

/// <summary>WeaponData ammo rules and explicitly ported retail fire callbacks.</summary>
public record struct Titanfall2WeaponFiring
{
	public int AmmoPerShot { get; set; }
	public int AmmoMinToFire { get; set; }
	public Titanfall2BoltPattern Pattern { get; set; }
	public float BoltSpreadHip { get; set; }
	public float BoltSpreadAds { get; set; }
	internal readonly int Cost => Math.Clamp( AmmoPerShot, 1, 250 );
	internal readonly int Minimum => Math.Max( Cost, Math.Clamp( AmmoMinToFire, 1, 250 ) );
	internal readonly bool HasPattern => Pattern is Titanfall2BoltPattern.DoubleTake or Titanfall2BoltPattern.Mastiff;
	internal readonly int Count( int configured ) => Pattern == Titanfall2BoltPattern.Mastiff ? 8
		: Math.Clamp( configured, 1, Pattern == Titanfall2BoltPattern.DoubleTake ? 3 : 12 );

	internal readonly Vector3 Bearing( Vector3 direction, int index, float zoom )
	{
		// AnglesToRight(VectorToAngles(dir)) from the retail script: keep the
		// authored plane relative to the attack direction, including pitched aim.
		var offset = Pattern switch
		{
			Titanfall2BoltPattern.DoubleTake => index switch { 0 => -0.25f, 1 => 0.25f, _ => 0f },
			Titanfall2BoltPattern.Mastiff => index switch { 0 => 0.15f, 1 => 0.3f, 2 => 0.6f, 3 => 1.2f,
				4 => -0.3f, 5 => -0.6f, 6 => -1.2f, _ => -0.15f },
			_ => 0
		};
		var spread = float.Lerp( BoltSpreadHip, BoltSpreadAds, Math.Clamp( zoom, 0, 1 ) );
		return (direction + Rotation.LookAt( direction ).Right * (offset * spread)).Normal;
	}

	internal readonly Titanfall2ProjectileSettings Flight( Titanfall2ProjectileSettings settings, float zoom, int index )
	{
		// SetReducedEffects on the third Double Take bolt: client.dll
		// 0x18050EA50 sets +0x1250 and clears the +0x124c impact table;
		// 0x180509390 / 0x18050A280 / 0x180510030 gate its trail presentation.
		if ( Pattern == Titanfall2BoltPattern.DoubleTake && index == 2 ) return settings with { ReducedEffects = true };
		// mp_weapon_mastiff.nut overrides these rather than reading WeaponData.
		return Pattern == Titanfall2BoltPattern.Mastiff ? settings with { Speed = 3000, GravityScale = 0.09f,
			Lifetime = (0.65f + Random.Shared.NextSingle() * 0.2f) * (zoom > 0.8f ? 1.25f : 1f) } : settings;
	}
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2WeaponFiring Firing { get; init; }
}

public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2WeaponFiring Firing { get; set; }
	internal int DisplayAmmo => UsesOrdnance ? OrdnanceCharges : UsesRetailCharge ? AvailableChargeAmmo : Math.Max( 0, Clip1 );
	internal int DisplayAmmoCapacity => UsesOrdnance ? OrdnanceMaximumCharges : UsesRetailCharge ? ChargeProfile.MaximumReserve : ClipMaxSize;
	bool HasRoundAmmo() => HasAmmoFor( Firing.Cost );
	bool HasAmmoFor( int amount ) => !IsHeld || !UsesAmmo || (UsesRetailCharge ? AvailableChargeAmmo
		: UsesPrimaryClip ? Clip1 : PrimaryAmmoType is not null ? Ammo1 : int.MaxValue) >= amount;
	public override bool HasPrimaryAmmo() => HasAmmoFor( _burstRemaining > 0
		|| HeatProfile.Valid && (Networking.IsHost ? _hostHeat.FiringRun : _ownerHeatRun) ? Firing.Cost : Firing.Minimum );
}
