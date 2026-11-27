using System.Text;

public partial class Titanfall2Mount
{
	internal static void AddGameplayRegistrations( List<ContentRegistration> catalog, Titanfall2ContentSnapshot snapshot, Action<string> warn )
	{
		Titanfall2TitanChassisCatalog.Reload( snapshot );
		Titanfall2NpcVoiceCatalog.Reload( snapshot );
		Titanfall2ImpactAudioAliasCatalog.Reload( snapshot );

		var models = catalog.Where( item => item.Type == ResourceType.Model ).Select( item => item.Path ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var weapons = new Dictionary<string, Titanfall2WeaponData>( StringComparer.OrdinalIgnoreCase );
		var scriptTexts = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var entry in Titanfall2GameplayCatalogData.LoadWeapons( snapshot ) )
			RegisterWeapon( entry );

		const string TitanSwordViewModel = "models/weapons/titan_sword/atpov_titan_sword.mdl";
		if ( models.Contains( TitanSwordViewModel ) && snapshot.GetSource( TitanSwordViewModel ) is { } swordViewSource
			&& models.Add( TitanSwordViewModel + ".titanpov" ) )
		{
			catalog.Add( new( ResourceType.Model, TitanSwordViewModel + ".titanpov",
				new ModelLoader( swordViewSource, separateResources: true, pilotViewAnimations: true ) ) );
		}

		foreach ( var npc in Titanfall2GameplayCatalogData.LoadActors( snapshot ) )
		{
			if ( !models.Contains( npc.Model ) || (npc.Role == Titanfall2Npc.FrontierRole.ArcTitan && !models.Contains( Titanfall2GameplayCatalogData.ArcCannonWorldModel )) ) continue;
			Titanfall2WeaponData weapon = null;
			if ( npc.Weapon is not null && !weapons.TryGetValue( npc.Weapon, out weapon ) ) continue;
			var source = snapshot.GetSource( npc.Model );
			if ( source is null ) continue;
			if ( models.Add( npc.Model + ".infantry" ) )
				catalog.Add( new( ResourceType.Model, npc.Model + ".infantry", new ModelLoader( source, separateResources: true, infantryAnimations: true ) ) );
			if ( models.Add( npc.Model + ".preview" ) )
				catalog.Add( new( ResourceType.Model, npc.Model + ".preview", new ModelLoader( source, separateResources: true, infantryAnimations: true, previewOnly: true ) ) );
			var loader = new Titanfall2ActorPrefabLoader( npc, weapon );
			loader.Tags.Add( "npc" );
			if ( npc.Role != Titanfall2Npc.FrontierRole.None ) loader.Tags.Add( "frontier-defense" );
			if ( npc.Titan ) loader.Tags.Add( "titan" );
			else if ( npc.Navigation == Titanfall2Npc.Navigation.Ground && npc.Height >= 72 && npc.MeleeDamage == 0 ) loader.Tags.Add( "humanoid" );
			catalog.Add( new( ResourceType.PrefabFile, $"entities/{(npc.Titan ? "titans" : "npcs")}/{npc.Id}", loader ) );
		}

		var jumpKitModel = models.Contains( Titanfall2JumpKitItem.DefaultWorldModel ) ? Titanfall2JumpKitItem.DefaultWorldModel
			: models.Contains( Titanfall2JumpKitItem.FallbackWorldModel ) ? Titanfall2JumpKitItem.FallbackWorldModel : null;
		var packModel = models.Contains( Titanfall2PilotParkour.DefaultPackModel ) ? Titanfall2PilotParkour.DefaultPackModel : jumpKitModel;
		if ( jumpKitModel is not null )
		{
			var pilotSettings = Titanfall2PilotSettings.Read( ReadScript );
			var kitLoader = new Titanfall2JumpKitPrefabLoader( "Pilot Jump Kit", jumpKitModel, packModel, pilotSettings );
			kitLoader.Tags.Add( "item" );
			kitLoader.Tags.Add( "equipment" );
			kitLoader.Tags.Add( "pilot" );
			catalog.Add( new( ResourceType.PrefabFile, "entities/items/pilot_jump_kit", kitLoader ) );
		}

		void RegisterWeapon( Titanfall2WeaponCatalogEntry entry )
		{
			var script = Titanfall2WeaponData.Folder + entry.ScriptName + ".txt";
			if ( !snapshot.TryGet( script, out _ ) ) return;
			try
			{
				var weapon = Titanfall2WeaponData.Read( script, ReadScript, entry.IsTitan, entry.Projectile, entry.IsIntegrated,
					entry.FallbackSpeed, entry.Tint, entry.IsOrdnance, entry.IsAntiTitan, entry.Behavior );
				if ( weapon is null || (!entry.IsIntegrated && !models.Contains( weapon.WorldModel )) )
				{
					warn( $"TF2 weapon {entry.ScriptName} was not registered: unsupported script or missing world model." );
					return;
				}
				if ( !models.Contains( weapon.ViewModel ) ) weapon = weapon with { ViewModel = null };
				if ( weapon.OrdnanceProfile.Enabled && weapon.ViewModel is not null )
					weapon = weapon with { OrdnanceProfile = Titanfall2OrdnanceData.WithModelTiming( snapshot, weapon.ViewModel, weapon.OrdnanceProfile ) };
				if ( weapon.HeatProfile.Valid && weapon.ViewModel is not null )
					weapon = weapon with { ReloadProfile = weapon.ReloadProfile with { EmptyStages = Titanfall2HeatReloadData.ReadStages( snapshot, weapon.ViewModel ) } };
				if ( entry.FallbackSample is not null ) weapon = weapon with { FireSounds = [.. weapon.FireSounds, entry.FallbackSample] };
				if ( weapon.Projectile is { Enabled: true, ModelPath: not null } flight )
					weapon = weapon with { Projectile = flight with { ModelPath = models.Contains( flight.ModelPath ) ? flight.ModelPath + ".visual" : null } };
				weapons.Add( entry.Key, weapon );
				if ( entry.IsTitan )
				{
					if ( weapon.ViewModel is { } titanViewPath
						&& snapshot.GetSource( titanViewPath ) is { } titanViewSource
						&& models.Add( titanViewPath + ".titanpov" ) )
					{
						catalog.Add( new( ResourceType.Model, titanViewPath + ".titanpov",
							new ModelLoader( titanViewSource, separateResources: true, pilotViewAnimations: true ) ) );
					}
					var armament = new Titanfall2TitanArmamentPrefabLoader( weapon, entry.ScriptName );
					armament.Tags.Add( "titan-weapon" );
					catalog.Add( new( ResourceType.PrefabFile, $"entities/titanweapons/{entry.ScriptName}", armament ) );
					return;
				}
				if ( entry.IsIntegrated ) return;
				if ( !entry.IsOrdnance )
				{
					var options = new List<Titanfall2WeaponModOption>();
					foreach ( var mod in weapon.ModCandidates )
					{
						try
						{
							var variant = Titanfall2WeaponData.Read( script, ReadScript, entry.IsTitan, entry.Projectile, entry.IsIntegrated,
								entry.FallbackSpeed, entry.Tint, entry.IsOrdnance, entry.IsAntiTitan, entry.Behavior, selectedMods: [mod] );
							if ( variant is not null ) options.Add( Titanfall2WeaponData.ModOption( mod, variant with
								{ ReloadProfile = variant.ReloadProfile with { EmptyStages = weapon.ReloadProfile.EmptyStages } } ) );
						}
						catch ( InvalidDataException error ) { warn( $"TF2 attachment {entry.ScriptName}/{mod} skipped: {error.Message}" ); }
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
				var loader = new Titanfall2WeaponPrefabLoader( entry.Title, weapon, pilotView );
				loader.Tags.Add( "weapon" );
				loader.Tags.Add( entry.IsOrdnance ? "ordnance" : "ranged" );
				if ( entry.Projectile ) loader.Tags.Add( "projectile" );
				if ( entry.IsAntiTitan ) loader.Tags.Add( "anti-titan" );
				catalog.Add( new( ResourceType.PrefabFile, $"entities/weapons/{entry.ScriptName}", loader ) );
			}
			catch ( InvalidDataException error ) { warn( $"TF2 weapon {entry.ScriptName} was not registered: {error.Message}" ); }
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

	internal string GetGameplayFireSound( string[] sounds )
	{
		foreach ( var sound in sounds )
		{
			var key = NormalizeSoundKey( sound );
			if ( _soundFileSources.ContainsKey( key + ".wav" ) || _milesSoundAssets.ContainsKey( key ) ) return sound;
		}
		return null;
	}
}
