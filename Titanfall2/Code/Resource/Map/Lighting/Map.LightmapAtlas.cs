using Titanfall2;

partial class MapLoader
{
	// G6a: upload the two largest lightmap channels as 16-bit formats. The shader keeps sampling
	// float4 values, so only precision changes (on retail pages RealTimeB is 93.7-96.7% binary and
	// loses ~0.2% on average, SkyB loses ~1% of its sky visibility), while the lightmap block drops
	// from 17 B/px to 13 B/px. SBOX_TITANFALL2_LIGHTMAP_16BIT=0 restores the RGBA8888 upload for A/B
	// and rollback. See PackBgr565/PackBgra4444 for the byte-order derivation.
	static bool Lightmap16Bit => Titanfall2StreamingSettings.Lightmap16Bit;

	/// <summary>
	/// Packs an RGBA8888 lightmap page for the native <see cref="ImageFormat.BGR565"/> upload:
	/// <c>R &lt;&lt; 11 | G &lt;&lt; 5 | B</c>, with blue in the low five bits of each little-endian
	/// word. Native GPU readback and world-shader sampling verify this layout. RGB565 is a different
	/// engine format: it can return a valid texture whose uploaded directions are corrupt, so the
	/// DDS loader's format mapping is not evidence that RGB565 works for this runtime upload.
	/// </summary>
	static byte[] PackBgr565( byte[] rgba, int pixelCount )
	{
		if ( rgba is null || rgba.Length != pixelCount * 4 ) return null;
		var packed = new byte[pixelCount * 2];
		for ( var pixel = 0; pixel < pixelCount; pixel++ )
		{
			var source = pixel * 4;
			// Round to nearest level: (v * levels + 127) / 255.
			var red = (rgba[source] * 31 + 127) / 255;
			var green = (rgba[source + 1] * 63 + 127) / 255;
			var blue = (rgba[source + 2] * 31 + 127) / 255;
			var value = (ushort)((red << 11) | (green << 5) | blue);
			packed[pixel * 2] = (byte)value;
			packed[pixel * 2 + 1] = (byte)(value >> 8);
		}
		return packed;
	}

	/// <summary>
	/// Packs an RGBA8888 lightmap page into BGRA4444 (four 4-bit channels, hardware filtered). Channel
	/// order matches the engine's other BGRA formats: the DDS table maps B=0x000000FF (lowest byte) to
	/// <see cref="ImageFormat.BGRA8888"/> (TextureLoader.DDS.cs:136), so here blue is the lowest nibble
	/// and alpha the highest: <c>A &lt;&lt; 12 | R &lt;&lt; 8 | G &lt;&lt; 4 | B</c>. The VTF decoder
	/// already feeds this format the same layout (VtfTextureDecoder.cs maps VTF format 19 straight
	/// through, and the VTF BGRA4444 decode uses B=bits 0-3 / G=4-7 / R=8-11 / A=12-15).
	/// </summary>
	static byte[] PackBgra4444( byte[] rgba, int pixelCount )
	{
		if ( rgba is null || rgba.Length != pixelCount * 4 ) return null;
		var packed = new byte[pixelCount * 2];
		for ( var pixel = 0; pixel < pixelCount; pixel++ )
		{
			var source = pixel * 4;
			var blue = (rgba[source + 2] * 15 + 127) / 255;
			var green = (rgba[source + 1] * 15 + 127) / 255;
			var red = (rgba[source] * 15 + 127) / 255;
			var alpha = (rgba[source + 3] * 15 + 127) / 255;
			var value = (ushort)((alpha << 12) | (red << 8) | (green << 4) | blue);
			packed[pixel * 2] = (byte)value;
			packed[pixel * 2 + 1] = (byte)(value >> 8);
		}
		return packed;
	}

