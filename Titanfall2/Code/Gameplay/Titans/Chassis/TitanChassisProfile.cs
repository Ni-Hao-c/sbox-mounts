using Chassis = Titanfall2TitanKit.Chassis;

/// <summary>
/// Unified declarative profile for a Titan chassis, replacing scattered switches across
/// TitanKit, TitanCore, and TitanWeaponAbilityData.
/// </summary>
internal sealed record Titanfall2TitanChassisProfile
{
	public Chassis Kit { get; init; }
	public int MaxDashCharges { get; init; } = 1;
	public string OffenseName { get; init; } = "Rocket Salvo";
	public string DefenseName { get; init; } = "Energy Siphon";
	public string MeleeName { get; init; } = "Titan Melee";
	public string OffenseScript { get; init; }
	public string DefenseScript { get; init; }
	public float OffenseFallbackSpeed { get; init; } = 2200f;
	public Titanfall2ProjectileBehavior OffenseBehavior { get; init; } = Titanfall2ProjectileBehavior.Standard;
	public string CoreName { get; init; } = "Core Unavailable";
	public bool CoreSupported { get; init; }
	public float CoreBuildSeconds { get; init; } = 150f;
	public float CoreDuration { get; init; }
	public float CoreChargeup { get; init; }
	public bool CoreBlocksPrimary { get; init; }
	public bool CoreUnlimitedAmmo { get; init; }
}

internal static class Titanfall2TitanChassisCatalog
{
	const string OverlayFile = "titans.chassis.json";

	static readonly Titanfall2TitanChassisProfile DefaultProfile = new();

	static readonly Dictionary<Chassis, Titanfall2TitanChassisProfile> Defaults = new()
	{
		[Chassis.Monarch] = new()
		{
			Kit = Chassis.Monarch,
			MaxDashCharges = 1,
			OffenseName = "Rocket Salvo",
			DefenseName = "Energy Siphon",
			OffenseScript = "mp_titanweapon_salvo_rockets",
			DefenseScript = "mp_titanweapon_stun_laser",
			OffenseFallbackSpeed = 2200f,
			CoreName = "Upgrade Core",
			CoreSupported = true,
			CoreBuildSeconds = 100f,
			CoreDuration = 0.5f,
			CoreChargeup = 0f
		},
		[Chassis.Ion] = new()
		{
			Kit = Chassis.Ion,
			MaxDashCharges = 1,
			OffenseName = "Laser Shot",
			DefenseName = "Vortex Guard",
			OffenseScript = "mp_titanweapon_laser_lite",
			OffenseFallbackSpeed = 2200f,
			CoreName = "Laser Core",
			CoreSupported = true,
			CoreBuildSeconds = 150f,
			CoreDuration = 3.5f,
			CoreChargeup = 1.1f,
			CoreBlocksPrimary = true
		},
		[Chassis.Legion] = new()
		{
			Kit = Chassis.Legion,
			MaxDashCharges = 1,
			OffenseName = "Power Shot",
			DefenseName = "Gun Shield",
			CoreName = "Smart Core",
			CoreSupported = true,
			CoreBuildSeconds = 150f,
			CoreDuration = 12f,
			CoreChargeup = 0f,
			CoreUnlimitedAmmo = true
		},
		[Chassis.Tone] = new()
		{
			Kit = Chassis.Tone,
			MaxDashCharges = 1,
			OffenseName = "Tracker Rockets",
			DefenseName = "Particle Guard",
			OffenseScript = "mp_titanweapon_tracker_rockets",
			OffenseFallbackSpeed = 1800f,
			OffenseBehavior = Titanfall2ProjectileBehavior.HomingTitan,
			CoreName = "Salvo Core",
			CoreSupported = true,
			CoreBuildSeconds = 150f,
			CoreDuration = 2f,
			CoreChargeup = 0.5f,
			CoreBlocksPrimary = true
		},
		[Chassis.Scorch] = new()
		{
			Kit = Chassis.Scorch,
			MaxDashCharges = 1,
			OffenseName = "Firewall",
			DefenseName = "Thermal Shield",
			CoreName = "Flame Core",
			CoreSupported = true,
			CoreBuildSeconds = 150f,
			CoreDuration = 0.5f,
			CoreChargeup = 0.75f,
			CoreBlocksPrimary = true
		},
		[Chassis.Northstar] = new()
		{
			Kit = Chassis.Northstar,
			MaxDashCharges = 2,
			OffenseName = "Cluster Missile",
			DefenseName = "Tether Trap",
			OffenseScript = "mp_titanweapon_dumbfire_rockets",
			OffenseFallbackSpeed = 3500f,
			OffenseBehavior = Titanfall2ProjectileBehavior.ClusterBurst,
			CoreName = "Flight Core",
			CoreSupported = true,
			CoreBuildSeconds = 150f,
			CoreDuration = 6f,
			CoreChargeup = 0.4f,
			CoreBlocksPrimary = true
		},
		[Chassis.Ronin] = new()
		{
			Kit = Chassis.Ronin,
			MaxDashCharges = 2,
			OffenseName = "Arc Wave",
			DefenseName = "Sword Block",
			MeleeName = "Broad Sword",
			CoreName = "Sword Core",
			CoreSupported = true,
			CoreBuildSeconds = 120f,
			CoreDuration = 12f,
			CoreChargeup = 0.75f,
			CoreBlocksPrimary = true
		}
	};

	static Dictionary<Chassis, Titanfall2TitanChassisProfile> _active = Defaults;

	internal static void Reload( Titanfall2ContentSnapshot snapshot )
	{
		if ( !Titanfall2RetailScriptReader.TryReadJsonOverlay<List<Titanfall2TitanChassisProfile>>( snapshot, OverlayFile, out var entries ) || entries.Count == 0 )
		{
			_active = Defaults;
			return;
		}
		var merged = new Dictionary<Chassis, Titanfall2TitanChassisProfile>( Defaults );
		foreach ( var entry in entries )
		{
			if ( entry is not null && entry.Kit != Chassis.None ) merged[entry.Kit] = entry;
		}
		_active = merged;
	}

	internal static void Reset() => _active = Defaults;

	internal static Titanfall2TitanChassisProfile Get( Chassis kit ) =>
		_active.TryGetValue( kit, out var profile ) ? profile : DefaultProfile;
}
