using Sandbox.Mounting.PerfKit;
using System.Runtime.InteropServices;
using Titanfall2;

partial class MapLoader
{
	// Edge length of the spatial cells world triangles are grouped into. Larger cells submit fewer
	// fragments (fewer draws) at the cost of a coarser frustum cull. The value comes from
	// SBOX_TITANFALL2_WORLD_CELL_SIZE; builds cache it in a local and pass it down, because the
	// triangle loops must not read a static property per triangle.
	static float WorldRenderCellSize => Titanfall2StreamingSettings.WorldRenderCellSize;

	Titanfall2MaterialPrefetch _materialPrefetch;
	WorldBuildResult BuildWorldModel( Titanfall2BspReader.ParsedBsp parsed, bool createFallbackCollision )
	{
		var vertexCount = parsed.World.Meshes.Sum( static mesh => mesh?.Vertices.Length ?? 0 );
		var indexCount = parsed.World.Meshes.Sum( static mesh => mesh?.Indices.Length ?? 0 );
		if ( vertexCount == 0 || indexCount == 0 ) return null;
		// Cached once per build: the parallel triangle workers get it as an argument.
		var cellSize = WorldRenderCellSize;

		var groups = new Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>>();
		var translucentGroups = new Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>>();
		var decalGroups = new Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>>();
		var godrayGroups = new Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>>();
		var waterGroups = new Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>>();
		var lightmaps = Titanfall2StreamingSettings.UseBspLightmaps
			? CreateLightmapTextures( parsed )
			: null;
		var worldMaterials = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var decalMaterials = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var godrayMaterials = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var cutoutMaterials = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var translucentMaterials = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var waterMaterials = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var shadowOnlyGroups = new Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>>();
		var shadowOnlyMaterials = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var shadowOnlyMeshCount = 0;
		var skippedToolMeshes = 0;
		var skippedGodrayMeshes = 0;
		var decalMeshCount = 0;
		var godrayMeshCount = 0;
		var visualEffectCollisionMeshCount = 0;
		// Classification stays serial: material metadata, the material name sets and the
		// fallback collision counters are shared state. Only the triangle work is parallel.
		var sources = new List<WorldMeshSource>( parsed.World.Meshes.Count );
		foreach ( var sourceMesh in parsed.World.Meshes )
		{
			if ( sourceMesh is null || sourceMesh.Vertices.Length == 0 || sourceMesh.Indices.Length < 3 ) continue;
			var materialName = NormalizeMaterialName( sourceMesh.MaterialName );
			// tools/toolsblocklight is an invisible light blocker in the original game. Drawing it
			// showed green "LIGHT BLOCK" walls; it now only enters the shadow pass (or nothing,
			// when map shadows are off), so it still keeps sunlight out of the spaces it seals.
			var isShadowOnly = Titanfall2CollisionFilter.IsNonSolidToolName( materialName );
			if ( ShouldSkipWorldMaterial( materialName ) || (isShadowOnly && !Titanfall2StreamingSettings.MapShadows) )
			{
				skippedToolMeshes++;
				continue;
			}
			var isGodray = MaterialLoader.IsGodrayMaterial( materialName );
			if ( isGodray && !Titanfall2StreamingSettings.Godrays )
			{
				// Additive light-shaft cards: skipping them removes the bright cards that can
				// flash while the camera moves, without touching opaque world geometry.
				skippedGodrayMeshes++;
				continue;
			}
			var materialMetadata = GetMaterialMetadata( materialName );
			var isDecal = !isGodray && materialMetadata.IsDecal;
			var allowsFallbackCollision = Titanfall2CollisionFilter.ShouldIncludeMaterial( materialName, materialMetadata );
			var includeCollision = createFallbackCollision && allowsFallbackCollision;
			if ( !isDecal && materialMetadata.Mode == Titanfall2MaterialMode.Cutout ) cutoutMaterials.Add( materialName );
			if ( !isDecal && materialMetadata.IsTranslucent ) translucentMaterials.Add( materialName );
			if ( !isDecal && materialMetadata.IsWater ) waterMaterials.Add( materialName );
			// Aggregate scene objects do not supply framebuffer copies for water.
			// Keep refractive surfaces on a dedicated translucent renderer.
			// Native aggregate objects enter the opaque/prepass lists. Transparent
			// grids and screens need ordinary renderers to reach the translucent
			// pass with correct depth testing and sorting.
			// Native s&box lighting does not need one material/draw group per
			// Respawn lightmap page. Merge those pages when the experimental
			// lightmap path is disabled to reduce both memory and draw calls.
			var lightmapPage = Titanfall2StreamingSettings.UseBspLightmaps && !isGodray && !isDecal && !isShadowOnly
				? sourceMesh.LightmapPage
				: -1;
			var category = isShadowOnly ? WorldGroupCategory.ShadowOnly
				: isGodray ? WorldGroupCategory.Godray
				: isDecal ? WorldGroupCategory.Decal
				: materialMetadata.IsWater ? WorldGroupCategory.Water
				: materialMetadata.IsTranslucent ? WorldGroupCategory.Translucent
				: WorldGroupCategory.Opaque;
			var targetMaterials = isShadowOnly ? shadowOnlyMaterials : isGodray ? godrayMaterials : isDecal ? decalMaterials : worldMaterials;
			if ( isShadowOnly ) shadowOnlyMeshCount++;
			targetMaterials.Add( materialName );
			if ( isGodray ) godrayMeshCount++;
			if ( isDecal ) decalMeshCount++;
			sources.Add( new WorldMeshSource
			{
				Mesh = sourceMesh,
				MaterialKey = new WorldMaterialKey( materialName, lightmapPage ),
				Category = category,
				IsDecal = isDecal,
				IncludeCollision = includeCollision
			} );
			if ( createFallbackCollision && !allowsFallbackCollision && !isGodray && !isDecal )
				visualEffectCollisionMeshCount++;
		}

		// Bucket each source mesh's triangles by spatial cell. A worker only touches the mesh it
		// owns, so nothing here is shared and no lock is needed.
		var groupingTimer = System.Diagnostics.Stopwatch.StartNew();
		var buckets = new WorldMeshCellBuckets[sources.Count];
		Sandbox.Utility.Parallel.For( 0, sources.Count, index => buckets[index] = BucketMeshTriangles( sources[index], cellSize ) );

		// Collect one contribution list per cell in source mesh order, so the cell workers below
		// emit triangles in exactly the order the previous serial loop emitted them.
		var cellContributions = new Dictionary<WorldRenderCell, List<WorldCellContribution>>();
		var cells = new List<WorldRenderCell>( sources.Count );
		for ( var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++ )
		{
			foreach ( var cellTriangles in buckets[sourceIndex].Cells )
			{
				if ( !cellContributions.TryGetValue( cellTriangles.Cell, out var contributions ) )
				{
					contributions = new List<WorldCellContribution>();
					cellContributions.Add( cellTriangles.Cell, contributions );
					cells.Add( cellTriangles.Cell );
				}
				contributions.Add( new WorldCellContribution( sourceIndex, cellTriangles.TriangleStarts ) );
			}
		}

		var cellWork = new List<WorldCellContribution>[cells.Count];
		for ( var cellIndex = 0; cellIndex < cells.Count; cellIndex++ )
			cellWork[cellIndex] = cellContributions[cells[cellIndex]];

		// One worker per spatial cell: every group a worker creates belongs to that cell, so no
		// other worker can reach the same MapMeshGroup.
		var cellGroups = new WorldCellGroups[cells.Count];
		Sandbox.Utility.Parallel.For( 0, cells.Count, cellIndex =>
			cellGroups[cellIndex] = BuildCellGroups( cellWork[cellIndex], sources ) );

		// Publish each finished cell into its category's dictionary. The cell already owns its
		// groups, so this moves them into place instead of copying vertex data.
		for ( var cellIndex = 0; cellIndex < cells.Count; cellIndex++ )
		{
			var materials = cellGroups[cellIndex].Materials;
			for ( var categoryIndex = 0; categoryIndex < materials.Count; categoryIndex++ )
			{
				var cellMaterials = materials[categoryIndex];
				if ( cellMaterials is null || cellMaterials.Count == 0 ) continue;
				var groupCategory = (WorldGroupCategory)categoryIndex;
				var targetGroups = groupCategory switch
				{
					WorldGroupCategory.Translucent => translucentGroups,
					WorldGroupCategory.Decal => decalGroups,
					WorldGroupCategory.Godray => godrayGroups,
					WorldGroupCategory.Water => waterGroups,
					WorldGroupCategory.ShadowOnly => shadowOnlyGroups,
					_ => groups
				};
				targetGroups.Add( cells[cellIndex], cellMaterials );
			}
		}

		// Fallback collision consumes the same triangle corners in the same order as before, so it
		// comes from the same per-mesh buckets. Retail maps with complete CM collision never build
		// this, and then neither list is allocated.
		List<Vector3> collisionPositions = null;
		List<int> collisionIndices = null;
		if ( createFallbackCollision )
		{
			collisionPositions = new List<Vector3>( indexCount );
			collisionIndices = new List<int>( indexCount );
			for ( var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++ )
			{
				var meshBuckets = buckets[sourceIndex];
				if ( meshBuckets.CollisionPositions is null || meshBuckets.CollisionPositions.Count == 0 ) continue;
				var baseVertex = collisionPositions.Count;
				collisionPositions.AddRange( meshBuckets.CollisionPositions );
				foreach ( var collisionIndex in meshBuckets.CollisionIndices )
					collisionIndices.Add( baseVertex + collisionIndex );
			}
		}
		groupingTimer.Stop();

		if ( groups.Count == 0 && translucentGroups.Count == 0 && waterGroups.Count == 0 ) return null;
		var worldGroupCount = groups.Values.Sum( static cell => cell.Count );
		var decalGroupCount = decalGroups.Values.Sum( static cell => cell.Count );
		var godrayGroupCount = godrayGroups.Values.Sum( static cell => cell.Count );
		// Largest single (cell, material) group; every group of the non-aggregate categories becomes
		// one mesh draw, so this is the size a bigger cell must keep bounded. The aggregate path
		// reports its own fragment sizes.
		var largestGroup = LargestGroup( groups, translucentGroups, decalGroups, godrayGroups, waterGroups, shadowOnlyGroups );
		// One fragment is a sub-range of one mesh buffer, and the aggregate path already uploads a
		// whole material per mesh, so a large cell cannot hit a vertex-count limit the current build
		// does not already hit. A group this big is still a 44 MB draw, though: say so instead of
		// letting it show up as a first-draw hitch.
		const int LargestGroupWarningVertices = 1_000_000;
		if ( largestGroup.Vertices > LargestGroupWarningVertices )
			Log.Warning( $"Titanfall 2 world cell {cellSize:0}u produces a {largestGroup.Vertices} vertex group "
				+ $"({largestGroup.Indices} indices); lower SBOX_TITANFALL2_WORLD_CELL_SIZE if it stalls." );
		Log.Info( $"Titanfall 2 map geometry grouped: {Path} ({worldMaterials.Count} world materials across "
			+ $"{groups.Count} spatial cells/{worldGroupCount} local meshes including "
			+ $"{cutoutMaterials.Count} cutout, {translucentMaterials.Count} translucent and {waterMaterials.Count} water; "
			+ $"{decalMaterials.Count} decal materials across {decalGroups.Count} cells/{decalGroupCount} local meshes, "
			+ $"{godrayMaterials.Count} Godray materials across {godrayGroups.Count} cells/{godrayGroupCount} local meshes, "
			+ $"{vertexCount} source vertices, {indexCount / 3} source triangles, {skippedToolMeshes} invisible tool meshes skipped, "
			+ $"{(skippedGodrayMeshes > 0 ? $"{skippedGodrayMeshes} godray meshes skipped by switch" : "godrays enabled")}, "
			+ $"{decalMeshCount} decal, {godrayMeshCount} Godray and {visualEffectCollisionMeshCount} "
			+ (createFallbackCollision
				? "visual FX meshes excluded from fallback collision"
				: "render collision disabled; authoritative rBSP contents used")
			+ $", cell {cellSize:0}u, largest group {largestGroup.Vertices}V/{largestGroup.Indices}I, "
			+ $"grouped in {groupingTimer.Elapsed.TotalSeconds:0.00}s)" );
		var renderTimer = System.Diagnostics.Stopwatch.StartNew();
		// Match consumption order. A queued water material held until the end must
		// not occupy every look-ahead slot while opaque materials load synchronously.
		var worldOrder = worldMaterials.Except( waterMaterials, StringComparer.OrdinalIgnoreCase )
			.Except( translucentMaterials, StringComparer.OrdinalIgnoreCase ).OrderBy( static name => name, StringComparer.OrdinalIgnoreCase );
		using var prefetch = new Titanfall2MaterialPrefetch( Host,
			worldOrder.Concat( EnumerateChunkMaterials( translucentGroups ) ).Concat( EnumerateChunkMaterials( decalGroups ) )
				.Concat( EnumerateChunkMaterials( godrayGroups ) ).Concat( EnumerateChunkMaterials( waterGroups ) )
				.Select( name => Host.TryGetMaterialPath( name, out var registered ) ? registered : name ) );
		_materialPrefetch = prefetch;
		// G4: world shadows can come from a depth-only copy of the opaque world instead of the
		// forward aggregates; the copy still exists here so this stays independent of SHADOW_CASCADES.
		var useShadowGeometry = Titanfall2StreamingSettings.MapShadows && Titanfall2StreamingSettings.ShadowGeometry;
		var useMaterialTable = Titanfall2StreamingSettings.WorldMaterialTable;
		var resolvedMaterials = new Dictionary<WorldMaterialKey, Material>();
		Material ResolveMaterial( WorldMaterialKey key, int index )
		{
			if ( resolvedMaterials.TryGetValue( key, out var material ) ) return material;
			material = CreateWorldLightmappedMaterial( key, index, lightmaps );
			resolvedMaterials.Add( key, material );
			return material;
		}
		List<MountedAggregate> worldAggregates = null;
		IReadOnlyList<WorldMaterialKey> worldAggregateKeys = [];
		if ( !useMaterialTable )
			worldAggregates = Titanfall2WorldAggregator.Build( groups, parsed.MapName, "world", cellSize, Path, Log,
				ResolveMaterial, out _, out worldAggregateKeys );
		if ( useMaterialTable )
		{
			foreach ( var key in groups.Values.SelectMany( materials => materials.Keys ).Distinct()
				.OrderBy( key => key.MaterialName, StringComparer.OrdinalIgnoreCase ).ThenBy( key => key.LightmapPage ) )
				ResolveMaterial( key, resolvedMaterials.Count );
		}
		var materialBatch = useMaterialTable ? Titanfall2WorldMaterialBatch.Build( groups, resolvedMaterials,
			key => !cutoutMaterials.Contains( key.MaterialName )
				&& !Titanfall2MaterialAnimationRegistry.IsAnimated( LoadWorldMaterial( key.MaterialName ) ),
			$"titanfall2/map/{parsed.MapName}_world_material_table" ) : null;
		if ( materialBatch is not null )
		{
			worldAggregates = materialBatch.Aggregates.ToList();
			worldAggregateKeys = materialBatch.Keys;
			Log.Info( $"Titanfall 2 world material table: {resolvedMaterials.Count} source materials -> {worldAggregates.Count} aggregates, "
				+ $"{materialBatch.Owned.Tables.Length} shared page materials, {materialBatch.Owned.Sources.Length} table rows, "
				+ $"{worldAggregates.Sum( aggregate => aggregate.Fragments.Count )} retained fragments ({Path})." );
		}
		var worldRenderMeshCount = worldAggregates.Count;
		// Per-phase timings show whether world geometry, decals or Godrays moved the load time.
		var worldPhase = renderTimer.Elapsed;
		var translucentChunks = BuildRenderChunks( translucentGroups, parsed.MapName, "translucent",
			( materialKey, meshIndex ) => LoadWorldMaterial( materialKey.MaterialName )
				?? MaterialLoader.CreateRuntimeMaterial( $"{Path}/translucent_fallback_{meshIndex}", GetMaterialMetadata( materialKey.MaterialName ) ),
			out var translucentRenderMeshCount );
		var translucentPhase = renderTimer.Elapsed - worldPhase;
		var decalChunks = BuildRenderChunks(
			decalGroups, parsed.MapName, "decals",
			( materialKey, meshIndex ) => LoadWorldMaterial( materialKey.MaterialName )
				?? MaterialLoader.CreateDecalMaterial( $"{Path}/decal_fallback_{meshIndex}", GetMaterialMetadata( materialKey.MaterialName ) ),
			out var decalRenderMeshCount );
		var decalPhase = renderTimer.Elapsed - worldPhase - translucentPhase;
		var godrayChunks = BuildRenderChunks(
			godrayGroups, parsed.MapName, "godrays",
			( materialKey, meshIndex ) => LoadWorldMaterial( materialKey.MaterialName )
				?? MaterialLoader.CreateGodrayMaterial( $"{Path}/godray_fallback_{meshIndex}", materialKey.MaterialName ),
			out var godrayRenderMeshCount );
		var godrayPhase = renderTimer.Elapsed - worldPhase - translucentPhase - decalPhase;
		var waterChunks = BuildRenderChunks( waterGroups, parsed.MapName, "water",
			( materialKey, meshIndex ) => LoadWorldMaterial( materialKey.MaterialName )
				?? MaterialLoader.CreateWaterMaterial( $"{Path}/water_fallback_{meshIndex}", GetMaterialMetadata( materialKey.MaterialName ) ),
			out var waterRenderMeshCount );
		var waterPhase = renderTimer.Elapsed - worldPhase - translucentPhase - decalPhase - godrayPhase;
		// Only depth reaches the shadow map, so a plain runtime material replaces the tool texture.
		var shadowOnlyChunks = BuildRenderChunks( shadowOnlyGroups, parsed.MapName, "shadow_only",
			( materialKey, meshIndex ) => MaterialLoader.CreateRuntimeMaterial( $"{Path}/shadow_only_{meshIndex}", GetMaterialMetadata( materialKey.MaterialName ) ),
			out var shadowOnlyRenderMeshCount );
		if ( worldAggregates.Count == 0 && translucentChunks.Count == 0 && waterChunks.Count == 0 ) return null;
		Log.Info( $"Titanfall 2 spatial render models created: {Path} "
			+ $"({worldAggregates.Count} world aggregates/{worldRenderMeshCount} meshes, "
			+ $"{translucentChunks.Count} translucent chunks/{translucentRenderMeshCount} meshes, "
			+ $"{decalChunks.Count} decal chunks/{decalRenderMeshCount} meshes, "
			+ $"{godrayChunks.Count} Godray chunks/{godrayRenderMeshCount} meshes; "
			+ $"{waterChunks.Count} water chunks/{waterRenderMeshCount} meshes, "
			+ $"{shadowOnlyChunks.Count} shadow-only light-blocker chunks/{shadowOnlyRenderMeshCount} meshes from {shadowOnlyMeshCount} source meshes; "
			+ $"world {worldPhase.TotalSeconds:0.00}s, translucent {translucentPhase.TotalSeconds:0.00}s, decals {decalPhase.TotalSeconds:0.00}s, "
			+ $"Godrays {godrayPhase.TotalSeconds:0.00}s, "
			+ $"water {waterPhase.TotalSeconds:0.00}s, "
			+ $"{renderTimer.Elapsed.TotalSeconds:0.00}s)" );

		// Depth-only world shadow geometry: same opaque world triangles, one mesh per spatial cell
		// (so the cascades still cull per cell) and one shared runtime material (so the shadow pass
		// binds a single depth shader instead of every forward material). Built after the null check
		// above so a rejected build never creates models nobody owns.
		IReadOnlyList<WorldRenderChunk> worldShadowChunks = null;
		if ( useShadowGeometry )
		{
			var shadowTimer = System.Diagnostics.Stopwatch.StartNew();
			var shadowMaterial = MaterialLoader.CreateRuntimeMaterial( $"{Path}/world_shadow_only" );
			worldShadowChunks = BuildWorldShadowChunks( groups, cutoutMaterials, parsed.MapName, shadowMaterial,
				out var shadowTriangleCount, out var shadowBytes );
			Log.Info( $"Titanfall 2 shadow-only world geometry: {Path} ({worldShadowChunks.Count} chunks, "
				+ $"{shadowTriangleCount} triangles, {shadowBytes / (1024.0 * 1024.0):0.0} MB, "
				+ $"built in {shadowTimer.Elapsed.TotalSeconds:0.00}s)" );
			if ( worldShadowChunks.Count == 0 )
			{
				// Never trade the world's shadows for nothing: an empty copy means the aggregates
				// keep casting exactly as before.
				Log.Warning( $"Titanfall 2 shadow-only world geometry was empty; world aggregates keep casting ({Path})." );
				worldShadowChunks = null;
				useShadowGeometry = false;
			}
		}

		// Alpha-tested (cutout) materials are not part of the depth-only copy: a plain depth material
		// would make them cast solid shadows instead of their authored cut-outs, so they keep casting
		// through their own aggregate. Every other aggregate stops casting when the copy exists.
		var aggregateCastShadows = new bool[worldAggregates.Count];
		for ( var index = 0; index < aggregateCastShadows.Length; index++ )
			aggregateCastShadows[index] = useShadowGeometry
				? index < worldAggregateKeys.Count && cutoutMaterials.Contains( worldAggregateKeys[index].MaterialName )
				: Titanfall2StreamingSettings.MapShadows;

		PhysicsGroupDescription collisionPhysics = null;
		if ( collisionPositions is { Count: > 0 } && collisionIndices is { Count: > 0 } )
		{
			var body = new PhysicsBodyBuilder();
			body.AddMesh( CollectionsMarshal.AsSpan( collisionPositions ), collisionIndices.Select( index => (uint)index ).ToArray(), [] );
			collisionPhysics = PhysicsGroupDescription.Create( $"{Path}/fallback.vphys", [body] );
			Log.Info( $"Titanfall 2 fallback render collision submitted: {Path} ({collisionIndices.Count / 3} triangles)" );
		}
		var ownedResources = (lightmaps?.OwnedResources ?? Array.Empty<IDisposable>()).ToList();
		var result = new WorldBuildResult( translucentChunks, decalChunks, godrayChunks, waterChunks, worldAggregates, collisionPhysics,
			ownedResources, shadowOnlyChunks, worldShadowChunks, aggregateCastShadows, materialBatch?.Owned.Sources,
			materialBatch is not null ? Titanfall2WorldMaterialBatch.Register( materialBatch.Owned.Sources ) : null );
		return result;
	}

