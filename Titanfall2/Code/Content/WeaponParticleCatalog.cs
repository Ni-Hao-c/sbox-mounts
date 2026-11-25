using Titanfall2;

public partial class Titanfall2Mount
{
	// Retail PCF dependencies for short-lived ordnance. Background map discovery
	// can be hundreds of files behind; an explosion cannot wait for that cursor.
	internal static string WeaponParticleLibrary( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return null;
		var shotLibrary = ShotParticleLibrary( name );
		if ( shotLibrary is not null ) return shotLibrary;
		if ( name.StartsWith( "P_impact_exp_smll_", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_exp_sm.pcf";
		if ( name.StartsWith( "P_impact_exp_med_", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_exp_med.pcf";
		if ( name.StartsWith( "P_impact_exp_lrg_", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_exp_lg.pcf";
		if ( name.StartsWith( "P_wpn_lasercannon", StringComparison.OrdinalIgnoreCase ) ) return "particles/xo_core_lasercannon.pcf";
		if ( name.StartsWith( "P_impact_lasercannon_", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_lasercannon.pcf";
		if ( name.StartsWith( "P_wpn_hand_laser_beam", StringComparison.OrdinalIgnoreCase ) || name == "P_handlaser_charge" ) return "particles/wpn_xo_handlaser.pcf";
		if ( name.StartsWith( "P_wpn_monarch_", StringComparison.OrdinalIgnoreCase ) ) return "particles/monarch_beam.pcf";
		if ( name.StartsWith( "P_xo_sword_core_hld", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_xo_sword.pcf";
		if ( name.StartsWith( "P_xo_sword_melee_", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_melee_titan.pcf";
		if ( name.StartsWith( "P_wpn_meteor_wave", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_xo_meteor_wall.pcf";
		if ( name.StartsWith( "P_SalvoCore_trail", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_salvocore_trail.pcf";
		if ( name.StartsWith( "wpn_muzzleflash_xo_elec", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_muzzleflash_xo16_mod.pcf";
		if ( name == "P_wpn_tracer_xo16_elec" ) return "particles/wpn_xo16_mod.pcf";
		if ( name.StartsWith( "wpn_vortex_chargingCP_titan", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_vortex_titan_cp.pcf";
		if ( name.StartsWith( "P_wpn_HeatShield", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_vortex_titan_heat.pcf";
		if ( name == "Rocket_Smoke_SMALL_Titan_2" ) return "particles/wpn_rocket_fx.pcf";
		if ( name == "P_xo_armor_impact" ) return "particles/xo_armor.pcf";
		if ( name.StartsWith( "impact_flesh", StringComparison.OrdinalIgnoreCase ) || name.StartsWith( "impact_alienflesh", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_flesh.pcf";
		if ( name.StartsWith( "impact_", StringComparison.OrdinalIgnoreCase ) || name == "P_impact_metal_spectre" ) return "particles/impact_fx.pcf";
		if ( name.StartsWith( "P_impact_exp_XLG_", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_exp_xl.pcf";
		if ( name.StartsWith( "P_impact_exp_frag_", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_exp_frag_grenade.pcf";
		if ( name.StartsWith( "P_impact_exp_emp_", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_exp_emp.pcf";
		if ( name.StartsWith( "P_impact_exp_xsmll_", StringComparison.OrdinalIgnoreCase ) ) return "particles/impact_fx_exp_xs.pcf";
		if ( name.StartsWith( "P_grenade_thermite", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_grenade_thermite.pcf";
		if ( name.StartsWith( "P_wpn_grenade_gravity", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_grenade_gravity.pcf";
		if ( name.StartsWith( "P_wpn_smk_electric_", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_smokescreen_elec.pcf";
		if ( name.StartsWith( "Rocket_Smoke_SMR", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_rocket_fx.pcf";
		if ( name.Equals( "wpn_grenade_frag", StringComparison.OrdinalIgnoreCase ) ) return "particles/wpn_grenade.pcf";
		if ( name.Equals( "P_sup_spectre_death", StringComparison.OrdinalIgnoreCase ) ) return "particles/super_spectre_death.pcf";
		if ( name.Equals( "P_exp_spectre_death", StringComparison.OrdinalIgnoreCase ) ) return "particles/deathfx_spectre.pcf";
		if ( name.Equals( "xo_exp_death", StringComparison.OrdinalIgnoreCase ) ) return "particles/xo_death_fx.pcf";
		if ( name.Equals( "P_turret_exp_plasma", StringComparison.OrdinalIgnoreCase ) ) return "particles/drone_fx.pcf";
		return null;
	}

	// These families come from the mounted multiplayer scripts and the PCF index.
	// Normalize perspective variants before matching; do not guess a path from untrusted names.
	static string ShotParticleLibrary( string name )
	{
		var key = name.ToLowerInvariant();
		if ( key.EndsWith( "_fp" ) ) key = key[..^3];
		if ( key.StartsWith( "wpn_shelleject_" ) ) return "particles/wpn_shellejects_fx.pcf";
		return key switch
		{
			"p_wpn_tracer" or "p_wpn_tracer_pistol" or "p_wpn_tracer_pulse" or "p_wpn_tracer_sniper" or "p_wpn_tracer_bc"
				or "weapon_tracers_40mm" or "weapon_tracers_mega_turret_aa" or "weapon_tracers_shotgun" or "weapon_tracers_vortex" or "weapon_tracers_xo16"
				or "weapon_kraber_projectile" or "weapon_40mm_projectile" => "particles/tracer_fx.pcf",
			"p_projectile_tpa" => "particles/wpn_particle_accelerator.pcf",
			"p_leadwall_proj" or "p_mastiff_proj" => "particles/wpn_dragonsbreath.pcf",
			"p_titan_sniper1" => "particles/wpn_xo_sniper.pcf",
			"p_wpn_meteor_trail" => "particles/wpn_xo_meteor.pcf",
			"p_plasma_proj_lg_dlight" or "p_plasma_proj_md" or "p_plasma_proj_md_dlight" or "p_plasma_proj_sm" => "particles/wpn_plasma_trail.pcf",
			"p_projectile_lstar" => "particles/wpn_lstar.pcf",
			"p_lstar_cooldown" or "p_lstar_cooldown_vent" or "p_lstar_overheat" or "p_lstar_overheat_smoke" => "particles/wpn_muzzleflash_lstar.pcf",
			"p_impact_lstar_default" or "p_impact_lstar_flesh" or "p_impact_lstar_victim" => "particles/impact_fx_lstar.pcf",
			"xo_spark_med" => "particles/xo_titan_sparks.pcf",
			"p_doubletake_proj" => "particles/wpn_doubletake.pcf",
			"wpn_grenade_frag_softball" or "wpn_grenade_frag_mag" or "wpn_grenade_frag_blue" => "particles/wpn_grenade.pcf",
			"p_wpn_defender_beam" or "p_wpn_defender_beam_burn" or "p_wpn_defender_charge" or "p_wpn_defender_charge_burn"
				or "p_wpn_defender_charge_fp_burn" or "defender_charge_ch_dlight" => "particles/wpn_defender_beam.pcf",
			"p_impact_exp_defender_default" => "particles/impact_fx_defender.pcf",
			"rocket_smoke_large" => "particles/wpn_rocket_fx.pcf",
			"p_wpn_arcball_trail" => "particles/wpn_xo_arcball.pcf",
			"p_wpn_grenade_smoke_trail" => "particles/wpn_grenade_smoke.pcf",
			"p_muzzleflash_predator" or "p_weapon_tracers_predator" or "p_weapon_tracers_predator_alt" => "particles/wpn_muzzleflash_xo_predator.pcf",
			"mflash_shotgun_fp_full" or "mflash_shotgun_full" => "particles/wpn_muzzleflash_shotgun.pcf",
			"p_wpn_muzzleflash_mgl_fp_full" or "p_wpn_muzzleflash_mgl_full" => "particles/wpn_muzzleflash_mgl.pcf",
			"p_muz_tpa" => "particles/wpn_muzzleflash_particle_accelerator.pcf",
			"p_muz_lstar" => "particles/wpn_muzzleflash_lstar.pcf",
			"p_mflash_pulse_lmg" => "particles/wpn_muzzleflash_pulse_lmg.pcf",
			"p_wpn_mflash_arcball" => "particles/wpn_muzzleflash_arcball.pcf",
			"p_wpn_mflash_dbltake" => "particles/wpn_muzzleflash_doubletake.pcf",
			"p_wpn_muz_mastiff" => "particles/wpn_muzzleflash_mastiff.pcf",
			"p_wpn_xo_sniper_muzzleflash" => "particles/wpn_muzzleflash_xo_sniper.pcf",
			"wpn_muzzleflash_xo_shotgun_alt" => "particles/wpn_muzzleflash_xo_shotgun.pcf",
			"wpn_muzzleflash_snp_hmn" or "wpn_mflash_snp_hmn_smoke_side" or "wpn_mflash_snp_hmn_smokepuff_side" => "particles/wpn_muzzleflash_sniper_hmn.pcf",
			"p_wpn_muzzleflash_alternator" or "p_wpn_muzzleflash_coldwar" or "p_wpn_muzzleflash_epg" or "p_wpn_muzzleflash_law" or "p_wpn_muzzleflash_smr" => $"particles/{key[2..]}.pcf",
			"wpn_muzzleflash_pistol" or "wpn_muzzleflash_40mm" or "wpn_muzzleflash_lmg" or "wpn_muzzleflash_pulse" or "wpn_muzzleflash_smg"
				or "wpn_muzzleflash_xo" or "wpn_muzzleflash_xo_rocket" or "wpn_muzzleflash_xo_triple" => $"particles/{key}.pcf",
			_ => null
		};
	}

	readonly Dictionary<string, Titanfall2ParticleDefinition> _readyShotParticles = new( StringComparer.OrdinalIgnoreCase );
	internal bool TryPrepareWeaponParticle( string name, out Titanfall2ParticleDefinition definition )
	{
		definition = null;
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		lock ( _particleIndexLock )
		{
			if ( _readyShotParticles.TryGetValue( name, out definition ) ) return true;
			IndexPriorityParticleLibrary( WeaponParticleLibrary( name ) );
			if ( !TryGetParticleDefinition( name, out definition ) || !CompleteGraph( definition, new( StringComparer.OrdinalIgnoreCase ) ) )
			{
				WarmWeaponParticles();
				if ( !TryGetParticleDefinition( name, out definition ) || (!IsParticleCatalogComplete && !CompleteGraph( definition, new( StringComparer.OrdinalIgnoreCase ) )) ) return false;
			}
			_readyShotParticles[name] = definition;
			return true;
		}
	}
	bool CompleteGraph( Titanfall2ParticleDefinition definition, HashSet<string> visited )
	{
		if ( !visited.Add( definition.Name ) ) return true;
		var complete = true;
		foreach ( var child in definition.Children )
		{
			if ( !TryGetParticleDefinition( child, out var next ) )
			{
				IndexPriorityParticleLibrary( WeaponParticleLibrary( child ) );
				if ( !TryGetParticleDefinition( child, out next ) ) { complete = false; continue; }
			}
			if ( !CompleteGraph( next, visited ) ) complete = false;
		}
		return complete;
	}
}
