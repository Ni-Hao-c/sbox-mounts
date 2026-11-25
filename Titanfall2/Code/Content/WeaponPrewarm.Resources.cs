using System.Buffers.Binary;
using System.Diagnostics;
using System.Threading;
using Titanfall2;

public partial class Titanfall2Mount
{
	sealed class WeaponPreparedItem
	{
		internal bool Cached, Wav;
		internal string CacheKey, NativeName, Error;
		internal byte[] Bytes;
		internal uint Rate;
		internal ushort Channels;
		internal long ReadTicks, DecodeTicks;
		internal double CpuMilliseconds;
		internal Titanfall2ParticleDefinition Root;
		internal Titanfall2ParticleDefinition[] Definitions = [];
		internal Titanfall2ParticleDefinition[] Graph = [];
		internal Titanfall2WeaponPrewarmItem[] SpriteDependencies = [];
		internal string[] Libraries = [];
		internal Titanfall2ContentSnapshot Snapshot;
		internal Titanfall2WeaponUiLayout UiLayout;
		internal RpakWeaponFontData FontData;
		internal int Width, Height;
	}
	readonly Dictionary<string, Titanfall2WeaponPrewarmItem[]> _weaponPrewarmedParticles = new( StringComparer.OrdinalIgnoreCase );

	Task<WeaponPreparedItem> BeginWeaponPreparedItem( Titanfall2WeaponPrewarmItem item, CancellationToken cancellation ) => item.Kind switch
	{
		Titanfall2WeaponPrewarmKind.Sound => BeginWeaponSoundPreparation( item, cancellation ),
		Titanfall2WeaponPrewarmKind.WeaponUi or Titanfall2WeaponPrewarmKind.FontGlyph => BeginWeaponUiPreparation( item, cancellation ),
		_ => BeginWeaponParticlePreparation( item.Name, cancellation )
	};

	Task<WeaponPreparedItem> BeginWeaponUiPreparation( Titanfall2WeaponPrewarmItem item, CancellationToken cancellation )
	{
		// Capture immutable inputs on the engine thread. This shares the existing
		// one-job coordinator; HUD getters never perform reads, decoding or rasterization.
		var snapshot = _contentSnapshot;
		if ( snapshot is null ) return Task.FromResult( new WeaponPreparedItem { Error = "Weapon UI content is not mounted." } );
		if ( item.Kind == Titanfall2WeaponPrewarmKind.WeaponUi && GetWeaponUiLayout( item.Name ) is { } layout )
			return Task.FromResult( new WeaponPreparedItem { Snapshot = snapshot, UiLayout = layout, Cached = true } );
		RpakWeaponFontData fontData = null;
		if ( item.Kind == Titanfall2WeaponPrewarmKind.FontGlyph && TryGetWeaponFont( item.FontId, out var font, out _ ) )
		{
			if ( font.TryGetGlyph( item.Name[0], item.FontSize, out _ ) )
				return Task.FromResult( new WeaponPreparedItem { Snapshot = snapshot, Cached = true } );
			fontData = font.Data;
		}
		return Titanfall2Workers.Run( () =>
		{
			var result = new WeaponPreparedItem { Snapshot = snapshot };
			var timer = Stopwatch.StartNew();
			try
			{
				cancellation.ThrowIfCancellationRequested();
				if ( item.Kind == Titanfall2WeaponPrewarmKind.WeaponUi )
				{
					result.UiLayout = ReadWeaponUiLayout( snapshot, item.Name, cancellation );
					if ( result.UiLayout is null ) result.Error = $"Weapon UI layout unavailable: {item.Name}";
				}
				else
				{
					if ( !float.IsFinite( item.FontSize ) || item.FontSize is < 8 or > 512 ) throw new InvalidDataException( "Invalid numeric font size." );
					result.FontData = fontData ?? ReadWeaponFontData( snapshot, item.FontId, cancellation );
					cancellation.ThrowIfCancellationRequested();
					if ( !result.FontData.Digits.TryGetValue( item.Name[0], out var glyph ) ) throw new InvalidDataException( "Retail numeric glyph is unavailable." );
					result.Bytes = Titanfall2WeaponFont.Rasterize( result.FontData, glyph, (int)MathF.Ceiling( item.FontSize ), out result.Width, out result.Height );
				}
				cancellation.ThrowIfCancellationRequested();
			}
			catch ( Exception exception ) { result.Bytes = null; result.Error = exception.Message; }
			finally { result.CpuMilliseconds = timer.Elapsed.TotalMilliseconds; }
			return result;
		} );
	}