	static int LightmapBytesPerPixel( ImageFormat format ) => format switch
	{
		ImageFormat.RGBA8888 or ImageFormat.BGRA8888 or ImageFormat.ARGB8888 or ImageFormat.ABGR8888 => 4,
		ImageFormat.RGB565 or ImageFormat.BGR565 or ImageFormat.BGRA4444 or ImageFormat.BGRA5551
			or ImageFormat.BGRX5551 => 2,
		ImageFormat.I8 or ImageFormat.A8 => 1,
		_ => 4
	};

	// ---- G6b: lightmap coverage, dilation and a short mip chain -----------------------------
	//
	// The atlas is tightly packed and the gaps between charts hold unrelated bytes, so a plain mip
	// chain smears that data into the lit texels. Before uploading we therefore dilate each chart
	// outward into the uncovered texels and only then build the chain from the dilated bytes.
	// Measured on retail pages (see .tmp/ttf2-review-verify/g6-lightmap.md): two dilation steps
	// already saturate the narrow gaps, four leave headroom; L0..L2 keeps the chain short because
	// deeper levels blur the thin charts more than they save bandwidth.
	const int LightmapDilationSteps = 4;
	const int LightmapMipLevelCount = 3;

	static bool LightmapMips => Titanfall2StreamingSettings.LightmapMips;

	static int LightmapChannelBytes( Texture texture )
	{
		if ( texture is null || !texture.IsValid() ) return 0;
		return LightmapChainBytes( texture.Width, texture.Height,
			LightmapBytesPerPixel( texture.ImageFormat ), Math.Max( 1, texture.Mips ) );
	}

	/// <summary>Bytes of a complete mip chain, level 0 first, matching the engine's level sizing.</summary>
	static int LightmapChainBytes( int width, int height, int bytesPerPixel, int mipLevels )
	{
		long total = 0;
		for ( var level = 0; level < mipLevels; level++ )
			total += (long)Math.Max( 1, width >> level ) * Math.Max( 1, height >> level ) * bytesPerPixel;
		return checked((int)total);
	}

	/// <summary>L0..L2 for the lightmap pages (the chain is only ever this short by design).</summary>
	static int LightmapLevels( int width, int height )
	{
		var full = System.Numerics.BitOperations.Log2( (uint)Math.Max( Math.Max( width, height ), 1 ) ) + 1;
		return Math.Min( LightmapMipLevelCount, full );
	}

	static int LightmapPageBytes( RuntimeLightmapPage page ) =>
		LightmapChannelBytes( page.SkyA ) + LightmapChannelBytes( page.SkyB )
		+ LightmapChannelBytes( page.RealTimeA ) + LightmapChannelBytes( page.RealTimeB )
		+ LightmapChannelBytes( page.SunChannels );

	static string LightmapChannelSummary( Texture texture ) => texture is null || !texture.IsValid()
		? "none"
		: $"{texture.ImageFormat} {texture.Width}x{texture.Height} ({LightmapBytesPerPixel( texture.ImageFormat )} B/px"
			+ $"{(texture.Mips > 1 ? $", {texture.Mips} mips" : string.Empty)})";

	static int _lightmapFormatFallbacks;

	static void WarnLightmapFormatFallback( ImageFormat format )
	{
		if ( System.Threading.Interlocked.Increment( ref _lightmapFormatFallbacks ) > 2 ) return;
		Titanfall2Log.Warning( $"Titanfall 2 could not create a {format} lightmap channel; "
			+ "falling back to RGBA8888 for that channel." );
	}

