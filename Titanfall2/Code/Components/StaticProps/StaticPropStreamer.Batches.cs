using System.Diagnostics;
using Sandbox.Mounting.PerfKit;
using Titanfall2;

public sealed partial class Titanfall2StaticPropStreamer
{
	/// <summary>
	/// Creates the instanced draw object for one batch. The batch itself (bounds, submission
	/// flags, explicit-LOD draw, instance-set replacement) is PerfKit's reusable implementation.
	/// </summary>
	MountedInstanceBatch CreateRenderBatch( PendingBatch batch, bool castShadows )
	{
		var model = batch.Model;
		var probeColor = batch.AverageProbeColor;
		var options = new MountedInstanceBatchOptions
		{
			CastShadows = castShadows,
			// Batchable lets the engine merge submissions; the two pass flags keep prop batches
			// out of the extra cubemap and environment-map passes.
			Batchable = true,
			IncludeInCubemap = false,
			NeedsEnvironmentMap = false,
			Tint = new Color( probeColor.x, probeColor.y, probeColor.z, 1f ),
			SelectLod = ModelLoader.SelectLodByDistance,
		};
		MountedInstanceBatch result = batch.Probes.Any( static probe => probe.Metadata.x > 0 )
			? new Titanfall2ProbeInstanceBatch( Scene.SceneWorld, model, batch.GetOrBuildTransformArray(), batch.Probes.ToArray(), BakedSkyColor, options, _localLights )
			: new Titanfall2PlainInstanceBatch( Scene.SceneWorld, model, batch.GetOrBuildTransformArray(), options );
		_renderResources.Track( result );
		// Production opaque/cutout variants return depth normals before reading probes.
		// Leave arbitrary forward-only diagnostic/custom batch shaders on the full path.
		if ( result is Titanfall2ProbeInstanceBatch probes ) probes.SkipDepthProbeBindings = true;
		return result;
	}

	void BuildPendingBatches()
	{
		if ( _batchesBuilt ) return;
		_batchesBuilt = true;

		TryBuildPropAggregates();

		foreach ( var cluster in _clusters.Values )
		{
			foreach ( var pendingBatch in cluster.Batches.Values )
			{
				if ( pendingBatch.Transforms.Count == 0 ) continue;
				var castShadows = pendingBatch.ShadowedInstanceCount > 0;
				try
				{
					pendingBatch.RenderBatch = CreateRenderBatch( pendingBatch, castShadows );
					pendingBatch.RenderBatch.RenderingEnabled = Active && pendingBatch.RenderingEnabled;
					_batchCount++;

					var batchedShadowCount = castShadows ? pendingBatch.Transforms.Count : 0;
					_shadowCastingProps += batchedShadowCount - pendingBatch.ShadowedInstanceCount;
					pendingBatch.ShadowedInstanceCount = batchedShadowCount;
				}
				catch ( Exception exception )
				{
					Log.Warning( exception, $"Unable to instance Titanfall 2 static model '{pendingBatch.Model.Name}'; "
						+ "retaining permanent individual objects." );
					for ( var index = 0; index < pendingBatch.Transforms.Count; ++index )
					{
						var fallback = new SceneObject(
							Scene.SceneWorld,
							pendingBatch.Model,
							pendingBatch.Transforms[index] );
						fallback.Flags.IsStatic = true;
						_renderResources.Track( fallback );
						var probeColor = pendingBatch.ProbeColors[index];
						fallback.ColorTint = new Color( probeColor.x, probeColor.y, probeColor.z, 1f );
						pendingBatch.Probes[index].Apply( fallback, BakedSkyColor );
						fallback.Flags.CastShadows = ShouldCastShadows( pendingBatch.Radii[index] );
						fallback.RenderingEnabled = Active && pendingBatch.RenderingEnabled;
						pendingBatch.FallbackObjects.Add( fallback );
					}
				}
			}
		}

		// The gate needs one visibility sweep from the current anchor before the first submitted
		// frame. Forcing it only when gating is on keeps the disabled configuration (prop shadows
		// off, or distance 0) exactly on its previous timing: the sweep is otherwise driven by the
		// camera refresh, and its own per-frame budget still bounds the work.
		if ( _shadowGateActive )
		{
			_runtimePvsUpdatePending = true;
			RebuildRuntimeClusterQueue();
		}

		RefreshCpuRenderOwnership();
		LogBatchShadowConfiguration();
		CompileRenderCensus();
	}