	Task<WeaponPreparedItem> BeginWeaponSoundPreparation( Titanfall2WeaponPrewarmItem item, CancellationToken cancellation )
	{
		var key = NormalizeSoundKey( item.Name );
		var cacheKey = "exact|" + key + (item.Loop ? "|loop" : "");
		lock ( _soundCacheLock )
			if ( _soundFileCache.ContainsKey( cacheKey ) ) return Task.FromResult( new WeaponPreparedItem { Cached = true } );
		var nativeName = $"titanfall2/{_contentFingerprint ?? "unversioned"}/exact/{(item.Loop ? "loop" : "oneshot")}/{key}.vsnd";
		// Capture immutable sources on the engine thread. Workers do not consult the
		// mutable mount dictionaries and never call SoundFile or another native API.
		ITitanfall2AssetSource wavSource = null;
		var archive = _milesArchive;
		_milesSoundAssets.TryGetValue( key, out var asset );
		if ( _contentSnapshot?.TryGet( key + ".wav", out var replacement ) == true
			&& replacement.SourceId.StartsWith( "northstar:", StringComparison.Ordinal ) )
		{
			wavSource = _contentSnapshot.GetSource( replacement.Path ); asset = null;
		}
		else if ( asset is null ) _soundFileSources.TryGetValue( key + ".wav", out wavSource );
		return Titanfall2Workers.Run( () =>
		{
			var result = new WeaponPreparedItem { CacheKey = cacheKey, NativeName = nativeName };
			var timer = Stopwatch.StartNew();
			try
			{
				cancellation.ThrowIfCancellationRequested();
				var started = Stopwatch.GetTimestamp();
				if ( wavSource is not null )
				{
					if ( !wavSource.TryReadAllBytes( out result.Bytes, out result.Error ) ) return result;
					result.ReadTicks = Stopwatch.GetTimestamp() - started;
					if ( result.Bytes.Length > 16 * 1024 * 1024 ) { result.Bytes = null; result.Error = "WAV exceeds prewarm budget."; }
					result.Wav = true;
					return result;
				}
				if ( asset is null || archive is null ) { result.Error = $"Exact sound unavailable: {item.Name}"; return result; }
				if ( asset.FileSize > 8 * 1024 * 1024 ) { result.Error = "BCF exceeds prewarm budget."; return result; }
				if ( !archive.TryReadBcf( asset, out var bcf, out result.Error ) ) return result;
				result.ReadTicks = Stopwatch.GetTimestamp() - started;
				cancellation.ThrowIfCancellationRequested();
				if ( bcf.Length < 24 || (long)BinaryPrimitives.ReadUInt32LittleEndian( bcf.AsSpan( 8, 4 ) ) * Math.Min( bcf[5], (byte)2 ) * 2 > 16 * 1024 * 1024 )
				{ result.Error = "Decoded sound exceeds 16 MiB prewarm budget."; return result; }
				started = Stopwatch.GetTimestamp();
				archive.TryDecode( bcf, out result.Bytes, out result.Rate, out result.Channels, out result.Error );
				result.DecodeTicks = Stopwatch.GetTimestamp() - started;
			}
			catch ( Exception e ) { result.Bytes = null; result.Error = e.Message; }
			finally { result.CpuMilliseconds = timer.Elapsed.TotalMilliseconds; }
			return result;
		} );
	}

