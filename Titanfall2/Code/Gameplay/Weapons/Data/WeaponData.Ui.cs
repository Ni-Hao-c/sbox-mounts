/// <summary>A retail UiData binding, resolved after multiplayer and attachment overrides.</summary>
public record struct Titanfall2WeaponUiDisplay
{
	public int Slot { get; set; }
	public string UiPath { get; set; }
	public string MeshName { get; set; }
	public bool FadeWithZoom { get; set; }
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2WeaponUiDisplay[] UiDisplays { get; init; } = [];

	static Titanfall2WeaponUiDisplay[] ReadUiDisplays( Block root, Func<string, string> get )
	{
		var displays = new List<Titanfall2WeaponUiDisplay>();
		for ( var slot = 1; slot <= 16; slot++ )
		{
			if ( get( $"ui{slot}_enable" ) != "1" ) continue;
			var name = $"UiData{slot}";
			var definition = new Block();
			if ( root.Blocks.TryGetValue( name, out var baseUi ) ) definition.Merge( baseUi );
			if ( root.Blocks.TryGetValue( "MP_BASE", out var mp ) && mp.Blocks.TryGetValue( name, out var mpUi ) ) definition.Merge( mpUi );
			var ui = definition.Values.GetValueOrDefault( "ui", "" );
			var mesh = definition.Values.GetValueOrDefault( "mesh", "" );
			var isAmmoDisplay = ui.StartsWith( "ui/", StringComparison.OrdinalIgnoreCase )
				&& (ui.Contains( "ammo_counter", StringComparison.OrdinalIgnoreCase ) || Titanfall2WeaponUiCatalog.IsKnownDisplay( ui ));
			if ( !isAmmoDisplay || !definition.Blocks.TryGetValue( "Args", out var args ) ) continue;
			var opticDisplay = Titanfall2WeaponUiCatalog.TryGet( ui, out var layout )
				&& layout.Mode is Titanfall2WeaponUiDisplayMode.OpticFrame or Titanfall2WeaponUiDisplayMode.OpticReticle;
			var bindsAmmo = string.Equals( args.Values.GetValueOrDefault( "ammo" ), "weapon_ammo", StringComparison.OrdinalIgnoreCase )
				|| string.Equals( args.Values.GetValueOrDefault( "clipAmmo" ), "weapon_ammo", StringComparison.OrdinalIgnoreCase );
			var bindsClipSize = string.Equals( args.Values.GetValueOrDefault( "clipSize" ), "weapon_clipSize", StringComparison.OrdinalIgnoreCase );
			if ( (!opticDisplay && (!bindsAmmo || !bindsClipSize))
				|| !Titanfall2ContentSnapshot.TryNormalize( mesh, out var normalized )
				|| !normalized.StartsWith( "models/weapons/attachments/", StringComparison.Ordinal ) ) continue;
			displays.Add( new() { Slot = slot, UiPath = ui, MeshName = normalized,
				FadeWithZoom = opticDisplay && (string.Equals( args.Values.GetValueOrDefault( "vis" ), "player_zoomfrac", StringComparison.OrdinalIgnoreCase )
					|| string.Equals( args.Values.GetValueOrDefault( "zoomFrac" ), "player_zoomfrac", StringComparison.OrdinalIgnoreCase )) } );
		}
		return displays.ToArray();
	}

