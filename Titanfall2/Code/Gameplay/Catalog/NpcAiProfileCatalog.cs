using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Chassis = Titanfall2TitanKit.Chassis;

/// <summary>Snapshot-scoped AI data. Weak keys avoid retaining retired mount snapshots; identities are kept in each result.</summary>
internal static class Titanfall2NpcAiProfileCatalog
{
	const string SettingsFolder = "scripts/aisettings/";
	const string WeaponFolder = "scripts/weapons/";
	static readonly ConditionalWeakTable<Titanfall2ContentSnapshot, SnapshotCache> Caches = new();
	sealed class SnapshotCache
	{
		internal readonly Dictionary<string, Titanfall2NpcAiProfile> Profiles = new( StringComparer.OrdinalIgnoreCase );
		internal readonly Dictionary<string, Titanfall2NpcWeaponAiProfile> Weapons = new( StringComparer.OrdinalIgnoreCase );
	}

	internal static string SettingsFor( Chassis kit, Titanfall2NpcAiMode mode, bool swordCore = false )
	{
		if ( kit == Chassis.Ronin && swordCore ) return "npc_titan_stryder_leadwall_shift_core";
		var suffix = kit switch
		{
			Chassis.Ion => "atlas_stickybomb", Chassis.Tone => "atlas_tracker", Chassis.Monarch => "atlas_vanguard",
			Chassis.Legion => "ogre_minigun", Chassis.Scorch => "ogre_meteor", Chassis.Northstar => "stryder_sniper", Chassis.Ronin => "stryder_leadwall",
			_ => "atlas"
		};
		return "npc_titan_" + (mode is not (Titanfall2NpcAiMode.EnemyMp or Titanfall2NpcAiMode.EnemySp) ? "auto_" : "") + suffix;
	}