	void TryBuildPropAggregates()
	{
		if ( !Titanfall2StreamingSettings.PropAggregate || Application.IsDedicatedServer || !Graphics.IsAvailable ) return;

		var mount = Sandbox.Mounting.Directory.Get( MountIdent ) as Titanfall2Mount;
		var entries = new List<Titanfall2StaticPropAggregate.PropEntry>();
		var residualEntries = new List<Titanfall2StaticPropAggregate.ResidualEntry>();
		var eligibleBatches = new List<(PropCluster Cluster, Model Model)>();
		var fullyResidualBatchedInstances = new List<PropInstance>();
		var mixedResidualKeptInstances = new List<(PropInstance Instance, ulong ResidualMask, bool ResidualHasOpaque)>();
		var nonOpaqueShadowInstances = new List<PropInstance>();
		var fallbackProps = new HashSet<Titanfall2StaticPropSceneData.StreamedProp>();
		var mixedPropCount = 0;

		foreach ( var cluster in _clusters.Values )
		{
			foreach ( var (model, pendingBatch) in cluster.Batches )
			{
				if ( pendingBatch.Transforms.Count == 0 ) continue;
				if ( !Titanfall2StaticPropAggregate.CanAggregate( model, out var geometry ) )
				{
					foreach ( var src in pendingBatch.Sources ) fallbackProps.Add( src );
					continue;
				}

				eligibleBatches.Add( (cluster, model) );
				for ( var index = 0; index < pendingBatch.Transforms.Count; index++ )
				{
					var radius = pendingBatch.Radii[index];
					var src = pendingBatch.Sources[index];
					entries.Add( new Titanfall2StaticPropAggregate.PropEntry(
						model,
						geometry,
						pendingBatch.Transforms[index],
						radius,
						src.FadeDistance,
						src.Collidable,
						ShouldAggregateCastShadows( radius ),
						pendingBatch.Probes[index],
						src.PvsCells ) );
				}
			}

			foreach ( var cell in cluster.Cells )
			{
				foreach ( var instance in cell.Instances )
				{
					if ( instance.IsBatched || !instance.RenderObject.IsValid() ) continue;
					var model = instance.RenderObject.Model;
					var prop = instance.Source;
					var scale = MathF.Max( prop.Scale, 0.001f );
					var transform = new Transform( prop.Position, prop.Rotation.ToRotation(), scale );

					if ( Titanfall2StaticPropAggregate.CanAggregate( model, out var mixedGeometry, allowMixed: true ) )
					{
						mixedPropCount++;
						entries.Add( new Titanfall2StaticPropAggregate.PropEntry(
							model,
							mixedGeometry,
							transform,
							instance.Radius,
							prop.FadeDistance,
							prop.Collidable,
							ShouldAggregateCastShadows( instance.Radius ),
							prop.Probe,
							prop.PvsCells ) );

						if ( Titanfall2StaticPropAggregate.CanBatchResidualMeshes( mixedGeometry ) )
						{
							residualEntries.Add( new Titanfall2StaticPropAggregate.ResidualEntry(
								mixedGeometry.ResidualMeshes,
								transform,
								ShouldAggregateCastShadows( instance.Radius ) && mixedGeometry.ResidualHasOpaque ) );
							fullyResidualBatchedInstances.Add( instance );
						}
						else
						{
							mixedResidualKeptInstances.Add( (instance, mixedGeometry.ResidualMask, mixedGeometry.ResidualHasOpaque) );
							fallbackProps.Add( prop );
						}
					}
					else if ( ModelLoader.TryGetGpuGeometry( model, out var pureSpecialGeometry )
						&& !pureSpecialGeometry.HasGpuMeshes
						&& Titanfall2StaticPropAggregate.CanBatchResidualMeshes( pureSpecialGeometry ) )
					{
						residualEntries.Add( new Titanfall2StaticPropAggregate.ResidualEntry(
							pureSpecialGeometry.ResidualMeshes,
							transform,
							ShouldAggregateCastShadows( instance.Radius ) && pureSpecialGeometry.ResidualHasOpaque ) );
						fullyResidualBatchedInstances.Add( instance );
					}
					else
					{
						fallbackProps.Add( prop );
						if ( instance.CastShadows && !HasOpaqueShadowMaterial( model ) )
							nonOpaqueShadowInstances.Add( instance );
					}
				}
			}
		}

		if ( entries.Count == 0 && residualEntries.Count == 0 ) return;

		var fallbackIndices = new List<int>( fallbackProps.Count );
		if ( fallbackProps.Count > 0 && _compiledScene?.Props is { Length: > 0 } allProps )
		{
			for ( var i = 0; i < allProps.Length; i++ )
			{
				if ( fallbackProps.Contains( allProps[i] ) ) fallbackIndices.Add( i );
			}
		}

		try
		{
			var payload = Titanfall2StaticPropAggregate.CompilePayload(
				entries,
				residualEntries,
				fallbackIndices.ToArray(),
				_totalProps,
				mixedPropCount,
				fullyResidualBatchedInstances.Count,
				out var liveMaterials );
			if ( payload is null ) return;

			var anchor = _hasAnchor ? _lastAnchor : FallbackAnchor;
			var aggregate = Titanfall2StaticPropAggregate.CreateFromPayload(
				GameObject,
				Scene,
				mount,
				MapPath,
				payload,
				BakedSkyColor,
				anchor,
				Active,
				loadedFromCache: false,
				liveMaterials );
			if ( aggregate is null ) return;

			_propAggregate = aggregate;
			_mixedAggregatedProps = mixedPropCount;
			_aggregatedProps = aggregate.PropCount;
			var fullBatchProps = Math.Max( 0, _aggregatedProps - _mixedAggregatedProps );
			_instancedProps = Math.Max( 0, _instancedProps - fullBatchProps );
			foreach ( var (cluster, model) in eligibleBatches )
				cluster.Batches.Remove( model );

			foreach ( var instance in fullyResidualBatchedInstances )
			{
				if ( instance.CastShadows )
				{
					instance.CastShadows = false;
					_shadowCastingProps = Math.Max( 0, _shadowCastingProps - 1 );
				}
				if ( instance.RenderObject.IsValid() ) instance.RenderObject.Delete();
			}

			foreach ( var (instance, residualMask, residualHasOpaque) in mixedResidualKeptInstances )
			{
				if ( !instance.RenderObject.IsValid() ) continue;
				instance.RenderObject.MeshGroupMask = residualMask;
				if ( !residualHasOpaque && instance.CastShadows )
				{
					instance.CastShadows = false;
					instance.RenderObject.Flags.CastShadows = false;
					_shadowCastingProps = Math.Max( 0, _shadowCastingProps - 1 );
				}
			}

			foreach ( var instance in nonOpaqueShadowInstances )
			{
				if ( !instance.RenderObject.IsValid() || !instance.CastShadows ) continue;
				instance.CastShadows = false;
				instance.RenderObject.Flags.CastShadows = false;
				_shadowCastingProps = Math.Max( 0, _shadowCastingProps - 1 );
			}

			var cacheId = Titanfall2StaticPropAggregate.BuildCacheIdentity( mount, MapPath );
			if ( !string.IsNullOrEmpty( cacheId ) )
				Titanfall2StaticPropAggregate.WriteCache( cacheId, payload );
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, $"Titanfall 2 static prop aggregation failed; falling back to instanced batches ({MapPath})." );
		}
	}

	static bool ShouldAggregateCastShadows( float radius )
		=> Titanfall2StreamingSettings.PropShadows && radius >= Titanfall2StaticPropAggregate.AggregateShadowMinimumRadius;

	static bool HasOpaqueShadowMaterial( Model model )
	{
		if ( model is null ) return false;
		foreach ( var material in model.Materials )
		{
			if ( material is null ) continue;
			var name = (material.ShaderName ?? string.Empty).Replace( '\\', '/' );
			if ( !name.Contains( "translucent", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "decal", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "additive", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "alpha", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "water", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "godray", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "distortion", StringComparison.OrdinalIgnoreCase )
				&& !name.Contains( "hologram", StringComparison.OrdinalIgnoreCase ) )
				return true;
		}
		return false;
	}

	/// <summary>
	/// Reports the effective shadow and LOD distances once, when the batches are built. Both
	/// switches change what the frame submits, so an A/B run has to read the effective values from
	/// the log rather than assume the environment variable took effect (a value that fails to
	/// parse falls back to the default silently). The sweep-time gated counts are logged by
	/// <see cref="LogShadowGateSweep"/> when a sweep actually crosses the distance.
	/// </summary>
	void LogBatchShadowConfiguration()
	{
		var shadowGate = _shadowGateActive
			? $"distance gate {MathF.Sqrt( _shadowGateDistanceSquared ):0} units (restore inside {MathF.Sqrt( _shadowGateRestoreDistanceSquared ):0})"
			: "distance gate disabled (SBOX_TITANFALL2_PROP_SHADOWS off, or SBOX_TITANFALL2_PROP_SHADOW_DISTANCE 0)";
		Log.Info( $"Titanfall 2 prop shadows: {shadowGate} on {_batchCount} batches; LOD switch distances "
			+ $"{ModelLoader.GetLodSwitchDistance( 1 ):0}/{ModelLoader.GetLodSwitchDistance( 2 ):0}/{ModelLoader.GetLodSwitchDistance( 3 ):0} "
			+ $"(SBOX_TITANFALL2_PROP_LOD_SCALE {Titanfall2StreamingSettings.PropLodDistanceScale:0.00}) ({MapPath})." );
	}

	static bool ShouldCastShadows( float radius )
		=> Titanfall2StreamingSettings.PropShadows && radius >= ShadowMinimumRadius;

	/// <summary>
	/// Clears borrowed batch/instance references for one cluster. The render owner deletes
	/// native objects after all clusters are detached; collision lifetime is independent.
	/// </summary>
	void DestroyClusterInstances( PropCluster cluster )
	{
		foreach ( var pendingBatch in cluster.Batches.Values )
		{
			_shadowCastingProps = Math.Max( 0, _shadowCastingProps - pendingBatch.ShadowedInstanceCount );
		}
		cluster.Batches.Clear();

		foreach ( var cell in cluster.Cells )
			DestroyCellInstances( cell );
	}

	/// <summary>
	/// Releases the instances and colliders of one cell. Batches are deliberately not touched
	/// here: a batch is shared with every other cell in the cluster, so deleting it from a
	/// single cell would leave the rest of the cluster pointing at a deleted native object.
	/// </summary>
	void DestroyCellInstances( PropCell cell )
	{
		if ( !cell.Active && cell.Instances.Count == 0 ) return;
		foreach ( var instance in cell.Instances )
		{
			if ( instance.RenderingEnabled ) _renderingProps = Math.Max( 0, _renderingProps - 1 );
			if ( !instance.IsBatched && instance.CastShadows )
				_shadowCastingProps = Math.Max( 0, _shadowCastingProps - 1 );
			Collisions.Release( instance );
		}
		_liveProps = Math.Max( 0, _liveProps - cell.Instances.Count );
		cell.Instances.Clear();
		cell.NextProp = 0;
		cell.Active = false;
		cell.Wanted = false;
		_activeCells = Math.Max( 0, _activeCells - 1 );
	}
}
