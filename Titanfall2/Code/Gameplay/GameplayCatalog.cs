using System.Text;

public partial class Titanfall2Mount
{
	// Miles event names in WeaponData are not always wave keys. Use verified retail
	// one-shot samples when the event itself does not resolve; do not guess with substring matches.
	static readonly (string Id, string Title, string Sample)[] PilotWeapons =
	[
		("rspn101", "R-201 Carbine", "wpn_r101_3p_wpnfire_firstshot_close_2ch_v4_01"),
		("car", "CAR", null),
		("r97", "R-97", "wpn_r97_3p_wpnfire_firstshotwtail_close_2ch_v1_01"),
		("vinson", "V-47 Flatline", "wpn_vinson_3p_wpnfire_loop_close_2ch_v1_01"),
		("g2", "G2A5", "wpn_g2a4_3p_wpnfire_shot_close_2ch_v2_01"),
		("semipistol", "Hammond P2016", "wpn_2011pistol_3p_wpnfire_shot_close_2ch_v1_01"),
		("autopistol", "RE-45 Auto", "wpn_re45_3p_wpnfire_firstshot_mid_2ch_v1_01"),
		("alternator_smg", "Alternator", null), ("hemlok_smg", "Volt", null), ("lmg", "Spitfire", null),
		("esaw", "Devotion", null), ("dmr", "Longbow-DMR", null), ("rspn101_og", "R-101 Carbine", "wpn_r101_3p_wpnfire_firstshot_close_2ch_v4_01"),
		("wingman", "B3 Wingman", null), ("shotgun", "EVA-8 Auto", null), ("hemlok", "Hemlok BF-R", null),
		("smart_pistol", "Smart Pistol MK6", null)
	];

	static readonly (string Id, string Title, Color Tint, float FallbackSpeed, Titanfall2ProjectileBehavior Behavior)[] ProjectileWeapons =
	[
		("epg", "EPG-1", new Color( 0.25f, 0.65f, 1f ), 0, Titanfall2ProjectileBehavior.Standard),
		("lstar", "L-STAR", new Color( 1f, 0.2f, 0.15f ), 0, Titanfall2ProjectileBehavior.Standard),
		("sniper", "Kraber-AP Sniper", new Color( 1f, 0.88f, 0.55f ), 0, Titanfall2ProjectileBehavior.Standard),
		("smr", "Sidewinder SMR", new Color( 1f, 0.55f, 0.25f ), 0, Titanfall2ProjectileBehavior.Standard),
		("doubletake", "D-2 Double Take", new Color( 0.55f, 0.85f, 1f ), 0, Titanfall2ProjectileBehavior.Standard),
		("wingman_n", "Wingman Elite", new Color( 1f, 0.85f, 0.55f ), 0, Titanfall2ProjectileBehavior.Standard),
		("softball", "R-6P Softball", new Color( 1f, 0.6f, 0.2f ), 2500, Titanfall2ProjectileBehavior.Standard),
		("pulse_lmg", "EM-4 Cold War", new Color( 0.3f, 0.78f, 1f ), 3500, Titanfall2ProjectileBehavior.Standard),
		("mastiff", "Mastiff", new Color( 1f, 0.45f, 0.15f ), 4500, Titanfall2ProjectileBehavior.Standard),
		("shotgun_pistol", "SA-3 Mozambique", new Color( 1f, 0.65f, 0.25f ), 4500, Titanfall2ProjectileBehavior.Standard)
	];

	static readonly (string Id, string Title, bool Projectile, float FallbackSpeed, Color Tint, Titanfall2ProjectileBehavior Behavior)[] AntiTitanWeapons =
	[
		("defender", "Charge Rifle", false, 0, new Color( 1f, 0.55f, 0.12f ), Titanfall2ProjectileBehavior.Standard),
		("mgl", "MGL Mag Launcher", true, 1200, new Color( 1f, 0.65f, 0.2f ), Titanfall2ProjectileBehavior.Standard),
		("rocket_launcher", "Archer", true, 1350, new Color( 1f, 0.45f, 0.15f ), Titanfall2ProjectileBehavior.HomingTitan),
		("arc_launcher", "LG-97 Thunderbolt", true, 550, new Color( 0.3f, 0.72f, 1f ), Titanfall2ProjectileBehavior.ArcZap)
	];

	static readonly (string Id, string Title, Color Tint, Titanfall2ProjectileBehavior Behavior)[] PilotOrdnance =
	[
		("frag_grenade", "Frag Grenade", new Color( 1f, 0.6f, 0.2f ), Titanfall2ProjectileBehavior.FragGrenade),
		("grenade_emp", "Arc Grenade", new Color( 0.35f, 0.75f, 1f ), Titanfall2ProjectileBehavior.ArcGrenade),
		("thermite_grenade", "Firestar", new Color( 1f, 0.35f, 0.08f ), Titanfall2ProjectileBehavior.ThermiteBurn),
		("grenade_gravity", "Gravity Star", new Color( 0.45f, 0.55f, 1f ), Titanfall2ProjectileBehavior.GravityPull),
		("grenade_electric_smoke", "Electric Smoke Grenade", new Color( 0.45f, 0.75f, 1f ), Titanfall2ProjectileBehavior.ElectricSmoke),
		("satchel", "Satchel Charge", new Color( 1f, 0.35f, 0.1f ), Titanfall2ProjectileBehavior.RemoteCharge)
	];

