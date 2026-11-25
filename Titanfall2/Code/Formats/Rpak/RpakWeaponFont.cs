using System.Buffers.Binary;
using System.Numerics;
using System.Text;

/// <summary>Retail RUI font v7 metrics. Positions are relative to a baseline and normalized by RUI line height.</summary>
internal sealed class RpakWeaponFontData
{
	internal int FontId;
	internal string Name, Source;
	internal float Ascent, Descent;
	internal ulong AtlasGuid;
	internal int AtlasWidth, AtlasHeight;
	internal byte[] Atlas;
	internal readonly Dictionary<char, RpakWeaponFontGlyph> Digits = new();
	internal readonly Dictionary<(char Previous, char Current), float> Kerning = new();
}

internal readonly record struct RpakWeaponFontGlyph( float Advance, Rect Bounds, Rect AtlasUv );

internal static class RpakWeaponFontReader
{
	internal static bool TryRead( RpakArchive archive, RpakAsset asset, int fontId, out RpakWeaponFontData font, out string error )
	{
		font = null; error = null;
		try
		{
			if ( asset.Type != "font" || asset.Version != 7 || !archive.TryReadAsset( asset, out var data, out error ) ) return false;
			var head = data.Header;
			if ( head is null || head.Length < 40 ) { error = "Truncated RUI font header."; return false; }
			var count = U16( head, 0 );
			if ( count is < 1 or > 64 ) { error = "Invalid RUI font face count."; return false; }
			var faces = Read( Pointer( head, 16 ), 0, count * 96 );
			byte[] face = null;
			for ( var i = 0; i < count; i++ )
				if ( U16( faces, i * 96 + 8 ) == fontId ) { face = faces.AsSpan( i * 96, 96 ).ToArray(); break; }
			if ( face is null ) { error = $"RUI font ID {fontId} is absent."; return false; }
			var nameBytes = Read( Pointer( face, 0 ), 0, 128 );
			var nameEnd = Array.IndexOf( nameBytes, (byte)0 );
			if ( nameEnd <= 0 ) { error = "RUI font name is invalid."; return false; }
			var result = new RpakWeaponFontData { FontId = fontId, Name = Encoding.UTF8.GetString( nameBytes, 0, nameEnd ), Source = archive.FilePath,
				Ascent = F32( face, 36 ), Descent = F32( face, 40 ), AtlasGuid = BinaryPrimitives.ReadUInt64LittleEndian( head.AsSpan( 32, 8 ) ),
				AtlasWidth = U16( head, 4 ), AtlasHeight = U16( head, 6 ) };
			var uvScaleX = F32( face, 28 ); var uvScaleY = F32( face, 32 );
			if ( !float.IsFinite( result.Ascent ) || !float.IsFinite( result.Descent ) || result.Ascent is <= 0 or > 2
				|| result.Descent is < 0 or > 2 || !float.IsFinite( uvScaleX ) || !float.IsFinite( uvScaleY )
				|| uvScaleX <= 0 || uvScaleY <= 0 ) { error = "Invalid RUI font scale."; return false; }
			var unicodeBase = BinaryPrimitives.ReadUInt32LittleEndian( face.AsSpan( 20, 4 ) );
			var blocks = U16( face, 14 );
			if ( blocks is < 1 or > 4096 ) { error = "Invalid RUI Unicode block count."; return false; }
			var blockMap = Read( Pointer( face, 48 ), 0, blocks * 2 );
			foreach ( var character in "0123456789" )
			{
				// engine!FAE80: subtract the font's Unicode base BEFORE selecting the
				// 64-codepoint block. Metronic's base is 10, not zero.
				if ( character < unicodeBase ) { error = "Digit precedes RUI Unicode base."; return false; }
				var code = (uint)character - unicodeBase;
				if ( (code >> 6) >= blocks ) { error = "Digit exceeds RUI Unicode map."; return false; }
				var mapped = U16( blockMap, (int)(code >> 6) * 2 );
				var bits = BinaryPrimitives.ReadUInt64LittleEndian( Read( Pointer( face, 64 ), mapped * 8, 8 ) );
				var bit = (int)(code & 63);
				if ( (bits & (1UL << bit)) == 0 ) { error = $"Digit {character} is absent from RUI font."; return false; }
				var index = U16( Read( Pointer( face, 56 ), mapped * 2, 2 ), 0 ) + BitOperations.PopCount( bits & ((1UL << bit) - 1) );
				var glyph = Read( Pointer( face, 80 ), checked(index * 32), 40 );
				var advance = F32( glyph, 0 );
				var left = F32( glyph, 16 ); var top = F32( glyph, 20 ); var right = F32( glyph, 24 ); var bottom = F32( glyph, 28 );
				var u = F32( glyph, 8 ); var v = F32( glyph, 12 );
				if ( new[] { advance, left, top, right, bottom, u, v }.Any( value => !float.IsFinite( value ) )
					|| advance is <= 0 or > 4 || right <= left || bottom <= top || right - left > 4 || bottom - top > 4 )
				{ error = "Invalid RUI digit metrics."; return false; }
				var bounds = new Rect( left, top, right - left, bottom - top );
				// Atlas coordinates point at the glyph baseline, not its upper-left corner.
				var uv = new Rect( u + left * uvScaleX, v + top * uvScaleY, bounds.Width * uvScaleX, bounds.Height * uvScaleY );
				if ( uv.Left < 0 || uv.Top < 0 || uv.Right > 1 || uv.Bottom > 1 ) { error = "RUI digit UV exceeds atlas."; return false; }
				result.Digits[character] = new( advance, bounds, uv );
				var kernStart = U16( glyph, 4 ); var kernEnd = U16( glyph, 36 );
				if ( kernEnd < kernStart || kernEnd - kernStart > 4096 ) { error = "Invalid RUI kerning range."; return false; }
				if ( kernEnd > kernStart )
				{
					var pairs = Read( Pointer( face, 88 ), kernStart * 8, (kernEnd - kernStart) * 8 );
					for ( var offset = 0; offset < pairs.Length; offset += 8 )
					{
						// Native draw and measure look up the PREVIOUS Unicode character
						// in the CURRENT glyph's kerning range (not a glyph ID).
						var previous = BinaryPrimitives.ReadUInt32LittleEndian( pairs.AsSpan( offset, 4 ) );
						var adjustment = F32( pairs, offset + 4 );
						if ( previous is >= '0' and <= '9' && float.IsFinite( adjustment ) && MathF.Abs( adjustment ) < 2 )
							result.Kerning[((char)previous, character)] = adjustment;
					}
				}
			}
			var textureAsset = archive.Assets.FirstOrDefault( entry => entry.Type == "txtr" && entry.Guid == result.AtlasGuid );
			if ( textureAsset.Type != "txtr" || !RpakTextureDecoder.TryRead( archive, textureAsset, out var texture, out error, 8192 ) )
			{ error ??= "RUI font atlas is absent from this package."; return false; }
			if ( texture.Format != ImageFormat.I8 || texture.ArraySize != 1 || texture.IsCube || texture.Width != result.AtlasWidth
				|| texture.Height != result.AtlasHeight || texture.Width > 8192 || texture.Height > 8192
				|| texture.Data.Length < checked(texture.Width * texture.Height) ) { error = "Unsupported RUI font atlas format."; return false; }
			result.Atlas = texture.Data; font = result; return true;

			byte[] Read( RpakPagePointer pointer, int offset, int size )
			{
				if ( offset < 0 || size < 0 || size > 1024 * 1024 || !archive.TryReadPagePointer(
					new RpakPagePointer( pointer.Index, checked(pointer.Offset + offset) ), size, out var bytes, out var readError ) )
					throw new InvalidDataException( "Invalid RUI font page range." );
				return bytes;
			}
		}
		catch ( Exception exception ) { font = null; error = exception.Message; return false; }
	}

	static ushort U16( byte[] bytes, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( bytes.AsSpan( offset, 2 ) );
	static float F32( byte[] bytes, int offset ) => BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset, 4 ) );
	static RpakPagePointer Pointer( byte[] bytes, int offset ) => new( BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( offset, 4 ) ),
		BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( offset + 4, 4 ) ) );
}
