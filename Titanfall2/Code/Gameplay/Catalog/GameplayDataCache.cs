using System.Globalization;
using Sandbox.Rendering;

/// <summary>
/// Owns all mount-scoped Gameplay caches (pilot settings, tacticals, Titan abilities/cores,
/// NPC ordnance, combat impact tables, decals, and resolved weapon audio).
/// Cleared atomically on Titanfall2Mount.Shutdown so snapshots can never leak.
/// </summary>
internal sealed class Titanfall2GameplayCache
{
	Titanfall2PilotSettings? _pilotSettings;
	Titanfall2PilotMeleeSettings? _pilotMeleeSettings;
	Titanfall2OffhandOrdnanceConfig? _defaultOffhandOrdnance;

	Titanfall2ContentSnapshot _mortarWeaponSnapshot;
	Titanfall2TitanAbilityData _mortarWeapon;

	Titanfall2ContentSnapshot _npcGrenadeSnapshot;
	Titanfall2NpcGrenadeData _npcGrenadeData;

	Titanfall2ContentSnapshot _pilotTacticalSnapshot;
	Titanfall2PilotTacticalSettings _pilotTacticalSettings = Titanfall2PilotTacticalSettings.RetailDefault;

	Titanfall2ContentSnapshot _salvoCoreSnapshot;
	Titanfall2TitanAbilityData _salvoCoreOrdnance;

	Titanfall2ContentSnapshot _flightCoreSnapshot;
	Titanfall2TitanAbilityData _flightCoreOrdnance;

	Titanfall2ContentSnapshot _ionSnapshot;
	Titanfall2IonData _ionData;

	Titanfall2ContentSnapshot _titanAbilitySnapshot;
	readonly Dictionary<string, Titanfall2TitanAbilityData> _titanAbilities = new( StringComparer.OrdinalIgnoreCase );

	readonly Dictionary<string, IReadOnlyList<Titanfall2VmtDefinition.VmtBlock>> _combatTables = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, DecalDefinition> _combatDecals = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, string[]> _ordnanceSoundSamples = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, Titanfall2WeaponAudio> _weaponAudio = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> _missingWeaponEvents = new( StringComparer.OrdinalIgnoreCase );

	internal void Reset()
	{
		_pilotSettings = null;
		_pilotMeleeSettings = null;
		_defaultOffhandOrdnance = null;
		_mortarWeaponSnapshot = null;
		_mortarWeapon = null;
		_npcGrenadeSnapshot = null;
		_npcGrenadeData = null;
		_pilotTacticalSnapshot = null;
		_pilotTacticalSettings = Titanfall2PilotTacticalSettings.RetailDefault;
		_salvoCoreSnapshot = null;
		_salvoCoreOrdnance = null;
		_flightCoreSnapshot = null;
		_flightCoreOrdnance = null;
		_ionSnapshot = null;
		_ionData = null;
		_titanAbilitySnapshot = null;
		_titanAbilities.Clear();
		_combatTables.Clear();
		_combatDecals.Clear();
		_ordnanceSoundSamples.Clear();
		_weaponAudio.Clear();
		_missingWeaponEvents.Clear();
		Titanfall2PhaseDimension.ClearCameraRefs();
		Titanfall2TitanChassisCatalog.Reset();
		Titanfall2NpcVoiceCatalog.Reset();
		Titanfall2ImpactAudioAliasCatalog.Reset();
	}

