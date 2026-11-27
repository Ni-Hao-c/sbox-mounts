using Chassis = Titanfall2TitanKit.Chassis;

internal enum Titanfall2WeaponCatalogKind
{
	PilotHitscan,
	PilotProjectile,
	AntiTitan,
	PilotOrdnance,
	Integrated,
	TitanHitscan,
	TitanProjectile
}

/// <summary>Declarative weapon registration record (JSON-serializable for mod/user overlays).</summary>
internal sealed record Titanfall2WeaponCatalogEntry
{
	public string Key { get; init; }
	public string ScriptName { get; init; }
	public string Title { get; init; }
	public Titanfall2WeaponCatalogKind Kind { get; init; }
	public string FallbackSample { get; init; }
	public bool Projectile { get; init; }
	public float FallbackSpeed { get; init; }
	public float[] TintRgb { get; init; }
	public Titanfall2ProjectileBehavior Behavior { get; init; } = Titanfall2ProjectileBehavior.Standard;

	internal bool IsTitan => Kind is Titanfall2WeaponCatalogKind.TitanHitscan or Titanfall2WeaponCatalogKind.TitanProjectile;
	internal bool IsIntegrated => Kind == Titanfall2WeaponCatalogKind.Integrated;
	internal bool IsOrdnance => Kind == Titanfall2WeaponCatalogKind.PilotOrdnance;
	internal bool IsAntiTitan => Kind == Titanfall2WeaponCatalogKind.AntiTitan;
	internal Color? Tint => TintRgb is { Length: >= 3 } rgb ? new Color( rgb[0], rgb[1], rgb[2] ) : null;
}

/// <summary>Declarative NPC and Titan actor registration record (JSON-serializable for overlays).</summary>
internal sealed record Titanfall2ActorCatalogEntry
{
	public string Id { get; init; }
	public string Title { get; init; }
	public string Model { get; init; }
	public string Weapon { get; init; }
	public float Health { get; init; }
	public float Speed { get; init; }
	public string Faction { get; init; } = "imc";
	public float Height { get; init; } = 72;
	public float Radius { get; init; } = 14;
	public float Shield { get; init; }
	public bool Titan { get; init; }
	public Titanfall2Npc.Navigation Navigation { get; init; } = Titanfall2Npc.Navigation.Ground;
	public float MeleeDamage { get; init; }
	public Titanfall2Npc.FrontierRole Role { get; init; } = Titanfall2Npc.FrontierRole.None;
	public string VoiceProfile { get; init; }
	public Chassis Chassis { get; init; } = Chassis.None;
	public string ProjectileTrailOverride { get; init; }
}

/// <summary>
/// Built-in retail gameplay catalog tables with optional JSON overlay support
/// (weapons.catalog.json / actors.catalog.json).
/// </summary>
internal static class Titanfall2GameplayCatalogData
{
	const string WeaponsOverlayFile = "weapons.catalog.json";
	const string ActorsOverlayFile = "actors.catalog.json";

	// Retail R2 has no titan_arc_rifle model. The FD Arc chassis uses the Ronin gun.
	internal const string ArcCannonWorldModel = "models/weapons/titan_triple_threat/w_titan_triple_threat.mdl";

	static Titanfall2WeaponCatalogEntry Pilot( string id, string title, string sample = null ) => new()
	{
		Key = id, ScriptName = "mp_weapon_" + id, Title = title, Kind = Titanfall2WeaponCatalogKind.PilotHitscan, FallbackSample = sample
	};

	static Titanfall2WeaponCatalogEntry PilotProj( string id, string title, float r, float g, float b, float fallbackSpeed = 0, Titanfall2ProjectileBehavior behavior = Titanfall2ProjectileBehavior.Standard ) => new()
	{
		Key = id, ScriptName = "mp_weapon_" + id, Title = title, Kind = Titanfall2WeaponCatalogKind.PilotProjectile,
		Projectile = true, FallbackSpeed = fallbackSpeed, TintRgb = [r, g, b], Behavior = behavior
	};

