using System.Buffers.Binary;

sealed record Titanfall2UiAtlasSource( RpakArchive Archive, RpakAsset Texture, Titanfall2UiAtlas Atlas );

/// <summary>A mount-owned LRU of decoded atlas pixels. Native textures exist only during a cache miss.</summary>
sealed class Titanfall2UiAtlasPixelCache
{
	internal const long Limit = 128L * 1024 * 1024;
	readonly object _gate = new();
	readonly Dictionary<Titanfall2UiAtlasSource, (Pixels Data, LinkedListNode<Titanfall2UiAtlasSource> Node)> _entries = new();
	readonly LinkedList<Titanfall2UiAtlasSource> _lru = new();
	long _bytes, _readbacks;
	internal sealed record Pixels( int Width, int Height, Color32[] Data )
	{
		internal long Bytes => Data.LongLength * 4;
	}

	internal object Statistics { get { lock ( _gate ) return new { Atlases = _entries.Count, Bytes = _bytes, Limit, Readbacks = _readbacks }; } }
	internal void Clear() { lock ( _gate ) { _entries.Clear(); _lru.Clear(); _bytes = 0; } }

	internal Pixels Get( Titanfall2UiAtlasSource source )
	{
		lock ( _gate )
		{
			if ( _entries.TryGetValue( source, out var cached ) )
			{
				_lru.Remove( cached.Node ); _lru.AddLast( cached.Node );
				return cached.Data;
			}
			// UI glyphs keep their authored resolution, independent of world-texture quality.
			if ( (long)source.Atlas.Width * source.Atlas.Height > 64L * 1024 * 1024 )
				throw new InvalidDataException( "UI atlas exceeds the 64 megapixel decode limit." );
			var expectedBytes = (long)source.Atlas.Width * source.Atlas.Height * 4;
			while ( _bytes + expectedBytes > Limit && _lru.First is { } oldest )
			{
				_bytes -= _entries[oldest.Value].Data.Bytes;
				_entries.Remove( oldest.Value ); _lru.RemoveFirst();
			}
			if ( !RpakTextureDecoder.TryRead( source.Archive, source.Texture, out var decoded, out var error, RpakUiAtlasReader.MaximumDimension ) )
				throw new InvalidDataException( "Cannot read UI atlas: " + error );
			if ( decoded.IsCube || decoded.ArraySize != 1 ) throw new InvalidDataException( "UI atlas must be a two-dimensional texture." );
			if ( !RpakTextureDecoder.TryCreate( decoded, out var runtimeTexture, out error ) ) throw new InvalidDataException( "Cannot upload UI atlas: " + error );
			using var texture = runtimeTexture;
			// These temporary uploads have straight alpha. Reading directly into Color32
			// avoids a second full-sized Bitmap allocation during BC7 decompression.
			var pixels = new Pixels( texture.Width, texture.Height, texture.GetPixels() );
			_readbacks++;
			if ( pixels.Bytes <= Limit )
			{
				while ( _bytes + pixels.Bytes > Limit && _lru.First is { } first )
				{
					_bytes -= _entries[first.Value].Data.Bytes;
					_entries.Remove( first.Value ); _lru.RemoveFirst();
				}
				_entries.Add( source, (pixels, _lru.AddLast( source )) );
				_bytes += pixels.Bytes;
			}
			return pixels;
		}
	}
}

sealed class Titanfall2UiAtlasTextureLoader( Titanfall2UiAtlasSource source, Titanfall2UiAtlasImage image, Titanfall2UiAtlasPixelCache cache, bool restoreCanvas = false )
	: ResourceLoader<Titanfall2Mount>
{
	internal static Texture CreateTexture( Titanfall2UiAtlasSource source, Titanfall2UiAtlasImage image, Titanfall2UiAtlasPixelCache cache, bool restoreCanvas = false )
	{
		var pixels = cache.Get( source );
		if ( !image.TryGetCrop( pixels.Width, pixels.Height, out var crop ) ) throw new InvalidDataException( "UI image UV bounds are invalid for the decoded atlas." );
		var destination = (X: 0, Y: 0, Width: crop.Width, Height: crop.Height);
		if ( restoreCanvas && !image.TryGetCanvasPlacement( crop.Width, crop.Height, out destination ) )
			throw new InvalidDataException( "UI image has no valid original canvas transform." );
		var output = new Color32[checked(destination.Width * destination.Height)];
		for ( var row = 0; row < crop.Height; row++ )
			Array.Copy( pixels.Data, (crop.Y + row) * pixels.Width + crop.X, output, (destination.Y + row) * destination.Width + destination.X, crop.Width );
		return Texture.Create( destination.Width, destination.Height, ImageFormat.RGBA8888 ).WithStaticUsage().WithData<Color32>( output ).Finish();
	}
	protected override object Load()
	{
		try
		{
			return CreateTexture( source, image, cache, restoreCanvas );
		}
		catch ( Exception exception )
		{
			Titanfall2Log.Warning( $"Cannot load Titanfall 2 UI image '{Path}': {exception.Message}" );
			return Texture.Transparent;
		}
	}
}