	/// <summary>
	/// Builds the depth-only world shadow geometry: merges all opaque (non-cutout) world groups of one
	/// spatial cell into a single mesh that shares one runtime material with every other cell. The
	/// triangle set, winding order and vertex data are the ones the forward aggregates already hold,
	/// so the shadow map sees the same surfaces at the same depth; only the submission cost changes.
	/// </summary>
	List<WorldRenderChunk> BuildWorldShadowChunks(
		Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>> chunks,
		HashSet<string> excludedMaterials,
		string mapName,
		Material material,
		out int triangleCount,
		out long bytes )
	{
		var result = new List<WorldRenderChunk>( chunks.Count );
		triangleCount = 0;
		bytes = 0;
		foreach ( var (cell, materials) in chunks
			.OrderBy( static pair => pair.Key.X )
			.ThenBy( static pair => pair.Key.Y )
			.ThenBy( static pair => pair.Key.Z ) )
		{
			var groups = materials
				.Where( static pair => pair.Value.Vertices.Count > 0 && pair.Value.Indices.Count > 0 )
				.Where( pair => !excludedMaterials.Contains( pair.Key.MaterialName ) )
				.OrderBy( static pair => pair.Key.MaterialName, StringComparer.OrdinalIgnoreCase )
				.ThenBy( static pair => pair.Key.LightmapPage )
				.ToArray();
			if ( groups.Length == 0 ) continue;

			var vertexCount = 0;
			var indexCount = 0;
			foreach ( var (_, group) in groups )
			{
				vertexCount += group.Vertices.Count;
				indexCount += group.Indices.Count;
			}

			var vertices = new List<TitanfallMapVertex>( vertexCount );
			var indices = new List<int>( indexCount );
			var bounds = groups[0].Value.Bounds;
			foreach ( var (_, group) in groups )
			{
				var offset = vertices.Count;
				vertices.AddRange( group.Vertices );
				foreach ( var index in group.Indices )
					indices.Add( offset + index );
			}
			for ( var index = 1; index < groups.Length; index++ )
				bounds = bounds.AddBBox( groups[index].Value.Bounds );

			var builder = Model.Builder.WithName( $"titanfall2/map/{mapName}_shadow_{cell}" );
			var mesh = new Mesh( $"{Path}_shadow_{cell}", material );
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.CreateIndexBuffer( indices.Count, indices );
			mesh.Bounds = bounds;
			builder.AddMesh( mesh );
			result.Add( new WorldRenderChunk( cell, builder.Create() ) );
			triangleCount += indices.Count / 3;
			bytes += (long)vertices.Count * System.Runtime.CompilerServices.Unsafe.SizeOf<TitanfallMapVertex>()
				+ (long)indices.Count * sizeof( int );
		}
		return result;
	}

