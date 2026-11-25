using Sandbox.UI;

public sealed partial class Titanfall2TitanCockpitHud
{
	void UpdateRetailArtwork( Titanfall2Npc actor, Titanfall2TitanKit kit )
	{
		var chassis = kit?.Kit ?? Titanfall2TitanKit.Chassis.None;
		if ( _artKit != chassis )
		{
			_artKit = chassis;
			var id = chassis == Titanfall2TitanKit.Chassis.Monarch ? "vanguard" : chassis.ToString().ToLowerInvariant();
			SetRetailImage( _chassisArt, $"r2_ui/menus/loadout_icons/titans/{id}_icon" );
			SetRetailImage( _abilityArt[0], OffenseArt( chassis ) );
			SetRetailImage( _abilityArt[1], DefenseArt( chassis ) );
			SetRetailImage( _coreArt, CoreArt( chassis ) );
			SetRetailImage( _damageArt, "vgui/hud/damage/damage_overlay_orangebloom_col" );
			const string cockpit = "models/titans_r2/titan_cockpit/titan_cockpit_rui_";
			SetRetailImage( _dashArt, cockpit + "dash_bar" );
			SetRetailImage( _reticleArt, "rui/hud/crosshairs/crosshair_bracket" );
			SetRetailImage( _reticleRight, "rui/hud/crosshairs/crosshair_bracket" );
			SetRetailImage( _reticleDot, "rui/hud/crosshairs/crosshair_dot" );
		}
		var healthSegments = actor.MaxHealth <= 7500 ? 3 : actor.MaxHealth <= 10000 ? 4 : 5;
		if ( _artHealthSegments != healthSegments )
		{
			_artHealthSegments = healthSegments;
			_healthSegments.DeleteChildren( true );
			for ( var segment = 1; segment < healthSegments; segment++ )
			{
				var divider = _healthSegments.AddChild<Panel>( "hull-divider" );
				divider.Style.Left = Length.Percent( 100f * segment / healthSegments );
			}
			const string cockpit = "models/titans_r2/titan_cockpit/titan_cockpit_rui_";
			var suffix = healthSegments == 5 ? "" : $"_{healthSegments}";
			SetRetailImage( _hullBackground, cockpit + "health_bar" + suffix + "_bg" );
			SetRetailImage( _hullArt, cockpit + "health_bar" + suffix );
			SetRetailImage( _shieldArt, cockpit + "shield_bar" + suffix );
		}
		if ( _artWeapon != actor.GunModelPath )
		{
			_artWeapon = actor.GunModelPath;
			SetRetailImage( _weaponArt, WeaponArt( _artWeapon ) );
		}
		if ( actor.Health < _lastHealth ) _damageFlash = 0.45f;
		_lastHealth = actor.Health;
		_damageArt.Style.Opacity = _damageFlash > 0 ? Math.Clamp( _damageFlash / 0.45f, 0, 0.65f )
			: actor.Health < actor.MaxHealth * 0.25f ? 0.2f : 0;
	}

	// These are the retail textures already indexed by the mount, not copied UI assets.
	// The original RUI VM is engine-specific; native panels bind the same artwork to our live state.
	async void SetRetailImage( Image panel, string path )
	{
		if ( !panel.IsValid() ) return;
		var ident = Seat?.Actor?.MountIdent ?? "titanfall2";
		var uri = string.IsNullOrWhiteSpace( path ) ? null : $"mount://{ident}/{path}.vtex";
		_requestedArt[panel] = uri;
		panel.Style.Display = DisplayMode.None;
		if ( uri is null || Sandbox.Mounting.Directory.Get( ident )?.GetByPath( uri ) is null ) return;
		try
		{
			var texture = await Texture.LoadAsync( uri );
			if ( !panel.IsValid() || !_requestedArt.TryGetValue( panel, out var current ) || current != uri ) return;
			if ( !texture.IsValid() || texture == Texture.Invalid || texture == Texture.White ) return;
			if ( panel == _hullArt || panel == _shieldArt || panel == _dashArt )
			{
				// Retail cockpit artwork is shaded by the cockpit material. Use its
				// authored silhouette as a native mask, with the HUD's readable tint.
				panel.Style.MaskImage = texture;
				panel.Texture = Texture.White;
			}
			else panel.Texture = texture;
			panel.Style.Display = DisplayMode.Flex;
		}
		catch ( Exception e )
		{
			Titanfall2Log.Warning( $"Cannot load cockpit artwork '{uri}': {e.Message}" );
		}
	}