	/// <summary>
	/// Rasterises the lightmap UVs of the given geometry into a per-page "texel is covered" mask: a
	/// texel counts as covered when its centre falls inside a triangle whose vertices carry a
	/// lightmap UV. UVs outside the page and degenerate triangles are skipped (counted in
	/// <paramref name="skippedTriangles"/>). Pages with no geometry stay null. The mask is a plain
	/// <c>bool[]</c>, one byte per texel - about 1 MB per 1024虏 page.
	/// </summary>
	internal static bool[][] BuildLightmapCoverage( IEnumerable<Titanfall2BspReader.WorldMesh> meshes,
		IReadOnlyList<Titanfall2BspReader.BspLightmapPage> pages, out int skippedTriangles )
	{
		skippedTriangles = 0;
		if ( meshes is null || pages is null || pages.Count == 0 ) return null;
		var masks = new bool[pages.Count][];
		foreach ( var mesh in meshes )
		{
			if ( mesh is null || mesh.Vertices is null || mesh.Indices is null ) continue;
			var pageIndex = mesh.LightmapPage;
			if ( pageIndex < 0 || pageIndex >= pages.Count ) continue;
			var page = pages[pageIndex];
			if ( page is null || page.Width <= 0 || page.Height <= 0 ) continue;
			var mask = masks[pageIndex] ??= new bool[page.Width * page.Height];
			RasterizeLightmapMesh( mesh, mask, page.Width, page.Height, ref skippedTriangles );
		}
		return masks;
	}

	static void RasterizeLightmapMesh( Titanfall2BspReader.WorldMesh mesh, bool[] mask, int width, int height,
		ref int skippedTriangles )
	{
		var vertices = mesh.Vertices;
		var indices = mesh.Indices;
		for ( var triangle = 0; triangle + 2 < indices.Length; triangle += 3 )
		{
			var indexA = indices[triangle];
			var indexB = indices[triangle + 1];
			var indexC = indices[triangle + 2];
			if ( (uint)indexA >= (uint)vertices.Length || (uint)indexB >= (uint)vertices.Length
				|| (uint)indexC >= (uint)vertices.Length )
			{
				skippedTriangles++;
				continue;
			}

			var uvA = vertices[indexA].LightmapTexCoord;
			var uvB = vertices[indexB].LightmapTexCoord;
			var uvC = vertices[indexC].LightmapTexCoord;
			if ( !float.IsFinite( uvA.X ) || !float.IsFinite( uvA.Y ) || !float.IsFinite( uvB.X )
				|| !float.IsFinite( uvB.Y ) || !float.IsFinite( uvC.X ) || !float.IsFinite( uvC.Y ) )
			{
				skippedTriangles++;
				continue;
			}

			var minU = MathF.Min( uvA.X, MathF.Min( uvB.X, uvC.X ) );
			var maxU = MathF.Max( uvA.X, MathF.Max( uvB.X, uvC.X ) );
			var minV = MathF.Min( uvA.Y, MathF.Min( uvB.Y, uvC.Y ) );
			var maxV = MathF.Max( uvA.Y, MathF.Max( uvB.Y, uvC.Y ) );
			if ( minU < -0.01f || minV < -0.01f || maxU > 1.01f || maxV > 1.01f )
			{
				skippedTriangles++;
				continue;
			}

			var ax = uvA.X * width;
			var ay = uvA.Y * height;
			var bx = uvB.X * width;
			var by = uvB.Y * height;
			var cx = uvC.X * width;
			var cy = uvC.Y * height;
			var x0 = Math.Clamp( (int)MathF.Floor( MathF.Min( ax, MathF.Min( bx, cx ) ) - 0.5f ), 0, width - 1 );
			var x1 = Math.Clamp( (int)MathF.Ceiling( MathF.Max( ax, MathF.Max( bx, cx ) ) + 0.5f ), 0, width - 1 );
			var y0 = Math.Clamp( (int)MathF.Floor( MathF.Min( ay, MathF.Min( by, cy ) ) - 0.5f ), 0, height - 1 );
			var y1 = Math.Clamp( (int)MathF.Ceiling( MathF.Max( ay, MathF.Max( by, cy ) ) + 0.5f ), 0, height - 1 );
			var denominator = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
			if ( MathF.Abs( denominator ) < 1e-6f )
			{
				skippedTriangles++;
				continue;
			}

			for ( var y = y0; y <= y1; y++ )
			{
				var py = y + 0.5f;
				var row = y * width;
				for ( var x = x0; x <= x1; x++ )
				{
					var px = x + 0.5f;
					var w0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / denominator;
					if ( w0 < 0f ) continue;
					var w1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / denominator;
					if ( w1 < 0f ) continue;
					if ( 1f - w0 - w1 < 0f ) continue;
					mask[row + x] = true;
				}
			}
		}
	}

