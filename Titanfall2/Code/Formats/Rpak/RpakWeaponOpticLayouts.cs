// Image bindings and RGBA come from retail ui(11).dll exports; rectangles and draw order
// come from the matching ui.rpak frame/style tables. Procedural noise is held at its midpoint.
// Retail assets remain in the installed game and are loaded through the mount.
internal static partial class Titanfall2WeaponUiCatalog
{
	static readonly Dictionary<string, Titanfall2WeaponUiLayout> OpticLayouts = new( StringComparer.OrdinalIgnoreCase )
	{
		["ui/acgs_redline"] = new( "ui/acgs_redline", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 0f, 64f, 128f, 64f ),
					new( 6.41531f, 0.240574f, 0.240574f, 0.05f ), false, 0f, false, false ),
				new( "models/weapons/attachments/holo_reflex_reticle_small_col", new( 153.6f, 153.6f, 76.8f, 76.8f ),
					new( 40f, 1f, 1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/holo_reflex_reticle_small_windage_col", new( 153.6f, 25.6f, 76.8f, 76.8f ),
					new( 7f, 1f, 1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 128f, 12.8f, 128f, 38.4f ),
					new( 2.405741f, 0.090215f, 0.090215f, 0.01875f ), false, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 128f, 12.8f, 128f, 115.2f ),
					new( 1.603827f, 0.060144f, 0.060144f, 0.0125f ), false, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 128f, 0f, 128f, 128f ),
					new( 6.41531f, 0.240574f, 0.240574f, 0.05f ), false, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 128f, 0f, 128f, 128f ),
					new( 6.41531f, 0.240574f, 0.240574f, 0.05f ), false, 0f, false, false ),
				new( "models/weapons/attachments/flicker_line", new( 145.92f, 12.8f, 92.16f, 115.2f ),
					new( 10f, 1f, 1f, 0.0025f ), false, 0f, false, false ),
				new( "models/weapons/attachments/ammo_counter_meter_col", new( 0f, 128f, 128f, 137.999872f ),
					new( 0.726844f, 2.492312f, 3f, 0.5f ), false, 3f, false, false ),
			] ),
		["ui/alternator_reticle"] = new( "ui/alternator_reticle", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/cqb_dot_front_col", new( 0f, 0f, 256f, 256f ),
					new( 7.217224f, 0.481148f, 0.481148f, 1f ), true, 0f, false, false ),
			] ),
		["ui/aog_multi_reticles"] = new( "ui/aog_multi_reticles", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/aog_reticle_windage", new( 0f, 0f, 128f, 128f ),
					new( 40f, 1f, 1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 128f, 0f, 128f, 128f ),
					new( 6.41531f, 0.240574f, 0.240574f, 0.05f ), false, 0f, false, false ),
				new( "models/weapons/attachments/whiteMult", new( 0f, 128f, 128f, 128f ),
					new( 0.242281f, 0.830771f, 1f, 1f ), false, 1f, false, false ),
				new( "models/weapons/attachments/aog_reticle_dot", new( 179.2f, 179.2f, 25.6f, 25.6f ),
					new( 3f, 0.1f, 0.1f, 1f ), true, 0f, false, false ),
			] ),
		["ui/aog_multi_reticles_lstar"] = new( "ui/aog_multi_reticles_lstar", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/aog_reticle_windage", new( 0f, 0f, 128f, 128f ),
					new( 40f, 1f, 1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 128f, 0f, 128f, 128f ),
					new( 6.41531f, 0.240574f, 0.240574f, 0.05f ), false, 0f, false, false ),
				new( "models/weapons/attachments/whiteMult", new( 0f, 128f, 128f, 128f ),
					new( 0.242281f, 0.830771f, 1f, 0.65f ), false, 1f, false, false ),
				new( "models/weapons/attachments/aog_reticle_dot", new( 179.2f, 179.2f, 25.6f, 25.6f ),
					new( 3f, 0.1f, 0.1f, 1f ), true, 0f, false, false ),
			] ),
		["ui/arc_launcher_reticle"] = new( "ui/arc_launcher_reticle", new( 256, 256 ), 9, 15, new( 61.44f, 115.2f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/aog_reticle_windage_bright", new( 0f, 0f, 124.16f, 128f ),
					new( 20f, 0.1f, 0.1f, 1.15f ), true, 0f, false, false ),
				new( "models/weapons/attachments/aog_reticle_windage_bright", new( 0f, 0f, 124.16f, 128f ),
					new( 20f, 0.1f, 0.1f, 0.25f ), true, 0f, false, false ),
				new( "models/weapons/attachments/hemlok_panel_bleed", new( 20.48f, 0f, 81.92f, 128f ),
					new( 0.45f, 0f, 0f, 0.06f ), false, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 128f, 0f, 128f, 115.2f ),
					new( 1f, 0f, 0f, 0.25f ), false, 0f, false, false ),
				new( "models/weapons/attachments/holo_reflex_reticle_front_col", new( 48.64f, 51.2f, 25.6f, 25.6f ),
					new( 20f, 0.1f, 0.1f, 1.15f ), true, 0f, false, true ),
			] ),
		["ui/cro_threat_front"] = new( "ui/cro_threat_front", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/threat_reticle_windage", new( 10.24f, 0f, 107.52f, 128f ),
					new( 1f, 1.6f, 3.1f, 6f ), true, 0f, false, false ),
				new( "rui/white_viewmodel", new( 0f, 0f, 128f, 128f ),
					new( -0.1f, -0.115f, -0.125f, 0.075f ), false, 0f, false, false ),
				new( "models/weapons/attachments/flicker_line", new( 0f, 0f, 128f, 153.6f ),
					new( 1f, 2f, 5f, 0.005f ), false, 0f, false, false ),
			] ),
		["ui/diamond_reticle_rear"] = new( "ui/diamond_reticle_rear", new( 100, 100 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/diamond_reticle_rear", new( 0f, 0f, 100f, 100f ),
					new( 3.207655f, 0.120287f, 0.120287f, 0.05f ), true, 0f, false, false ),
			] ),
		["ui/doubletake_reticle_doublev"] = new( "ui/doubletake_reticle_doublev", new( 512, 512 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 256f, 0f, 256f, 256f ),
					new( 6.41531f, 0.240574f, 0.240574f, 0.05f ), false, 0f, false, false ),
				new( "models/weapons/attachments/double_take_reticle_trans", new( 0f, 0f, 256f, 256f ),
					new( 1f, 1f, 1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/double_take_reticle", new( 0f, 0f, 256f, 256f ),
					new( 0.962297f, 0.036086f, 0.036086f, 1f ), true, 0f, false, false ),
			] ),
		["ui/g2a4_sights"] = new( "ui/g2a4_sights", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 0f, 0f, 128f, 128f ),
					new( 5f, 0.1f, 0.1f, 1f ), false, 0f, false, false ),
				new( "models/weapons/attachments/cqb_dot_front_col", new( 128f, 0f, 128f, 128f ),
					new( 8f, 0.05f, 0.05f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/cqb_dot_front_col", new( 0f, 128f, 128f, 128f ),
					new( 5f, 0.1f, 0.1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/g2a4_sight", new( 128f, 128f, 128f, 128f ),
					new( 10f, 0.25f, 0.25f, 1f ), true, 0f, false, false ),
			] ),
		["ui/hcog_lower"] = new( "ui/hcog_lower", new( 256, 256 ), 9, 20, new( 192f, 120.32f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/hcog_reticle_front_col", new( 0f, 3.84f, 128f, 128f ),
					new( 12f, 1f, 1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_front_col_trans", new( 0f, 3.84f, 128f, 128f ),
					new( 5f, 1f, 1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_rear_col", new( 128f, 5.12f, 128f, 128f ),
					new( 6f, 0f, 0f, 0.5f ), true, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_rear_col", new( 128f, 3.84f, 128f, 128f ),
					new( 3f, 0f, 0f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/ammo_counter_meter_col", new( 0f, 128f, 128f, 137.999872f ),
					new( 0.242281f, 0.830771f, 1f, 1f ), false, 1f, false, false ),
			] ),
		["ui/hcog_upper"] = new( "ui/hcog_upper", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticFrame, Images:
			[
				new( "models/weapons/attachments/ammo_counter_meter_col", new( 0f, 0f, 256f, 256f ),
					new( 0.969126f, 3.323083f, 4f, 0.45f ), false, 4f, false, false ),
			] ),
		["ui/holo_scope"] = new( "ui/holo_scope", new( 256, 256 ), 9, 14, new( 192f, 97.28f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/holo_reflex_reticle_front_col", new( 25.6f, 25.6f, 76.8f, 76.8f ),
					new( 2f, 5f, 5f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/holo_reflex_reticle_rear_col", new( 148.48f, 20.48f, 87.04f, 87.04f ),
					new( 1f, 3f, 5f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/holo_reflex_glow_inner_ring", new( 192f, 40.96f, 58.88f, 79.36f ),
					new( 0.484563f, 1.661541f, 2f, 1f ), false, 2f, false, false ),
				new( "models/weapons/attachments/holo_reflex_glow_inner_ring", new( 130.56f, 40.96f, 61.44f, 79.36f ),
					new( 0.484563f, 1.661541f, 2f, 1f ), false, 2f, false, true ),
				new( "models/weapons/attachments/holo_reflex_spinner", new( 128f, -5.12f, 128f, 133.12f ),
					new( 0f, 1f, 1f, 0.0055f ), false, 0f, false, false ),
				new( "models/weapons/attachments/holo_reflex_glow_ring", new( 128f, 0f, 128f, 128f ),
					new( 2f, 9f, 11f, 1f ), false, 0f, false, false ),
				new( "models/weapons/attachments/ammo_counter_mag_col", new( 171.52f, 20.48f, 40.96f, 3.84f ),
					new( 2f, 5f, 5f, 1f ), false, 0f, false, false ),
			] ),
		["ui/mastiff_horseshoe"] = new( "ui/mastiff_horseshoe", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/cqb_reticle_rear_col", new( 0f, 0f, 256f, 256f ),
					new( 11.226792f, 0.421005f, 0.421005f, 0.0875f ), true, 0f, false, false ),
				new( "models/weapons/attachments/horseshoe_accentl", new( 0f, 0f, 256f, 256f ),
					new( 3f, 0f, 0f, 1f ), true, 0f, false, false ),
			] ),
		["ui/mgl_sights"] = new( "ui/mgl_sights", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/mgl_range", new( 0f, 2.56f, 33.28f, 99.84f ),
					new( 2f, 0f, 0f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/hemlok_panel_bleed", new( 0f, 2.56f, 33.28f, 99.84f ),
					new( 0.45f, 0f, 0f, 0.06f ), false, 0f, false, false ),
			] ),
		["ui/p2011_green_sights"] = new( "ui/p2011_green_sights", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/pistol_reticle_dot", new( 0f, 0f, 128f, 128f ),
					new( 0f, 10f, 0f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/pistol_reticle_dot", new( 128f, 0f, 128f, 128f ),
					new( 10f, 10f, 10f, 1f ), true, 0f, false, false ),
			] ),
		["ui/r101_sights"] = new( "ui/r101_sights", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/hcog_reticle_front_col_trans", new( 0f, 0f, 128f, 128f ),
					new( 5f, 1f, 1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/diamond_reticle_front", new( 0f, 0f, 128f, 128f ),
					new( 12.83062f, 0.481148f, 0.481148f, 0.1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/diamond_reticle_mid", new( 128f, 0f, 128f, 128f ),
					new( 3.207655f, 0.120287f, 0.120287f, 0.15f ), true, 0f, false, false ),
			] ),
		["ui/r97_reticle"] = new( "ui/r97_reticle", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/holo_reflex_reticle_front_col", new( -25.6f, -25.6f, 179.2f, 179.2f ),
					new( 5f, 0.1f, 0.1f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/hcog_reticle_field_emitter_col", new( 128f, 0f, 128f, 128f ),
					new( 6.41531f, 0.240574f, 0.240574f, 0.05f ), false, 0f, false, false ),
			] ),
		["ui/re45_diopter"] = new( "ui/re45_diopter", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/pistol_reticle_dot_ring", new( 0f, 0f, 256f, 256f ),
					new( 10f, 0f, 0f, 0.05f ), true, 0f, false, false ),
				new( "models/weapons/attachments/holo_reflex_reticle_front_col", new( -25.6f, -25.6f, 307.2f, 307.2f ),
					new( 10f, 0f, 0f, 1f ), true, 0f, false, false ),
			] ),
		["ui/red_dot_basic"] = new( "ui/red_dot_basic", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/holo_reflex_reticle_front_col", new( 0f, 0f, 256f, 256f ),
					new( 2f, 5f, 5f, 1f ), true, 0f, false, false ),
			] ),
		["ui/sa3_crosshair"] = new( "ui/sa3_crosshair", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/cqb_dot_front_col", new( 0f, 12.8f, 128f, 204.8f ),
					new( 28.868895f, 1.082584f, 1.082584f, 0.225f ), true, 0f, false, false ),
			] ),
		["ui/spitfire_crosshair"] = new( "ui/spitfire_crosshair", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/cqb_dot_front_col", new( 89.6f, 89.6f, 76.8f, 76.8f ),
					new( 0.801914f, 3.207655f, 4.811482f, 1f ), true, 0f, false, false ),
			] ),
		["ui/tracker_reticle"] = new( "ui/tracker_reticle", new( 512, 512 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/double_take_tracker_reticle", new( 153.6f, 153.6f, 102.4f, 204.8f ),
					new( 12.028706f, 0.451077f, 0.451077f, 0.09375f ), true, 0f, false, false ),
				new( "models/weapons/attachments/double_take_milspec_visible", new( 256f, 136f, 281.6f, 240f ),
					new( 12.028706f, 0.451077f, 0.451077f, 0.09375f ), true, 0f, false, false ),
				new( "models/weapons/attachments/double_take_milspec", new( 256f, 136f, 281.6f, 240f ),
					new( 12.028706f, 0.451077f, 0.451077f, 0.09375f ), true, 0f, false, false ),
			] ),
		["ui/vinson_sights"] = new( "ui/vinson_sights", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/vinson_reticle_rear_col", new( 128f, 0f, 128f, 128f ),
					new( 8f, 2f, 2f, 1f ), true, 0f, false, false ),
				new( "models/weapons/attachments/vinson_reticle_front_col", new( 128f, 128f, 128f, 128f ),
					new( 1f, 3f, 10f, 0.5f ), true, 0f, false, false ),
				new( "models/weapons/attachments/vinson_reticle_emitter_col", new( 0f, 128f, 128f, 128f ),
					new( 1f, 2f, 5f, 1f ), false, 0f, false, false ),
			] ),
		["ui/volt_sights"] = new( "ui/volt_sights", new( 256, 256 ), 0, 0, new( 0f, 0f ), new( .5f, .5f ), "retail-ui-dll-and-rpak",
			Titanfall2WeaponUiDisplayMode.OpticReticle, Images:
			[
				new( "models/weapons/attachments/cqb_dot_front_col", new( 166.4f, 38.4f, 51.2f, 51.2f ),
					new( 0.1f, 5f, 5f, 1f ), true, 0f, false, false ),
				new( "white", new( 0f, 128f, 128f, 115.2f ),
					new( 0.484563f, 1.661541f, 2f, 1f ), false, 2f, false, false ),
			] ),
	};
}