	static (Titanfall2CrosshairStyle Style, float BaseSpread) ReadCrosshair( Block root, bool ordnance )
	{
		if ( ordnance ) return (Titanfall2CrosshairStyle.Ordnance, 0f);
		var block = new Block();
		if ( root.Blocks.TryGetValue( "RUI_CrosshairData", out var baseCh ) ) block.Merge( baseCh );
		if ( root.Blocks.TryGetValue( "MP_BASE", out var mp ) && mp.Blocks.TryGetValue( "RUI_CrosshairData", out var mpCh ) ) block.Merge( mpCh );
		// Retail scripts use Crosshair_1 except mp_weapon_vinson.txt which uses Crosshair_0.
		if ( !block.Blocks.TryGetValue( "Crosshair_1", out var entry ) )
			block.Blocks.TryGetValue( "Crosshair_0", out entry );
		var ui = entry?.Values.GetValueOrDefault( "ui", "" ) ?? "";
		var baseSpread = 0f;
		if ( entry is not null && entry.Values.TryGetValue( "base_spread", out var spreadText )
			&& float.TryParse( spreadText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed )
			&& float.IsFinite( parsed ) )
			baseSpread = Math.Clamp( parsed, -10f, 30f );
		var style = ui.ToLowerInvariant() switch
		{
			"ui/crosshair_tri" => Titanfall2CrosshairStyle.Tri,
			"ui/crosshair_plus" => Titanfall2CrosshairStyle.Plus,
			"ui/crosshair_alternator" => Titanfall2CrosshairStyle.Alternator,
			"ui/crosshair_sniper_amped" => Titanfall2CrosshairStyle.Sniper,
			"ui/crosshair_charge_rifle" => Titanfall2CrosshairStyle.ChargeRifle,
			"ui/crosshair_shotgun" => Titanfall2CrosshairStyle.Shotgun,
			"ui/crosshair_mastiff" => Titanfall2CrosshairStyle.Mastiff,
			"ui/crosshair_mozambique" => Titanfall2CrosshairStyle.Mozambique,
			"ui/crosshair_wingman" => Titanfall2CrosshairStyle.Wingman,
			"ui/crosshair_wingman_n" => Titanfall2CrosshairStyle.WingmanElite,
			"ui/crosshair_smart_pistol" => Titanfall2CrosshairStyle.SmartPistol,
			"ui/crosshair_circle2" => Titanfall2CrosshairStyle.Circle,
			"ui/crosshair_lstar" => Titanfall2CrosshairStyle.LStar,
			"ui/crosshair_smr" => Titanfall2CrosshairStyle.Smr,
			"ui/crosshair_grenade_launcher" => Titanfall2CrosshairStyle.GrenadeLauncher,
			"ui/crosshair_grenade_launcher2" => Titanfall2CrosshairStyle.GrenadeLauncherArc,
			_ => Titanfall2CrosshairStyle.Tri
		};
		return (style, baseSpread);
	}

	static Titanfall2OpticReticleStyle ReadOpticReticle( Block root, Func<string, string> get )
	{
		for ( var slot = 1; slot <= 16; slot++ )
		{
			if ( get( $"ui{slot}_enable" ) != "1" ) continue;
			var name = $"UiData{slot}";
			var definition = new Block();
			if ( root.Blocks.TryGetValue( name, out var baseUi ) ) definition.Merge( baseUi );
			if ( root.Blocks.TryGetValue( "MP_BASE", out var mp ) && mp.Blocks.TryGetValue( name, out var mpUi ) ) definition.Merge( mpUi );
			var ui = definition.Values.GetValueOrDefault( "ui", "" ).ToLowerInvariant();
			var reticle = ui switch
			{
				"ui/hcog_upper" => Titanfall2OpticReticleStyle.Hcog,
				"ui/holo_scope" => Titanfall2OpticReticleStyle.Holo,
				"ui/acgs_redline" => Titanfall2OpticReticleStyle.AcgsRedline,
				"ui/cro_threat_front" => Titanfall2OpticReticleStyle.ThreatScope,
				"ui/aog_multi_reticles" or "ui/aog_multi_reticles_lstar" => Titanfall2OpticReticleStyle.Aog,
				"ui/mastiff_horseshoe" => Titanfall2OpticReticleStyle.MastiffHorseshoe,
				"ui/doubletake_reticle_doublev" => Titanfall2OpticReticleStyle.DoubleTakeDoubleV,
				"ui/diamond_reticle_rear" or "ui/diamond_reticle_front" or "ui/diamond_reticle_mid" => Titanfall2OpticReticleStyle.Diamond,
				"ui/p2011_green_sights" => Titanfall2OpticReticleStyle.GreenDot,
				"ui/r101_sights" or "ui/vinson_sights" or "ui/g2a4_sights" or "ui/r97_reticle"
					or "ui/alternator_reticle" or "ui/volt_sights" or "ui/spitfire_crosshair"
					or "ui/sa3_crosshair" or "ui/re45_diopter" or "ui/mgl_sights" => Titanfall2OpticReticleStyle.Dot,
				_ => Titanfall2OpticReticleStyle.None
			};
			if ( reticle != Titanfall2OpticReticleStyle.None ) return reticle;
		}
		return Titanfall2OpticReticleStyle.Dot;
	}
}