	/// <summary>
	/// Fills the uncovered texels of one lightmap channel from the nearest covered colour, one ring
	/// per step, so the mip chain averages chart colour instead of unrelated atlas bytes. Covered
	/// texels are never written: the result differs from <paramref name="rgba"/> only where
	/// <paramref name="covered"/> is false. Returns the input array itself when there is nothing to
	/// fill.
	/// </summary>
	internal static byte[] DilateLightmap( byte[] rgba, int width, int height, bool[] covered, int steps,
		out int filledTexels )
	{
		filledTexels = 0;
		var texelCount = width * height;
		if ( rgba is null || covered is null || steps <= 0 || texelCount <= 0
			|| rgba.Length != texelCount * 4 || covered.Length != texelCount ) return rgba;

		var known = covered.ToArray();
		var frontier = new List<int>( texelCount / 8 );
		for ( var index = 0; index < texelCount; index++ )
			if ( known[index] ) frontier.Add( index );
		if ( frontier.Count == 0 ) return rgba;

		var output = rgba.ToArray();
		var next = new List<int>();
		for ( var step = 0; step < steps && frontier.Count > 0; step++ )
		{
			next.Clear();
			for ( var frontierIndex = 0; frontierIndex < frontier.Count; frontierIndex++ )
			{
				var index = frontier[frontierIndex];
				var x = index % width;
				var y = index / width;
				for ( var dy = -1; dy <= 1; dy++ )
				{
					var ny = y + dy;
					if ( ny < 0 || ny >= height ) continue;
					for ( var dx = -1; dx <= 1; dx++ )
					{
						if ( dx == 0 && dy == 0 ) continue;
						var nx = x + dx;
						if ( nx < 0 || nx >= width ) continue;
						var neighbour = ny * width + nx;
						if ( known[neighbour] ) continue;

						// Average the known 8-neighbours: smoother than nearest on oblique surfaces.
						int red = 0, green = 0, blue = 0, alpha = 0, count = 0;
						for ( var sy = -1; sy <= 1; sy++ )
						{
							var sampleY = ny + sy;
							if ( sampleY < 0 || sampleY >= height ) continue;
							for ( var sx = -1; sx <= 1; sx++ )
							{
								var sampleX = nx + sx;
								if ( sampleX < 0 || sampleX >= width ) continue;
								var sample = sampleY * width + sampleX;
								if ( !known[sample] ) continue;
								var offset = sample * 4;
								red += output[offset];
								green += output[offset + 1];
								blue += output[offset + 2];
								alpha += output[offset + 3];
								count++;
							}
						}
						if ( count == 0 ) continue;

						var target = neighbour * 4;
						output[target] = (byte)(red / count);
						output[target + 1] = (byte)(green / count);
						output[target + 2] = (byte)(blue / count);
						output[target + 3] = (byte)(alpha / count);
						known[neighbour] = true;
						next.Add( neighbour );
						filledTexels++;
					}
				}
			}
			(frontier, next) = (next, frontier);
		}
		return output;
	}

