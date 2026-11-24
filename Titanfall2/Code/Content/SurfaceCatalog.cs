using System.Globalization;
using System.Text.Json;

/// <summary>Snapshot-bound retail surface definitions. No Source 1 gamematerial conversion.</summary>
sealed class Titanfall2SurfaceCatalog( Titanfall2ContentSnapshot snapshot )
{
	readonly object _gate = new();
	Dictionary<string, Dictionary<string, string>> _definitions;
	Dictionary<string, Dictionary<string, string>> Definitions
	{
		get { lock ( _gate ) return _definitions ??= ReadDefinitions(); }
	}

	Dictionary<string, Dictionary<string, string>> ReadDefinitions()
	{
		var definitions = new Dictionary<string, Dictionary<string, string>>( StringComparer.OrdinalIgnoreCase );
		var manifest = snapshot.GetSource( "scripts/surfaceproperties_manifest.txt" );
		var files = new List<string>();
		if ( manifest is not null )
		{
			foreach ( var block in ReadBlocks( manifest ) )
				if ( block.Name.Equals( "surfaceproperties_manifest", StringComparison.OrdinalIgnoreCase ) )
					files.AddRange( block.OrderedValues.Where( pair => pair.Key.Equals( "file", StringComparison.OrdinalIgnoreCase ) ).Select( pair => pair.Value ) );
		}
		else files.Add( "scripts/surfaceproperties.txt" );
		if ( files.Count > 256 ) throw new InvalidDataException( "Too many surface definition files." );
		foreach ( var file in files.Distinct( StringComparer.OrdinalIgnoreCase ) )
		{
			if ( !Titanfall2ContentSnapshot.TryNormalize( file, out var path ) || !path.StartsWith( "scripts/", StringComparison.OrdinalIgnoreCase ) )
				throw new InvalidDataException( $"Invalid surface script path '{file}'." );
			var source = snapshot.GetSource( path );
			if ( source is null ) throw new InvalidDataException( $"Surface script '{path}' is missing from the content snapshot." );
			foreach ( var block in ReadBlocks( source ) )
			{
				if ( !definitions.TryGetValue( block.Name, out var properties ) ) definitions[block.Name] = properties = new( StringComparer.OrdinalIgnoreCase );
				// Retail explicitly specifies that later files override earlier properties.
				foreach ( var pair in block.Values ) properties[pair.Key] = pair.Value;
			}
		}
		return definitions;
	}

	static IReadOnlyList<Titanfall2VmtDefinition.VmtBlock> ReadBlocks( ITitanfall2AssetSource source )
	{
		if ( !source.TryReadAllBytes( out var bytes, out var error ) ) throw new InvalidDataException( error );
		if ( bytes.Length > 4 * 1024 * 1024 ) throw new InvalidDataException( "Surface script exceeds 4 MB." );
		if ( !Titanfall2VmtReader.TryReadBlocks( bytes, out var blocks, out error ) ) throw new InvalidDataException( error );
		return blocks;
	}

	internal bool Contains( string name ) => Definitions.ContainsKey( name );
	internal string Index( string path ) => JsonSerializer.Serialize( Definitions.Keys.OrderBy( name => name, StringComparer.OrdinalIgnoreCase )
		.Select( name => new { Name = name, Surface = $"{path}/{Uri.EscapeDataString( name.ToLowerInvariant() )}.surface", Metadata = $"{path}/{Uri.EscapeDataString( name.ToLowerInvariant() )}.json" } ) );

	internal Dictionary<string, string> Resolve( string name )
	{
		if ( !Definitions.ContainsKey( name ) ) return null;
		var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		Inherit( name );
		return result;
		void Inherit( string current )
		{
			if ( visited.Count >= 64 || !visited.Add( current ) ) throw new InvalidDataException( $"Cyclic or deep surface inheritance at '{current}'." );
			if ( !Definitions.TryGetValue( current, out var values ) ) throw new InvalidDataException( $"Missing surface base '{current}'." );
			if ( !current.Equals( "default", StringComparison.OrdinalIgnoreCase ) )
			{
				var parent = values.GetValueOrDefault( "base", "default" );
				if ( Definitions.ContainsKey( parent ) || values.ContainsKey( "base" ) ) Inherit( parent );
			}
			foreach ( var pair in values ) result[pair.Key] = pair.Value;
		}
	}

	internal Surface CreateSurface( string name )
	{
		var values = Resolve( name );
		if ( values is null ) return null;
		var surface = new Surface { Description = $"Titanfall 2: {name}", Tags = $"titanfall2 surface_{name.ToLowerInvariant()}" };
		surface.Friction = Number( "friction", surface.Friction );
		surface.Elasticity = Number( "elasticity", surface.Elasticity );
		surface.Density = Number( "density", surface.Density );
		return surface;
		float Number( string key, float fallback ) => values.TryGetValue( key, out var text )
			&& float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) && value >= 0 ? value : fallback;
	}
}

sealed class Titanfall2SurfaceCatalogLoader( Titanfall2SurfaceCatalog catalog ) : ResourceLoader<Titanfall2Mount>
{
	protected override object Load() => catalog.Index( Path );
	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		var surface = name.EndsWith( ".surface", StringComparison.OrdinalIgnoreCase );
		if ( !surface && !name.EndsWith( ".json", StringComparison.OrdinalIgnoreCase ) ) return null;
		if ( name.Contains( '/' ) || name.Contains( '\\' ) ) return null;
		var key = Uri.UnescapeDataString( name[..^(surface ? 8 : 5)] );
		if ( string.IsNullOrWhiteSpace( key ) ) return null;
		type = surface ? ResourceType.GameResource : ResourceType.Text;
		return new SurfaceLoader( catalog, key, surface );
	}
	sealed class SurfaceLoader( Titanfall2SurfaceCatalog catalog, string name, bool surface ) : ResourceLoader<Titanfall2Mount>
	{
		protected override object Load()
		{
			if ( surface ) return catalog.CreateSurface( name );
			var properties = catalog.Resolve( name );
			return properties is null ? null : JsonSerializer.Serialize( new { Format = "Titanfall2.SurfaceProperties", Name = name, Properties = properties } );
		}
	}
}

public partial class Titanfall2Mount
{
	Titanfall2SurfaceCatalog _surfaceCatalog;
	internal Surface ResolveOriginalSurface( string name ) => _surfaceCatalog?.Contains( name ) == true
		? Surface.FindByName( $"mount://{Ident}/surfaceproperties/{Uri.EscapeDataString( name.ToLowerInvariant() )}.surface" ) : null;
}