	static Titanfall2WeaponCatalogEntry AntiTitan( string id, string title, bool projectile, float fallbackSpeed, float r, float g, float b, Titanfall2ProjectileBehavior behavior = Titanfall2ProjectileBehavior.Standard ) => new()
	{
		Key = id, ScriptName = "mp_weapon_" + id, Title = title, Kind = Titanfall2WeaponCatalogKind.AntiTitan,
		Projectile = projectile, FallbackSpeed = fallbackSpeed, TintRgb = [r, g, b], Behavior = behavior
	};

	static Titanfall2WeaponCatalogEntry Ordnance( string id, string title, float r, float g, float b, Titanfall2ProjectileBehavior behavior ) => new()
	{
		Key = id, ScriptName = "mp_weapon_" + id, Title = title, Kind = Titanfall2WeaponCatalogKind.PilotOrdnance,
		Projectile = true, FallbackSpeed = 1100, TintRgb = [r, g, b], Behavior = behavior
	};

	static Titanfall2WeaponCatalogEntry Integrated( string key, string scriptName, float fallbackSpeed, float[] tintRgb = null ) => new()
	{
		Key = key, ScriptName = scriptName, Kind = Titanfall2WeaponCatalogKind.Integrated,
		Projectile = true, FallbackSpeed = fallbackSpeed, TintRgb = tintRgb
	};

	static Titanfall2WeaponCatalogEntry TitanHitscan( string id ) => new()
	{
		Key = "mp_titanweapon_" + id, ScriptName = "mp_titanweapon_" + id, Kind = Titanfall2WeaponCatalogKind.TitanHitscan
	};

	static Titanfall2WeaponCatalogEntry TitanProj( string id, float speed, float r, float g, float b, Titanfall2ProjectileBehavior behavior = Titanfall2ProjectileBehavior.Standard ) => new()
	{
		Key = "mp_titanweapon_" + id, ScriptName = "mp_titanweapon_" + id, Kind = Titanfall2WeaponCatalogKind.TitanProjectile,
		Projectile = true, FallbackSpeed = speed, TintRgb = [r, g, b], Behavior = behavior
	};

