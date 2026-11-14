/// <summary>
/// Sandboxed mounts cannot read process environment variables. Import overrides live
/// in the package's data filesystem, retaining the established option names.
/// </summary>
static class Titanfall2Options
{
	static Dictionary<string, string> _values;
	internal static string Get( string name )
	{
		if ( _values is null )
		{
			try { _values = FileSystem.Data.ReadJsonOrDefault<Dictionary<string, string>>( "titanfall2.settings.json", new() ); }
			catch { _values = new(); }
		}
		return _values.TryGetValue( name, out var value ) ? value : null;
	}
}

/// <summary>Bump when serialized importer data or its interpretation changes.</summary>
static class Titanfall2BuildIdentity
{
	internal static readonly Guid VersionId = new( "632a1fb1-9736-4c5a-8a06-f5a26b69d701" );
}