	Task<WeaponPreparedItem> BeginWeaponParticlePreparation( string name, CancellationToken cancellation )
	{
		if ( _weaponPrewarmedParticles.TryGetValue( name, out var dependencies ) )
			return Task.FromResult( new WeaponPreparedItem { Cached = true, SpriteDependencies = dependencies } );
		Dictionary<string, Titanfall2ParticleDefinition> known;
		Dictionary<string, ITitanfall2AssetSource> sources;
		lock ( _particleCatalogLock )
		{
			known = new( _particleDefinitions, StringComparer.OrdinalIgnoreCase );
			sources = _particleFiles.GroupBy( file => file.Path, StringComparer.OrdinalIgnoreCase )
				.ToDictionary( group => group.Key, group => group.First().Source, StringComparer.OrdinalIgnoreCase );
		}
		return Titanfall2Workers.Run( () =>
		{
			var result = new WeaponPreparedItem();
			var parsed = new List<Titanfall2ParticleDefinition>();
			var parsedFiles = new List<string>();
			var files = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			long readBytes = 0;
			var timer = Stopwatch.StartNew();
			try
			{
				bool Resolve( string effect, int depth )
				{
					cancellation.ThrowIfCancellationRequested();
					if ( !visited.Add( effect ) ) return true;
					if ( depth > 16 || visited.Count > 256 ) { result.Error = "Particle graph exceeds prewarm budget."; return false; }
					if ( !known.TryGetValue( effect, out var definition ) )
					{
						var library = WeaponParticleLibrary( effect );
						if ( library is null || !files.Add( library ) || files.Count > 32 || !sources.TryGetValue( library, out var source ) )
						{ result.Error = $"No targeted PCF for '{effect}'; global catalog scan was not requested."; return false; }
						if ( !source.TryReadAllBytes( out var bytes, out result.Error ) ) return false;
						readBytes += bytes.Length;
						if ( readBytes > 32 * 1024 * 1024 ) { result.Error = "PCF bytes exceed prewarm budget."; return false; }
						if ( !Titanfall2PcfReader.TryReadDefinitions( bytes, library, out var definitions, out result.Error ) ) return false;
						parsedFiles.Add( library );
						foreach ( var entry in definitions ) { known.TryAdd( entry.Name, entry ); parsed.Add( entry ); }
						if ( !known.TryGetValue( effect, out definition ) ) { result.Error = $"PCF does not contain '{effect}'."; return false; }
					}
					foreach ( var child in definition.Children ) if ( !Resolve( child, depth + 1 ) ) return false;
					return true;
				}
				if ( Resolve( name, 0 ) )
				{
					result.Root = known[name];
					// Only the requested root and its transitive children belong to the
					// warm graph. Definitions also includes unrelated systems from a PCF.
					result.Graph = visited.Select( effect => known[effect] ).ToArray();
				}
			}
			catch ( Exception e ) { result.Error = e.Message; }
			finally
			{
				result.Definitions = parsed.ToArray(); result.Libraries = parsedFiles.ToArray();
				result.CpuMilliseconds = timer.Elapsed.TotalMilliseconds;
			}
			return result;
		} );
	}

	bool PublishWeaponPreparedItem( Titanfall2WeaponPrewarmItem item, WeaponPreparedItem prepared,
		Titanfall2WeaponPrewarmRequest request, out string error )
	{
		error = prepared.Error;
		if ( request.Cancelled || !IsMounted ) { error = "Prewarm mount generation ended."; return false; }
		if ( item.Kind is Titanfall2WeaponPrewarmKind.WeaponUi or Titanfall2WeaponPrewarmKind.FontGlyph )
		{
			if ( prepared.Error is not null ) return false;
			if ( !ReferenceEquals( prepared.Snapshot, _contentSnapshot ) ) { error = "Weapon UI content generation ended."; return false; }
			if ( item.Kind == Titanfall2WeaponPrewarmKind.WeaponUi )
			{
				if ( prepared.UiLayout is not { } layout ) { error = "Prepared weapon UI is unavailable."; return false; }
				PublishWeaponUiLayout( layout );
				foreach ( var character in "0123456789" ) request.Add( new( Titanfall2WeaponPrewarmKind.FontGlyph,
					character.ToString(), FontId: layout.FontId, FontSize: layout.FontSize ) );
				return true;
			}
			if ( !prepared.Cached && !PublishWeaponFontGlyph( prepared.FontData, item.FontId, item.FontSize, item.Name[0],
				prepared.Bytes, prepared.Width, prepared.Height, out error ) ) return false;
			prepared.Bytes = null; request.FontGlyphsReady++; return true;
		}
		if ( item.Kind == Titanfall2WeaponPrewarmKind.Sound )
		{
			if ( prepared.Cached ) { request.SoundsReady++; return true; }
			if ( prepared.Bytes is null || error is not null ) return false;
			lock ( _soundCacheLock )
			{
				if ( !_soundFileCache.ContainsKey( prepared.CacheKey ) )
				{
					var started = Stopwatch.GetTimestamp();
					var sound = prepared.Wav ? SoundFile.FromWav( prepared.NativeName, prepared.Bytes, new SoundFile.LoadOptions { Loop = item.Loop } )
						: SoundFile.FromPcm( prepared.NativeName, prepared.Bytes, new SoundFile.PcmOptions { Channels = prepared.Channels, Rate = prepared.Rate, Bits = 16, Loop = item.Loop } );
					_soundCreateTicks += Stopwatch.GetTimestamp() - started;
					if ( sound is null ) { error = "Native SoundFile creation failed."; return false; }
					_soundFileCache[prepared.CacheKey] = sound; _soundFailureRetryAfter.Remove( prepared.CacheKey );
					_soundReadTicks += prepared.ReadTicks; _soundDecodeTicks += prepared.DecodeTicks;
					_soundLoadCount++; _soundPcmBytes += prepared.Bytes.Length;
				}
			}
			request.SoundsReady++; request.PcmBytes += prepared.Bytes.Length;
			prepared.Bytes = null; // no decoded PCM retained by the coordinator
			return true;
		}
		if ( prepared.Cached )
		{
			// Another request may reuse the graph before its sprite items finish.
			// Every requester must still wait for all exact sequence identities.
			foreach ( var dependency in prepared.SpriteDependencies ) request.Add( dependency );
			request.ParticleGraphsReady++; return true;
		}
		lock ( _particleCatalogLock )
		{
			foreach ( var definition in prepared.Definitions ) _particleDefinitions.TryAdd( definition.Name, definition );
			// Even partial discovery remains useful, but it never certifies a graph ready.
			foreach ( var library in prepared.Libraries ) _indexedParticleFiles.Add( library );
		}
		if ( prepared.Root is null || error is not null ) return false;
		if ( !GetParticleSpritePrewarmItems( prepared.Graph, out var spriteItems, out error ) ) return false;
		lock ( _particleIndexLock ) _readyShotParticles[item.Name] = prepared.Root;
		// Construct native components/resources in an isolated, never-rendered scene.
		// There is no camera or audio playback, so no warm particles/voices enter gameplay.
		// This warms factory/JIT/texture/model creation, not a driver's first draw pipeline.
		var staging = new Scene();
		try
		{
			using var scope = staging.Push();
			var root = new GameObject( staging, false, "Weapon resource prewarm" );
			root.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			if ( !Titanfall2ParticleFactory.TryCreate( root, this, prepared.Root, out _, out error ) ) return false;
			root.Enabled = true;
			foreach ( var particle in root.GetComponentsInChildren<ParticleEffect>( true ) ) particle.Paused = true;
			foreach ( var dependency in spriteItems ) request.Add( dependency );
			_weaponPrewarmedParticles[item.Name] = spriteItems; request.ParticleGraphsReady++;
			return true;
		}
		finally { staging.Destroy(); }
	}