	static IEnumerable<string> EnumerateChunkMaterials( Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>> chunks ) =>
		chunks.OrderBy( pair => pair.Key.X ).ThenBy( pair => pair.Key.Y ).ThenBy( pair => pair.Key.Z )
			.SelectMany( pair => pair.Value.Keys.OrderBy( key => key.MaterialName, StringComparer.OrdinalIgnoreCase )
				.ThenBy( key => key.LightmapPage ) ).Select( key => key.MaterialName );

	/// <summary>
	/// Largest single (cell, material) group across the given dictionaries, as vertices and indices.
	/// Reported so a cell-size change can be judged against the per-draw buffer size it produces.
	/// </summary>
	static (int Vertices, int Indices) LargestGroup(
		params Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>>[] groupSets )
	{
		var vertices = 0;
		var indices = 0;
		foreach ( var chunkSet in groupSets )
		{
			foreach ( var materials in chunkSet.Values )
			{
				foreach ( var group in materials.Values )
				{
					vertices = Math.Max( vertices, group.Vertices.Count );
					indices = Math.Max( indices, group.Indices.Count );
				}
			}
		}
		return (vertices, indices);
	}

	internal static TitanfallMapVertex CreateWorldVertex( Titanfall2BspReader.WorldVertex sourceVertex, bool isDecal )
	{
		var normal = ToSandbox( sourceVertex.Normal ).Normal;
		var position = ToSandbox( sourceVertex.Position );
		if ( isDecal ) position = DecalOffsetPosition( position, normal );
		return new TitanfallMapVertex
		{
			Position = position,
			Normal = normal,
			TexCoord = ToSandbox( sourceVertex.TexCoord ),
			LightmapTexCoord = ToSandbox( sourceVertex.LightmapTexCoord ),
			Color = new Color32( sourceVertex.Color.R, sourceVertex.Color.G, sourceVertex.Color.B, sourceVertex.Color.A )
		};
	}