	internal Titanfall2PilotSettings GetPilotSettings( Titanfall2ContentSnapshot snapshot )
	{
		if ( _pilotSettings is { } cached ) return cached;
		if ( snapshot is null ) return Titanfall2PilotSettings.RetailDefault;
		var settings = Titanfall2PilotSettings.Read( path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path, int.MaxValue ) );
		_pilotSettings = settings;
		return settings;
	}

	internal Titanfall2PilotMeleeSettings GetPilotMeleeSettings( Titanfall2ContentSnapshot snapshot )
	{
		if ( _pilotMeleeSettings is { } cached ) return cached;
		if ( snapshot is null ) return Titanfall2PilotMeleeSettings.RetailDefault;
		var settings = Titanfall2PilotMeleeSettings.Read( path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path, int.MaxValue ) );
		_pilotMeleeSettings = settings;
		return settings;
	}

	internal Titanfall2OffhandOrdnanceConfig GetDefaultOffhandOrdnance( Titanfall2ContentSnapshot snapshot, string mountIdent )
	{
		if ( _defaultOffhandOrdnance is { } cached ) return cached;
		if ( snapshot is null ) return Titanfall2OffhandOrdnanceConfig.RetailFragDefault;

		const string scriptPath = Titanfall2WeaponData.Folder + "mp_weapon_frag_grenade.txt";
		var data = Titanfall2WeaponData.Read( scriptPath,
			path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path, int.MaxValue ),
			projectile: true, tint: new Color( 1f, 0.6f, 0.2f ), ordnance: true,
			behavior: Titanfall2ProjectileBehavior.FragGrenade );
		if ( data is { OrdnanceProfile.Enabled: true, ViewModel: not null } )
			data = data with { OrdnanceProfile = Titanfall2OrdnanceData.WithModelTiming( snapshot, data.ViewModel, data.OrdnanceProfile ) };
		if ( data is { Projectile: { Enabled: true, ModelPath: not null } flight } )
			data = data with { Projectile = flight with { ModelPath = flight.ModelPath + ".visual" } };

		var config = Titanfall2OffhandOrdnanceConfig.FromWeaponData( "mp_weapon_frag_grenade", "Frag Grenade", data, mountIdent );
		_defaultOffhandOrdnance = config;
		return config;
	}

	internal Titanfall2PilotTacticalSettings GetPilotTacticalSettings( Titanfall2ContentSnapshot snapshot )
	{
		if ( snapshot is null ) return Titanfall2PilotTacticalSettings.RetailDefault;
		if ( ReferenceEquals( _pilotTacticalSnapshot, snapshot ) ) return _pilotTacticalSettings;
		_pilotTacticalSnapshot = snapshot;
		_pilotTacticalSettings = Titanfall2PilotTacticalSettings.Read( path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path ) );
		return _pilotTacticalSettings;
	}

	internal Titanfall2TitanAbilityData GetMortarWeapon( Titanfall2ContentSnapshot snapshot )
	{
		if ( snapshot is null ) return null;
		if ( ReferenceEquals( _mortarWeaponSnapshot, snapshot ) ) return _mortarWeapon;
		_mortarWeaponSnapshot = snapshot;
		try { _mortarWeapon = Titanfall2WeaponData.ReadMortarAbility( path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path, int.MaxValue ) ); }
		catch ( InvalidDataException error ) { Titanfall2Log.Warning( $"Mortar weapon unavailable: {error.Message}" ); _mortarWeapon = null; }
		return _mortarWeapon;
	}

	internal Titanfall2NpcGrenadeData GetNpcGrenadeData( Titanfall2ContentSnapshot snapshot )
	{
		if ( ReferenceEquals( _npcGrenadeSnapshot, snapshot ) ) return _npcGrenadeData;
		_npcGrenadeSnapshot = snapshot;
		_npcGrenadeData = null;
		try { _npcGrenadeData = Titanfall2NpcGrenadeData.Read( path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path ) ); }
		catch ( InvalidDataException error ) { Titanfall2Log.Warning( $"NPC frag grenade is unavailable: {error.Message}" ); }
		return _npcGrenadeData;
	}

	internal Titanfall2TitanAbilityData GetSalvoCoreOrdnance( Titanfall2ContentSnapshot snapshot )
	{
		if ( snapshot is null ) return null;
		if ( ReferenceEquals( _salvoCoreSnapshot, snapshot ) ) return _salvoCoreOrdnance;
		_salvoCoreSnapshot = snapshot;
		try
		{
			_salvoCoreOrdnance = Titanfall2WeaponData.ReadTitanAbility( "scripts/weapons/mp_titancore_salvo_core.txt",
				path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path ),
				1000, Titanfall2ProjectileBehavior.HomingTitan );
		}
		catch ( InvalidDataException ) { _salvoCoreOrdnance = null; }
		return _salvoCoreOrdnance;
	}

	internal Titanfall2TitanAbilityData GetFlightCoreOrdnance( Titanfall2ContentSnapshot snapshot )
	{
		if ( snapshot is null ) return null;
		if ( ReferenceEquals( _flightCoreSnapshot, snapshot ) ) return _flightCoreOrdnance;
		_flightCoreSnapshot = snapshot;
		try
		{
			_flightCoreOrdnance = Titanfall2WeaponData.ReadTitanAbility( "scripts/weapons/mp_titanweapon_flightcore_rockets.txt",
				path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path ),
				1850f, Titanfall2ProjectileBehavior.Standard );
		}
		catch ( InvalidDataException ) { _flightCoreOrdnance = null; }
		return _flightCoreOrdnance;
	}

	internal Titanfall2IonData GetIonData( Titanfall2ContentSnapshot snapshot )
	{
		if ( snapshot is null ) return null;
		if ( ReferenceEquals( _ionSnapshot, snapshot ) ) return _ionData;
		_ionSnapshot = snapshot;
		try { _ionData = Titanfall2WeaponData.ReadIonData( path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path ) ); }
		catch ( InvalidDataException ) { _ionData = null; }
		return _ionData;
	}

	internal Titanfall2TitanAbilityData GetTitanAbility( Titanfall2ContentSnapshot snapshot, Titanfall2TitanKit.Chassis kit, bool defense )
	{
		var script = Titanfall2TitanAbilityData.ScriptFor( kit, defense );
		if ( script is null || snapshot is null ) return null;
		if ( !ReferenceEquals( _titanAbilitySnapshot, snapshot ) )
		{
			_titanAbilitySnapshot = snapshot;
			_titanAbilities.Clear();
		}
		if ( _titanAbilities.TryGetValue( script, out var result ) ) return result;
		try
		{
			var profile = Titanfall2TitanChassisCatalog.Get( kit );
			result = Titanfall2WeaponData.ReadTitanAbility( $"scripts/weapons/{script}.txt",
				path => Titanfall2RetailScriptReader.ReadSnapshotText( snapshot, path ),
				profile.OffenseFallbackSpeed, profile.OffenseBehavior );
		}
		catch ( InvalidDataException ) { result = null; }
		_titanAbilities[script] = result;
		return result;
	}

	internal IReadOnlyList<Titanfall2VmtDefinition.VmtBlock> GetCombatImpactBlocks( Titanfall2ContentSnapshot snapshot, string table )
	{
		if ( string.IsNullOrWhiteSpace( table ) || table.IndexOfAny( ['/', '\\', '.'] ) >= 0 ) table = "default";
		if ( !_combatTables.TryGetValue( table, out var blocks ) )
		{
			blocks = [];
			if ( snapshot?.GetSource( $"scripts/impacts/{table}.txt" ) is { } source
				&& source.TryReadAllBytes( out var bytes, out _ ) && bytes.Length < 1024 * 1024
				&& Titanfall2VmtReader.TryReadBlocks( bytes, out var parsed, out _ ) ) blocks = parsed;
			_combatTables[table] = blocks;
		}
		return blocks;
	}

	internal string CombatImpactValue( Titanfall2ContentSnapshot snapshot, string table, string section, string material ) =>
		GetCombatImpactBlocks( snapshot, table ).FirstOrDefault()
			?.Children.FirstOrDefault( b => b.Name.Equals( section, StringComparison.OrdinalIgnoreCase ) )
			?.Values.GetValueOrDefault( material );

	internal DecalDefinition GetCombatDecal( Titanfall2Mount mount, string surface, bool scorch, string table )
	{
		var energy = table?.ToLowerInvariant() switch { "lstar" => "lstar", "exp_plasma_lg" or "exp_plasma_sm" => "plasma", "particle_accelerator" => "particle_accelerator", _ => null };
		var name = energy is not null ? $"decals/bullets/{energy}/shot1" : scorch ? "decals/scorch1"
			: surface is "M" or "G" or "V" or "E" ? "decals/bullets/metal/shot1"
			: surface == "W" ? "decals/bullets/wood/shot1" : surface is "Y" or "U" ? "decals/bullets/glass/shot1" : "decals/bullets/concrete/shot1";
		if ( _combatDecals.TryGetValue( name, out var found ) ) return found;
		var material = Material.Load( $"mount://{mount.Ident}/{name}.vmat" );
		if ( material is null || !material.IsValid || !Titanfall2DecalFactory.TryCreateDefinition( material, out var definition, out _ ) ) return _combatDecals[name] = null;
		if ( !mount.TryGetMaterialDescriptor( name, out var descriptor ) ) return _combatDecals[name] = null;
		var subrect = mount.TryGetLegacyMaterialDefinition( name + "_subrect", out var wrapper, out _ ) ? wrapper : null;
		var color = definition.ColorTexture;
		var size = subrect?.GetString( "$size" )?.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		var scale = subrect?.GetFloat( "$decalscale", scorch ? 0.25f : 0.025f ) ?? (energy is not null ? 0.3f : scorch ? 0.25f : 0.025f);
		float Number( int index, int fallback ) => size is not null && size.Length > index && float.TryParse( size[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) ? number : fallback;
		definition.Width = Number( 0, color.Width ) * scale;
		definition.Height = Number( 1, color.Height ) * scale;
		if ( !descriptor.TextureBindings.Any( binding => binding.Semantic == Titanfall2TextureSemantic.Normal ) ) definition.NormalTexture = null;
		if ( !descriptor.TextureBindings.Any( binding => binding.Semantic == Titanfall2TextureSemantic.Emissive ) ) definition.EmissiveTexture = null;
		var opacity = descriptor.TextureBindings.Any( binding => binding.Semantic == Titanfall2TextureSemantic.Opacity ) ? material.GetTexture( "g_tOpacity" ) : null;
		if ( opacity.IsValid() && opacity != Texture.White )
		{
			var pixels = color.GetPixels(); var mask = opacity.GetPixels();
			var rgba = new byte[pixels.Length * 4];
			for ( var y = 0; y < color.Height; y++ )
			for ( var x = 0; x < color.Width; x++ )
			{
				var i = y * color.Width + x; var p = pixels[i];
				var alpha = mask[(y * opacity.Height / color.Height) * opacity.Width + x * opacity.Width / color.Width].r;
				rgba[i * 4] = p.r; rgba[i * 4 + 1] = p.g; rgba[i * 4 + 2] = p.b; rgba[i * 4 + 3] = (byte)(p.a * alpha / 255);
			}
			definition.ColorTexture = Texture.Create( color.Width, color.Height ).WithData( rgba ).Finish();
		}
		definition.BlendMode = energy is not null ? DecalBlendMode.Additive : DecalBlendMode.Multiply;
		if ( energy is not null && mount.TryGetLegacyMaterialDefinition( name, out var energyMaterial, out _ ) )
		{
			var tint = energyMaterial.GetVector( "$layercolor2", Vector4.One );
			definition.Tint = new Color( tint.x, tint.y, tint.z );
		}
		return _combatDecals[name] = definition;
	}

	internal string[] GetOrdnanceSamples( string role, IEnumerable<string> bankKeys )
	{
		if ( _ordnanceSoundSamples.TryGetValue( role, out var samples ) ) return samples;
		var prefix = Titanfall2OrdnanceSounds.Prefix( role );
		return _ordnanceSoundSamples[role] = prefix is null ? [] : bankKeys
			.Where( name => name.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( name => name, StringComparer.OrdinalIgnoreCase ).ToArray();
	}

	internal Titanfall2WeaponAudio GetWeaponAudio( Titanfall2Mount mount, Titanfall2WeaponData data )
	{
		if ( _weaponAudio.TryGetValue( data.ScriptName, out var audio ) ) return audio;
		audio = data.Audio;
		string[] Sources( string name ) => mount.TryGetSoundEvent( name, out var definition )
			? definition.Layers.SelectMany( layer => layer.Choice.Sources ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray() : [];
		audio.Start1P = Sources( string.IsNullOrEmpty( audio.Player1P.Start ) ? audio.Player1P.Fire1 : audio.Player1P.Start );
		audio.Start3P = Sources( string.IsNullOrEmpty( audio.Player3P.Start ) ? audio.Player3P.Fire1 : audio.Player3P.Start );
		audio.Loop1P = Sources( audio.Player1P.Loop ); audio.Loop3P = Sources( audio.Player3P.Loop );
		audio.Tail1P = Sources( audio.Player1P.End ); audio.Tail3P = Sources( audio.Player3P.End );
		return _weaponAudio[data.ScriptName] = audio;
	}

	internal bool RecordMissingWeaponEvent( string name ) => _missingWeaponEvents.Add( name );
}

/// <summary>Single Gameplay-to-Mount cache facade, replacing 8 scattered partial mount files.</summary>
public partial class Titanfall2Mount
{
	readonly Titanfall2GameplayCache _gameplayCache = new();

	internal Titanfall2PilotSettings GetPilotSettings() => _gameplayCache.GetPilotSettings( _contentSnapshot );
	internal Titanfall2PilotMeleeSettings GetPilotMeleeSettings() => _gameplayCache.GetPilotMeleeSettings( _contentSnapshot );
	internal Titanfall2OffhandOrdnanceConfig GetDefaultOffhandOrdnance() => _gameplayCache.GetDefaultOffhandOrdnance( _contentSnapshot, Ident );
	internal Titanfall2PilotTacticalSettings GetPilotTacticalSettings() => _gameplayCache.GetPilotTacticalSettings( _contentSnapshot );
	internal Titanfall2TitanAbilityData GetMortarWeapon() => _gameplayCache.GetMortarWeapon( _contentSnapshot );
	internal Titanfall2NpcGrenadeData GetNpcGrenadeData() => _gameplayCache.GetNpcGrenadeData( _contentSnapshot );
	internal Titanfall2TitanAbilityData GetSalvoCoreOrdnance() => _gameplayCache.GetSalvoCoreOrdnance( _contentSnapshot );
	internal Titanfall2TitanAbilityData GetFlightCoreOrdnance() => _gameplayCache.GetFlightCoreOrdnance( _contentSnapshot );
	internal Titanfall2IonData GetIonData() => _gameplayCache.GetIonData( _contentSnapshot );
	internal Titanfall2TitanAbilityData GetTitanAbility( Titanfall2TitanKit.Chassis kit, bool defense = false ) => _gameplayCache.GetTitanAbility( _contentSnapshot, kit, defense );
	internal IReadOnlyList<Titanfall2VmtDefinition.VmtBlock> GetCombatImpactBlocks( string table ) => _gameplayCache.GetCombatImpactBlocks( _contentSnapshot, table );
	internal string CombatImpactValue( string table, string section, string material ) => _gameplayCache.CombatImpactValue( _contentSnapshot, table, section, material );
	internal DecalDefinition GetCombatDecal( string surface, bool scorch, string table ) => _gameplayCache.GetCombatDecal( this, surface, scorch, table );
	internal string[] GetOrdnanceSamples( string role ) => _gameplayCache.GetOrdnanceSamples( role, _milesSoundAssets.Keys );
	internal Titanfall2WeaponAudio GetWeaponAudio( Titanfall2WeaponData data ) => _gameplayCache.GetWeaponAudio( this, data );

	internal string CombatSurfaceMaterial( Surface surface )
	{
		var path = surface?.ResourcePath ?? "";
		var marker = path.IndexOf( "surfaceproperties/", StringComparison.OrdinalIgnoreCase );
		if ( marker < 0 ) return null;
		var name = Uri.UnescapeDataString( path[(marker + 18)..] );
		if ( name.EndsWith( ".surface", StringComparison.OrdinalIgnoreCase ) ) name = name[..^8];
		return _surfaceCatalog?.Resolve( name )?.GetValueOrDefault( "gamematerial" )?.ToUpperInvariant();
	}
}
