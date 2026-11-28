public sealed partial class Titanfall2StaticPropStreamer
{
	sealed record ModelCensus( string Name, int MaterialBindings, ModelLoader.StaticGeometryStatistics Geometry );
	sealed record RenderCensus( int ActualMaterialIdentities, int[] BatchSizeUpperBounds, int[] BatchSizeCounts, ModelCensus[] Models );
	RenderCensus _renderCensus;
	readonly Guid _sceneGeneration = Guid.NewGuid();

	void CompileRenderCensus()
	{
		var upperBounds = new[] { 1, 4, 8, 16, 32, 64, 128, int.MaxValue };
		var counts = new int[upperBounds.Length];
		var models = new HashSet<Model>( _individualModels );
		foreach ( var cluster in _clusters.Values )
		{
			foreach ( var batch in cluster.Batches.Values )
			{
				models.Add( batch.Model );
				var bucket = Array.FindIndex( upperBounds, maximum => batch.Transforms.Count <= maximum );
				counts[bucket]++;
			}
		}
		var materials = new HashSet<Material>();
		var census = new List<ModelCensus>();
		foreach ( var model in models.OrderBy( model => model.Name, StringComparer.Ordinal ) )
		{
			foreach ( var material in model.Materials ) materials.Add( material );
			ModelLoader.TryGetGeometryStatistics( model, out var geometry );
			census.Add( new( model.Name, model.Materials.Length, geometry ) );
		}
		_renderCensus = new( materials.Count, upperBounds, counts, census.ToArray() );
	}

	/// <summary>
	/// Explicit diagnostic snapshot. No serialization, model enumeration or file I/O happens in
	/// the frame loop. Material identities are a census, not a promise of final MDI group count.
	/// </summary>
	public object GetCompiledSceneStatistics() => new
	{
		MapPath,
		SceneGeneration = _sceneGeneration,
		Configuration = Titanfall2EffectiveConfiguration.Capture(),
		RuntimeState = _runtimePhase.ToString(),
		CpuRenderClusters = _cpuRenderClusters,
		PayloadVersion = DataVersion,
		CompiledLayout = _compiledScene?.FromCompiledPayload ?? false,
		Instances = _compiledScene?.Props.Length ?? 0,
		ModelSkinGroups = _compiledScene?.Layout.Models.Length ?? 0,
		SpatialCells = _compiledScene?.Layout.Cells.Length ?? 0,
		PvsCells = _compiledVisibility?.CellCount ?? 0,
		PvsTableBytes = (_compiledVisibility?.CellCount ?? 0) * Titanfall2.Titanfall2BspReader.VisibilityCellMask.WordCount * sizeof( ulong ),
		KeepAllCollisions,
		PreparationComplete,
		ResourcesPrepared,
		RenderResourcesPrepared = _renderResources.IsPrepared,
		NativeRenderObjects = _renderResources.NativeObjectCount,
		ResidentCollisionsReady,
		ResidentCollisionCount,
		CollidableProps = Collisions.Expected,
		FilteredCollisions = Collisions.Filtered,
		MissingCollisionModels = Collisions.Failures,
		UnresolvedCollisions = Math.Max( 0, Collisions.Expected - Collisions.Created - Collisions.Filtered - Collisions.Failures ),
		IndividualSpecialMaterialProps = _plainStaticProps,
		FullModelFallbackProps = _plainFallbackProps,
		FrozenSkinnedProps = _skinnedProps,
		FailedProps = _failedProps,
		FullGpuInstances = Math.Max( 0, _aggregatedProps - _mixedAggregatedProps ),
		MixedGpuInstances = _mixedAggregatedProps,
		GpuRenderingEnabled = _propAggregate is not null,
		PropAggregate = _propAggregate is null ? null : new
		{
			_propAggregate.PropCount,
			_propAggregate.MixedPropCount,
			_propAggregate.ResidualBatchedPropCount,
			_propAggregate.SectorCount,
			_propAggregate.NearSectorCount,
			_propAggregate.PvsCulledSectorCount,
			_propAggregate.AggregateCount,
			_propAggregate.ShadowChunkCount,
			_propAggregate.ResidualGroupCount,
			_propAggregate.FragmentCount,
			_propAggregate.MaterialCount,
			_propAggregate.ProbeCount,
			_propAggregate.SharedTriangles,
			_propAggregate.NearTriangles,
			_propAggregate.FarTriangles,
			_propAggregate.ShadowTriangles,
			_propAggregate.ResidualTriangles,
			_propAggregate.LoadedFromCache,
			CollisionLoadedFromCache = Collisions.LoadedFromCache
		},
		Render = _renderCensus,
		GpuScene = (object)null
	};
}
