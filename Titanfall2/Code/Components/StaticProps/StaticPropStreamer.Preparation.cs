using System.Diagnostics;

public sealed partial class Titanfall2StaticPropStreamer
{
	protected override async Task OnLoad( LoadingContext context )
	{
		_runtimePhase = RuntimePhase.Preparing;
		try { await PrepareResources( context ); }
		catch
		{
			// A failed/cancelled load must not leave native resources attached to a
			// partially loaded component while the caller handles the exception.
			ReleaseResources();
			throw;
		}
		finally
		{
			if ( !_releasing ) _runtimePhase = PreparationComplete ? RuntimePhase.Ready : RuntimePhase.Uninitialized;
		}
	}

	async Task PrepareResources( LoadingContext context )
	{
		PreparationComplete = false;
		Collisions.ResidentReady = false;
		if ( Application.IsDedicatedServer ) KeepAllCollisions = true;
		if ( !_initialized ) InitializeCells();
		if ( !_initialized || _initializationFailed ) return;
		if ( _cells.Count == 0 || _totalProps == 0 )
		{
			Collisions.ResidentReady = KeepAllCollisions;
			PreparationComplete = true;
			return;
		}

		_renderResources.Prepare( EncodedLocalLights );
		var loadTimer = Stopwatch.StartNew();
		var cacheBefore = Titanfall2ProcessedCache.GetStatistics();
		Log.Info( $"Titanfall 2 effective configuration: {System.Text.Json.JsonSerializer.Serialize( Titanfall2EffectiveConfiguration.Capture() )}" );
		context.Title = $"Loading Titanfall 2 static props (0/{_totalProps})";
		// The active camera is not guaranteed to exist while the SceneFile is being
		// deserialized. The first map spawn remains a deterministic priority anchor.
		RefreshCells( FallbackAnchor );

		var loadedFromDiskCache = await TryPrepareFromDiskCacheAsync( context );
		if ( _releasing || !this.IsValid() ) return;

		if ( !loadedFromDiskCache )
		{
			while ( _liveProps + _failedProps < _totalProps )
			{
				if ( !this.IsValid() || !GameObject.IsValid() ) return;
				PopulateCells( PreloadPerYield, PreloadBudgetMilliseconds );
				var processed = _liveProps + _failedProps;
				context.Title = $"Loading Titanfall 2 static props ({processed}/{_totalProps})";
				if ( processed < _totalProps ) await Task.Yield();
			}

			if ( KeepAllCollisions ) await Collisions.Preload( context );
			if ( _releasing || !this.IsValid() ) return;

			LogCompletion();
			LogBatchComposition();
		}

		SetResourcesEnabled( Active );
		Log.Info( $"Titanfall 2 static props finished during scene loading in "
			+ $"{loadTimer.Elapsed.TotalSeconds:0.00}s ({(loadedFromDiskCache ? "disk cache fast path; " : string.Empty)}{MapPath})." );
		if ( KeepAllCollisions && !ResidentCollisionsReady )
			Log.Warning( $"Titanfall 2 resident prop collision is incomplete: {_failedProps} missing props, "
				+ $"{Collisions.Failures} missing collision models, {Collisions.Created}/{Collisions.Expected} colliders; "
				+ $"ttf2_map_ready will not be published ({MapPath})." );
		var cacheAfter = Titanfall2ProcessedCache.GetStatistics();
		Log.Info( $"Titanfall 2 processed model cache: {cacheAfter.ModelHits - cacheBefore.ModelHits} hits, "
			+ $"{cacheAfter.ModelMisses - cacheBefore.ModelMisses} misses; textures {cacheAfter.TextureHits - cacheBefore.TextureHits} hits/"
			+ $"{cacheAfter.TextureMisses - cacheBefore.TextureMisses} misses; materials {cacheAfter.MaterialHits - cacheBefore.MaterialHits} hits/"
			+ $"{cacheAfter.MaterialMisses - cacheBefore.MaterialMisses} misses; {cacheAfter.Writes - cacheBefore.Writes} derived writes; "
			+ $"{cacheAfter.Rejected} rejected / {cacheAfter.WriteFailures} write failures total; "
			+ $"mounted prop shadows {(Titanfall2StreamingSettings.PropShadows ? "enabled" : "disabled")}." );
		PreparationComplete = true;
	}