	/// <summary>
	/// Position a triangle corner is grouped and collided at. Uses the same decal offset as
	/// <see cref="CreateWorldVertex"/> so cell selection keeps matching the stored vertex.
	/// </summary>
	static Vector3 SourceTrianglePosition( Titanfall2BspReader.WorldVertex vertex, bool isDecal )
	{
		var position = ToSandbox( vertex.Position );
		if ( !isDecal ) return position;
		return DecalOffsetPosition( position, ToSandbox( vertex.Normal ).Normal );
	}

	static Vector3 DecalOffsetPosition( Vector3 position, Vector3 normal ) => position + normal * DecalNormalOffset;

	/// <summary>
	/// Buckets one source mesh's triangles by spatial cell and, when fallback collision needs it,
	/// emits the triangle corner positions the serial loop used to append.
	/// </summary>
	static WorldMeshCellBuckets BucketMeshTriangles( WorldMeshSource source, float cellSize )
	{
		var mesh = source.Mesh;
		var vertices = mesh.Vertices;
		var indices = mesh.Indices;
		var result = new WorldMeshCellBuckets
		{
			CollisionPositions = source.IncludeCollision ? new List<Vector3>( indices.Length ) : null,
			CollisionIndices = source.IncludeCollision ? new List<int>( indices.Length ) : null
		};
		var cells = new Dictionary<WorldRenderCell, List<int>>();
		for ( var index = 0; index + 2 < indices.Length; index += 3 )
		{
			var sourceIndex0 = indices[index];
			var sourceIndex1 = indices[index + 1];
			var sourceIndex2 = indices[index + 2];
			if ( (uint)sourceIndex0 >= vertices.Length
				|| (uint)sourceIndex1 >= vertices.Length
				|| (uint)sourceIndex2 >= vertices.Length ) continue;

			var position0 = SourceTrianglePosition( vertices[sourceIndex0], source.IsDecal );
			var position1 = SourceTrianglePosition( vertices[sourceIndex1], source.IsDecal );
			var position2 = SourceTrianglePosition( vertices[sourceIndex2], source.IsDecal );
			var cell = WorldRenderCell.FromTriangle( position0, position1, position2, cellSize );
			if ( !cells.TryGetValue( cell, out var triangles ) )
			{
				triangles = new List<int>();
				cells.Add( cell, triangles );
				result.Cells.Add( new WorldCellTriangles( cell, triangles ) );
			}
			triangles.Add( index );
			if ( result.CollisionPositions is null ) continue;

			var collisionBase = result.CollisionPositions.Count;
			result.CollisionPositions.Add( position0 );
			result.CollisionPositions.Add( position1 );
			result.CollisionPositions.Add( position2 );
			result.CollisionIndices.Add( collisionBase );
			result.CollisionIndices.Add( collisionBase + 2 );
			result.CollisionIndices.Add( collisionBase + 1 );
		}
		return result;
	}

