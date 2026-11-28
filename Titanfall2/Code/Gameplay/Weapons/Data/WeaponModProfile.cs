/// <summary>Player attachments with implemented gameplay or model presentation.</summary>
internal static class Titanfall2WeaponModProfile
{
	// Empty callback-only, NPC, burn-card, thermal and variable-zoom mods are deliberately
	// absent. The latter require additional gameplay/UI before they can be handed out.
	internal static readonly string[] Supported = ["extended_ammo", "pas_fast_reload", "pas_fast_ads", "hcog", "redline_sight"];
	internal static bool IsOptic( string id ) => id is "hcog" or "redline_sight";
	internal static bool IsSupported( string id ) => Supported.Contains( id, StringComparer.Ordinal );

	internal static bool OwnsEffectiveField( string id, string key ) => id switch
	{
		"extended_ammo" => key == "ammo_clip_size",
		"pas_fast_reload" => key is "reload_time" or "reloadempty_time",
		"pas_fast_ads" => key is "zoom_time_in" or "zoom_time_out",
		"hcog" or "redline_sight" => key == "viewmodel_offset_ads" || key == "zoom_fov" || key.StartsWith( "bodygroup", StringComparison.Ordinal )
			|| key.StartsWith( "sway_", StringComparison.Ordinal ) || key.StartsWith( "bob_", StringComparison.Ordinal ),
		_ => false
	};
}
