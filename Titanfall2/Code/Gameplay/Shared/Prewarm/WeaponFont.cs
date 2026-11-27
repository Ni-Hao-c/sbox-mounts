internal readonly record struct Titanfall2WeaponFontGlyph( Texture Texture, Rect Bounds, float Advance, Rect AtlasUv );

/// <summary>Original RUI metrics with a cached CPU coverage adaptation of the retail SDF atlas.</summary>
internal sealed class Titanfall2WeaponFont( RpakWeaponFontData data )
{
	readonly Dictionary<(char Character, int Size), Titanfall2WeaponFontGlyph> _glyphs = new();
	internal int FontId => data.FontId;
	internal string Name => data.Name;
	internal float Ascent => data.Ascent;
	internal float Descent => data.Descent;
	internal RpakWeaponFontData Data => data;
	internal string LastError { get; private set; }
	internal float GetKerning( char previous, char current ) => data.Kerning.GetValueOrDefault( (previous, current) );

	internal bool TryGetGlyph( char character, float fontSize, out Titanfall2WeaponFontGlyph glyph )
	{
		glyph = default;
		if ( !float.IsFinite( fontSize ) || fontSize is < 8 or > 512 || !data.Digits.TryGetValue( character, out var metrics ) )
		{ LastError = "Unsupported retail numeric glyph or font size."; return false; }
		var size = (int)MathF.Ceiling( fontSize );
		return _glyphs.TryGetValue( (character, size), out glyph ) && glyph.Texture.IsValid();
	}

	internal bool PublishGlyph( char character, float fontSize, byte[] pixels, int width, int height, out string error )
	{
		error = null;
		if ( TryGetGlyph( character, fontSize, out _ ) ) return true;
		if ( !data.Digits.TryGetValue( character, out var metrics ) || pixels is null || width <= 0 || height <= 0
			|| width > 2048 || height > 2048 || pixels.Length != width * height * 4 ) { error = "Invalid prepared numeric glyph."; return false; }
		var size = (int)MathF.Ceiling( fontSize );
		if ( _glyphs.Count >= 80 ) { error = LastError = "Retail numeric glyph cache limit reached."; return false; }
		try
		{
			var texture = Texture.Create( width, height, ImageFormat.RGBA8888 ).WithData( pixels ).WithStaticUsage().Finish();
			if ( !texture.IsValid() ) { error = LastError = "Unable to create retail numeric coverage texture."; return false; }
			var glyph = new Titanfall2WeaponFontGlyph( texture, metrics.Bounds, metrics.Advance, metrics.AtlasUv );
			_glyphs[(character, size)] = glyph; LastError = null; return true;
		}
		catch ( Exception exception ) { error = LastError = exception.Message; return false; }
	}