	/// <summary>
	/// Builds every group of one spatial cell. The worker that owns the cell is the only thread that
	/// touches these groups, so the material dictionaries and vertex lists need no lock.
	/// </summary>
	static WorldCellGroups BuildCellGroups( IReadOnlyList<WorldCellContribution> contributions, IReadOnlyList<WorldMeshSource> sources )
	{
		var result = new WorldCellGroups();
		foreach ( var contribution in contributions )
		{
			var source = sources[contribution.SourceIndex];
			var mesh = source.Mesh;
			var group = result.GetGroup( source.Category, source.MaterialKey );
			// One slot per source vertex inside this cell. The BSP reader already compacted the
			// source triangles onto a vertex table, so an int array beats a dictionary here.
			var remap = new int[mesh.Vertices.Length];
			Array.Fill( remap, -1 );
			foreach ( var start in contribution.TriangleStarts )
			{
				var local0 = AppendGroupVertex( group, remap, mesh.Vertices, mesh.Indices[start], source.IsDecal, out var position0 );
				var local1 = AppendGroupVertex( group, remap, mesh.Vertices, mesh.Indices[start + 1], source.IsDecal, out var position1 );
				var local2 = AppendGroupVertex( group, remap, mesh.Vertices, mesh.Indices[start + 2], source.IsDecal, out var position2 );
				// Same corner data and winding flip as the serial loop; only repeated vertices are shared.
				group.Indices.Add( local0 );
				group.Indices.Add( local2 );
				group.Indices.Add( local1 );
				group.Bounds = group.Bounds
					.AddPoint( position0 )
					.AddPoint( position1 )
					.AddPoint( position2 );
			}
		}
		return result;
	}