	static readonly (string Id, float FallbackSpeed, Color Tint)[] IntegratedWeapons =
	[
		("super_spectre", 1400, new Color( 0.35f, 0.65f, 1f )),
		("turretplasma", 2000, new Color( 1f, 0.38f, 0.15f )),
		("droneplasma", 1100, new Color( 0.45f, 0.8f, 1f ))
	];

	// Retail R2 has no titan_arc_rifle model. The FD Arc chassis uses the Ronin gun.
	const string ArcCannonWorldModel = "models/weapons/titan_triple_threat/w_titan_triple_threat.mdl";

	static readonly string[] TitanWeapons = ["xo16_shorty", "xo16_vanguard", "predator_cannon"];
	static readonly (string Id, float Speed, Color Tint, Titanfall2ProjectileBehavior Behavior)[] TitanProjectileWeapons =
	[
		("particle_accelerator", 7000, new Color( 1f, 0.25f, 0.12f ), Titanfall2ProjectileBehavior.Standard),
		("leadwall", 5000, new Color( 1f, 0.75f, 0.3f ), Titanfall2ProjectileBehavior.Standard),
		("sniper", 10000, new Color( 0.4f, 0.8f, 1f ), Titanfall2ProjectileBehavior.Standard),
		("sticky_40mm", 5000, new Color( 1f, 0.65f, 0.2f ), Titanfall2ProjectileBehavior.Standard),
		("meteor", 2200, new Color( 1f, 0.3f, 0.05f ), Titanfall2ProjectileBehavior.ThermiteBurn)
	];