	internal static byte[] Rasterize( RpakWeaponFontData font, RpakWeaponFontGlyph glyph, int size, out int width, out int height )
	{
		width = Math.Max( 1, (int)MathF.Ceiling( glyph.Bounds.Width * size ) );
		height = Math.Max( 1, (int)MathF.Ceiling( glyph.Bounds.Height * size ) );
		if ( width > 2048 || height > 2048 ) throw new InvalidDataException( "Retail numeric raster exceeds budget." );
		var pixels = new byte[checked(width * height * 4)];
		var rasterWidth = width; var rasterHeight = height;
		var sourceWidth = glyph.AtlasUv.Width * font.AtlasWidth;
		var sourceHeight = glyph.AtlasUv.Height * font.AtlasHeight;
		var scale = MathF.Min( width / sourceWidth, height / sourceHeight ) * 2.236068f;
		for ( var y = 0; y < height; y++ )
			for ( var x = 0; x < width; x++ )
			{
				// The retail UI pixel shader uses sample*12.75-10, i.e. (byte-200)/20,
				// then derivative-scaled coverage and these five subpixel taps. A 0.5
				// texture threshold would make the characters much too thick.
				// This freezes coverage at the requested canvas size, without the
				// original shader's view derivatives, animated outline or glow layers.
				var coverage = Sample( x + 0.5f, y + 0.5f )
					+ Sample( x + 0.1f, y + 0.3f ) + Sample( x + 0.3f, y + 0.9f )
					+ Sample( x + 0.7f, y + 0.1f ) + Sample( x + 0.9f, y + 0.7f );
				var index = (y * width + x) * 4;
				pixels[index] = pixels[index + 1] = pixels[index + 2] = 255;
				pixels[index + 3] = (byte)Math.Clamp( (int)MathF.Round( coverage * 51 ), 0, 255 );
			}
		return pixels;

		float Sample( float x, float y )
		{
			var sx = glyph.AtlasUv.Left * font.AtlasWidth + x / rasterWidth * sourceWidth - 0.5f;
			var sy = glyph.AtlasUv.Top * font.AtlasHeight + y / rasterHeight * sourceHeight - 0.5f;
			var x0 = (int)MathF.Floor( sx ); var y0 = (int)MathF.Floor( sy );
			var fx = sx - x0; var fy = sy - y0;
			float Value( int px, int py ) => font.Atlas[Math.Clamp( py, 0, font.AtlasHeight - 1 ) * font.AtlasWidth + Math.Clamp( px, 0, font.AtlasWidth - 1 )];
			var a = Value( x0, y0 ) * (1 - fx) + Value( x0 + 1, y0 ) * fx;
			var b = Value( x0, y0 + 1 ) * (1 - fx) + Value( x0 + 1, y0 + 1 ) * fx;
			return Math.Clamp( ((a * (1 - fy) + b * fy - 200) / 20) * scale + 0.5f, 0, 1 );
		}
	}
}

public partial class Titanfall2Mount
{
	readonly Dictionary<int, Titanfall2WeaponFont> _weaponFonts = new();
	string _weaponFontRevision;
	internal void ResetWeaponFonts()
	{
		// Panels can still hold a font during a mount generation transition. Drop
		// cache ownership without forcibly disposing their live texture references.
		_weaponFonts.Clear(); _weaponFontRevision = null;
	}
	internal bool TryGetWeaponFont( int fontId, out Titanfall2WeaponFont font, out string error )
	{
		font = null; error = null;
		if ( !IsMounted || _contentSnapshot is null ) { error = "Titanfall 2 content is not mounted."; return false; }
		if ( _weaponFontRevision != _contentSnapshot.Revision ) { ResetWeaponFonts(); _weaponFontRevision = _contentSnapshot.Revision; }
		if ( _weaponFonts.TryGetValue( fontId, out font ) ) return true;
		error = $"Retail font ID {fontId} has not finished background preparation."; return false;
	}
	internal bool TryPrepareWeaponFontGlyph( int fontId, float fontSize, char character, out string error )
	{
		// Cached-only compatibility query. Preparation is admitted by the shared
		// single-worker coordinator; HUD calls never perform disk or raster work.
		if ( !TryGetWeaponFont( fontId, out var font, out error ) ) return false;
		if ( font.TryGetGlyph( character, fontSize, out _ ) ) return true;
		error = font.LastError ?? "Retail numeric glyph has not finished background preparation."; return false;
	}

	static RpakWeaponFontData ReadWeaponFontData( Titanfall2ContentSnapshot snapshot, int fontId, System.Threading.CancellationToken cancellation )
	{
		string error = "Retail font source is unavailable.";
		foreach ( var entry in snapshot.Entries.Where( entry => entry.Kind == "font" && entry.Archive is not null ) )
		{
			cancellation.ThrowIfCancellationRequested();
			if ( RpakWeaponFontReader.TryRead( entry.Archive, entry.Asset, fontId, out var data, out error ) ) return data;
		}
		throw new InvalidDataException( error );
	}

	bool PublishWeaponFontGlyph( RpakWeaponFontData data, int fontId, float fontSize, char character, byte[] pixels, int width, int height, out string error )
	{
		error = null;
		if ( !TryGetWeaponFont( fontId, out var font, out _ ) )
		{
			if ( data is null || data.FontId != fontId || _weaponFonts.Count >= 16 ) { error = "Invalid prepared retail font."; return false; }
			font = new( data ); _weaponFonts[fontId] = font;
		}
		return font.PublishGlyph( character, fontSize, pixels, width, height, out error );
	}
}
