/// <summary>
/// Bounded table of verified legacy dotted-to-underscore Miles impact event aliases.
/// Supports optional JSON overlay via audio_aliases.json.
/// </summary>
internal static class Titanfall2ImpactAudioAliasCatalog
{
	const string OverlayFile = "audio_aliases.json";

	static readonly Dictionary<string, string> Defaults = new( StringComparer.OrdinalIgnoreCase )
	{
		["Default.Energy.BulletImpact_3P_vs_3P"] = "default_energy_bulletimpact_3p_vs_3p",
		["Titan.Energy.BulletImpact_3P_vs_3P"] = "titan_energy_bulletimpact_3p_vs_3p",
		["Android.Energy.BulletImpact_3P_vs_3P"] = "android_energy_bulletimpact_3p_vs_3p",
		["TitanBubbleShield.Energy.BulletImpact_3P_vs_3P"] = "titanbubbleshield_energy_bulletimpact_3p_vs_3p",
		["TitanShield.Energy.BulletImpact_3P_vs_3P"] = "titanshield_energy_bulletimpact_3p_vs_3p",
		["Concrete.Heavy.BulletImpact_3P_vs_3P"] = "concrete_heavy_bulletimpact_3p_vs_3p",
		["Dirt.Heavy.BulletImpact_3P_vs_3P"] = "dirt_heavy_bulletimpact_3p_vs_3p",
		["Titan.AmpedXO16.BulletImpact_3P_vs_3P"] = "titan_ampedxo16_bulletimpact_3p_vs_3p",
		["Flesh.LSTAR.BulletImpact_3P_vs_3P"] = "flesh_lstar_bulletimpact_3p_vs_3p",
		["MetalGrate.Heavy.BulletImpact_3P_vs_3P"] = "metalgrate_heavy_bulletimpact_3p_vs_3p",
		["Grass.Heavy.BulletImpact_3P_vs_3P"] = "grass_heavy_bulletimpact_3p_vs_3p",
		["Mud.Heavy.BulletImpact_3P_vs_3P"] = "mud_heavy_bulletimpact_3p_vs_3p",
		["Default.Heavy.BulletImpact_3P_vs_3P"] = "default_heavy_bulletimpact_3p_vs_3p",
		["SolidMetal.Heavy.BulletImpact_3P_vs_3P"] = "solidmetal_heavy_bulletimpact_3p_vs_3p",
		["Sand.Heavy.BulletImpact_3P_vs_3P"] = "sand_heavy_bulletimpact_3p_vs_3p"
	};

	static Dictionary<string, string> _active = Defaults;

	internal static void Reload( Titanfall2ContentSnapshot snapshot )
	{
		if ( !Titanfall2RetailScriptReader.TryReadJsonOverlay<Dictionary<string, string>>( snapshot, OverlayFile, out var overlay )
			|| overlay.Count == 0 )
		{
			_active = Defaults;
			return;
		}
		var merged = new Dictionary<string, string>( Defaults, StringComparer.OrdinalIgnoreCase );
		foreach ( var (key, value) in overlay )
		{
			if ( !string.IsNullOrWhiteSpace( key ) && !string.IsNullOrWhiteSpace( value ) )
				merged[key] = value;
		}
		_active = merged;
	}

	internal static void Reset() => _active = Defaults;

	internal static bool TryGetAlias( string eventName, out string alias ) =>
		_active.TryGetValue( eventName, out alias );
}