	/// <summary>Returns the group-local index of one source vertex inside a cell, adding it on first use.</summary>
	static int AppendGroupVertex( MapMeshGroup group, int[] remap, Titanfall2BspReader.WorldVertex[] vertices,
		int sourceIndex, bool isDecal, out Vector3 position )
	{
		var slot = remap[sourceIndex];
		if ( slot >= 0 )
		{
			position = group.Vertices[slot].Position;
			return slot;
		}

		var vertex = CreateWorldVertex( vertices[sourceIndex], isDecal );
		slot = group.Vertices.Count;
		group.Vertices.Add( vertex );
		remap[sourceIndex] = slot;
		position = vertex.Position;
		return slot;
	}

	List<WorldRenderChunk> BuildRenderChunks(
		Dictionary<WorldRenderCell, Dictionary<WorldMaterialKey, MapMeshGroup>> chunks,
		string mapName,
		string category,
		Func<WorldMaterialKey, int, Material> materialFactory,
		out int totalMeshCount )
	{
		var result = new List<WorldRenderChunk>( chunks.Count );
		totalMeshCount = 0;
		foreach ( var (cell, materials) in chunks
			.OrderBy( static pair => pair.Key.X )
			.ThenBy( static pair => pair.Key.Y )
			.ThenBy( static pair => pair.Key.Z ) )
		{
			var builder = Model.Builder.WithName( $"titanfall2/map/{mapName}_{category}_{cell}" );
			var chunkMeshCount = 0;
			foreach ( var (materialKey, group) in materials
				.OrderBy( static pair => pair.Key.MaterialName, StringComparer.OrdinalIgnoreCase )
				.ThenBy( static pair => pair.Key.LightmapPage ) )
			{
				if ( group.Vertices.Count == 0 || group.Indices.Count == 0 ) continue;
				var material = materialFactory( materialKey, totalMeshCount );
				var mesh = new Mesh( $"{Path}_{category}_{cell}_{totalMeshCount}", material );
				mesh.CreateVertexBuffer( group.Vertices.Count, group.Vertices );
				mesh.CreateIndexBuffer( group.Indices.Count, group.Indices );
				mesh.Bounds = group.Bounds;
				builder.AddMesh( mesh );
				chunkMeshCount++;
				totalMeshCount++;
			}
			if ( chunkMeshCount > 0 ) result.Add( new WorldRenderChunk( cell, builder.Create() ) );
		}
		return result;
	}