	bool GetParticleSpritePrewarmItems( Titanfall2ParticleDefinition[] graph, out Titanfall2WeaponPrewarmItem[] items, out string error )
	{
		items = [];
		error = null;
		var resources = new Dictionary<string, (string Texture, float Rate, int Sequence)>( StringComparer.OrdinalIgnoreCase );
		foreach ( var definition in graph )
		{
			if ( (definition.Renderer & (Titanfall2ParticleRenderer.Sprite | Titanfall2ParticleRenderer.Trail)) == 0
				|| string.IsNullOrWhiteSpace( definition.MaterialName ) ) continue;
			if ( !TryGetOrCreateLegacyMaterialDescriptor( definition.MaterialName, out var descriptor, out error )
				|| !TryGetLegacyMaterialDefinition( definition.MaterialName, out var material, out error ) ) return false;
			var texture = descriptor.TextureBindings.FirstOrDefault( binding => binding.Semantic == Titanfall2TextureSemantic.Albedo ).TexturePath;
			if ( string.IsNullOrWhiteSpace( texture ) ) { error = $"Particle '{definition.Name}' has no sprite texture."; return false; }
			var rate = definition.AnimationRate;
			if ( material.TryGetAnimatedTexture( false, out var materialRate ) && materialRate > 0f ) rate = materialRate;
			rate = Math.Clamp( rate > 0f ? rate : 1f, 0.1f, 120f );
			texture = Titanfall2LegacyPath.NormalizeTextureName( texture );
			// Random.Int includes both endpoints; the renderer clamps negative picks
			// to zero. Enumerate those exact cache identities, without random sampling.
			var first = Math.Max( 0, definition.SequenceMinimum );
			var last = Math.Max( first, definition.SequenceMaximum );
			if ( (long)last - first + 1 > 64 ) { error = $"Particle '{definition.Name}' exceeds 64 prewarm sequences."; return false; }
			for ( long sequence = first; sequence <= last; sequence++ )
			{
				resources.TryAdd( $"{texture}|{MathF.Round( rate * 100f )}|{sequence}", (texture, rate, (int)sequence) );
				if ( resources.Count > 256 ) { error = "Particle graph exceeds 256 prewarm sprite variants."; return false; }
			}
		}
		items = resources.Values.Select( resource => new Titanfall2WeaponPrewarmItem( Titanfall2WeaponPrewarmKind.ParticleSprite,
			resource.Texture, Sequence: resource.Sequence, FrameRate: resource.Rate ) ).ToArray();
		return true;
	}
}
