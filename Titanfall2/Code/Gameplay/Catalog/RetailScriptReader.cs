using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Shared reader for retail Titanfall 2 KeyValues scripts and optional JSON data overlays.
/// Overlay precedence: active content snapshot (Northstar mods) -> FileSystem.Data user overrides.
/// </summary>
internal static class Titanfall2RetailScriptReader
{
	internal const int DefaultMaxScriptBytes = 512 * 1024;
	const string SnapshotOverlayPrefix = "scripts/titanfall2_sbox/";
	const string UserOverlayPrefix = "titanfall2/data/";

	static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		Converters = { new JsonStringEnumConverter( allowIntegerValues: true ) }
	};

	internal static string ReadSnapshotText( Titanfall2ContentSnapshot snapshot, string path, int maxBytes = DefaultMaxScriptBytes )
	{
		if ( snapshot is null || string.IsNullOrWhiteSpace( path ) ) return null;
		return snapshot.GetSource( path ) is { } source
			&& source.TryReadAllBytes( out var bytes, out _ )
			&& bytes.Length <= maxBytes
				? Encoding.UTF8.GetString( bytes )
				: null;
	}

	internal static bool TryReadJsonOverlay<T>( Titanfall2ContentSnapshot snapshot, string fileName, out T result )
	{
		result = default;
		if ( string.IsNullOrWhiteSpace( fileName ) ) return false;

		var snapshotJson = ReadSnapshotText( snapshot, SnapshotOverlayPrefix + fileName );
		if ( !string.IsNullOrWhiteSpace( snapshotJson ) && TryDeserialize( snapshotJson, SnapshotOverlayPrefix + fileName, out result ) )
			return true;

		try
		{
			var dataFs = FileSystem.Data;
			var userPath = UserOverlayPrefix + fileName;
			if ( dataFs is not null && dataFs.FileExists( userPath ) )
			{
				var text = dataFs.ReadAllText( userPath );
				if ( !string.IsNullOrWhiteSpace( text ) && TryDeserialize( text, userPath, out result ) )
					return true;
			}
		}
		catch ( Exception error )
		{
			Titanfall2Log.Warning( $"TF2 data overlay '{fileName}' could not be read: {error.Message}" );
		}

		return false;
	}

	static bool TryDeserialize<T>( string json, string sourceName, out T result )
	{
		try
		{
			result = JsonSerializer.Deserialize<T>( json, JsonOptions );
			return result is not null;
		}
		catch ( Exception error )
		{
			Titanfall2Log.Warning( $"TF2 data overlay '{sourceName}' failed to parse: {error.Message}" );
			result = default;
			return false;
		}
	}

	internal static bool TryParseFloat( IReadOnlyDictionary<string, string> table, string key, out float value )
	{
		value = 0f;
		return table.TryGetValue( key, out var raw )
			&& float.TryParse( raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value )
			&& float.IsFinite( value );
	}
}