	static string OffenseArt( Titanfall2TitanKit.Chassis chassis ) => chassis switch
	{
		Titanfall2TitanKit.Chassis.Monarch => "rui/titan_loadout/ordnance/multilock_rockets",
		Titanfall2TitanKit.Chassis.Ion => "rui/titan_loadout/ordnance/laser_shot",
		Titanfall2TitanKit.Chassis.Legion => "rui/titan_loadout/ordnance/concussive_shot_short",
		Titanfall2TitanKit.Chassis.Tone => "rui/titan_loadout/ordnance/tracking_rockets",
		Titanfall2TitanKit.Chassis.Scorch => "rui/titan_loadout/ordnance/flame_wall",
		Titanfall2TitanKit.Chassis.Ronin => "rui/titan_loadout/ordnance/arc_wave",
		Titanfall2TitanKit.Chassis.Northstar => "rui/titan_loadout/ordnance/cluster_missile",
		_ => null
	};
	static string DefenseArt( Titanfall2TitanKit.Chassis chassis ) => chassis switch
	{
		Titanfall2TitanKit.Chassis.Monarch => "rui/titan_loadout/defensive/titan_defensive_energy_siphon",
		Titanfall2TitanKit.Chassis.Ion => "rui/titan_loadout/defensive/titan_defensive_vortex",
		Titanfall2TitanKit.Chassis.Legion => "rui/titan_loadout/defensive/titan_defensive_gun_shield",
		Titanfall2TitanKit.Chassis.Tone => "rui/titan_loadout/defensive/titan_defensive_particle_wall",
		Titanfall2TitanKit.Chassis.Scorch => "rui/titan_loadout/defensive/titan_defensive_heat_shield",
		Titanfall2TitanKit.Chassis.Northstar => "rui/titan_loadout/ordnance/tether",
		Titanfall2TitanKit.Chassis.Ronin => "rui/titan_loadout/defensive/titan_defensive_sword_block",
		_ => null
	};
	static string CoreArt( Titanfall2TitanKit.Chassis chassis ) => chassis switch
	{
		Titanfall2TitanKit.Chassis.Monarch => "rui/titan_loadout/core/titan_core_vanguard",
		Titanfall2TitanKit.Chassis.Ion => "rui/titan_loadout/core/titan_core_laser",
		Titanfall2TitanKit.Chassis.Legion => "rui/titan_loadout/core/titan_core_smart",
		Titanfall2TitanKit.Chassis.Tone => "rui/titan_loadout/core/titan_core_salvo",
		Titanfall2TitanKit.Chassis.Scorch => "rui/titan_loadout/core/titan_core_flame_wave",
		Titanfall2TitanKit.Chassis.Northstar => "rui/titan_loadout/core/titan_core_flight",
		Titanfall2TitanKit.Chassis.Ronin => "rui/titan_loadout/core/titan_core_sword",
		_ => null
	};
	static string WeaponArt( string path ) => path?.ToLowerInvariant() switch
	{
		{ } p when p.Contains( "xo16" ) => "r2_ui/menus/loadout_icons/titan_weapon/titan_weapon_xo16",
		{ } p when p.Contains( "predator" ) => "r2_ui/menus/loadout_icons/titan_weapon/titan_weapon_predator",
		{ } p when p.Contains( "particle_accelerator" ) => "r2_ui/menus/loadout_icons/titan_weapon/titan_weapon_particle_ac",
		{ } p when p.Contains( "leadwall" ) || p.Contains( "triple_threat" ) => "r2_ui/menus/loadout_icons/titan_weapon/titan_weapon_leadwall",
		{ } p when p.Contains( "sniper" ) => "r2_ui/menus/loadout_icons/titan_weapon/titan_weapon_railgun",
		{ } p when p.Contains( "40mm" ) => "r2_ui/menus/loadout_icons/titan_weapon/titan_weapon_40mm",
		{ } p when p.Contains( "meteor" ) => "r2_ui/menus/loadout_icons/titan_weapon/titan_weapon_thermite_cannon",
		{ } p when p.Contains( "sword" ) => "r2_ui/menus/loadout_icons/titan_weapon/titan_weapon_sword",
		_ => null
	};
}