	/// <summary>Box-filters an RGBA8888 page down to <paramref name="levels"/> levels (level 0 = input).</summary>
	internal static List<byte[]> BuildLightmapMipChain( byte[] rgba, int width, int height, int levels )
	{
		var chain = new List<byte[]>( Math.Max( 1, levels ) ) { rgba };
		if ( levels <= 1 || rgba is null ) return chain;

		var source = rgba;
		var sourceWidth = width;
		var sourceHeight = height;
		for ( var level = 1; level < levels; level++ )
		{
			var nextWidth = Math.Max( 1, width >> level );
			var nextHeight = Math.Max( 1, height >> level );
			var next = new byte[nextWidth * nextHeight * 4];
			for ( var y = 0; y < nextHeight; y++ )
			{
				var sourceY0 = Math.Min( sourceHeight - 1, y * 2 );
				var sourceY1 = Math.Min( sourceHeight - 1, y * 2 + 1 );
				for ( var x = 0; x < nextWidth; x++ )
				{
					var sourceX0 = Math.Min( sourceWidth - 1, x * 2 );
					var sourceX1 = Math.Min( sourceWidth - 1, x * 2 + 1 );
					var a = (sourceY0 * sourceWidth + sourceX0) * 4;
					var b = (sourceY0 * sourceWidth + sourceX1) * 4;
					var c = (sourceY1 * sourceWidth + sourceX0) * 4;
					var d = (sourceY1 * sourceWidth + sourceX1) * 4;
					var target = (y * nextWidth + x) * 4;
					for ( var channel = 0; channel < 4; channel++ )
						next[target + channel] = (byte)((source[a + channel] + source[b + channel]
							+ source[c + channel] + source[d + channel]) / 4);
				}
			}
			chain.Add( next );
			source = next;
			sourceWidth = nextWidth;
			sourceHeight = nextHeight;
		}
		return chain;
	}

	/// <summary>
	/// Uploads one lightmap channel. With <paramref name="levels"/> above one the complete chain is
	/// built on the CPU and uploaded smallest mip first, as required by the native texture API
	/// (also used by TextureLoader.DDS.GetMipData), so the result does not depend on the
	/// backend's mip generation (the engine's TSPEC_TEXTURE_GEN_MIP_MAPS flag is documented as
	/// render-target only). <paramref name="packer"/> converts each level's RGBA bytes to the
	/// upload format, because packing after filtering is the only correct order for 565/4444.
	/// </summary>
	static Texture CreateLightmapTexture( byte[] rgba, int width, int height, ImageFormat format, int levels,
		Func<byte[], int, byte[]> packer )
	{
		if ( rgba is null ) return null;
		levels = Math.Max( 1, levels );
		if ( levels <= 1 )
		{
			var single = packer is null ? rgba : packer( rgba, width * height );
			return CreateLightmapTextureRaw( single, width, height, format, 1 );
		}

		var chain = BuildLightmapMipChain( rgba, width, height, levels );
		var bytesPerPixel = LightmapBytesPerPixel( format );
		var total = LightmapChainBytes( width, height, bytesPerPixel, levels );
		var full = new byte[total];
		var offset = 0;
		for ( var level = chain.Count - 1; level >= 0; --level )
		{
			var levelWidth = Math.Max( 1, width >> level );
			var levelHeight = Math.Max( 1, height >> level );
			var levelPixels = levelWidth * levelHeight;
			var levelData = packer is null ? chain[level] : packer( chain[level], levelPixels );
			if ( levelData is null || levelData.Length != levelPixels * bytesPerPixel ) return null;
			Buffer.BlockCopy( levelData, 0, full, offset, levelData.Length );
			offset += levelData.Length;
		}
		return CreateLightmapTextureRaw( full, width, height, format, levels );
	}

	static Texture CreateLightmapTextureRaw( byte[] data, int width, int height, ImageFormat format, int mipLevels )
	{
		if ( data is null ) return null;
		mipLevels = Math.Max( 1, mipLevels );
		if ( data.Length != LightmapChainBytes( width, height, LightmapBytesPerPixel( format ), mipLevels ) ) return null;
		try
		{
			var builder = Texture.Create( width, height, format );
			if ( mipLevels > 1 ) builder = builder.WithMips( mipLevels );
			var texture = builder.WithData( data ).Finish();
			if ( texture is not null && texture.IsValid() ) return texture;
		}
		catch ( Exception )
		{
			// Fall through: the caller retries with the untouched RGBA8888 page.
		}
		WarnLightmapFormatFallback( format );
		return null;
	}