	Material CreateWorldLightmappedMaterial( WorldMaterialKey key, int meshIndex, RuntimeLightmaps lightmaps )
	{
		var sourceMaterial = LoadWorldMaterial( key.MaterialName )
			?? MaterialLoader.CreateRuntimeMaterial( $"{Path}/world_fallback_{meshIndex}", GetMaterialMetadata( key.MaterialName ) );
		if ( !Titanfall2StreamingSettings.UseBspLightmaps )
			return sourceMaterial;
		var metadata = GetMaterialMetadata( key.MaterialName );
		if ( lightmaps is null || key.LightmapPage < 0 || key.LightmapPage >= lightmaps.Pages.Count
			|| metadata.Mode != Titanfall2MaterialMode.Opaque || metadata.IsUnlit || metadata.IsWater )
			return sourceMaterial;

		var page = lightmaps.Pages[key.LightmapPage];
		if ( page.SkyA is null || !page.SkyA.IsValid || page.SkyB is null || !page.SkyB.IsValid )
			return sourceMaterial;

		return CreateLightmappedMaterial( $"{Path}/world_lightmapped_{meshIndex}_{key.LightmapPage}", sourceMaterial, page, lightmaps.SkyColor );
	}

	internal static Material CreateLightmappedMaterial( string name, Material sourceMaterial, RuntimeLightmapPage page, Color skyColor )
	{
		var material = Material.Create( name, "shaders/titanfall2_world_lightmapped.shader" );
		CopyWorldMaterialParameters( sourceMaterial, material );
		material.Set( "g_tLightmapSkyA", page.SkyA );
		material.Set( "g_tLightmapSkyB", page.SkyB );
		if ( page.RealTimeA is not null ) material.Set( "g_tLightmapRealTimeA", page.RealTimeA );
		if ( page.RealTimeB is not null ) material.Set( "g_tLightmapRealTimeB", page.RealTimeB );
		if ( page.SunChannels is not null ) material.Set( "g_tLightmapSunChannels", page.SunChannels );
		material.Set( "g_flT2HasSunLightmap", page.SunChannels is not null ? 1f : 0f );
		material.Set( "g_flT2LightmapIntensity", 1f );
		material.Set( "g_vT2LightmapSkyColor", new Vector3( skyColor.r, skyColor.g, skyColor.b ) );
		material.Set( "g_flT2RealTimeLightContribution", 0f );
		return material;
	}