	internal static readonly Titanfall2WeaponCatalogEntry[] DefaultWeapons =
	[
		Integrated( "mortar:rocketstream", "mp_titanweapon_rocketeer_rocketstream", 2500 ),
		// Pilot hitscan (Miles event names are not always wave keys; verified fallback samples where needed).
		Pilot( "rspn101", "R-201 Carbine", "wpn_r101_3p_wpnfire_firstshot_close_2ch_v4_01" ),
		Pilot( "car", "CAR" ),
		Pilot( "r97", "R-97", "wpn_r97_3p_wpnfire_firstshotwtail_close_2ch_v1_01" ),
		Pilot( "vinson", "V-47 Flatline", "wpn_vinson_3p_wpnfire_loop_close_2ch_v1_01" ),
		Pilot( "g2", "G2A5", "wpn_g2a4_3p_wpnfire_shot_close_2ch_v2_01" ),
		Pilot( "semipistol", "Hammond P2016", "wpn_2011pistol_3p_wpnfire_shot_close_2ch_v1_01" ),
		Pilot( "autopistol", "RE-45 Auto", "wpn_re45_3p_wpnfire_firstshot_mid_2ch_v1_01" ),
		Pilot( "alternator_smg", "Alternator" ),
		Pilot( "hemlok_smg", "Volt" ),
		Pilot( "lmg", "Spitfire" ),
		Pilot( "esaw", "Devotion" ),
		Pilot( "dmr", "Longbow-DMR" ),
		Pilot( "rspn101_og", "R-101 Carbine", "wpn_r101_3p_wpnfire_firstshot_close_2ch_v4_01" ),
		Pilot( "wingman", "B3 Wingman" ),
		Pilot( "shotgun", "EVA-8 Auto" ),
		Pilot( "hemlok", "Hemlok BF-R" ),
		Pilot( "smart_pistol", "Smart Pistol MK6" ),
		// Titan hitscan
		TitanHitscan( "xo16_shorty" ),
		TitanHitscan( "xo16_vanguard" ),
		TitanHitscan( "predator_cannon" ),
		// Titan projectile
		TitanProj( "particle_accelerator", 7000, 1f, 0.25f, 0.12f ),
		TitanProj( "leadwall", 5000, 1f, 0.75f, 0.3f ),
		TitanProj( "sniper", 10000, 0.4f, 0.8f, 1f ),
		TitanProj( "sticky_40mm", 5000, 1f, 0.65f, 0.2f ),
		TitanProj( "meteor", 2200, 1f, 0.3f, 0.05f, Titanfall2ProjectileBehavior.ThermiteBurn ),
		// Pilot projectile
		PilotProj( "epg", "EPG-1", 0.25f, 0.65f, 1f ),
		PilotProj( "lstar", "L-STAR", 1f, 0.2f, 0.15f ),
		PilotProj( "sniper", "Kraber-AP Sniper", 1f, 0.88f, 0.55f ),
		PilotProj( "smr", "Sidewinder SMR", 1f, 0.55f, 0.25f ),
		PilotProj( "doubletake", "D-2 Double Take", 0.55f, 0.85f, 1f ),
		PilotProj( "wingman_n", "Wingman Elite", 1f, 0.85f, 0.55f ),
		PilotProj( "softball", "R-6P Softball", 1f, 0.6f, 0.2f, 2500 ),
		PilotProj( "pulse_lmg", "EM-4 Cold War", 0.3f, 0.78f, 1f, 3500 ),
		PilotProj( "mastiff", "Mastiff", 1f, 0.45f, 0.15f, 4500 ),
		PilotProj( "shotgun_pistol", "SA-3 Mozambique", 1f, 0.65f, 0.25f, 4500 ),
		// Anti-Titan
		AntiTitan( "defender", "Charge Rifle", false, 0, 1f, 0.55f, 0.12f ),
		AntiTitan( "mgl", "MGL Mag Launcher", true, 1200, 1f, 0.65f, 0.2f ),
		AntiTitan( "rocket_launcher", "Archer", true, 1350, 1f, 0.45f, 0.15f, Titanfall2ProjectileBehavior.HomingTitan ),
		AntiTitan( "arc_launcher", "LG-97 Thunderbolt", true, 550, 0.3f, 0.72f, 1f, Titanfall2ProjectileBehavior.ArcZap ),
		// Pilot ordnance
		Ordnance( "frag_grenade", "Frag Grenade", 1f, 0.6f, 0.2f, Titanfall2ProjectileBehavior.FragGrenade ),
		Ordnance( "grenade_emp", "Arc Grenade", 0.35f, 0.75f, 1f, Titanfall2ProjectileBehavior.ArcGrenade ),
		Ordnance( "thermite_grenade", "Firestar", 1f, 0.35f, 0.08f, Titanfall2ProjectileBehavior.ThermiteBurn ),
		Ordnance( "grenade_gravity", "Gravity Star", 0.45f, 0.55f, 1f, Titanfall2ProjectileBehavior.GravityPull ),
		Ordnance( "grenade_electric_smoke", "Electric Smoke Grenade", 0.45f, 0.75f, 1f, Titanfall2ProjectileBehavior.ElectricSmoke ),
		Ordnance( "satchel", "Satchel Charge", 1f, 0.35f, 0.1f, Titanfall2ProjectileBehavior.RemoteCharge ),
		// Integrated NPC weapons
		Integrated( "integrated:super_spectre", "mp_weapon_super_spectre", 1400, [0.35f, 0.65f, 1f] ),
		Integrated( "integrated:turretplasma", "mp_weapon_turretplasma", 2000, [1f, 0.38f, 0.15f] ),
		Integrated( "integrated:droneplasma", "mp_weapon_droneplasma", 1100, [0.45f, 0.8f, 1f] )
	];