	// Tries the packed 16-bit upload first, then the untouched RGBA8888 page, so a backend without
	// the 16-bit format degrades instead of losing the channel. The same retry drops the mip chain:
	// the base level is what LOD 0 and clamped samplers read, so it is worth keeping even if the
	// backend rejects a multi-level upload.
	static Texture CreateLightmapChannel( byte[] rgba, int width, int height, ImageFormat format, int levels,
		Func<byte[], int, byte[]> packer )
	{
		var texture = CreateLightmapTexture( rgba, width, height, format, levels, packer );
		if ( texture is not null ) return texture;
		if ( levels > 1 ) texture = CreateLightmapTexture( rgba, width, height, format, 1, packer );
		if ( texture is not null ) return texture;
		return CreateLightmapTexture( rgba, width, height, ImageFormat.RGBA8888, 1, null );
	}


	RuntimeLightmaps CreateLightmapTextures( Titanfall2BspReader.ParsedBsp parsed )
	{
		var source = parsed.Lightmaps;
		if ( source is null || !source.IsValid ) return null;
		// The coverage mask is only needed by the dilate + mip path: one pass over the world lightmap
		// UVs. A default run (mips off) does not pay for it and logs nothing extra.
		var mips = LightmapMips;
		var skippedTriangles = 0;
		var coverage = mips ? BuildLightmapCoverage( parsed.World.Meshes, source.Pages, out skippedTriangles ) : null;
		var mipTimer = mips ? System.Diagnostics.Stopwatch.StartNew() : null;
		var pages = new List<RuntimeLightmapPage>( source.Pages.Count );
		long totalBytes = 0;
		for ( var index = 0; index < source.Pages.Count; index++ )
		{
			var page = source.Pages[index];
			var runtime = CreateLightmapPage( page, source.RealTimeLightPages, parsed.WorldLights,
				coverage is not null && index < coverage.Length ? coverage[index] : null, Path );
			var pageBytes = LightmapPageBytes( runtime );
			totalBytes += pageBytes;
			pages.Add( runtime );
			Log.Info( $"Titanfall 2 lightmap page {index}: {page.Width}x{page.Height}, "
				+ $"SkyA {LightmapChannelSummary( runtime.SkyA )}, SkyB {LightmapChannelSummary( runtime.SkyB )}, "
				+ $"RealTimeA {LightmapChannelSummary( runtime.RealTimeA )}, "
				+ $"RealTimeB {LightmapChannelSummary( runtime.RealTimeB )}, "
				+ $"sun {LightmapChannelSummary( runtime.SunChannels )}; "
				+ $"{pageBytes / (1024.0 * 1024.0):0.00} MB ({Path})." );
		}
		Log.Info( $"Titanfall 2 lightmap upload: {pages.Count} pages, {totalBytes / (1024.0 * 1024.0):0.00} MB total, "
			+ $"{(Lightmap16Bit ? "16-bit lightmap optimization (BGR565 SkyB, BGRA4444 single-level RealTimeB)" : "RGBA8888 SkyB/RealTimeB")}"
			+ $"{(mips ? $", mips L0..L{LightmapMipLevelCount - 1} (dilate {LightmapDilationSteps})" : string.Empty)}"
			+ $" ({Path})." );
		if ( mipTimer is not null )
		{
			mipTimer.Stop();
			var coveredPages = coverage is null ? 0 : Array.FindAll( coverage, static mask => mask is not null ).Length;
			Log.Info( $"Titanfall 2 lightmap coverage: {coveredPages} pages with geometry, {skippedTriangles} skipped triangles, "
				+ $"{mipTimer.Elapsed.TotalMilliseconds:0.0} ms for dilation and mips ({Path})." );
			if ( mipTimer.Elapsed.TotalMilliseconds > 120.0 )
				Titanfall2Log.ReportSpike( mipTimer, "lightmap dilation/mips for {0}", Path );
		}
		return new RuntimeLightmaps( pages, ReadBakedSkyColor( parsed ) );
	}