	internal static Titanfall2NpcAiProfile Get( Titanfall2ContentSnapshot snapshot, string settingsName, Titanfall2NpcAiMode mode )
	{
		if ( snapshot is null ) return Read( settingsName, mode, _ => null );
		var cache = Caches.GetValue( snapshot, _ => new() );
		var key = $"{settingsName}:{mode}";
		lock ( cache )
		{
			if ( !cache.Profiles.TryGetValue( key, out var profile ) )
			{
				profile = Read( settingsName, mode, path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path ), path => Source( snapshot, path ) );
				cache.Profiles[key] = profile;
			}
			return profile;
		}
	}

	internal static Titanfall2NpcWeaponAiProfile GetWeapon( Titanfall2ContentSnapshot snapshot, string script, Titanfall2NpcAiMode mode )
	{
		if ( snapshot is null ) return ReadWeapon( script, mode, _ => null );
		var cache = Caches.GetValue( snapshot, _ => new() );
		var key = $"{script}:{mode}";
		lock ( cache )
		{
			if ( !cache.Weapons.TryGetValue( key, out var profile ) )
				cache.Weapons[key] = profile = ReadWeapon( script, mode, path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path ), path => Source( snapshot, path ) );
			return profile;
		}
	}

	internal static Titanfall2NpcAiProfile GetTitan( Titanfall2ContentSnapshot snapshot, Chassis kit, Titanfall2NpcAiMode mode, bool swordCore = false )
	{
		var settings = SettingsFor( kit, mode, swordCore );
		if ( snapshot is null ) return WithWeapons( Get( null, settings, mode ), kit, script => GetWeapon( null, script, mode ) );
		var cache = Caches.GetValue( snapshot, _ => new() );
		var key = $"titan:{kit}:{mode}:{swordCore}";
		lock ( cache )
		{
			if ( !cache.Profiles.TryGetValue( key, out var profile ) )
				cache.Profiles[key] = profile = WithWeapons( Get( snapshot, settings, mode ), kit, script => GetWeapon( snapshot, script, mode ) );
			return profile;
		}
	}

	internal static Titanfall2NpcAiProfile ReadTitan( Chassis kit, Titanfall2NpcAiMode mode, Func<string, string> read, bool swordCore = false )
		=> WithWeapons( Read( SettingsFor( kit, mode, swordCore ), mode, read ), kit, script => ReadWeapon( script, mode, read ) );

	static Titanfall2NpcAiProfile WithWeapons( Titanfall2NpcAiProfile profile, Chassis kit, Func<string, Titanfall2NpcWeaponAiProfile> read )
	{
		var scripts = kit switch
		{
			Chassis.Ion => ("mp_titanweapon_particle_accelerator", "mp_titanweapon_laser_lite", "mp_titanweapon_vortex_shield_ion", "mp_titanability_laser_trip"),
			Chassis.Ronin => ("mp_titanweapon_leadwall", "mp_titanweapon_arc_wave", "mp_titanability_basic_block", "mp_titanability_phase_dash"),
			Chassis.Legion => ("mp_titanweapon_predator_cannon", "mp_titanability_power_shot", "mp_titanability_gun_shield", "mp_titanability_ammo_swap"),
			Chassis.Northstar => ("mp_titanweapon_sniper", "mp_titanweapon_dumbfire_rockets", "mp_titanability_tether_trap", "mp_titanability_hover"),
			Chassis.Tone => ("mp_titanweapon_sticky_40mm", "mp_titanweapon_tracker_rockets", "mp_titanability_particle_wall", "mp_titanability_sonar_pulse"),
			Chassis.Scorch => ("mp_titanweapon_meteor", "mp_titanweapon_flame_wall", "mp_titanweapon_heat_shield", "mp_titanability_slow_trap"),
			Chassis.Monarch => ("mp_titanweapon_xo16_shorty", "mp_titanweapon_salvo_rockets", "mp_titanweapon_stun_laser", "mp_titanability_rearm"),
			_ => ("", "", "", "")
		};
		var primary = read( scripts.Item1 ); var offense = read( scripts.Item2 ); var defense = read( scripts.Item3 ); var utility = read( scripts.Item4 );
		return profile with
		{
			Primary = primary, Offense = offense, Defense = defense, Utility = utility,
			Sources = Array.AsReadOnly( profile.Sources.Concat( primary.Sources ).Concat( offense.Sources ).Concat( defense.Sources ).Concat( utility.Sources ).Distinct().ToArray() ),
			Diagnostics = Array.AsReadOnly( profile.Diagnostics.Concat( primary.Diagnostics ).Concat( offense.Diagnostics ).Concat( defense.Diagnostics ).Concat( utility.Diagnostics ).Distinct().ToArray() )
		};
	}

	internal static Titanfall2NpcAiProfile Read( string settingsName, Titanfall2NpcAiMode mode, Func<string, string> read,
		Func<string, Titanfall2NpcAiSource> source = null )
	{
		var diagnostics = new List<string>(); var sources = new List<string>();
		var profile = new Titanfall2NpcAiProfile { SettingsName = settingsName ?? "", Mode = mode };
		try
		{
			var path = ScriptPath( settingsName, SettingsFolder );
			var root = Titanfall2NpcAiKeyValues.Read( path, SettingsFolder, read, profile.Singleplayer, diagnostics, sources );
			var values = Titanfall2NpcAiKeyValues.EffectiveValues( root, profile.Singleplayer );
			ValidateNumbers( values, diagnostics, ["Health", "chaseStopDist", "chaseStopDistHeavyArmor", "circleStrafeDist", "evasiveCombatShieldPct",
				"evasiveCombatHealthChangeRateDiff", "aggresiveCombatHealthChangeRateDiff", "evasiveCombatTotalHealthDiffPct", "aggressiveCombatTotalHealthDiffPct",
				"dodgePeriod", "maxDodgePerPeriod", "sharedEnergyTotal", "sharedEnergyRegenRate", "sharedEnergyRegenDelay", "meleeInterval", "meleeMaxCombo", "traverseCostFactor"] );
			var behavior = values.TryGetValue( "BehaviorSelector", out var selected ) ? selected.Text : "";
			behavior = settingsName?.EndsWith( "_shift_core", StringComparison.OrdinalIgnoreCase ) == true ? behavior : mode switch
			{
				Titanfall2NpcAiMode.AutoTitanFollow => "behavior_mp_auto_titan",
				Titanfall2NpcAiMode.AutoTitanGuard => "behavior_mp_auto_titan_guard",
				Titanfall2NpcAiMode.EnhancedAutoTitanFollow => "behavior_mp_auto_titan_enhanced",
				Titanfall2NpcAiMode.EnhancedAutoTitanGuard => "behavior_mp_auto_titan_enhanced_guard",
				_ => behavior
			};
			profile = profile with { Available = true, Values = values, BehaviorSelector = behavior };
			profile = profile with { Selectors = ReadSelectors( behavior, read, diagnostics, sources ) };
			if ( values.TryGetValue( "DefaultWeapon", out var weapon ) && !string.IsNullOrWhiteSpace( weapon.Text ) )
				profile = profile with { Primary = ReadWeapon( weapon.Text, mode, read, source ) };
		}
		catch ( InvalidDataException error ) { diagnostics.Add( error.Message + "; using explicit field fallbacks." ); }
		return profile with { Sources = Sources( sources, source ), Diagnostics = Array.AsReadOnly( diagnostics.Distinct().ToArray() ) };
	}

	internal static Titanfall2NpcWeaponAiProfile ReadWeapon( string scriptName, Titanfall2NpcAiMode mode, Func<string, string> read,
		Func<string, Titanfall2NpcAiSource> source = null, IEnumerable<string> mods = null )
	{
		var diagnostics = new List<string>(); var sources = new List<string>();
		var profile = new Titanfall2NpcWeaponAiProfile { ScriptName = scriptName ?? "" };
		if ( string.IsNullOrWhiteSpace( scriptName ) ) return profile;
		try
		{
			var path = ScriptPath( scriptName, WeaponFolder );
			var root = Titanfall2NpcAiKeyValues.Read( path, WeaponFolder, read, mode == Titanfall2NpcAiMode.EnemySp, diagnostics, sources );
			var values = Titanfall2NpcAiKeyValues.EffectiveValues( root, mode == Titanfall2NpcAiMode.EnemySp, mods );
			ValidateNumbers( values, diagnostics, ["npc_min_engage_range", "npc_max_engage_range", "npc_max_range", "npc_min_engage_range_heavy_armor", "npc_max_engage_range_heavy_armor",
				"npc_min_burst", "npc_max_burst", "npc_rest_time_between_bursts_min", "npc_rest_time_between_bursts_max", "npc_pre_fire_delay", "npc_charge_time_min", "npc_charge_time_max",
				"shared_energy_cost", "shared_energy_charge_cost", "npc_use_short_duration", "npc_use_normal_duration", "npc_use_min_damage", "npc_use_max_damage", "npc_use_min_projectile_damage", "npc_alt_fire_check_value"] );
			if ( mods is not null )
				foreach ( var mod in mods ) if ( !root.Children.TryGetValue( "Mods", out var modRoot ) || !modRoot.Children.ContainsKey( mod ) )
					diagnostics.Add( $"Unknown AI weapon mod '{mod}' in {path}; not applied." );
			profile = profile with { Values = values, Available = true, ScriptName = path[(path.LastIndexOf( '/' ) + 1)..^4] };
			var useCheck = profile.Text( "npc_use_check_type", "none" ).ToLowerInvariant() switch
			{
				"none" or "wnuc_none" => Titanfall2NpcAbilityUseCheck.None, "tracker_hit" => Titanfall2NpcAbilityUseCheck.TrackerHit,
				"ground_clear" => Titanfall2NpcAbilityUseCheck.GroundClear, "while_moving" => Titanfall2NpcAbilityUseCheck.WhileMoving,
				"while_evasive" => Titanfall2NpcAbilityUseCheck.WhileEvasive, "electric_smoke" => Titanfall2NpcAbilityUseCheck.ElectricSmoke,
				"grapple" => Titanfall2NpcAbilityUseCheck.Grapple, "shared_energy" => Titanfall2NpcAbilityUseCheck.SharedEnergy,
				"wide_sight_clear" => Titanfall2NpcAbilityUseCheck.WideSightClear, "enemy_range_change" => Titanfall2NpcAbilityUseCheck.EnemyRangeChange,
				_ => Titanfall2NpcAbilityUseCheck.Unsupported
			};
			var kind = profile.Text( "npc_titan_ability", "WTA_NONE" ).ToUpperInvariant() switch
			{
				"WTA_NONE" => Titanfall2NpcAbilityKind.None, "WTA_VORTEX" => Titanfall2NpcAbilityKind.Vortex,
				"WTA_VORTEX_MELEE" => Titanfall2NpcAbilityKind.VortexMelee, "WTA_WALL" => Titanfall2NpcAbilityKind.Wall,
				"WTA_ATTACHED_WALL" => Titanfall2NpcAbilityKind.AttachedWall, _ => Titanfall2NpcAbilityKind.Unsupported
			};
			if ( useCheck == Titanfall2NpcAbilityUseCheck.Unsupported ) diagnostics.Add( $"Unsupported npc_use_check_type '{profile.Text( "npc_use_check_type" )}' in {path}; ability disabled." );
			if ( kind == Titanfall2NpcAbilityKind.Unsupported ) diagnostics.Add( $"Unsupported npc_titan_ability '{profile.Text( "npc_titan_ability" )}' in {path}." );
			profile = profile with { UseCheck = useCheck, AbilityKind = kind };
		}
		catch ( InvalidDataException error ) { diagnostics.Add( error.Message + "; ability data unavailable." ); }
		return profile with { Sources = Sources( sources, source ), Diagnostics = Array.AsReadOnly( diagnostics.Distinct().ToArray() ) };
	}

	static IReadOnlyDictionary<string, Titanfall2NpcAiSelector[]> ReadSelectors( string behavior, Func<string, string> read,
		List<string> diagnostics, List<string> sources )
	{
		var result = new Dictionary<string, Titanfall2NpcAiSelector[]>( StringComparer.OrdinalIgnoreCase );
		if ( string.IsNullOrWhiteSpace( behavior ) ) { diagnostics.Add( "Missing BehaviorSelector; native selector order unavailable." ); return result; }
		var path = ScriptPath( behavior, "scripts/aibehavior/" );
		var text = read( path );
		if ( text is null ) { diagnostics.Add( $"Missing AI behavior: {path}" ); return result; }
		if ( text.Length > 512 * 1024 ) throw new InvalidDataException( $"AI behavior too large: {path}" );
		sources.Add( path );
		string state = null; var entries = new List<Titanfall2NpcAiSelector>(); var lineNumber = 0;
		foreach ( var raw in text.Split( '\n' ) )
		{
			lineNumber++;
			var line = raw.Split( "//", 2, StringSplitOptions.None )[0].Trim( ' ', '\t', '\r', '\uFEFF' );
			if ( line.Length == 0 ) continue;
			var parts = line.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
			if ( parts[0] == "Selector" && parts.Length == 2 )
			{
				if ( state is not null ) throw new InvalidDataException( $"Unclosed Selector in {path}:{lineNumber}" );
				state = parts[1]; entries.Clear(); continue;
			}
			if ( parts[0] == "Selector_End" )
			{
				if ( state is null ) throw new InvalidDataException( $"Unexpected Selector_End in {path}:{lineNumber}" );
				result[state] = entries.ToArray(); state = null; continue;
			}
			if ( state is null || !parts[0].Contains( "SelectSchedule_", StringComparison.Ordinal ) )
			{
				diagnostics.Add( $"Unsupported behavior entry in {path}:{lineNumber}: {line}" ); continue;
			}
			var predicate = string.Join( ' ', parts.Skip( 1 ) );
			if ( predicate.Length > 0 && !(parts.Length == 4 && parts[1] == "health" && parts[2] is ">" or "<") )
				diagnostics.Add( $"Unsupported selector predicate in {path}:{lineNumber}: {predicate}" );
			entries.Add( new( parts[0], predicate, path, lineNumber ) );
		}
		if ( state is not null ) throw new InvalidDataException( $"Unclosed Selector at end of {path}" );
		return new ReadOnlyDictionary<string, Titanfall2NpcAiSelector[]>( result );
	}

	static string ScriptPath( string name, string folder )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) throw new InvalidDataException( "Empty AI script name." );
		var path = name.StartsWith( folder, StringComparison.OrdinalIgnoreCase ) ? name : folder + name;
		if ( !path.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) ) path += ".txt";
		return Titanfall2NpcAiKeyValues.Normalize( path, folder );
	}
	static IReadOnlyList<Titanfall2NpcAiSource> Sources( IEnumerable<string> paths, Func<string, Titanfall2NpcAiSource> source )
		=> Array.AsReadOnly( paths.Distinct( StringComparer.OrdinalIgnoreCase ).Select( path => source?.Invoke( path ) ?? new( path, "reader", "unspecified" ) ).ToArray() );
	static Titanfall2NpcAiSource Source( Titanfall2ContentSnapshot snapshot, string path ) => snapshot.TryGet( path, out var entry )
		? new( path, entry.SourceId, entry.SourceIdentity ) : new( path, "missing", snapshot.Revision );
	static void ValidateNumbers( IReadOnlyDictionary<string, Titanfall2NpcAiKeyValues.Value> values, List<string> diagnostics, string[] keys )
	{
		foreach ( var key in keys )
			if ( values.TryGetValue( key, out var value ) && (!float.TryParse( value.Text, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out var number ) || !float.IsFinite( number )) )
				diagnostics.Add( $"Invalid numeric AI field '{key}={value.Text}' in {value.Path}; explicit fallback used." );
	}
}

public partial class Titanfall2Mount
{
	internal Titanfall2NpcAiProfile GetNpcAiProfile( string settingsName, Titanfall2NpcAiMode mode = Titanfall2NpcAiMode.EnemyMp )
		=> Titanfall2NpcAiProfileCatalog.Get( _contentSnapshot, settingsName, mode );
	internal Titanfall2NpcAiProfile GetTitanAiProfile( Chassis kit, Titanfall2NpcAiMode mode = Titanfall2NpcAiMode.EnemyMp, bool swordCore = false )
		=> Titanfall2NpcAiProfileCatalog.GetTitan( _contentSnapshot, kit, mode, swordCore );
	internal Titanfall2NpcWeaponAiProfile GetNpcWeaponAiProfile( string scriptName, Titanfall2NpcAiMode mode = Titanfall2NpcAiMode.EnemyMp )
		=> Titanfall2NpcAiProfileCatalog.GetWeapon( _contentSnapshot, scriptName, mode );
}
