/// <summary>
/// Sandboxed mounts cannot read process environment variables. Import overrides live
/// in the package's data filesystem, retaining the established option names.
/// </summary>
static class Titanfall2Options
{
	static readonly object Sync = new();
	static volatile Dictionary<string, string> _values;

	internal static string Get( string name )
	{
		var values = _values;
		if ( values is null )
		{
			lock ( Sync )
			{
				values = _values;
				if ( values is null )
				{
					try { values = FileSystem.Data.ReadJsonOrDefault<Dictionary<string, string>>( "titanfall2.settings.json", new() ) ?? new(); }
					catch { values = new(); }
					_values = values;
				}
			}
		}
		return values.TryGetValue( name, out var value ) ? value : null;
	}

	internal static bool GetBool( string name, bool fallback )
	{
		var value = Get( name )?.Trim();
		if ( string.IsNullOrWhiteSpace( value ) ) return fallback;
		return value == "1"
			|| value.Equals( "true", StringComparison.OrdinalIgnoreCase )
			|| value.Equals( "yes", StringComparison.OrdinalIgnoreCase )
			|| value.Equals( "on", StringComparison.OrdinalIgnoreCase );
	}

	internal static int? GetOptionalInt( string name, int minimum, int maximum ) =>
		ParseOptionalInt( Get( name ), minimum, maximum );

	internal static int? ParseOptionalInt( string text, int minimum, int maximum ) =>
		int.TryParse( text, out var value ) ? Math.Clamp( value, minimum, maximum ) : null;
}

/// <summary>Bump when serialized importer data or its interpretation changes.</summary>
static class Titanfall2BuildIdentity
{
	internal static readonly Guid VersionId = new( "632a1fb1-9736-4c5a-8a06-f5a26b69d701" );
}