	sealed record NpcDefinition( string Id, string Title, string Model, string Weapon, float Health, float Speed,
		string Faction = "imc", float Height = 72, float Radius = 14, float Shield = 0, bool Titan = false, Titanfall2Npc.Navigation Navigation = Titanfall2Npc.Navigation.Ground, float MeleeDamage = 0, Titanfall2Npc.FrontierRole Role = Titanfall2Npc.FrontierRole.None );
	static readonly NpcDefinition[] Npcs =
	[
		new( "npc_soldier", "IMC Grunt", "models/humans/grunts/imc_grunt_rifle.mdl", "rspn101", 100, 155 ),
		new( "npc_spectre", "Spectre", "models/robots/spectre/imc_spectre.mdl", "car", 150, 135 ),
		new( "npc_stalker", "Stalker", "models/robots/stalker/robot_stalker.mdl", "rspn101", 240, 105 ),
		new( "npc_militia_grunt", "Militia Grunt", "models/humans/grunts/mlt_grunt_rifle.mdl", "rspn101", 100, 155, "militia" ),
		new( "npc_imc_heavy", "IMC Heavy Grunt", "models/humans/grunts/imc_grunt_lmg.mdl", "lmg", 160, 115 ),
		new( "npc_reaper", "Reaper", "models/robots/super_spectre/super_spectre_v1.mdl", "integrated:super_spectre", 650, 135, "imc", 136, 28 ),
		new( "npc_marvin", "MRVN", "models/robots/marvin/marvin.mdl", null, 85, 0, "neutral" ),
		new( "npc_plasma_turret", "Plasma Turret", "models/robots/turret_plasma/turret_plasma.mdl", "integrated:turretplasma", 350, 0, "imc", 54, 25, Navigation: Titanfall2Npc.Navigation.Stationary ),
		new( "npc_attack_drone", "Attack Drone", "models/robots/drone_air_attack/drone_air_attack_plasma.mdl", "integrated:droneplasma", 120, 240, "imc", 36, 23, Navigation: Titanfall2Npc.Navigation.Hover ),
		new( "npc_prowler", "Prowler", "models/creatures/prowler/r2_prowler.mdl", null, 180, 235, "wildlife", 52, 24, MeleeDamage: 36 ),
		new( "npc_imc_smg", "IMC SMG Grunt", "models/humans/grunts/imc_grunt_smg.mdl", "r97", 100, 175 ),
		new( "npc_imc_shotgun", "IMC Shotgun Grunt", "models/humans/grunts/imc_grunt_shotgun.mdl", "shotgun", 140, 160 ),
		new( "npc_imc_captain", "IMC Shield Captain", "models/humans/grunts/imc_grunt_shield_captain.mdl", "lmg", 220, 125, Shield: 100 ),
		new( "npc_militia_heavy", "Militia Heavy Grunt", "models/humans/grunts/mlt_grunt_lmg.mdl", "lmg", 160, 115, "militia" ),
		new( "npc_militia_smg", "Militia SMG Grunt", "models/humans/grunts/mlt_grunt_smg.mdl", "car", 100, 175, "militia" ),
		new( "npc_militia_shotgun", "Militia Shotgun Grunt", "models/humans/grunts/mlt_grunt_shotgun.mdl", "shotgun", 140, 160, "militia" ),
		new( "npc_stalker_red", "Red Stalker", "models/robots/stalker/robot_stalker_red.mdl", "lstar", 320, 125 ),
		new( "npc_imc_pilot", "IMC Pilot", "models/humans/pilots/pilot_medium_stalker_m.mdl", "car", 250, 240 ),
		new( "npc_militia_pilot", "Militia Pilot", "models/humans/pilots/pilot_medium_stalker_f.mdl", "rspn101", 250, 240, "militia" ),
		new( "npc_corporate_spectre", "Corporate Spectre", "models/robots/spectre/spectre_corporate.mdl", "alternator_smg", 180, 150 ),
		new( "npc_mossy_stalker", "Overgrown Stalker", "models/robots/stalker/robot_stalker_mossy.mdl", "shotgun", 280, 120 ),
		new( "npc_militia_spectre", "Militia Spectre", "models/robots/spectre/imc_spectre.mdl", "car", 180, 155, "militia" ),
		new( "npc_militia_stalker", "Militia Stalker", "models/robots/stalker/robot_stalker.mdl", "shotgun", 280, 120, "militia" ),
		new( "npc_mossy_marvin", "Overgrown MRVN", "models/robots/marvin/marvin_mossy.mdl", null, 85, 0, "neutral" ),
		new( "monarch", "Monarch", "models/titans/medium/titan_medium_vanguard.mdl", "mp_titanweapon_xo16_shorty", 10000, 200, "imc", 284, 54, 1600, true ),
		new( "bt7274", "BT-7274", "models/titans/buddy/titan_buddy.mdl", "mp_titanweapon_xo16_vanguard", 11000, 190, "militia", 284, 54, 1750, true ),
		new( "legion", "Legion", "models/titans/heavy/titan_heavy_deadbolt.mdl", "mp_titanweapon_predator_cannon", 12500, 150, "imc", 296, 61, 2200, true ),
		new( "ion", "Ion", "models/titans/medium/titan_medium_wraith.mdl", "mp_titanweapon_particle_accelerator", 10000, 195, "imc", 284, 54, 1600, true ),
		new( "tone", "Tone", "models/titans/medium/titan_medium_ajax.mdl", "mp_titanweapon_sticky_40mm", 10000, 185, "imc", 284, 54, 1600, true ),
		new( "scorch", "Scorch", "models/titans/heavy/titan_heavy_ogre.mdl", "mp_titanweapon_meteor", 12500, 145, "imc", 296, 61, 2200, true ),
		new( "northstar", "Northstar", "models/titans/light/titan_light_raptor.mdl", "mp_titanweapon_sniper", 7500, 220, "imc", 273, 49, 1500, true ),
		new( "ronin", "Ronin", "models/titans/light/titan_light_locust.mdl", "mp_titanweapon_leadwall", 7500, 235, "imc", 273, 49, 1500, true ),
		new( "arc_titan", "Arc Titan", "models/titans/light/titan_light_locust.mdl", null, 7500, 218, "imc", 273, 49, 1500, true, Role: Titanfall2Npc.FrontierRole.ArcTitan ),
		new( "nuke_titan", "Nuke Titan", "models/titans/heavy/titan_heavy_deadbolt.mdl", "mp_titanweapon_predator_cannon", 12500, 171, "imc", 296, 61, 2200, true, Role: Titanfall2Npc.FrontierRole.NukeTitan ),
		new( "elite_monarch", "Elite Monarch", "models/titans/medium/titan_medium_vanguard.mdl", "mp_titanweapon_xo16_vanguard", 12000, 215, "imc", 284, 54, 7500, true, Role: Titanfall2Npc.FrontierRole.EliteMonarch ),
		new( "mortar_titan", "Mortar Titan", "models/titans/medium/titan_medium_wraith.mdl", "mortar:rocketstream", 10000, 185, "imc", 284, 54, 1600, true, Role: Titanfall2Npc.FrontierRole.MortarTitan )
	];