public partial class Titanfall2Mount
{
	Titanfall2UiAtlasPixelCache _uiAtlasPixels;
	internal void ClearUiAtlasCache() => _uiAtlasPixels?.Clear();
	public object GetUiAtlasStatistics() => _uiAtlasPixels?.Statistics;

	internal void AddUiAtlasRegistrations( List<ContentRegistration> catalog, HashSet<string> registered, Titanfall2ContentSnapshot snapshot )
	{
		ClearUiAtlasCache();
		var cache = _uiAtlasPixels = new Titanfall2UiAtlasPixelCache();
		ReadMapPreviewMappings( snapshot );
		foreach ( var source in DiscoverUiAtlases( snapshot ) )
			foreach ( var image in source.Atlas.Images )
			{
				if ( image.Name.StartsWith( "loadscreens/", StringComparison.OrdinalIgnoreCase ) )
					_mapPreviewImages.TryAdd( image.Name, (source, image) );
				if ( registered.Add( image.Name + ".vtex" ) )
					catalog.Add( new( ResourceType.Texture, image.Name, new Titanfall2UiAtlasTextureLoader( source, image, cache ) ) );
				// Physical RUI planes use original-image UVs, unlike a cropped HUD icon.
				// Give them a separate resource so a same-named world TXTR cannot win.
				if ( image.HasTrimTransform && registered.Add( "rui_full/" + image.Name + ".vtex" ) )
					catalog.Add( new( ResourceType.Texture, "rui_full/" + image.Name, new Titanfall2UiAtlasTextureLoader( source, image, cache, restoreCanvas: true ) ) );
			}
	}

	internal static IReadOnlyList<Titanfall2UiAtlasSource> DiscoverUiAtlases( Titanfall2ContentSnapshot snapshot )
	{
		var sources = new List<Titanfall2UiAtlasSource>();
		var selected = new HashSet<ulong>();
		var textures = snapshot.Entries.Where( entry => entry.Kind == "txtr" && entry.Archive is not null )
			.GroupBy( entry => entry.Asset.Guid ).ToDictionary( group => group.Key, group => group.ToArray() );
		foreach ( var entry in snapshot.Entries.Where( entry => entry.Kind == "uimg" && entry.Archive is not null ) )
		{
			// Retail patch-only packages retain pointers into predecessor pages. Until the
			// generic RPak reader reconstructs those pages, use the first complete candidate
			// of this GUID, preserving snapshot priority and pairing it with compatible TXTR.
			if ( selected.Contains( entry.Asset.Guid ) || !RpakUiAtlasReader.TryRead( entry.Archive, entry.Asset, out var atlas, out _ )
				|| !textures.TryGetValue( atlas.TextureGuid, out var candidates ) ) continue;
			foreach ( var candidate in candidates )
			{
				if ( candidate.Asset.Version != 8 || !candidate.Archive.TryReadAsset( candidate.Asset, out var data, out _ ) || data.Header.Length < 24 ) continue;
				var width = BinaryPrimitives.ReadUInt16LittleEndian( data.Header.AsSpan( 16 ) );
				var height = BinaryPrimitives.ReadUInt16LittleEndian( data.Header.AsSpan( 18 ) );
				if ( width != atlas.Width || height != atlas.Height ) continue;
				sources.Add( new( candidate.Archive, candidate.Asset, atlas ) );
				selected.Add( entry.Asset.Guid );
				break;
			}
		}
		return sources.AsReadOnly();
	}
}