	async Task<bool> TryPrepareFromDiskCacheAsync( LoadingContext context )
	{
		if ( !Titanfall2StreamingSettings.PropAggregate
			|| !Titanfall2StreamingSettings.ProcessedCache
			|| Application.IsDedicatedServer
			|| !Graphics.IsAvailable )
			return false;

		if ( Sandbox.Mounting.Directory.Get( MountIdent ) is not Titanfall2Mount mount ) return false;
		var aggCacheId = Titanfall2StaticPropAggregate.BuildCacheIdentity( mount, MapPath );
		if ( !Titanfall2StaticPropAggregate.HasCacheEntry( aggCacheId ) ) return false;
		if ( KeepAllCollisions && !Collisions.HasDiskCache() ) return false;

		context.Title = "Loading cached Titanfall 2 static props";
		var payloadTask = Titanfall2Workers.Run( () =>
			Titanfall2StaticPropAggregate.TryReadCache( aggCacheId, out var decoded ) ? decoded : null );

		if ( KeepAllCollisions )
		{
			var colHit = await Collisions.TryPreloadFromCacheAsync( context );
			if ( !colHit || _releasing || !this.IsValid() )
			{
				await payloadTask;
				return false;
			}
		}

		var payload = await payloadTask;
		if ( payload is null || payload.TotalPropCount != _totalProps || _releasing || !this.IsValid() )
		{
			if ( KeepAllCollisions ) Collisions.Clear();
			return false;
		}

		try
		{
			var aggregate = Titanfall2StaticPropAggregate.CreateFromPayload(
				GameObject,
				Scene,
				mount,
				MapPath,
				payload,
				BakedSkyColor,
				FallbackAnchor,
				Active,
				loadedFromCache: true );
			if ( aggregate is null )
			{
				if ( KeepAllCollisions ) Collisions.Clear();
				return false;
			}

			_propAggregate = aggregate;
			_aggregatedProps = aggregate.PropCount;
			_mixedAggregatedProps = aggregate.MixedPropCount;

			// Spawn only the tiny subset of props that could not be aggregated (e.g. animated or cubemap materials).
			if ( payload.FallbackPropIndices.Length > 0 && _compiledScene?.Props is { Length: > 0 } allProps )
			{
				var fallbackSet = new HashSet<Titanfall2StaticPropSceneData.StreamedProp>();
				foreach ( var idx in payload.FallbackPropIndices )
				{
					if ( (uint)idx < (uint)allProps.Length ) fallbackSet.Add( allProps[idx] );
				}

				_skipCollisionRegistration = KeepAllCollisions && Collisions.LoadedFromCache;
				try
				{
					foreach ( var cell in _cells.Values )
					{
						foreach ( var prop in cell.Props )
						{
							if ( !fallbackSet.Contains( prop ) ) continue;
							if ( !cell.Active ) { cell.Active = true; _activeCells++; }
							SpawnProp( cell, prop );
						}
						cell.NextProp = cell.Props.Count;
					}
				}
				finally
				{
					_skipCollisionRegistration = false;
				}

				// Mixed fallback instances already have their opaque geometry inside the cached aggregate;
				// restrict their individual SceneObject to its residual group and skip non-opaque shadows.
				foreach ( var cell in _cells.Values )
				{
					foreach ( var instance in cell.Instances )
					{
						if ( instance.IsBatched || !instance.RenderObject.IsValid() ) continue;
						var model = instance.RenderObject.Model;
						if ( Titanfall2StaticPropAggregate.CanAggregate( model, out var mixedGeometry, allowMixed: true ) )
						{
							instance.RenderObject.MeshGroupMask = mixedGeometry.ResidualMask;
							if ( !mixedGeometry.ResidualHasOpaque && instance.CastShadows )
							{
								instance.CastShadows = false;
								instance.RenderObject.Flags.CastShadows = false;
								_shadowCastingProps = Math.Max( 0, _shadowCastingProps - 1 );
							}
						}
						else if ( instance.CastShadows && !HasOpaqueShadowMaterial( model ) )
						{
							instance.CastShadows = false;
							instance.RenderObject.Flags.CastShadows = false;
							_shadowCastingProps = Math.Max( 0, _shadowCastingProps - 1 );
						}
					}
				}
			}
			else
			{
				foreach ( var cell in _cells.Values ) cell.NextProp = cell.Props.Count;
			}

			// Build any remaining fallback CPU batches (e.g. animated batchable models), without re-running TryBuildPropAggregates.
			_batchesBuilt = true;
			foreach ( var cluster in _clusters.Values )
			{
				foreach ( var pendingBatch in cluster.Batches.Values )
				{
					if ( pendingBatch.Transforms.Count == 0 ) continue;
					var castShadows = pendingBatch.ShadowedInstanceCount > 0;
					pendingBatch.RenderBatch = CreateRenderBatch( pendingBatch, castShadows );
					pendingBatch.RenderBatch.RenderingEnabled = Active && pendingBatch.RenderingEnabled;
					_batchCount++;
				}
			}

			_liveProps = _totalProps;
			_workQueue.Clear();
			_completionLogged = true;
			_batchCompositionLogged = true;
			RefreshCpuRenderOwnership();
			CompileRenderCensus();
			Log.Info( $"Titanfall 2 prop streaming complete (disk cache hit): {_liveProps}/{_totalProps} props, "
				+ $"{_aggregatedProps} aggregated ({_mixedAggregatedProps} mixed, {aggregate.ResidualBatchedPropCount} residual-batched), "
				+ $"{payload.FallbackPropIndices.Length} fallback props, {_batchCount} CPU batches, "
				+ $"{Collisions.Created}/{Collisions.Expected} colliders in {Collisions.MergedBodyCount} merged bodies ({MapPath})." );
			return true;
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, $"Titanfall 2 static prop disk cache restore failed; rebuilding from source models ({MapPath})." );
			_propAggregate?.Dispose();
			_propAggregate = null;
			_aggregatedProps = _mixedAggregatedProps = _liveProps = _batchCount = _activeCells = 0;
			_batchesBuilt = false;
			foreach ( var cluster in _clusters.Values ) DestroyClusterInstances( cluster );
			if ( KeepAllCollisions ) Collisions.Clear();
			return false;
		}
	}

}