	internal static RuntimeLightmapPage CreateLightmapPage( Titanfall2BspReader.BspLightmapPage page,
		IReadOnlyList<short> lightPages, IReadOnlyList<Titanfall2WorldLight> worldLights,
		bool[] coverage = null, string context = null )
	{
		// Respawn's atlas contains tightly packed lightmap charts and the gaps hold unrelated bytes,
		// so a plain mip chain smears those into the lit texels (soft rectangular blotches at
		// grazing angles). With mips enabled the chain is therefore built from a dilated copy: the
		// uncovered texels are filled from the nearest chart colour first, and the covered texels are
		// never written.
		var mipLevels = LightmapMips && coverage is not null ? LightmapLevels( page.Width, page.Height ) : 1;
		var stopwatch = mipLevels > 1 ? System.Diagnostics.Stopwatch.StartNew() : null;
		var skyA = page.SkyA;
		var skyB = page.SkyB;
		var realTimeB = page.RealTimeB;
		var dilatedTexels = 0;
		if ( mipLevels > 1 )
		{
			skyA = DilateLightmap( skyA, page.Width, page.Height, coverage, LightmapDilationSteps, out dilatedTexels );
			skyB = DilateLightmap( skyB, page.Width, page.Height, coverage, LightmapDilationSteps, out _ );
			realTimeB = DilateLightmap( realTimeB, page.Width, page.Height, coverage, LightmapDilationSteps, out _ );
		}

		var sunChannels = Titanfall2WorldLight.CreateSunChannels( page.RealTimeA, lightPages, worldLights );
		var sunTexture = sunChannels.Length == page.Width * page.Height
			? Texture.Create( page.Width, page.Height, ImageFormat.I8 ).WithData( sunChannels ).Finish() : null;
		// RealTimeA and SunChannels are point-sampled with Load in the shader, so they get neither
		// dilation nor a mip chain (it would only cost memory). SkyA, SkyB and RealTimeB are filtered
		// through Tex2DS and are the three channels that benefit.
		// No shader samples the authored RealTimeC page, so no texture is created or bound for it;
		// Titanfall2BspReader.ReadLightmaps also skips its copy. That is one page-sized upload less
		// (1 B/px, i.e. 1/17 of the lightmap block) with no visual change.
		var runtime = new RuntimeLightmapPage(
			CreateLightmapChannel( skyA, page.Width, page.Height, ImageFormat.RGBA8888, mipLevels, null ),
			Lightmap16Bit
				? CreateLightmapChannel( skyB, page.Width, page.Height, ImageFormat.BGR565, mipLevels, PackBgr565 )
				: CreateLightmapChannel( skyB, page.Width, page.Height, ImageFormat.RGBA8888, mipLevels, null ),
			CreateLightmapTextureRaw( page.RealTimeA, page.Width, page.Height, ImageFormat.RGBA8888, 1 ),
			// Native BGRA4444 multi-mip uploads can fail or corrupt lower levels. Keep the
			// single-level optimization, but use the verified RGBA path for filtered mips.
			Lightmap16Bit && mipLevels == 1
				? CreateLightmapChannel( realTimeB, page.Width, page.Height, ImageFormat.BGRA4444, mipLevels, PackBgra4444 )
				: CreateLightmapChannel( realTimeB, page.Width, page.Height, ImageFormat.RGBA8888, mipLevels, null ),
			sunTexture );

		if ( stopwatch is not null && coverage is not null )
		{
			stopwatch.Stop();
			var covered = 0;
			foreach ( var value in coverage )
				if ( value ) covered++;
			Log.Info( $"Titanfall 2 lightmap page mips: {page.Width}x{page.Height}, covered {covered}/{coverage.Length} texels "
				+ $"({(coverage.Length == 0 ? 0.0 : covered * 100.0 / coverage.Length):0.0}%), "
				+ $"dilated {dilatedTexels} texels in {LightmapDilationSteps} steps, mips {mipLevels} (L0..L{mipLevels - 1}), "
				+ $"{stopwatch.Elapsed.TotalMilliseconds:0.0} ms ({context ?? "lightmap"})." );
		}
		return runtime;
	}

}
