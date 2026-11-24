using System.Buffers.Binary;
using System.Text;

/// <summary>A named image inside a retail UI atlas. UVs belong to the paired TXTR, not a fixed game revision.</summary>
readonly record struct Titanfall2UiAtlasImage( string Name, float U, float V, float SizeU, float SizeV, int DisplayWidth, int DisplayHeight )
{
	internal bool TryGetCrop( int width, int height, out (int X, int Y, int Width, int Height) crop )
	{
		crop = default;
		if ( width <= 0 || height <= 0 || !float.IsFinite( U ) || !float.IsFinite( V )
			|| !float.IsFinite( SizeU ) || !float.IsFinite( SizeV ) || U < 0 || V < 0 || SizeU <= 0 || SizeV <= 0
			|| (double)U + SizeU > 1.000001 || (double)V + SizeV > 1.000001 ) return false;
		var x = (int)((double)U * width);
		var y = (int)((double)V * height);
		var w = (int)((double)SizeU * width + 0.5);
		var h = (int)((double)SizeV * height + 0.5);
		if ( w <= 0 || h <= 0 || x > width - w || y > height - h ) return false;
		crop = (x, y, w, h);
		return true;
	}
}

sealed record Titanfall2UiAtlas( ulong TextureGuid, int Width, int Height, IReadOnlyList<Titanfall2UiAtlasImage> Images );

delegate bool Titanfall2UiAtlasPageReader( RpakPagePointer pointer, int length, out byte[] bytes, out string error );

/// <summary>Reads uimg v10/v11 metadata only. No GPU work, executable UI code, or copied game assets.</summary>
static class RpakUiAtlasReader
{
	const int MaximumImages = 16384;
	const int MaximumNameBytes = 1024;
	internal const int MaximumDimension = 16384;

	internal static bool TryRead( RpakArchive archive, RpakAsset asset, out Titanfall2UiAtlas atlas, out string error )
	{
		atlas = null;
		if ( asset.Type != "uimg" ) { error = "Asset is not a UI image atlas."; return false; }
		if ( !archive.TryReadAsset( asset, out var data, out error ) ) return false;
		return TryRead( asset.Version, data.Header, asset.Data, archive.TryReadPagePointer, out atlas, out error );
	}

	internal static bool TryRead( int version, byte[] header, RpakPagePointer boundsPointer, Titanfall2UiAtlasPageReader read,
		out Titanfall2UiAtlas atlas, out string error )
	{
		atlas = null;
		error = null;
		try
		{
			if ( version is not (10 or 11) || header is null || header.Length < 64 )
				throw new InvalidDataException( "Unsupported or truncated uimg v10/v11 header." );
			var width = U16( header, 8 );
			var height = U16( header, 10 );
			var count = U16( header, 12 );
			var guid = BinaryPrimitives.ReadUInt64LittleEndian( header.AsSpan( 56, 8 ) );
			if ( width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension || count <= 0 || count > MaximumImages || guid == 0 )
				throw new InvalidDataException( "UI atlas dimensions, image count, or texture GUID are invalid." );
			var hashSize = version == 10 ? 8 : 12;
			if ( !read( boundsPointer, checked(count * 16), out var bounds, out error )
				|| !read( Pointer( header, 24 ), checked(count * 4), out var dimensions, out error )
				|| !read( Pointer( header, 40 ), checked(count * hashSize), out var hashes, out error ) ) return false;
			if ( bounds?.Length != count * 16 || dimensions?.Length != count * 4 || hashes?.Length != count * hashSize )
				throw new InvalidDataException( "UI atlas arrays are truncated." );
			var namesPointer = Pointer( header, 48 );
			var images = new List<Titanfall2UiAtlasImage>( count );
			var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			for ( var index = 0; index < count; index++ )
			{
				var pathOffset = version == 10 ? U16( hashes, index * hashSize + 6 ) : I32( hashes, index * hashSize + 4 );
				if ( pathOffset < 0 || namesPointer.Index < 0 || namesPointer.Offset < 0 || pathOffset > int.MaxValue - namesPointer.Offset ) continue;
				if ( !TryReadName( namesPointer with { Offset = namesPointer.Offset + pathOffset }, read, out var name ) ) continue;
				var offset = index * 16;
				var image = new Titanfall2UiAtlasImage( name,
					BitConverter.ToSingle( bounds, offset ), BitConverter.ToSingle( bounds, offset + 4 ),
					BitConverter.ToSingle( bounds, offset + 8 ), BitConverter.ToSingle( bounds, offset + 12 ),
					U16( dimensions, index * 4 ), U16( dimensions, index * 4 + 2 ) );
				// An empty/invalid glyph must not poison the other images in a valid atlas.
				if ( image.TryGetCrop( width, height, out _ ) && image.DisplayWidth > 0 && image.DisplayHeight > 0 && names.Add( name ) ) images.Add( image );
			}
			if ( images.Count == 0 ) throw new InvalidDataException( "UI atlas contains no valid named images." );
			atlas = new( guid, width, height, images.AsReadOnly() );
			return true;
		}
		catch ( Exception exception )
		{
			error = exception.Message;
			return false;
		}
	}

	static bool TryReadName( RpakPagePointer pointer, Titanfall2UiAtlasPageReader read, out string name )
	{
		name = null;
		var bytes = new List<byte>();
		while ( bytes.Count < MaximumNameBytes )
		{
			var length = Math.Min( 128, MaximumNameBytes - bytes.Count );
			byte[] part = null;
			while ( length > 0 && !read( pointer, length, out part, out _ ) ) length /= 2;
			if ( length == 0 || part is null || part.Length != length ) return false;
			var terminator = Array.IndexOf( part, (byte)0 );
			bytes.AddRange( terminator < 0 ? part : part.AsSpan( 0, terminator ).ToArray() );
			if ( terminator >= 0 )
			{
				var value = Encoding.UTF8.GetString( bytes.ToArray() ).Replace( '\\', '/' );
				if ( value.Length == 0 || value[0] == '/' || value.Contains( ':' ) || value.Any( char.IsControl )
					|| value.Split( '/' ).Any( part => part is ".." or "." or "" ) ) return false;
				name = value.ToLowerInvariant();
				return true;
			}
			pointer = pointer with { Offset = checked(pointer.Offset + length) };
		}
		return false;
	}

	static ushort U16( byte[] data, int offset ) => BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( offset, 2 ) );
	static int I32( byte[] data, int offset ) => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( offset, 4 ) );
	static RpakPagePointer Pointer( byte[] data, int offset ) => new( I32( data, offset ), I32( data, offset + 4 ) );
}