	static void CopyWorldMaterialParameters( Material source, Material target )
	{
		string[] textureNames =
		[
			"g_tAlbedo", "g_tNormal", "g_tDetailNormal", "g_tGloss", "g_tSpecular", "g_tAO", "g_tCavity", "g_tOpacity",
			"g_tEmissive", "g_tEmissiveMultiply", "g_tDetail", "g_tDistortion", "g_tEnvironment",
			"g_tAlbedo2", "g_tNormal2", "g_tGloss2", "g_tSpecular2", "g_tLayerBlend"
		];
		foreach ( var name in textureNames )
		{
			var texture = source?.GetTexture( name );
			if ( texture is not null && texture.IsValid )
				target.Set( name, texture );
		}

		string[] vectorNames =
		[
			"g_vT2DetailNormal", "g_flT2GlossScale", "g_vT2SpecularTint", "g_flT2EmissiveStrength",
			"g_flT2DetailBlend", "g_vT2UvDistortion", "g_flT2HasEnvironment",
			"g_flT2EnvironmentIntensity", "g_flT2FresnelStrength", "g_vT2Uv1RotScale",
			"g_vT2Uv1Translate", "g_vT2AlbedoTint", "g_vT2EmissiveTint",
			"g_flT2MaterialOpacity", "g_flT2AlphaTestReference", "g_flUseVertexColor",
			"g_flT2Layered", "g_flT2LayerBlendRamp", "g_flHasNormalMap", "g_flHasNormalMap2",
			"g_flT2SpecularWorkflow",
			"g_flT2HasCavity", "g_flT2CavityUvChannel",
			"g_flT2HasEmissiveMultiply", "g_flT2EmissiveMultiplyUvChannel", "g_vT2AuxUvChannels",
			"g_vT2SurfaceUvChannels", "g_vT2SurfaceUvChannels2", "g_flT2LayerBlendUvChannel",
			"g_vT2Uv2RotScale", "g_vT2Uv2Translate", "g_vT2Uv3RotScale", "g_vT2Uv3Translate",
			"g_flT2NormalSlope", "g_flT2NormalSlope2", "g_flT2NormalTwoChannel", "g_flT2NormalTwoChannel2"
		];
		foreach ( var name in vectorNames )
			target.Set( name, source?.GetVector4( name ) ?? default );
	}

	Color ReadBakedSkyColor( Titanfall2BspReader.ParsedBsp parsed )
	{
		return ReadBakedSkyColor( parsed, ReadEnvironmentEntities( parsed ) );
	}

	internal static Color ReadBakedSkyColor( Titanfall2BspReader.ParsedBsp parsed, IReadOnlyList<Dictionary<string, string>> entities )
	{
		var lightValues = Titanfall2LightEnvironment.Default( entities );
		var compiled = SelectBakedEnvironmentLight( parsed, lightValues, Titanfall2WorldLight.SkyAmbientType );
		if ( compiled is not null ) return new Color( compiled.Intensity.X, compiled.Intensity.Y, compiled.Intensity.Z );
		var color = TryParseSourceLightColor( lightValues?.GetValueOrDefault( "_ambient" ), 0.30f, out var authored )
			? authored : new Color( 0.22f, 0.28f, 0.36f );
		return color * AmbientScale;
	}

	Material LoadWorldMaterial( string materialName )
	{
		if ( string.IsNullOrWhiteSpace( materialName ) || !Host.TryGetMaterialPath( materialName, out var registeredPath ) )
			return null;
		_materialPrefetch?.Wait( registeredPath );
		try
		{
			var material = Material.Load( $"mount://{Host.Ident}/{registeredPath}.vmat" );
			return material is not null && material.IsValid ? material : null;
		}
		finally { _materialPrefetch?.Consumed( registeredPath ); }
	}

	Titanfall2MaterialMetadata GetMaterialMetadata( string materialName )
	{
		if ( Host.TryGetMaterialMetadata( materialName, out var metadata ) ) return metadata;
		return MaterialLoader.InferMetadata( materialName );
	}

	static string NormalizeMaterialName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return string.Empty;
		var normalized = name.Replace( '\\', '/' ).Trim( '/' );
		foreach ( var extension in new[] { ".vmat", ".rpak", ".matl" } )
		{
			if ( normalized.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) )
				normalized = normalized[..^extension.Length];
		}
		return normalized;
	}

	static bool ShouldSkipWorldMaterial( string materialName )
	{
		if ( string.IsNullOrWhiteSpace( materialName ) ) return false;
		return materialName.Contains( "toolsskybox", StringComparison.OrdinalIgnoreCase )
			|| materialName.Contains( "toolsnodraw", StringComparison.OrdinalIgnoreCase )
			|| materialName.Contains( "toolsinvisible", StringComparison.OrdinalIgnoreCase );
	}

	static float DistanceSquared( System.Numerics.Vector3 source, Vector3 target )
	{
		var x = source.X - target.x;
		var y = source.Y - target.y;
		var z = source.Z - target.z;
		return x * x + y * y + z * z;
	}
}