	internal static readonly Titanfall2ActorCatalogEntry[] DefaultActors =
	[
		new() { Id = "npc_soldier", Title = "IMC Grunt", Model = "models/humans/grunts/imc_grunt_rifle.mdl", Weapon = "rspn101", Health = 100, Speed = 155, VoiceProfile = "imc_grunt" },
		new() { Id = "npc_spectre", Title = "Spectre", Model = "models/robots/spectre/imc_spectre.mdl", Weapon = "car", Health = 150, Speed = 135, VoiceProfile = "spectre" },
		new() { Id = "npc_stalker", Title = "Stalker", Model = "models/robots/stalker/robot_stalker.mdl", Weapon = "rspn101", Health = 240, Speed = 105, VoiceProfile = "spectre" },
		new() { Id = "npc_militia_grunt", Title = "Militia Grunt", Model = "models/humans/grunts/mlt_grunt_rifle.mdl", Weapon = "rspn101", Health = 100, Speed = 155, Faction = "militia", VoiceProfile = "militia_grunt" },
		new() { Id = "npc_imc_heavy", Title = "IMC Heavy Grunt", Model = "models/humans/grunts/imc_grunt_lmg.mdl", Weapon = "lmg", Health = 160, Speed = 115, VoiceProfile = "imc_grunt" },
		new() { Id = "npc_reaper", Title = "Reaper", Model = "models/robots/super_spectre/super_spectre_v1.mdl", Weapon = "integrated:super_spectre", Health = 650, Speed = 135, Height = 136, Radius = 28, ProjectileTrailOverride = "P_plasma_proj_MD_DLight" },
		new() { Id = "npc_marvin", Title = "MRVN", Model = "models/robots/marvin/marvin.mdl", Health = 85, Speed = 0, Faction = "neutral" },
		new() { Id = "npc_plasma_turret", Title = "Plasma Turret", Model = "models/robots/turret_plasma/turret_plasma.mdl", Weapon = "integrated:turretplasma", Health = 350, Speed = 0, Height = 54, Radius = 25, Navigation = Titanfall2Npc.Navigation.Stationary },
		new() { Id = "npc_attack_drone", Title = "Attack Drone", Model = "models/robots/drone_air_attack/drone_air_attack_plasma.mdl", Weapon = "integrated:droneplasma", Health = 120, Speed = 240, Height = 36, Radius = 23, Navigation = Titanfall2Npc.Navigation.Hover, VoiceProfile = "drone" },
		new() { Id = "npc_prowler", Title = "Prowler", Model = "models/creatures/prowler/r2_prowler.mdl", Health = 180, Speed = 235, Faction = "wildlife", Height = 52, Radius = 24, MeleeDamage = 36 },
		new() { Id = "npc_imc_smg", Title = "IMC SMG Grunt", Model = "models/humans/grunts/imc_grunt_smg.mdl", Weapon = "r97", Health = 100, Speed = 175, VoiceProfile = "imc_grunt" },
		new() { Id = "npc_imc_shotgun", Title = "IMC Shotgun Grunt", Model = "models/humans/grunts/imc_grunt_shotgun.mdl", Weapon = "shotgun", Health = 140, Speed = 160, VoiceProfile = "imc_grunt" },
		new() { Id = "npc_imc_captain", Title = "IMC Shield Captain", Model = "models/humans/grunts/imc_grunt_shield_captain.mdl", Weapon = "lmg", Health = 220, Speed = 125, Shield = 100, VoiceProfile = "imc_grunt" },
		new() { Id = "npc_militia_heavy", Title = "Militia Heavy Grunt", Model = "models/humans/grunts/mlt_grunt_lmg.mdl", Weapon = "lmg", Health = 160, Speed = 115, Faction = "militia", VoiceProfile = "militia_grunt" },
		new() { Id = "npc_militia_smg", Title = "Militia SMG Grunt", Model = "models/humans/grunts/mlt_grunt_smg.mdl", Weapon = "car", Health = 100, Speed = 175, Faction = "militia", VoiceProfile = "militia_grunt" },
		new() { Id = "npc_militia_shotgun", Title = "Militia Shotgun Grunt", Model = "models/humans/grunts/mlt_grunt_shotgun.mdl", Weapon = "shotgun", Health = 140, Speed = 160, Faction = "militia", VoiceProfile = "militia_grunt" },
		new() { Id = "npc_stalker_red", Title = "Red Stalker", Model = "models/robots/stalker/robot_stalker_red.mdl", Weapon = "lstar", Health = 320, Speed = 125, VoiceProfile = "spectre" },
		new() { Id = "npc_imc_pilot", Title = "IMC Pilot", Model = "models/humans/pilots/pilot_medium_stalker_m.mdl", Weapon = "car", Health = 250, Speed = 240 },
		new() { Id = "npc_militia_pilot", Title = "Militia Pilot", Model = "models/humans/pilots/pilot_medium_stalker_f.mdl", Weapon = "rspn101", Health = 250, Speed = 240, Faction = "militia" },
		new() { Id = "npc_corporate_spectre", Title = "Corporate Spectre", Model = "models/robots/spectre/spectre_corporate.mdl", Weapon = "alternator_smg", Health = 180, Speed = 150 },
		new() { Id = "npc_mossy_stalker", Title = "Overgrown Stalker", Model = "models/robots/stalker/robot_stalker_mossy.mdl", Weapon = "shotgun", Health = 280, Speed = 120 },
		new() { Id = "npc_militia_spectre", Title = "Militia Spectre", Model = "models/robots/spectre/imc_spectre.mdl", Weapon = "car", Health = 180, Speed = 155, Faction = "militia" },
		new() { Id = "npc_militia_stalker", Title = "Militia Stalker", Model = "models/robots/stalker/robot_stalker.mdl", Weapon = "shotgun", Health = 280, Speed = 120, Faction = "militia" },
		new() { Id = "npc_mossy_marvin", Title = "Overgrown MRVN", Model = "models/robots/marvin/marvin_mossy.mdl", Health = 85, Speed = 0, Faction = "neutral" },
		// Drivable Titans
		new() { Id = "monarch", Title = "Monarch", Model = "models/titans/medium/titan_medium_vanguard.mdl", Weapon = "mp_titanweapon_xo16_shorty", Health = 10000, Speed = 200, Height = 284, Radius = 54, Shield = 1600, Titan = true, VoiceProfile = "titan", Chassis = Chassis.Monarch },
		new() { Id = "bt7274", Title = "BT-7274", Model = "models/titans/buddy/titan_buddy.mdl", Weapon = "mp_titanweapon_xo16_vanguard", Health = 11000, Speed = 190, Faction = "militia", Height = 284, Radius = 54, Shield = 1750, Titan = true, VoiceProfile = "bt7274", Chassis = Chassis.Monarch },
		new() { Id = "legion", Title = "Legion", Model = "models/titans/heavy/titan_heavy_deadbolt.mdl", Weapon = "mp_titanweapon_predator_cannon", Health = 12500, Speed = 150, Height = 296, Radius = 61, Shield = 2200, Titan = true, VoiceProfile = "titan", Chassis = Chassis.Legion },
		new() { Id = "ion", Title = "Ion", Model = "models/titans/medium/titan_medium_wraith.mdl", Weapon = "mp_titanweapon_particle_accelerator", Health = 10000, Speed = 195, Height = 284, Radius = 54, Shield = 1600, Titan = true, VoiceProfile = "titan", Chassis = Chassis.Ion },
		new() { Id = "tone", Title = "Tone", Model = "models/titans/medium/titan_medium_ajax.mdl", Weapon = "mp_titanweapon_sticky_40mm", Health = 10000, Speed = 185, Height = 284, Radius = 54, Shield = 1600, Titan = true, VoiceProfile = "titan", Chassis = Chassis.Tone },
		new() { Id = "scorch", Title = "Scorch", Model = "models/titans/heavy/titan_heavy_ogre.mdl", Weapon = "mp_titanweapon_meteor", Health = 12500, Speed = 145, Height = 296, Radius = 61, Shield = 2200, Titan = true, VoiceProfile = "titan", Chassis = Chassis.Scorch },
		new() { Id = "northstar", Title = "Northstar", Model = "models/titans/light/titan_light_raptor.mdl", Weapon = "mp_titanweapon_sniper", Health = 7500, Speed = 220, Height = 273, Radius = 49, Shield = 1500, Titan = true, VoiceProfile = "titan", Chassis = Chassis.Northstar },
		new() { Id = "ronin", Title = "Ronin", Model = "models/titans/light/titan_light_locust.mdl", Weapon = "mp_titanweapon_leadwall", Health = 7500, Speed = 235, Height = 273, Radius = 49, Shield = 1500, Titan = true, VoiceProfile = "titan", Chassis = Chassis.Ronin },
		// Frontier Defense Titans
		new() { Id = "arc_titan", Title = "Arc Titan", Model = "models/titans/light/titan_light_locust.mdl", Health = 7500, Speed = 218, Height = 273, Radius = 49, Shield = 1500, Titan = true, Role = Titanfall2Npc.FrontierRole.ArcTitan, VoiceProfile = "titan" },
		new() { Id = "nuke_titan", Title = "Nuke Titan", Model = "models/titans/heavy/titan_heavy_deadbolt.mdl", Weapon = "mp_titanweapon_predator_cannon", Health = 12500, Speed = 171, Height = 296, Radius = 61, Shield = 2200, Titan = true, Role = Titanfall2Npc.FrontierRole.NukeTitan, VoiceProfile = "titan" },
		new() { Id = "elite_monarch", Title = "Elite Monarch", Model = "models/titans/medium/titan_medium_vanguard.mdl", Weapon = "mp_titanweapon_xo16_vanguard", Health = 12000, Speed = 215, Height = 284, Radius = 54, Shield = 7500, Titan = true, Role = Titanfall2Npc.FrontierRole.EliteMonarch, VoiceProfile = "titan" },
		new() { Id = "mortar_titan", Title = "Mortar Titan", Model = "models/titans/medium/titan_medium_wraith.mdl", Weapon = "mortar:rocketstream", Health = 10000, Speed = 185, Height = 284, Radius = 54, Shield = 1600, Titan = true, Role = Titanfall2Npc.FrontierRole.MortarTitan, VoiceProfile = "titan" }
	];