	internal static void AddGameplayRegistrations( List<ContentRegistration> catalog, Titanfall2ContentSnapshot snapshot, Action<string> warn )
	{
		var models = catalog.Where( item => item.Type == ResourceType.Model ).Select( item => item.Path ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var weapons = new Dictionary<string, Titanfall2WeaponData>( StringComparer.OrdinalIgnoreCase );
		var scriptTexts = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		RegisterWeapon( "mortar:rocketstream", "mp_titanweapon_rocketeer_rocketstream", null, null, titan: false, projectile: true, integrated: true, fallbackSpeed: 2500 );
		foreach ( var (id, title, sample) in PilotWeapons ) RegisterWeapon( id, "mp_weapon_" + id, title, sample, titan: false );
		foreach ( var id in TitanWeapons ) RegisterWeapon( "mp_titanweapon_" + id, "mp_titanweapon_" + id, null, null, titan: true );
		foreach ( var (id, speed, tint, behavior) in TitanProjectileWeapons )
			RegisterWeapon( "mp_titanweapon_" + id, "mp_titanweapon_" + id, null, null, titan: true, projectile: true, fallbackSpeed: speed, tint: tint, behavior: behavior );
		foreach ( var (id, title, tint, speed, behavior) in ProjectileWeapons ) RegisterWeapon( id, "mp_weapon_" + id, title, null, false, projectile: true, fallbackSpeed: speed, tint: tint, behavior: behavior );
		foreach ( var (id, title, proj, speed, tint, behavior) in AntiTitanWeapons ) RegisterWeapon( id, "mp_weapon_" + id, title, null, false, projectile: proj, fallbackSpeed: speed, tint: tint, antiTitan: true, behavior: behavior );
		foreach ( var (id, title, tint, behavior) in PilotOrdnance ) RegisterWeapon( id, "mp_weapon_" + id, title, null, false, projectile: true, fallbackSpeed: 1100, tint: tint, ordnance: true, behavior: behavior );
		foreach ( var (id, speed, tint) in IntegratedWeapons ) RegisterWeapon( "integrated:" + id, "mp_weapon_" + id, null, null, false, projectile: true, integrated: true, fallbackSpeed: speed, tint: tint );
		foreach ( var npc in Npcs )
		{
			if ( !models.Contains( npc.Model ) || (npc.Role == Titanfall2Npc.FrontierRole.ArcTitan && !models.Contains( ArcCannonWorldModel )) ) continue;
			Titanfall2WeaponData weapon = null;
			if ( npc.Weapon is not null && !weapons.TryGetValue( npc.Weapon, out weapon ) ) continue;
			var source = snapshot.GetSource( npc.Model );
			if ( source is null ) continue;
			if ( models.Add( npc.Model + ".infantry" ) )
				catalog.Add( new( ResourceType.Model, npc.Model + ".infantry", new ModelLoader( source, separateResources: true, infantryAnimations: true ) ) );
			if ( models.Add( npc.Model + ".preview" ) )
				catalog.Add( new( ResourceType.Model, npc.Model + ".preview", new ModelLoader( source, separateResources: true, infantryAnimations: true, previewOnly: true ) ) );
			var loader = new GameplayPrefabLoader( npc.Title, weapon, npc );
			loader.Tags.Add( "npc" );
			if ( npc.Role != Titanfall2Npc.FrontierRole.None ) loader.Tags.Add( "frontier-defense" );
			if ( npc.Titan ) loader.Tags.Add( "titan" );
			else if ( npc.Navigation == Titanfall2Npc.Navigation.Ground && npc.Height >= 72 && npc.MeleeDamage == 0 ) loader.Tags.Add( "humanoid" );
			catalog.Add( new( ResourceType.PrefabFile, $"entities/{(npc.Titan ? "titans" : "npcs")}/{npc.Id}", loader ) );
		}

		void RegisterWeapon( string key, string name, string title, string sample, bool titan, bool projectile = false, bool integrated = false, float fallbackSpeed = 0, Color? tint = null, bool ordnance = false, bool antiTitan = false, Titanfall2ProjectileBehavior behavior = Titanfall2ProjectileBehavior.Standard )
		{
			var script = Titanfall2WeaponData.Folder + name + ".txt";
			if ( !snapshot.TryGet( script, out _ ) ) return;
			try
			{
				var weapon = Titanfall2WeaponData.Read( script, ReadScript, titan, projectile, integrated, fallbackSpeed, tint, ordnance, antiTitan, behavior );
				if ( weapon is null || (!integrated && !models.Contains( weapon.WorldModel )) )
				{
					warn( $"TF2 weapon {name} was not registered: unsupported script or missing world model." );
					return;
				}
				if ( !models.Contains( weapon.ViewModel ) ) weapon = weapon with { ViewModel = null };
				if ( weapon.OrdnanceProfile.Enabled && weapon.ViewModel is not null )
					weapon = weapon with { OrdnanceProfile = Titanfall2OrdnanceData.WithModelTiming( snapshot, weapon.ViewModel, weapon.OrdnanceProfile ) };
				if ( weapon.HeatProfile.Valid && weapon.ViewModel is not null )
					weapon = weapon with { ReloadProfile = weapon.ReloadProfile with { EmptyStages = Titanfall2HeatReloadData.ReadStages( snapshot, weapon.ViewModel ) } };
				if ( sample is not null ) weapon = weapon with { FireSounds = [.. weapon.FireSounds, sample] };
				if ( weapon.Projectile is { Enabled: true, ModelPath: not null } flight )
					weapon = weapon with { Projectile = flight with { ModelPath = models.Contains( flight.ModelPath ) ? flight.ModelPath + ".visual" : null } };
				weapons.Add( key, weapon );
				if ( titan )
				{
					var armament = new TitanArmamentPrefabLoader( weapon, name );
					armament.Tags.Add( "titan-weapon" );
					catalog.Add( new( ResourceType.PrefabFile, $"entities/titanweapons/{name}", armament ) );
					return;
				}
				if ( integrated ) return;
				if ( !ordnance )
				{
					var options = new List<Titanfall2WeaponModOption>();
					foreach ( var mod in weapon.ModCandidates )
					{
						try
						{
							var variant = Titanfall2WeaponData.Read( script, ReadScript, titan, projectile, integrated, fallbackSpeed, tint,
								ordnance, antiTitan, behavior, selectedMods: [mod] );
							if ( variant is not null ) options.Add( Titanfall2WeaponData.ModOption( mod, variant with
								{ ReloadProfile = variant.ReloadProfile with { EmptyStages = weapon.ReloadProfile.EmptyStages } } ) );
						}
						catch ( InvalidDataException error ) { warn( $"TF2 attachment {name}/{mod} skipped: {error.Message}" ); }
					}
					weapon = weapon with { AttachmentOptions = options.ToArray() };
				}
				if ( models.Add( weapon.WorldModel + ".held" ) && snapshot.GetSource( weapon.WorldModel ) is { } heldSource )
					catalog.Add( new( ResourceType.Model, weapon.WorldModel + ".held", new ModelLoader( heldSource, separateResources: true, heldWeapon: true ) ) );
				if ( models.Add( weapon.WorldModel + ".preview" ) && snapshot.GetSource( weapon.WorldModel ) is { } previewSource )
					catalog.Add( new( ResourceType.Model, weapon.WorldModel + ".preview", new ModelLoader( previewSource, separateResources: true, heldWeapon: true, previewOnly: true ) ) );
				var pilotView = weapon.ViewModel is { } viewPath
					&& models.Contains( Titanfall2PilotViewModel.PilotArms )
					&& snapshot.GetSource( viewPath ) is { };
				if ( pilotView && models.Add( weapon.ViewModel + ".pov" ) )
					catalog.Add( new( ResourceType.Model, weapon.ViewModel + ".pov",
						new ModelLoader( snapshot.GetSource( weapon.ViewModel ), separateResources: true, pilotViewAnimations: true ) ) );
				var loader = new GameplayPrefabLoader( title, weapon, pilotView: pilotView );
				loader.Tags.Add( "weapon" );
				loader.Tags.Add( ordnance ? "ordnance" : "ranged" );
				if ( projectile ) loader.Tags.Add( "projectile" );
				if ( antiTitan ) loader.Tags.Add( "anti-titan" );
				catalog.Add( new( ResourceType.PrefabFile, $"entities/weapons/{name}", loader ) );
			}
			catch ( InvalidDataException error ) { warn( $"TF2 weapon {name} was not registered: {error.Message}" ); }
		}

		string ReadScript( string path )
		{
			if ( scriptTexts.TryGetValue( path, out var cached ) ) return cached;
			var source = snapshot.GetSource( path );
			if ( source is null ) return scriptTexts[path] = null;
			if ( !source.TryReadAllBytes( out var bytes, out var error ) ) throw new InvalidDataException( $"{path}: {error}" );
			return scriptTexts[path] = Encoding.UTF8.GetString( bytes );
		}
	}

	string GetGameplayFireSound( string[] sounds )
	{
		foreach ( var sound in sounds )
		{
			var key = NormalizeSoundKey( sound );
			if ( _soundFileSources.ContainsKey( key + ".wav" ) || _milesSoundAssets.ContainsKey( key ) ) return sound;
		}
		return null;
	}

	sealed class TitanArmamentPrefabLoader( Titanfall2WeaponData data, string name ) : ResourceLoader<Titanfall2Mount>
	{
		PrefabFile _prefab;
		protected override object Load()
		{
			var model = $"mount://{Host.Ident}/{data.WorldModel}.visual.vmdl";
			var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", name ).WithMetadata( "PreviewModel", model );
			using ( builder.Scope() )
			{
				Game.ActiveScene.Enabled = false;
				var root = new GameObject( true, name );
				root.Tags.Add( "titan-weapon" );
				var gun = root.AddComponent<Titanfall2TitanArmament>();
				gun.ModelPath = model;
				gun.MountIdent = Host.Ident;
				gun.FireSound = Host.GetGameplayFireSound( data.FireSounds );
				gun.Presentation = data.Presentation;
				gun.Audio = Host.GetWeaponAudio( data );
				gun.Projectile = data.Projectile with { ModelPath = data.Projectile.ModelPath is null ? null : $"mount://{Host.Ident}/{data.Projectile.ModelPath}.vmdl", MountIdent = Host.Ident };
				gun.Damage = data.Damage;
				gun.TitanDamage = data.TitanDamage;
				gun.DamageProfile = data.DamageProfile;
				gun.Firing = data.Firing;
				gun.SpreadProfile = data.SpreadProfile;
				gun.Interval = 1 / data.ShotsPerSecond;
				gun.Magazine = data.Magazine;
				gun.ReloadSeconds = data.ReloadSeconds;
				gun.Pellets = data.Pellets;
				gun.Spread = data.Spread;
				gun.Range = MathF.Min( 6000, data.Range );
			}
			return _prefab = builder.Create();
		}
		protected override void Shutdown() { PrefabBuilder.Destroy( _prefab ); _prefab = null; }
	}

	sealed class GameplayPrefabLoader( string title, Titanfall2WeaponData data, NpcDefinition definition = null, bool pilotView = false ) : ResourceLoader<Titanfall2Mount>
	{
		PrefabFile _prefab;

		protected override object Load()
		{
			string Mounted( string name ) => name is null ? null : $"mount://{Host.Ident}/{name}.vmdl";
			var preview = Mounted( (definition is null ? data.WorldModel : definition.Model) + ".preview" );
			var builder = new PrefabBuilder().WithName( Path )
				.WithMetadata( "Title", title )
				.WithMetadata( "PreviewModel", preview )
				.WithMetadata( "PreviewSequence", null );
			using ( builder.Scope() )
			{
				Game.ActiveScene.Enabled = false;
				var root = new GameObject( true, title );
				if ( definition is null )
				{
					root.Tags.Add( "weapon" );
					var gun = root.AddComponent<Titanfall2Weapon>();
					gun.DisplayName = title;
					gun.WorldModelPath = Mounted( data.WorldModel + ".held" );
					gun.ViewModelPath = Mounted( data.ViewModel is null ? null : data.ViewModel + (pilotView ? ".pov" : ".visual") );
					gun.UseOriginalViewModel = pilotView;
					if ( pilotView ) gun.ViewArmsModelPath = Mounted( Titanfall2PilotViewModel.PilotArms + ".visual" );
					gun.MountIdent = Host.Ident;
					gun.Presentation = data.Presentation;
					gun.Audio = Host.GetWeaponAudio( data );
					gun.FireSound = Host.GetGameplayFireSound( data.FireSounds );
					gun.FireSound1P = Host.GetGameplayFireSound( data.FireSounds1P ) ?? gun.FireSound;
					gun.AdsFov = data.AdsFov;
					gun.HoldType = data.HoldType;
					gun.PreferredSlot = data.HoldType == "grenade" ? 2 : data.HoldType == "pistol" ? 1 : 0;
					gun.SmartLockEnabled = data.SmartAmmo.Enabled;
					gun.SmartAmmo = data.SmartAmmo;
					gun.PrimaryAutomatic = data.Automatic;
					gun.PrimaryDelay = 1 / data.ShotsPerSecond;
					gun.SpinUp = data.SpinUp;
					gun.ChargeProfile = data.ChargeProfile;
					gun.ChargePresentation = data.ChargePresentation;
					gun.HeatProfile = data.HeatProfile;
					gun.OrdnanceProfile = data.OrdnanceProfile;
					gun.CanCancelReload = !data.HeatProfile.Valid;
					gun.UsesClips = !data.ChargeProfile.Valid;
					gun.AutoReload = !data.ChargeProfile.Valid && !data.OrdnanceProfile.Enabled;
					gun.ClipMaxSize = data.Magazine;
					gun.Firing = data.Firing;
					gun.SpreadProfile = data.SpreadProfile;
					gun.ReloadTime = data.ReloadSeconds;
					gun.ViewKick = data.ViewKick;
					gun.UiDisplays = data.UiDisplays;
					gun.AttachmentBaseTitle = title;
					gun.AttachmentOptions = data.AttachmentOptions;
					gun.ReloadProfile = data.ReloadProfile;
					gun.Projectile = data.Projectile with { ModelPath = Mounted( data.Projectile.ModelPath ), MountIdent = Host.Ident };
					gun.TitanDamage = data.TitanDamage;
					gun.DamageProfile = data.DamageProfile;
					gun.BurstCount = data.BurstCount;
					gun.BurstRecovery = data.BurstDelay;
					gun.Ballistics = new() { Damage = data.Damage, Pellets = data.Pellets, Range = data.Range, SpreadBase = new Vector2( data.Spread, data.Spread ), SpreadGrowth = Vector2.Zero };
					var visual = new GameObject( root, true, "Dropped weapon model" );
					gun.DroppedModel = visual.AddComponent<SkinnedModelRenderer>();
					gun.DroppedModel.UseAnimGraph = false;
					root.AddComponent<BoxCollider>().Scale = data.HoldType == "pistol" ? new( 12, 5, 6 ) : new( 30, 6, 8 );
					root.AddComponent<Rigidbody>();
				}
				else
				{
					root.Tags.Add( "npc" );
					var senses = root.AddComponent<Sandbox.Npcs.Layers.SensesLayer>();
					senses.SightRange = definition.Titan ? 4200f : 2000f;
					senses.HearingRange = senses.SightRange * 0.65f;
					if ( definition.Navigation == Titanfall2Npc.Navigation.Ground && definition.Role == Titanfall2Npc.FrontierRole.None )
					{
						var navigation = root.AddComponent<Sandbox.Npcs.Layers.NavigationLayer>();
						navigation.WalkSpeed = definition.Speed * 0.65f;
						navigation.RunSpeed = definition.Speed;
					}
					var npc = root.AddComponent<Titanfall2Npc>();
					npc.DisplayName = definition.Title;
					npc.ModelPath = Mounted( definition.Model + ".infantry" );
					npc.MountIdent = Host.Ident;
					npc.Faction = definition.Faction;
					npc.VoiceProfile = definition.Id switch
					{
						"npc_soldier" or "npc_imc_heavy" or "npc_imc_smg" or "npc_imc_shotgun" or "npc_imc_captain" => "imc_grunt",
						"npc_militia_grunt" or "npc_militia_heavy" or "npc_militia_smg" or "npc_militia_shotgun" => "militia_grunt",
						"bt7274" => "bt7274",
						"npc_spectre" or "npc_stalker" or "npc_stalker_red" => "spectre",
						"npc_attack_drone" => "drone",
						_ when definition.Titan => "titan",
						_ => null
					};
					npc.MaxHealth = definition.Health;
					npc.MaxShield = definition.Shield;
					npc.IsTitan = definition.Titan;
					// Native AI settings distinguish lock class from armor. In particular
					// Reapers are large smart-ammo targets even though IsTitan is false.
					npc.SmartAmmoLockType = definition.Titan || definition.Id == "npc_reaper" ? Titanfall2SmartAmmoLockType.Large
						: definition.Model.Contains( "/marvin/", StringComparison.OrdinalIgnoreCase ) ? Titanfall2SmartAmmoLockType.None
						: definition.Id is "npc_attack_drone" or "npc_plasma_turret" ? Titanfall2SmartAmmoLockType.Any : Titanfall2SmartAmmoLockType.Small;
					if ( definition.Titan && definition.Role == Titanfall2Npc.FrontierRole.None )
					{
						var kit = root.AddComponent<Titanfall2TitanKit>();
						kit.Actor = npc;
						kit.Kit = definition.Id switch
						{
							"ion" => Titanfall2TitanKit.Chassis.Ion, "legion" => Titanfall2TitanKit.Chassis.Legion,
							"tone" => Titanfall2TitanKit.Chassis.Tone, "scorch" => Titanfall2TitanKit.Chassis.Scorch,
							"northstar" => Titanfall2TitanKit.Chassis.Northstar, "ronin" => Titanfall2TitanKit.Chassis.Ronin,
							_ => Titanfall2TitanKit.Chassis.Monarch
						};
						root.AddComponent<Titanfall2TitanCore>().Actor = npc;
					}
					npc.BodyHeight = definition.Height;
					npc.BodyRadius = definition.Radius;
					npc.NavigationMode = definition.Navigation;
					npc.MoveSpeed = definition.Speed;
					npc.MeleeOnly = definition.MeleeDamage > 0 && data is null;
					npc.MeleeDamage = definition.MeleeDamage > 0 ? definition.MeleeDamage
						: definition.Titan ? 100 : definition.Id == "npc_reaper" ? 55
						: definition.Id.Contains( "stalker", StringComparison.Ordinal ) ? 35
						: definition.Id.Contains( "spectre", StringComparison.Ordinal ) ? 28 : data is not null && definition.Navigation == Titanfall2Npc.Navigation.Ground ? 20 : 0;
					npc.MeleeRange = definition.Titan ? 190 : definition.Id == "npc_reaper" ? 125 : definition.Radius + 46;
					npc.MeleeWindup = definition.Height > 100 ? 0.4f : 0.28f;
					npc.MeleeInterval = definition.Height > 100 ? 1.6f : 1.15f;
					npc.Role = definition.Role;
					npc.CombatStyle = definition.Id.Contains( "pilot", StringComparison.OrdinalIgnoreCase ) ? Titanfall2Npc.CombatStyles.Pilot
						: definition.Id.Contains( "stalker", StringComparison.OrdinalIgnoreCase ) || definition.MeleeDamage > 0 ? Titanfall2Npc.CombatStyles.Assault : Titanfall2Npc.CombatStyles.Infantry;
					npc.UseCover = !definition.Titan && definition.Height <= 80 && definition.MeleeDamage == 0;
					npc.ReactionSeconds = npc.CombatStyle == Titanfall2Npc.CombatStyles.Pilot ? 0.12f : 0.25f;
					npc.BurstShots = definition.Titan || npc.CombatStyle == Titanfall2Npc.CombatStyles.Assault ? 6 : npc.CombatStyle == Titanfall2Npc.CombatStyles.Pilot ? 5 : 3;
					npc.BurstPause = npc.CombatStyle == Titanfall2Npc.CombatStyles.Pilot ? 0.2f : 0.45f;
					npc.CanFight = data is not null || definition.MeleeDamage > 0 || definition.Role == Titanfall2Npc.FrontierRole.ArcTitan;
					if ( definition.MeleeDamage > 0 ) npc.AttackRange = definition.Radius + 46;
					npc.AttackPlayers = npc.CanFight && definition.Faction != "militia";
					if ( data is not null )
					{
						npc.GunModelPath = Mounted( data.WorldModel is null ? null : data.WorldModel + ".visual" );
						npc.Projectile = data.Projectile with { ModelPath = Mounted( data.Projectile.ModelPath ), MountIdent = Host.Ident };
						if ( definition.Id == "npc_reaper" ) npc.Projectile = npc.Projectile with { TrailEffect = "P_plasma_proj_MD_DLight", MountIdent = Host.Ident };
						npc.FireSound = Host.GetGameplayFireSound( data.FireSounds );
						npc.WeaponPresentation = data.Presentation;
						npc.WeaponAudio = Host.GetWeaponAudio( data );
						npc.ShotInterval = 1 / data.ShotsPerSecond;
						npc.ShotDamage = data.Damage;
						npc.TitanShotDamage = data.TitanDamage;
						npc.DamageProfile = data.DamageProfile;
						npc.ShotPellets = data.Pellets;
						npc.Firing = data.Firing;
						npc.SpreadProfile = data.SpreadProfile;
						npc.ShotSpread = data.Spread;
						npc.MagazineSize = data.Magazine;
						npc.ReloadSeconds = data.ReloadSeconds;
						if ( definition.Titan )
						{
							npc.AttackRange = MathF.Min( data.Range, 2800 );
							npc.DetectionRange = 4200;
						}
					}
					if ( definition.Role == Titanfall2Npc.FrontierRole.ArcTitan )
					{
						npc.GunModelPath = Mounted( ArcCannonWorldModel + ".visual" );
						npc.DetectionRange = 4200;
						npc.AttackRange = 2400;
					}
					if ( definition.Role == Titanfall2Npc.FrontierRole.EliteMonarch ) { npc.ShotSpread *= 0.3f; npc.ShotSpreadScale = 0.3f; }
					if ( definition.Role == Titanfall2Npc.FrontierRole.MortarTitan && Host.GetMortarWeapon() is { } mortar )
					{
						npc.MortarProjectile = mortar.Projectile with { ModelPath = Mounted( mortar.Projectile.ModelPath is { } projectileModel ? projectileModel + ".visual" : null ), MountIdent = Host.Ident };
						npc.MortarDamage = mortar.Damage;
						npc.MortarTitanDamage = mortar.TitanDamage;
						npc.FireSound = "Weapon_Titan_Rocket_Launcher.RapidFire_3P";
					}
					if ( definition.Navigation == Titanfall2Npc.Navigation.Ground && definition.Speed > 0 )
					{
						npc.Agent = root.AddComponent<NavMeshAgent>();
						npc.Agent.Height = definition.Height;
						npc.Agent.Radius = definition.Radius;
						npc.Agent.MaxSpeed = definition.Speed;
						npc.Agent.UpdateRotation = false;
					}
					npc.Renderer = root.AddComponent<SkinnedModelRenderer>();
					npc.Renderer.UseAnimGraph = false;
					var hull = root.AddComponent<BoxCollider>();
					hull.Scale = new( 2 * definition.Radius - 2, 2 * definition.Radius - 2, definition.Height - 2 );
					hull.Center = Vector3.Up * (hull.Scale.z * 0.5f);
					root.AddComponent<Rigidbody>().MotionEnabled = false;
					if ( definition.Titan )
					{
						root.Tags.Add( "titan" );
					}
					if ( definition.Titan && definition.Role == Titanfall2Npc.FrontierRole.None )
					{
						npc.Seat = root.AddComponent<Titanfall2TitanSeat>();
						npc.Seat.Actor = npc;
						npc.Seat.DriveSpeed = definition.Speed;
						npc.Seat.TooltipTitle = title;
						npc.Seat.TooltipDescription = "Embark";
						npc.Seat.PitchRange = new Vector2( -65, 65 );
						npc.Seat.YawRange = new Vector2( -115, 115 );
						npc.Seat.SeatPosition = new GameObject( root, true, "Cockpit" ) { LocalPosition = new Vector3( 16, 0, definition.Height * 0.72f ) };
						npc.Seat.EyePosition = new GameObject( npc.Seat.SeatPosition, true, "Pilot View" ) { LocalPosition = new Vector3( 55, 0, 26 ) };
						npc.Seat.ExitPoints =
						[
							new GameObject( root, true, "Left Exit" ) { LocalPosition = new Vector3( 0, definition.Radius + 36, 8 ) },
							new GameObject( root, true, "Right Exit" ) { LocalPosition = new Vector3( 0, -definition.Radius - 36, 8 ) }
						];
					}
				}
			}
			return _prefab = builder.Create();
		}

		protected override void Shutdown()
		{
			PrefabBuilder.Destroy( _prefab );
			_prefab = null;
		}
	}
}
