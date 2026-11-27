/// <summary>
/// Declarative table for retail Squirrel weapon callbacks that augment WeaponData key-values
/// (bolt patterns, extra predicted trails, side muzzle vents, shell drops, and hazard effects).
/// Keeps Titanfall2WeaponData.Read free of weapon-name conditional branches.
/// </summary>
internal static class Titanfall2WeaponScriptOverrides
{
	internal readonly record struct PrimaryAttackOverride(
		Titanfall2BoltPattern Pattern,
		int? Pellets = null,
		float? Speed = null,
		float? BoltSpreadHip = null,
		float? BoltSpreadAds = null,
		float? GravityScale = null,
		string ScriptTrailEffect = null );

	internal readonly record struct AnimEventOverride(
		Titanfall2WeaponMuzzleLayer[] MuzzleLayers,
		string ShellSound = null,
		float ShellSoundDelay = 0f );

	static readonly Dictionary<string, PrimaryAttackOverride> PrimaryAttackOverrides = new( StringComparer.Ordinal )
	{
		["OnWeaponPrimaryAttack_weapon_doubletake"] = new( Titanfall2BoltPattern.DoubleTake ),
		["OnWeaponPrimaryAttack_weapon_mastiff"] = new(
			Titanfall2BoltPattern.Mastiff,
			Pellets: 8, Speed: 3000f, BoltSpreadHip: 0.05f, BoltSpreadAds: 0.025f, GravityScale: 0.09f ),
		["OnWeaponPrimaryAttack_weapon_shotgun_pistol"] = new(
			Titanfall2BoltPattern.Mozambique,
			Pellets: 3, Speed: 3000f, BoltSpreadHip: 0.05f, BoltSpreadAds: 0.025f, GravityScale: 0.09f ),
		// This active client callback adds an ABSORIGIN_FOLLOW effect in addition to projectile_trail_effect_0.
		["OnWeaponPrimaryAttack_weapon_sniper"] = new(
			Titanfall2BoltPattern.Default,
			ScriptTrailEffect: "Rocket_Smoke_SMR_Glow" )
	};

	static readonly Dictionary<string, AnimEventOverride> ClientAnimEventOverrides = new( StringComparer.Ordinal )
	{
		["OnClientAnimEvent_weapon_sniper"] = new(
			[
				new( "wpn_mflash_snp_hmn_smoke_side_FP", "wpn_mflash_snp_hmn_smoke_side", "muzzle_flash_L", true, false ),
				new( "wpn_mflash_snp_hmn_smoke_side_FP", "wpn_mflash_snp_hmn_smoke_side", "muzzle_flash_R", true, false )
			] ),
		["OnClientAnimEvent_weapon_dmr"] = new(
			[
				new( "wpn_mflash_snp_hmn_smoke_side_FP", "wpn_mflash_snp_hmn_smoke_side", "muzzle_flash_L", true, true ),
				new( "wpn_mflash_snp_hmn_smoke_side_FP", "wpn_mflash_snp_hmn_smoke_side", "muzzle_flash_R", true, true )
			],
			ShellSound: "large_shell_drop",
			ShellSoundDelay: 0.6f )
	};

	static readonly Dictionary<Titanfall2ProjectileBehavior, string> HazardEffects = new()
	{
		[Titanfall2ProjectileBehavior.ThermiteBurn] = "P_grenade_thermite",
		[Titanfall2ProjectileBehavior.GravityPull] = "P_wpn_grenade_gravity",
		[Titanfall2ProjectileBehavior.ElectricSmoke] = "P_wpn_smk_electric_pilot"
	};

	internal static bool TryGetPrimaryAttack( string callback, out PrimaryAttackOverride rule ) =>
		PrimaryAttackOverrides.TryGetValue( callback ?? "", out rule );

	internal static bool TryGetClientAnimEvent( string callback, out AnimEventOverride rule ) =>
		ClientAnimEventOverrides.TryGetValue( callback ?? "", out rule );

	internal static string GetHazardEffect( Titanfall2ProjectileBehavior behavior ) =>
		HazardEffects.GetValueOrDefault( behavior );
}