	internal static IReadOnlyList<Titanfall2WeaponCatalogEntry> LoadWeapons( Titanfall2ContentSnapshot snapshot )
	{
		if ( !Titanfall2RetailScriptReader.TryReadJsonOverlay<List<Titanfall2WeaponCatalogEntry>>( snapshot, WeaponsOverlayFile, out var overlay ) || overlay.Count == 0 )
			return DefaultWeapons;

		var merged = DefaultWeapons.ToList();
		var indexByKey = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < merged.Count; i++ ) indexByKey[merged[i].Key] = i;

		foreach ( var entry in overlay )
		{
			if ( entry is null || string.IsNullOrWhiteSpace( entry.Key ) || string.IsNullOrWhiteSpace( entry.ScriptName ) ) continue;
			if ( indexByKey.TryGetValue( entry.Key, out var existing ) ) merged[existing] = entry;
			else { indexByKey[entry.Key] = merged.Count; merged.Add( entry ); }
		}
		return merged;
	}

	internal static IReadOnlyList<Titanfall2ActorCatalogEntry> LoadActors( Titanfall2ContentSnapshot snapshot )
	{
		if ( !Titanfall2RetailScriptReader.TryReadJsonOverlay<List<Titanfall2ActorCatalogEntry>>( snapshot, ActorsOverlayFile, out var overlay ) || overlay.Count == 0 )
			return DefaultActors;

		var merged = DefaultActors.ToList();
		var indexById = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < merged.Count; i++ ) indexById[merged[i].Id] = i;

		foreach ( var entry in overlay )
		{
			if ( entry is null || string.IsNullOrWhiteSpace( entry.Id ) || string.IsNullOrWhiteSpace( entry.Model ) ) continue;
			if ( indexById.TryGetValue( entry.Id, out var existing ) ) merged[existing] = entry;
			else { indexById[entry.Id] = merged.Count; merged.Add( entry ); }
		}
		return merged;
	}
}
