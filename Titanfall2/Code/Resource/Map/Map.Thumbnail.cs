using System.Text;
using System.Text.RegularExpressions;

partial class MapLoader
{
	// SceneLoader also falls back to Mounts/{ident}/thumbs/{relative}.png.
	// Retail images avoid building an entire BSP just to populate the browser.
	protected override Texture GetThumbnail() => Host.CreateMapThumbnail( Path );
}

public partial class Titanfall2Mount
{
	readonly Dictionary<string, (Titanfall2UiAtlasSource Source, Titanfall2UiAtlasImage Image)> _mapPreviewImages = new( StringComparer.OrdinalIgnoreCase );
	Dictionary<string, string> _mapPreviewNames = new( StringComparer.OrdinalIgnoreCase );

	void ReadMapPreviewMappings( Titanfall2ContentSnapshot snapshot )
	{
		_mapPreviewImages.Clear();
		_mapPreviewNames.Clear();
		if ( snapshot.GetSource( "scripts/vscripts/ui/menu_private_match.nut" ) is { } source
			&& source.TryReadAllBytes( out var bytes, out _ ) && bytes.Length <= 512 * 1024 )
			_mapPreviewNames = ParseMapPreviewMappings( Encoding.UTF8.GetString( bytes ) );
	}

	internal static Dictionary<string, string> ParseMapPreviewMappings( string script )
	{
		var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		// Read data without executing Squirrel. Keep authored aliases such as
		// angel_city -> angle_city_r2 and lf_stacks -> stacks.
		foreach ( Match match in Regex.Matches( script ?? "", "(?m)^\\s*(mp_[a-z0-9_]+)\\s*=\\s*\\$\"(loadscreens/[a-z0-9_/]+)\"", RegexOptions.IgnoreCase ) )
			result.TryAdd( match.Groups[1].Value, match.Groups[2].Value );
		return result;
	}

	internal Texture CreateMapThumbnail( string path )
	{
		var map = System.IO.Path.GetFileName( path ?? "" ).Split( '.' )[0];
		var image = _mapPreviewNames.GetValueOrDefault( map, $"loadscreens/{map}_lobby" );
		try
		{
			// SceneLoader destroys its thumbnail at shutdown. Give it its own texture
			// instead of destroying one shared with the HUD/UI resource loader.
			if ( _mapPreviewImages.TryGetValue( image, out var entry ) )
				return Titanfall2UiAtlasTextureLoader.CreateTexture( entry.Source, entry.Image, _uiAtlasPixels );
			var texture = Texture.Load( $"mount://{Ident}/{image}.vtex", false );
			if ( !texture.IsValid() || texture.Width <= 1 || texture.Height <= 1 ) return null;
			return Texture.Create( texture.Width, texture.Height, ImageFormat.RGBA8888 )
				.WithStaticUsage().WithData<Color32>( texture.GetPixels() ).Finish();
		}
		catch ( Exception error )
		{
			Titanfall2Log.Warning( $"TF2 map preview '{map}' unavailable: {error.Message}" );
			return null;
		}
	}
}
