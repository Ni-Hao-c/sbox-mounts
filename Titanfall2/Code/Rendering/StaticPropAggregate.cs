using System.Diagnostics;
using System.Runtime.InteropServices;
using Sandbox.Mounting.PerfKit;
using Titanfall2;

/// <summary>
/// World-space static prop vertex carrying per-vertex material and light-probe table indices
/// for native AggregateRenderer indirect submission (S_T2_PROP_AGGREGATE).
/// </summary>
[StructLayout( LayoutKind.Sequential )]
struct TitanfallPropAggregateVertex
{
	[VertexLayout.Position] public Vector3 Position;
	[VertexLayout.Normal] public Vector3 Normal;
	[VertexLayout.TexCoord] public Vector2 TexCoord;
	[VertexLayout.Color] public Color32 Color;
	[VertexLayout.TexCoord( 9 )] public Vector2 PropIds;
}

/// <summary>Minimal world-space vertex for depth-only LOD3 static prop shadow chunks.</summary>
[StructLayout( LayoutKind.Sequential )]
struct TitanfallPropShadowVertex
{
	[VertexLayout.Position] public Vector3 Position;
	[VertexLayout.Normal] public Vector3 Normal;
}

/// <summary>World-space vertex for batched residual decal/translucent static prop meshes.</summary>
[StructLayout( LayoutKind.Sequential )]
struct TitanfallPropResidualVertex
{
	[VertexLayout.Position] public Vector3 Position;
	[VertexLayout.Normal] public Vector3 Normal;
	[VertexLayout.TexCoord] public Vector2 TexCoord;
	[VertexLayout.Color] public Color32 Color;
}

/// <summary>
/// Aggregates compatible static props into:
///   1. 2048u macro sectors with Shared (1-LOD) + Near/Far 2-tier LOD native AggregateRenderers
///      backed by map-wide material (T2PropMaterials) and probe (T2PropLightProbes) GPU buffers;
///   2. 2048u depth-only LOD3 ShadowsOnly ModelRenderers (opaque shadow casters);
///   3. Material-grouped residual AggregateRenderers for non-animated decals/translucent submeshes.
/// </summary>
sealed partial class Titanfall2StaticPropAggregate : IDisposable
{
	static readonly Sandbox.Diagnostics.Logger Log = new( "Titanfall2Props" );
	// Macro sector size for Near/Far LOD switching (~16 sectors per retail map).
	const float SectorSize = 4096f;
	// Camera distance thresholds (with hysteresis) for switching a sector between Near and Far tiers.
	const float NearLodRadius = 2048f;
	const float NearLodExitRadius = NearLodRadius + 256f;
	const float CameraRecheckDistance = 128f;
	// Spatial fragment grid inside each sector/group for native frustum culling.
	const float FragmentCellSize = 1024f;
	// Spatial chunk size for depth-only LOD3 ShadowsOnly ModelRenderers (matches Map.World.cs).
	const float ShadowCellSize = 2048f;
	// Spatial chunk size for batched residual translucent/decal ModelRenderers.
	const float ResidualCellSize = 2048f;
	// Cap vertices per aggregate model (~44 MB VB) so dense maps split cleanly across models.
	const int MaxVerticesPerAggregate = 1_000_000;
	// Exact integer precision limit of IEEE-754 float32 for PropIds.
	const int MaxTableEntries = 16_777_216;
	// Major structural/cover props cast LOD3 shadows; small clutter uses baked sun visibility + contact shadows.
	internal const float AggregateShadowMinimumRadius = 128f;
	const float SmallPropLodRadius = 48f;
	const float MediumPropLodRadius = 128f;
	// Tiny non-collidable litter or small/medium non-collidable clutter with short retail FadeDistance
	// is omitted in Far (>2304u) sectors, matching retail TF2's authored fade budget while keeping
	// every collidable cover prop and large architectural prop (>=96u) visible map-wide.
	const float FarTinyNonCollidableMaxRadius = 24f;
	const float FarFadeExemptionRadius = 96f;
	const float FarAuthoredFadeCutoff = 4500f;

	internal readonly record struct PropEntry(
		Model Model,
		ModelLoader.GpuGeometry Geometry,
		Transform Transform,
		float Radius,
		float FadeDistance,
		bool Collidable,
		bool CastShadows,
		Titanfall2ProbeData Probe,
		Titanfall2BspReader.VisibilityCellMask PvsCells );

	internal readonly record struct ResidualEntry(
		ModelLoader.GpuGeometryMesh[] Meshes,
		Transform Transform,
		bool CastShadows );

	internal readonly record struct BucketSpec( string ShaderName, int TextureFiltering, bool CastShadows, int TemplateMaterialIndex );
	internal sealed record AggregatePartData( TitanfallPropAggregateVertex[] Vertices, int[] Indices, BBox Bounds );
	internal sealed record AggregateModelData( int BucketIndex, AggregatePartData[] Parts );
	internal sealed record SectorData(
		int SectorX,
		int SectorY,
		BBox Bounds,
		Titanfall2BspReader.VisibilityCellMask PvsCells,
		AggregateModelData[] NearModels,
		AggregateModelData[] FarModels );
	internal sealed record ShadowChunkData( int CellX, int CellY, TitanfallPropShadowVertex[] Vertices, int[] Indices, BBox Bounds );
	internal sealed record ResidualPartData( TitanfallPropResidualVertex[] Vertices, int[] Indices, BBox Bounds );
	internal sealed record ResidualGroupData( string MaterialName, bool CastShadows, ResidualPartData[] Parts );

	internal sealed record PropAggregatePayload(
		string[] MaterialNames,
		Titanfall2ProbeData[] Probes,
		BucketSpec[] Buckets,
		AggregateModelData[] SharedModels,
		SectorData[] Sectors,
		ShadowChunkData[] ShadowChunks,
		ResidualGroupData[] ResidualGroups,
		int[] FallbackPropIndices,
		int TotalPropCount,
		int AggregatedPropCount,
		int MixedPropCount,
		int ResidualBatchedPropCount,
		int SharedTriangles,
		int NearTriangles,
		int FarTriangles,
		int ShadowTriangles,
		int ResidualTriangles );

	readonly record struct BucketKey( string ShaderName, int TextureFiltering, bool CastShadows );
	readonly record struct CellKey( int X, int Y );
	readonly record struct SectorKey( int X, int Y );
	readonly record struct MeshPlacement(
		ModelLoader.GpuGeometryMesh Mesh,
		Transform Transform,
		int MaterialId,
		int ProbeIndex );
	readonly record struct ShadowPlacement(
		ModelLoader.GpuGeometryMesh Mesh,
		Transform Transform );
	readonly record struct ResidualPlacement(
		ModelLoader.GpuGeometryMesh Mesh,
		Transform Transform );

	enum TierKind { Shared, Near, Far }

	sealed class AggregateCellJob( SectorKey sector, TierKind tier, int bucketIndex, CellKey cell, List<MeshPlacement> placements )
	{
		public readonly SectorKey Sector = sector;
		public readonly TierKind Tier = tier;
		public readonly int BucketIndex = bucketIndex;
		public readonly CellKey Cell = cell;
		public readonly List<MeshPlacement> Placements = placements;
		public AggregatePartData Result;
	}

	sealed class ShadowChunkJob( CellKey cell, List<ShadowPlacement> placements )
	{
		public readonly CellKey Cell = cell;
		public readonly List<ShadowPlacement> Placements = placements;
		public ShadowChunkData Result;
	}

	sealed class ResidualCellJob( int groupIndex, CellKey cell, List<ResidualPlacement> placements )
	{
		public readonly int GroupIndex = groupIndex;
		public readonly CellKey Cell = cell;
		public readonly List<ResidualPlacement> Placements = placements;
		public ResidualPartData Result;
	}

	sealed class RuntimeSector( BBox bounds, Titanfall2BspReader.VisibilityCellMask pvsCells )
	{
		public readonly BBox Bounds = bounds;
		public readonly Titanfall2BspReader.VisibilityCellMask PvsCells = pvsCells;
		public readonly List<GameObject> NearObjects = new();
		public readonly List<GameObject> FarObjects = new();
		public bool HasLodSwitch => NearObjects.Count > 0 || FarObjects.Count > 0;
		public bool IsNear = true;
		public bool PvsVisible = true;
	}

	readonly Scene _scene;
	readonly List<RuntimeSector> _sectors = new();
	readonly List<GameObject> _allObjects = new();
	readonly List<MountedAggregate> _aggregates = new();
	readonly List<Model> _shadowModels = new();
	readonly List<Model> _residualModels = new();
	// Keep all source and shared materials strongly referenced so bindless Texture.Index handles stay alive.
	readonly Material[] _sourceMaterials;
	readonly Material[] _sharedMaterials;
	readonly Material[] _residualMaterials;
	readonly Material _shadowMaterial;
	GpuBuffer<Titanfall2GpuMaterialData> _materialBuffer;
	GpuBuffer<Titanfall2ProbeData> _probeBuffer;
	Vector3 _lastLodAnchor;
	Titanfall2BspReader.VisibilityCellMask _lastPvsMask;
	bool _hasLodAnchor;
	bool _enabled;
	bool _disposed;

	public int PropCount { get; }
	public int MixedPropCount { get; }
	public int ResidualBatchedPropCount { get; }
	public int AggregateCount => _aggregates.Count;
	public int SectorCount => _sectors.Count;
	public int NearSectorCount { get; private set; }
	public int PvsCulledSectorCount { get; private set; }
	public int ShadowChunkCount => _shadowModels.Count;
	public int ResidualGroupCount { get; }
	public int FragmentCount { get; }
	public int MaterialCount => _sourceMaterials.Length;
	public int ProbeCount { get; }
	public int SharedTriangles { get; }
	public int NearTriangles { get; }
	public int FarTriangles { get; }
	public int ShadowTriangles { get; }
	public int ResidualTriangles { get; }
	public bool LoadedFromCache { get; }

	Titanfall2StaticPropAggregate(
		Scene scene,
		Material[] sourceMaterials,
		Material[] sharedMaterials,
		Material[] residualMaterials,
		Material shadowMaterial,
		GpuBuffer<Titanfall2GpuMaterialData> materialBuffer,
		GpuBuffer<Titanfall2ProbeData> probeBuffer,
		PropAggregatePayload payload,
		int fragmentCount,
		bool loadedFromCache,
		bool enabled )
	{
		_scene = scene;
		_sourceMaterials = sourceMaterials;
		_sharedMaterials = sharedMaterials;
		_residualMaterials = residualMaterials;
		_shadowMaterial = shadowMaterial;
		_materialBuffer = materialBuffer;
		_probeBuffer = probeBuffer;
		PropCount = payload.AggregatedPropCount;
		MixedPropCount = payload.MixedPropCount;
		ResidualBatchedPropCount = payload.ResidualBatchedPropCount;
		ResidualGroupCount = payload.ResidualGroups.Length;
		FragmentCount = fragmentCount;
		ProbeCount = payload.Probes.Length;
		SharedTriangles = payload.SharedTriangles;
		NearTriangles = payload.NearTriangles;
		FarTriangles = payload.FarTriangles;
		ShadowTriangles = payload.ShadowTriangles;
		ResidualTriangles = payload.ResidualTriangles;
		LoadedFromCache = loadedFromCache;
		_enabled = enabled;
	}

	internal static bool CanAggregate( Model model, out ModelLoader.GpuGeometry geometry, bool allowMixed = false )
	{
		geometry = null;
		if ( model is null || !ModelLoader.TryGetGpuGeometry( model, out var candidate ) ) return false;
		if ( !candidate.HasGpuMeshes ) return false;
		if ( !allowMixed && candidate.IsMixed ) return false;
		if ( allowMixed && (!candidate.IsMixed || candidate.ResidualMask == 0) ) return false;

		foreach ( var lodMeshes in candidate.Lods )
		{
			if ( lodMeshes is null ) continue;
			foreach ( var mesh in lodMeshes )
			{
				if ( mesh.Vertices.Length == 0 || mesh.Indices.Length < 3 ) continue;
				if ( !CanTableMaterial( mesh.Material ) ) return false;
			}
		}

		geometry = candidate;
		return true;
	}

	internal static bool CanBatchResidualMeshes( ModelLoader.GpuGeometry geometry )
	{
		if ( geometry is null || !geometry.HasResidualMeshes ) return false;
		foreach ( var mesh in geometry.ResidualMeshes )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Indices.Length < 3 ) continue;
			if ( mesh.Material is null || !mesh.Material.IsValid ) return false;
			if ( string.IsNullOrWhiteSpace( mesh.MaterialName ) ) return false;
			if ( Titanfall2MaterialAnimationRegistry.IsAnimated( mesh.Material ) ) return false;
		}
		return true;
	}

	static bool CanTableMaterial( Material material )
	{
		if ( material is null || !material.IsValid ) return false;
		var shader = NormalizeShaderName( material.ShaderName );
		if ( !shader.EndsWith( "/titanfall2_default.shader", StringComparison.OrdinalIgnoreCase )
			&& !shader.EndsWith( "/titanfall2_cutout.shader", StringComparison.OrdinalIgnoreCase ) )
			return false;

		return material.GetVector4( "g_flT2HasEnvironment" ).x < 0.5f
			&& material.GetFeature( "F_ADDITIVE_BLEND" ) == 0
			&& !Titanfall2MaterialAnimationRegistry.IsAnimated( material );
	}

	static int SelectNearLod( float radius, int lodCount )
	{
		if ( lodCount <= 1 ) return 0;
		if ( radius < SmallPropLodRadius ) return Math.Min( 2, lodCount - 1 );
		if ( radius < MediumPropLodRadius ) return Math.Min( 1, lodCount - 1 );
		return 0;
	}

	static int CountTriangles( ModelLoader.GpuGeometryMesh[] meshes )
	{
		var tris = 0;
		for ( var i = 0; i < meshes.Length; i++ ) tris += meshes[i].Indices.Length / 3;
		return tris;
	}

	static string NormalizeShaderName( string name ) => (name ?? string.Empty).Replace( '\\', '/' );

	static bool IsCutoutShader( string shaderName ) =>
		shaderName.Contains( "cutout", StringComparison.OrdinalIgnoreCase );

	internal static PropAggregatePayload CompilePayload(
		IReadOnlyList<PropEntry> entries,
		IReadOnlyList<ResidualEntry> residualEntries,
		int[] fallbackPropIndices,
		int totalPropCount,
		int mixedPropCount,
		int residualBatchedPropCount,
		out Dictionary<string, Material> liveMaterials )
	{
		var materialsByName = new Dictionary<string, Material>( StringComparer.OrdinalIgnoreCase );
		liveMaterials = materialsByName;
		if ( (entries is null || entries.Count == 0) && (residualEntries is null || residualEntries.Count == 0) )
			return null;

		var entryCount = entries?.Count ?? 0;
		if ( entryCount >= MaxTableEntries )
			throw new InvalidOperationException( $"Static prop aggregate exceeds probe table precision ({entryCount})." );

		var sourceMaterials = new List<Material>();
		var sourceMaterialNames = new List<string>();
		var materialIds = new Dictionary<Material, int>();
		var bucketIndices = new Dictionary<BucketKey, int>();
		var bucketSpecs = new List<BucketSpec>();

		// Sector -> (Tier, BucketIndex, Cell) -> Placements
		var sectorTierCells = new Dictionary<(SectorKey Sector, TierKind Tier, int BucketIndex, CellKey Cell), List<MeshPlacement>>();
		var sectorPvsMasks = new Dictionary<SectorKey, Titanfall2BspReader.VisibilityCellMask>();
		var shadowCells = new Dictionary<CellKey, List<ShadowPlacement>>();
		var probes = new Titanfall2ProbeData[Math.Max( 1, entryCount )];
		var inverseSector = 1f / SectorSize;
		var inverseCell = 1f / FragmentCellSize;
		var inverseShadowCell = 1f / ShadowCellSize;
		var inverseResidualCell = 1f / ResidualCellSize;
		var simplifiedFarCache = new Dictionary<Model, ModelLoader.GpuGeometryMesh[]>();
		var simplifiedShadowCache = new Dictionary<Model, ModelLoader.GpuGeometryMesh[]>();

		int GetOrRegisterMaterial( Material material, string materialName )
		{
			if ( materialIds.TryGetValue( material, out var id ) ) return id;
			id = sourceMaterials.Count;
			if ( id >= MaxTableEntries )
				throw new InvalidOperationException( $"Static prop aggregate exceeds material table precision ({id})." );
			var keyName = string.IsNullOrWhiteSpace( materialName ) ? material.Name : materialName;
			materialIds.Add( material, id );
			sourceMaterials.Add( material );
			sourceMaterialNames.Add( keyName );
			materialsByName[keyName] = material;
			return id;
		}

		int GetOrRegisterBucket( BucketKey key, int templateMaterialId )
		{
			if ( bucketIndices.TryGetValue( key, out var idx ) ) return idx;
			idx = bucketSpecs.Count;
			bucketIndices.Add( key, idx );
			bucketSpecs.Add( new BucketSpec( key.ShaderName, key.TextureFiltering, key.CastShadows, templateMaterialId ) );
			return idx;
		}

		void AddTierMesh( SectorKey sector, TierKind tier, CellKey cell, ModelLoader.GpuGeometryMesh mesh, Transform transform, bool propCastShadows, int probeIndex )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Indices.Length < 3 ) return;
			var matId = GetOrRegisterMaterial( mesh.Material, mesh.MaterialName );
			var shader = NormalizeShaderName( mesh.Material.ShaderName );
			// Opaque props cast shadows via dedicated depth-only LOD3 ShadowChunks; only alpha-tested cutouts keep forward CastShadows.
			var forwardCastShadows = propCastShadows && IsCutoutShader( shader );
			var bucketKey = new BucketKey( shader, mesh.Material.GetFeature( "F_TEXTURE_FILTERING" ), forwardCastShadows );
			var bucketIdx = GetOrRegisterBucket( bucketKey, matId );
			// Shared (1-LOD) props never switch between Near and Far, so keep them in one global map-wide
			// aggregate per bucket instead of splitting across sectors.
			var effectiveSector = tier == TierKind.Shared ? default : sector;
			var mapKey = (effectiveSector, tier, bucketIdx, cell);
			if ( !sectorTierCells.TryGetValue( mapKey, out var list ) )
			{
				list = new List<MeshPlacement>();
				sectorTierCells.Add( mapKey, list );
			}
			list.Add( new MeshPlacement( mesh, transform, matId, probeIndex ) );
		}

		for ( var propIndex = 0; propIndex < entryCount; propIndex++ )
		{
			var entry = entries[propIndex];
			probes[propIndex] = entry.Probe;
			var lods = entry.Geometry.Lods;
			var nearLod = SelectNearLod( entry.Radius, lods.Length );
			var farLod = Math.Max( 0, lods.Length - 1 );
			var nearMeshes = lods[nearLod];
			if ( nearMeshes is null || nearMeshes.Length == 0 ) nearMeshes = lods[0];
			var farMeshes = lods[farLod];
			if ( farMeshes is null || farMeshes.Length == 0 ) farMeshes = nearMeshes;

			var pos = entry.Transform.Position;
			var sectorKey = new SectorKey(
				(int)MathF.Floor( pos.x * inverseSector ),
				(int)MathF.Floor( pos.y * inverseSector ) );
			var cellKey = new CellKey(
				(int)MathF.Floor( pos.x * inverseCell ),
				(int)MathF.Floor( pos.y * inverseCell ) );

			var omitInFar = !entry.Collidable
				&& (entry.Radius < FarTinyNonCollidableMaxRadius
					|| (entry.Radius < FarFadeExemptionRadius && entry.FadeDistance > 0f && entry.FadeDistance <= FarAuthoredFadeCutoff));
			var distinctFarLod = !ReferenceEquals( nearMeshes, farMeshes )
				&& CountTriangles( farMeshes ) < CountTriangles( nearMeshes ) * 0.90f;

			// For 1-LOD props that lack an authored coarser LOD, synthesize an offline vertex-clustered
			// Far mesh once per Model so Shared triangles drop sharply without touching Near LOD0 quality.
			if ( !omitInFar && !distinctFarLod )
			{
				if ( !simplifiedFarCache.TryGetValue( entry.Model, out var syntheticFar ) )
				{
					var modelRadius = entry.Model.Bounds.Size.Length * 0.5f;
					var anySimplified = false;
					var built = new ModelLoader.GpuGeometryMesh[nearMeshes.Length];
					for ( var m = 0; m < nearMeshes.Length; m++ )
					{
						var simp = Titanfall2PropMeshSimplifier.TrySimplifyForFar( nearMeshes[m], modelRadius );
						if ( simp is not null ) { built[m] = simp; anySimplified = true; }
						else built[m] = nearMeshes[m];
					}
					syntheticFar = anySimplified && CountTriangles( built ) < CountTriangles( nearMeshes ) * 0.85f
						? built
						: null;
					simplifiedFarCache[entry.Model] = syntheticFar;
				}
				if ( syntheticFar is not null )
				{
					farMeshes = syntheticFar;
					distinctFarLod = true;
				}
			}

			if ( omitInFar || distinctFarLod )
			{
				sectorPvsMasks[sectorKey] = sectorPvsMasks.TryGetValue( sectorKey, out var existingPvs )
					? existingPvs.Union( entry.PvsCells )
					: entry.PvsCells;
				foreach ( var mesh in nearMeshes )
					AddTierMesh( sectorKey, TierKind.Near, cellKey, mesh, entry.Transform, entry.CastShadows, propIndex );
				if ( !omitInFar )
				{
					foreach ( var mesh in farMeshes )
						AddTierMesh( sectorKey, TierKind.Far, cellKey, mesh, entry.Transform, entry.CastShadows, propIndex );
				}
			}
			else
			{
				foreach ( var mesh in nearMeshes )
					AddTierMesh( sectorKey, TierKind.Shared, cellKey, mesh, entry.Transform, entry.CastShadows, propIndex );
			}

			// Dedicated depth-only LOD3 shadow geometry for opaque shadow casters (mirrors WorldShadowChunks).
			if ( entry.CastShadows )
			{
				if ( !simplifiedShadowCache.TryGetValue( entry.Model, out var shadowMeshes ) )
				{
					var modelRadius = entry.Model.Bounds.Size.Length * 0.5f;
					var baseShadow = lods[farLod];
					if ( baseShadow is null || baseShadow.Length == 0 ) baseShadow = nearMeshes;
					shadowMeshes = new ModelLoader.GpuGeometryMesh[baseShadow.Length];
					for ( var m = 0; m < baseShadow.Length; m++ )
						shadowMeshes[m] = Titanfall2PropMeshSimplifier.SimplifyForShadow( baseShadow[m], modelRadius );
					simplifiedShadowCache[entry.Model] = shadowMeshes;
				}

				var shadowCell = new CellKey(
					(int)MathF.Floor( pos.x * inverseShadowCell ),
					(int)MathF.Floor( pos.y * inverseShadowCell ) );
				foreach ( var mesh in shadowMeshes )
				{
					if ( mesh.Vertices.Length == 0 || mesh.Indices.Length < 3 ) continue;
					if ( IsCutoutShader( NormalizeShaderName( mesh.Material.ShaderName ) ) ) continue;
					if ( !shadowCells.TryGetValue( shadowCell, out var list ) )
					{
						list = new List<ShadowPlacement>();
						shadowCells.Add( shadowCell, list );
					}
					list.Add( new ShadowPlacement( mesh, entry.Transform ) );
				}
			}
		}

		// Collect residual decal/translucent prop meshes grouped by (MaterialName, CastShadows) and 2048u cell.
		var residualGroupMap = new Dictionary<(string MaterialName, bool CastShadows), int>();
		var residualGroupSpecs = new List<(string MaterialName, bool CastShadows)>();
		var residualCellMap = new Dictionary<(int GroupIndex, CellKey Cell), List<ResidualPlacement>>();
		if ( residualEntries is not null )
		{
			foreach ( var entry in residualEntries )
			{
				var pos = entry.Transform.Position;
				var cellKey = new CellKey(
					(int)MathF.Floor( pos.x * inverseResidualCell ),
					(int)MathF.Floor( pos.y * inverseResidualCell ) );
				foreach ( var mesh in entry.Meshes )
				{
					if ( mesh.Vertices.Length == 0 || mesh.Indices.Length < 3 ) continue;
					var matName = string.IsNullOrWhiteSpace( mesh.MaterialName ) ? mesh.Material.Name : mesh.MaterialName;
					materialsByName[matName] = mesh.Material;
					var matCastShadow = entry.CastShadows && IsOpaqueResidualShader( NormalizeShaderName( mesh.Material.ShaderName ) );
					var gKey = (matName, matCastShadow);
					if ( !residualGroupMap.TryGetValue( gKey, out var gIdx ) )
					{
						gIdx = residualGroupSpecs.Count;
						residualGroupMap.Add( gKey, gIdx );
						residualGroupSpecs.Add( gKey );
					}
					var cKey = (gIdx, cellKey);
					if ( !residualCellMap.TryGetValue( cKey, out var list ) )
					{
						list = new List<ResidualPlacement>();
						residualCellMap.Add( cKey, list );
					}
					list.Add( new ResidualPlacement( mesh, entry.Transform ) );
				}
			}
		}

		// Build all aggregate, shadow, and residual geometry in parallel (pure managed math, zero engine calls).
		var aggJobs = sectorTierCells
			.OrderBy( pair => pair.Key.Sector.X )
			.ThenBy( pair => pair.Key.Sector.Y )
			.ThenBy( pair => (int)pair.Key.Tier )
			.ThenBy( pair => pair.Key.BucketIndex )
			.ThenBy( pair => pair.Key.Cell.X )
			.ThenBy( pair => pair.Key.Cell.Y )
			.Select( pair => new AggregateCellJob( pair.Key.Sector, pair.Key.Tier, pair.Key.BucketIndex, pair.Key.Cell, pair.Value ) )
			.ToArray();

		var shadowJobs = shadowCells
			.OrderBy( pair => pair.Key.X )
			.ThenBy( pair => pair.Key.Y )
			.Select( pair => new ShadowChunkJob( pair.Key, pair.Value ) )
			.ToArray();

		var residualJobs = residualCellMap
			.OrderBy( pair => pair.Key.GroupIndex )
			.ThenBy( pair => pair.Key.Cell.X )
			.ThenBy( pair => pair.Key.Cell.Y )
			.Select( pair => new ResidualCellJob( pair.Key.GroupIndex, pair.Key.Cell, pair.Value ) )
			.ToArray();

		Sandbox.Utility.Parallel.ForEach( aggJobs, BuildAggregateCell );
		Sandbox.Utility.Parallel.ForEach( shadowJobs, BuildShadowChunk );
		Sandbox.Utility.Parallel.ForEach( residualJobs, BuildResidualCell );

		// Assemble global Shared models and per-sector Near / Far AggregateModelData.
		var sharedByBucket = new Dictionary<int, List<AggregatePartData>>();
		var sectorMap = new Dictionary<SectorKey, (Dictionary<int, List<AggregatePartData>> Near, Dictionary<int, List<AggregatePartData>> Far)>();
		int sharedTris = 0, nearTris = 0, farTris = 0;

		foreach ( var job in aggJobs )
		{
			if ( job.Result is null ) continue;
			var tris = job.Result.Indices.Length / 3;
			if ( job.Tier == TierKind.Shared )
			{
				if ( !sharedByBucket.TryGetValue( job.BucketIndex, out var list ) )
				{
					list = new List<AggregatePartData>();
					sharedByBucket.Add( job.BucketIndex, list );
				}
				list.Add( job.Result );
				sharedTris += tris;
				continue;
			}

			if ( !sectorMap.TryGetValue( job.Sector, out var buckets ) )
			{
				buckets = (new(), new());
				sectorMap.Add( job.Sector, buckets );
			}
			var target = job.Tier == TierKind.Near ? buckets.Near : buckets.Far;
			if ( !target.TryGetValue( job.BucketIndex, out var targetList ) )
			{
				targetList = new List<AggregatePartData>();
				target.Add( job.BucketIndex, targetList );
			}
			targetList.Add( job.Result );
			if ( job.Tier == TierKind.Near ) nearTris += tris;
			else farTris += tris;
		}

		var sharedModels = PackBucketModels( sharedByBucket );

		var sectors = new List<SectorData>( sectorMap.Count );
		foreach ( var (sectorKey, buckets) in sectorMap.OrderBy( pair => pair.Key.X ).ThenBy( pair => pair.Key.Y ) )
		{
			var nearModels = PackBucketModels( buckets.Near );
			var farModels = PackBucketModels( buckets.Far );
			if ( nearModels.Length == 0 && farModels.Length == 0 ) continue;

			var hasBounds = false;
			var bounds = default( BBox );
			void Expand( AggregateModelData[] models )
			{
				foreach ( var m in models )
				{
					foreach ( var p in m.Parts )
					{
						bounds = hasBounds ? bounds.AddBBox( p.Bounds ) : p.Bounds;
						hasBounds = true;
					}
				}
			}
			Expand( nearModels );
			Expand( farModels );

			var pvsMask = sectorPvsMasks.TryGetValue( sectorKey, out var mask ) ? mask : default;
			sectors.Add( new SectorData( sectorKey.X, sectorKey.Y, bounds, pvsMask, nearModels, farModels ) );
		}

		var shadowChunks = shadowJobs.Where( static j => j.Result is not null ).Select( static j => j.Result ).ToArray();
		var shadowTris = shadowChunks.Sum( static c => c.Indices.Length / 3 );

		var residualPartsByGroup = new List<ResidualPartData>[residualGroupSpecs.Count];
		for ( var i = 0; i < residualPartsByGroup.Length; i++ ) residualPartsByGroup[i] = new();
		var residualTris = 0;
		foreach ( var job in residualJobs )
		{
			if ( job.Result is null ) continue;
			residualPartsByGroup[job.GroupIndex].Add( job.Result );
			residualTris += job.Result.Indices.Length / 3;
		}
		var residualGroups = new List<ResidualGroupData>( residualGroupSpecs.Count );
		for ( var i = 0; i < residualGroupSpecs.Count; i++ )
		{
			if ( residualPartsByGroup[i].Count == 0 ) continue;
			residualGroups.Add( new ResidualGroupData(
				residualGroupSpecs[i].MaterialName,
				residualGroupSpecs[i].CastShadows,
				residualPartsByGroup[i].ToArray() ) );
		}

		return new PropAggregatePayload(
			sourceMaterialNames.ToArray(),
			probes,
			bucketSpecs.ToArray(),
			sharedModels,
			sectors.ToArray(),
			shadowChunks,
			residualGroups.ToArray(),
			fallbackPropIndices ?? [],
			totalPropCount,
			entryCount,
			mixedPropCount,
			residualBatchedPropCount,
			sharedTris,
			nearTris,
			farTris,
			shadowTris,
			residualTris );
	}

	static AggregateModelData[] PackBucketModels( Dictionary<int, List<AggregatePartData>> byBucket )
	{
		if ( byBucket.Count == 0 ) return [];
		var maxFragments = AggregateRenderer.MaxFragments;
		var models = new List<AggregateModelData>( byBucket.Count );
		foreach ( var (bucketIndex, parts) in byBucket.OrderBy( static pair => pair.Key ) )
		{
			var cursor = 0;
			while ( cursor < parts.Count )
			{
				var start = cursor;
				var vertAcc = 0;
				var count = 0;
				while ( cursor < parts.Count && count < maxFragments )
				{
					var v = parts[cursor].Vertices.Length;
					if ( count > 0 && vertAcc + v > MaxVerticesPerAggregate ) break;
					vertAcc += v;
					count++;
					cursor++;
				}
				models.Add( new AggregateModelData( bucketIndex, parts.GetRange( start, count ).ToArray() ) );
			}
		}
		return models.ToArray();
	}

	internal static Titanfall2StaticPropAggregate CreateFromPayload(
		GameObject parent,
		Scene scene,
		Titanfall2Mount mount,
		string mapPath,
		PropAggregatePayload payload,
		Color skyColor,
		Vector3 initialAnchor,
		bool enabled,
		bool loadedFromCache,
		IReadOnlyDictionary<string, Material> liveMaterials = null )
	{
		if ( payload is null || !parent.IsValid() || !scene.IsValid() ) return null;

		var timer = Stopwatch.StartNew();

		// On disk cache hit, prefetch all referenced materials in parallel before Material.Load runs on the main thread.
		Titanfall2MaterialPrefetch prefetch = null;
		if ( loadedFromCache && mount is not null )
		{
			var allNames = payload.MaterialNames
				.Concat( payload.ResidualGroups.Select( static g => g.MaterialName ) )
				.Distinct( StringComparer.OrdinalIgnoreCase )
				.Select( name =>
				{
					var normalized = NormalizeMaterialName( name );
					return mount.TryGetMaterialPath( normalized, out var reg ) ? reg : normalized;
				} );
			prefetch = new Titanfall2MaterialPrefetch( mount, allNames );
		}

		Material[] sourceMaterials;
		Material[] residualMaterials;
		try
		{
			sourceMaterials = new Material[payload.MaterialNames.Length];
			for ( var i = 0; i < sourceMaterials.Length; i++ )
			{
				var mat = ResolveMaterial( mount, payload.MaterialNames[i], liveMaterials, $"prop_table_fb_{i}" );
				if ( mat is null || !mat.IsValid )
					throw new InvalidDataException( $"Unable to resolve static prop material '{payload.MaterialNames[i]}'." );
				sourceMaterials[i] = mat;
			}

			residualMaterials = new Material[payload.ResidualGroups.Length];
			for ( var i = 0; i < residualMaterials.Length; i++ )
			{
				var mat = ResolveMaterial( mount, payload.ResidualGroups[i].MaterialName, liveMaterials, $"prop_res_fb_{i}" );
				if ( mat is null || !mat.IsValid )
					throw new InvalidDataException( $"Unable to resolve residual prop material '{payload.ResidualGroups[i].MaterialName}'." );
				residualMaterials[i] = mat;
			}
		}
		finally
		{
			prefetch?.Dispose();
		}

		var resourcePrefix = string.IsNullOrWhiteSpace( mapPath )
			? "titanfall2/props"
			: $"titanfall2/props/{System.IO.Path.GetFileNameWithoutExtension( mapPath )}";

		var sharedMaterials = new Material[payload.Buckets.Length];
		for ( var i = 0; i < payload.Buckets.Length; i++ )
		{
			var spec = payload.Buckets[i];
			var template = sourceMaterials[spec.TemplateMaterialIndex];
			var shared = template.CreateCopy( $"{resourcePrefix}/prop_table_{i}" );
			shared.SetFeature( "F_T2_PROP_AGGREGATE", 1 );
			sharedMaterials[i] = shared;
		}

		var shadowMaterial = payload.ShadowChunks.Length > 0
			? MaterialLoader.CreateRuntimeMaterial( $"{resourcePrefix}/prop_shadow_only" )
			: null;

		GpuBuffer<Titanfall2GpuMaterialData> materialBuffer = null;
		GpuBuffer<Titanfall2ProbeData> probeBuffer = null;
		Titanfall2StaticPropAggregate result = null;

		try
		{
			if ( sourceMaterials.Length > 0 )
			{
				var materialTable = new Titanfall2GpuMaterialData[sourceMaterials.Length];
				for ( var i = 0; i < sourceMaterials.Length; i++ )
					materialTable[i] = Titanfall2GpuMaterialData.Capture( sourceMaterials[i] );
				materialBuffer = new GpuBuffer<Titanfall2GpuMaterialData>( materialTable.Length );
				materialBuffer.SetData( materialTable.AsSpan() );

				probeBuffer = new GpuBuffer<Titanfall2ProbeData>( payload.Probes.Length );
				probeBuffer.SetData( payload.Probes.AsSpan() );

				scene.RenderAttributes.Set( "T2PropMaterials", materialBuffer );
				scene.RenderAttributes.Set( "T2PropLightProbes", probeBuffer );
				scene.RenderAttributes.Set( "T2PropSkyColor", new Vector3( skyColor.r, skyColor.g, skyColor.b ) );
			}

			var totalFragments = 0;
			foreach ( var m in payload.SharedModels ) totalFragments += m.Parts.Length;
			foreach ( var sector in payload.Sectors )
			{
				foreach ( var m in sector.NearModels ) totalFragments += m.Parts.Length;
				foreach ( var m in sector.FarModels ) totalFragments += m.Parts.Length;
			}
			foreach ( var g in payload.ResidualGroups ) totalFragments += g.Parts.Length;

			result = new Titanfall2StaticPropAggregate(
				scene,
				sourceMaterials,
				sharedMaterials,
				residualMaterials,
				shadowMaterial,
				materialBuffer,
				probeBuffer,
				payload,
				totalFragments,
				loadedFromCache,
				enabled );

			materialBuffer = null;
			probeBuffer = null;

			var modelIndex = 0;

			// Attach global Shared (1-LOD) aggregates once per bucket instead of once per sector.
			foreach ( var modelData in payload.SharedModels )
			{
				var spec = payload.Buckets[modelData.BucketIndex];
				var parts = new List<MountedGeometryPart<TitanfallPropAggregateVertex>>( modelData.Parts.Length );
				foreach ( var p in modelData.Parts )
					parts.Add( new MountedGeometryPart<TitanfallPropAggregateVertex>( new List<TitanfallPropAggregateVertex>( p.Vertices ), new List<int>( p.Indices ), p.Bounds ) );

				var geom = MountedAggregateBuilder.PrepareGeometry( parts, 0, parts.Count );
				var name = $"{resourcePrefix}/shared_{modelIndex}";
				var agg = MountedAggregateBuilder.CreateAggregate( sharedMaterials[modelData.BucketIndex], geom, name );
				var renderer = MountedAggregateBuilder.Attach(
					parent,
					$"t2_prop_shared_{modelIndex}",
					agg,
					spec.CastShadows );
				renderer.GameObject.Flags |= GameObjectFlags.NotSaved;
				renderer.GameObject.Enabled = enabled;

				result._aggregates.Add( agg );
				result._allObjects.Add( renderer.GameObject );
				modelIndex++;
			}

			var nearRadiusSq = NearLodRadius * NearLodRadius;
			var nearCount = 0;

			foreach ( var sector in payload.Sectors )
			{
				var runtimeSector = new RuntimeSector( sector.Bounds, sector.PvsCells );
				var distSq = sector.Bounds.ClosestPoint( initialAnchor ).DistanceSquared( initialAnchor );
				runtimeSector.IsNear = distSq <= nearRadiusSq;

				void AttachTier( AggregateModelData[] models, List<GameObject> targetList, string tierTag, bool tierEnabled )
				{
					foreach ( var modelData in models )
					{
						var spec = payload.Buckets[modelData.BucketIndex];
						var parts = new List<MountedGeometryPart<TitanfallPropAggregateVertex>>( modelData.Parts.Length );
						foreach ( var p in modelData.Parts )
							parts.Add( new MountedGeometryPart<TitanfallPropAggregateVertex>( new List<TitanfallPropAggregateVertex>( p.Vertices ), new List<int>( p.Indices ), p.Bounds ) );

						var geom = MountedAggregateBuilder.PrepareGeometry( parts, 0, parts.Count );
						var name = $"{resourcePrefix}/sec_{sector.SectorX}_{sector.SectorY}_{tierTag}_{modelIndex}";
						var agg = MountedAggregateBuilder.CreateAggregate( sharedMaterials[modelData.BucketIndex], geom, name );
						var renderer = MountedAggregateBuilder.Attach(
							parent,
							$"t2_prop_{tierTag}_{sector.SectorX}_{sector.SectorY}_{modelIndex}",
							agg,
							spec.CastShadows );
						renderer.GameObject.Flags |= GameObjectFlags.NotSaved;
						renderer.GameObject.Enabled = enabled && tierEnabled;

						result._aggregates.Add( agg );
						result._allObjects.Add( renderer.GameObject );
						targetList.Add( renderer.GameObject );
						modelIndex++;
					}
				}

				AttachTier( sector.NearModels, runtimeSector.NearObjects, "near", runtimeSector.IsNear );
				AttachTier( sector.FarModels, runtimeSector.FarObjects, "far", !runtimeSector.IsNear );
				if ( runtimeSector.HasLodSwitch && runtimeSector.IsNear ) nearCount++;
				result._sectors.Add( runtimeSector );
			}

			result.NearSectorCount = nearCount;
			result._lastLodAnchor = initialAnchor;
			result._hasLodAnchor = true;

			// Attach depth-only LOD3 shadow chunks (same pattern as Map.cs WorldShadowChunks).
			if ( shadowMaterial is not null )
			{
				for ( var i = 0; i < payload.ShadowChunks.Length; i++ )
				{
					var chunk = payload.ShadowChunks[i];
					var mesh = new Mesh( $"{resourcePrefix}/shadow_{chunk.CellX}_{chunk.CellY}_{i}", shadowMaterial );
					mesh.CreateVertexBuffer( chunk.Vertices.Length, chunk.Vertices );
					mesh.CreateIndexBuffer( chunk.Indices.Length, chunk.Indices );
					mesh.Bounds = chunk.Bounds;
					var shadowModel = Model.Builder.WithName( $"{resourcePrefix}/shadow_model_{i}" ).AddMesh( mesh ).Create();
					result._shadowModels.Add( shadowModel );

					var obj = new GameObject( parent, enabled, $"t2_prop_shadow_{chunk.CellX}_{chunk.CellY}_{i}" );
					obj.Flags |= GameObjectFlags.NotSaved;
					obj.IsStatic = true;
					var renderer = obj.AddComponent<ModelRenderer>();
					renderer.Model = shadowModel;
					renderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
					result._allObjects.Add( obj );
				}
			}

			// Attach batched residual decal/translucent meshes via ModelRenderer (matching Map.cs
			// TranslucentChunks/DecalChunks). AggregateRenderer is an opaque-only native path and
			// breaks translucent depth sorting/alpha blending on shop banners and glass billboards.
			for ( var g = 0; g < payload.ResidualGroups.Length; g++ )
			{
				var group = payload.ResidualGroups[g];
				var mat = residualMaterials[g];
				var renderType = group.CastShadows
					? ModelRenderer.ShadowRenderType.On
					: ModelRenderer.ShadowRenderType.Off;

				for ( var p = 0; p < group.Parts.Length; p++ )
				{
					var part = group.Parts[p];
					if ( part.Vertices.Length == 0 || part.Indices.Length < 3 ) continue;
					var mesh = new Mesh( $"{resourcePrefix}/res_{g}_{p}", mat );
					mesh.CreateVertexBuffer( part.Vertices.Length, part.Vertices );
					mesh.CreateIndexBuffer( part.Indices.Length, part.Indices );
					mesh.Bounds = part.Bounds;
					var resModel = Model.Builder.WithName( $"{resourcePrefix}/res_model_{g}_{p}" ).AddMesh( mesh ).Create();
					result._residualModels.Add( resModel );

					var obj = new GameObject( parent, enabled, $"t2_prop_res_{g}_{p}" );
					obj.Flags |= GameObjectFlags.NotSaved;
					obj.IsStatic = true;
					var renderer = obj.AddComponent<ModelRenderer>();
					renderer.Model = resModel;
					renderer.RenderType = renderType;
					result._allObjects.Add( obj );
				}
			}

			timer.Stop();
			Log.Info( $"Titanfall 2 static prop aggregates ready ({(loadedFromCache ? "disk cache hit" : "compiled")}): "
				+ $"{payload.AggregatedPropCount} props ({payload.MixedPropCount} mixed, {payload.ResidualBatchedPropCount} residual-batched) -> "
				+ $"{result.SectorCount} sectors ({nearCount} near), {result.AggregateCount} aggregates, "
				+ $"{result.ShadowChunkCount} LOD3 shadow chunks ({payload.ShadowTriangles} tris), "
				+ $"{result.ResidualGroupCount} residual groups ({payload.ResidualTriangles} tris), "
				+ $"forward tris shared={payload.SharedTriangles}/near={payload.NearTriangles}/far={payload.FarTriangles}, "
				+ $"{sourceMaterials.Length} tabled materials in {timer.Elapsed.TotalSeconds:0.00}s ({mapPath})." );
			return result;
		}
		catch
		{
			result?.Dispose();
			materialBuffer?.Dispose();
			probeBuffer?.Dispose();
			throw;
		}
	}

	public void UpdateCamera( Vector3 cameraPosition, Titanfall2BspReader.VisibilityCellMask visiblePvsCells )
	{
		if ( _disposed || !_enabled || _sectors.Count == 0 ) return;
		var pvsChanged = !visiblePvsCells.Equals( _lastPvsMask );
		if ( !pvsChanged && _hasLodAnchor && _lastLodAnchor.DistanceSquared( cameraPosition ) < CameraRecheckDistance * CameraRecheckDistance )
			return;
		_lastLodAnchor = cameraPosition;
		_lastPvsMask = visiblePvsCells;
		_hasLodAnchor = true;

		var enterSq = NearLodRadius * NearLodRadius;
		var exitSq = NearLodExitRadius * NearLodExitRadius;
		var nearCount = 0;
		var pvsCulled = 0;

		for ( var i = 0; i < _sectors.Count; i++ )
		{
			var sector = _sectors[i];
			if ( !sector.HasLodSwitch ) continue;
			var pvsVisible = visiblePvsCells.IsEmpty || sector.PvsCells.IsEmpty || sector.PvsCells.Intersects( visiblePvsCells );
			var distSq = sector.Bounds.ClosestPoint( cameraPosition ).DistanceSquared( cameraPosition );
			var wantNear = sector.IsNear ? distSq <= exitSq : distSq <= enterSq;

			if ( !pvsVisible ) pvsCulled++;
			else if ( wantNear ) nearCount++;

			if ( pvsVisible == sector.PvsVisible && wantNear == sector.IsNear ) continue;

			sector.PvsVisible = pvsVisible;
			sector.IsNear = wantNear;
			var showNear = pvsVisible && wantNear;
			var showFar = pvsVisible && !wantNear;
			foreach ( var obj in sector.NearObjects )
				if ( obj.IsValid() && !obj.IsDestroyed ) obj.Enabled = showNear;
			foreach ( var obj in sector.FarObjects )
				if ( obj.IsValid() && !obj.IsDestroyed ) obj.Enabled = showFar;
		}
		NearSectorCount = nearCount;
		PvsCulledSectorCount = pvsCulled;
	}

	static void BuildAggregateCell( AggregateCellJob job )
	{
		var vertexTotal = 0;
		var indexTotal = 0;
		foreach ( var placement in job.Placements )
		{
			vertexTotal += placement.Mesh.Vertices.Length;
			indexTotal += placement.Mesh.Indices.Length;
		}
		if ( vertexTotal == 0 || indexTotal < 3 ) return;

		var vertices = new List<TitanfallPropAggregateVertex>( vertexTotal );
		var indices = new List<int>( indexTotal );
		var min = new Vector3( float.PositiveInfinity );
		var max = new Vector3( float.NegativeInfinity );

		foreach ( var placement in job.Placements )
		{
			var mesh = placement.Mesh;
			var srcVerts = mesh.Vertices;
			var srcIndices = mesh.Indices;
			var transform = placement.Transform;
			var rotation = transform.Rotation;
			var propIds = new Vector2( placement.MaterialId, placement.ProbeIndex );
			var baseVertex = vertices.Count;

			for ( var i = 0; i < srcVerts.Length; i++ )
			{
				var src = srcVerts[i];
				var worldPos = transform.PointToWorld( src.Position );
				var rotatedNormal = rotation * src.Normal;
				var lenSq = rotatedNormal.LengthSquared;
				var worldNormal = lenSq > 1e-8f ? rotatedNormal * (1f / MathF.Sqrt( lenSq )) : Vector3.Up;

				min = Vector3.Min( min, worldPos );
				max = Vector3.Max( max, worldPos );
				vertices.Add( new TitanfallPropAggregateVertex
				{
					Position = worldPos,
					Normal = worldNormal,
					TexCoord = src.TexCoord,
					Color = src.Color,
					PropIds = propIds
				} );
			}

			AppendValidTriangles( indices, srcIndices, baseVertex, srcVerts.Length );
		}

		if ( vertices.Count == 0 || indices.Count < 3 ) return;
		job.Result = new AggregatePartData( vertices.ToArray(), indices.ToArray(), new BBox( min, max ) );
	}

	static void BuildShadowChunk( ShadowChunkJob job )
	{
		var vertexTotal = 0;
		var indexTotal = 0;
		foreach ( var placement in job.Placements )
		{
			vertexTotal += placement.Mesh.Vertices.Length;
			indexTotal += placement.Mesh.Indices.Length;
		}
		if ( vertexTotal == 0 || indexTotal < 3 ) return;

		var vertices = new List<TitanfallPropShadowVertex>( vertexTotal );
		var indices = new List<int>( indexTotal );
		var min = new Vector3( float.PositiveInfinity );
		var max = new Vector3( float.NegativeInfinity );

		foreach ( var placement in job.Placements )
		{
			var srcVerts = placement.Mesh.Vertices;
			var srcIndices = placement.Mesh.Indices;
			var transform = placement.Transform;
			var rotation = transform.Rotation;
			var baseVertex = vertices.Count;

			for ( var i = 0; i < srcVerts.Length; i++ )
			{
				var src = srcVerts[i];
				var worldPos = transform.PointToWorld( src.Position );
				var rotatedNormal = rotation * src.Normal;
				var lenSq = rotatedNormal.LengthSquared;
				var worldNormal = lenSq > 1e-8f ? rotatedNormal * (1f / MathF.Sqrt( lenSq )) : Vector3.Up;

				min = Vector3.Min( min, worldPos );
				max = Vector3.Max( max, worldPos );
				vertices.Add( new TitanfallPropShadowVertex { Position = worldPos, Normal = worldNormal } );
			}

			AppendValidTriangles( indices, srcIndices, baseVertex, srcVerts.Length );
		}

		if ( vertices.Count == 0 || indices.Count < 3 ) return;
		job.Result = new ShadowChunkData( job.Cell.X, job.Cell.Y, vertices.ToArray(), indices.ToArray(), new BBox( min, max ) );
	}

	static void BuildResidualCell( ResidualCellJob job )
	{
		var vertexTotal = 0;
		var indexTotal = 0;
		foreach ( var placement in job.Placements )
		{
			vertexTotal += placement.Mesh.Vertices.Length;
			indexTotal += placement.Mesh.Indices.Length;
		}
		if ( vertexTotal == 0 || indexTotal < 3 ) return;

		var vertices = new List<TitanfallPropResidualVertex>( vertexTotal );
		var indices = new List<int>( indexTotal );
		var min = new Vector3( float.PositiveInfinity );
		var max = new Vector3( float.NegativeInfinity );

		foreach ( var placement in job.Placements )
		{
			var srcVerts = placement.Mesh.Vertices;
			var srcIndices = placement.Mesh.Indices;
			var transform = placement.Transform;
			var rotation = transform.Rotation;
			var baseVertex = vertices.Count;

			for ( var i = 0; i < srcVerts.Length; i++ )
			{
				var src = srcVerts[i];
				var worldPos = transform.PointToWorld( src.Position );
				var rotatedNormal = rotation * src.Normal;
				var lenSq = rotatedNormal.LengthSquared;
				var worldNormal = lenSq > 1e-8f ? rotatedNormal * (1f / MathF.Sqrt( lenSq )) : Vector3.Up;

				min = Vector3.Min( min, worldPos );
				max = Vector3.Max( max, worldPos );
				vertices.Add( new TitanfallPropResidualVertex
				{
					Position = worldPos,
					Normal = worldNormal,
					TexCoord = src.TexCoord,
					Color = src.Color
				} );
			}

			AppendValidTriangles( indices, srcIndices, baseVertex, srcVerts.Length );
		}

		if ( vertices.Count == 0 || indices.Count < 3 ) return;
		job.Result = new ResidualPartData( vertices.ToArray(), indices.ToArray(), new BBox( min, max ) );
	}

	static void AppendValidTriangles( List<int> destination, int[] srcIndices, int baseVertex, int vertexCount )
	{
		for ( var i = 0; i + 2 < srcIndices.Length; i += 3 )
		{
			var ia = srcIndices[i];
			var ib = srcIndices[i + 1];
			var ic = srcIndices[i + 2];
			if ( (uint)ia >= (uint)vertexCount || (uint)ib >= (uint)vertexCount || (uint)ic >= (uint)vertexCount ) continue;
			destination.Add( baseVertex + ia );
			destination.Add( baseVertex + ib );
			destination.Add( baseVertex + ic );
		}
	}

	static Material ResolveMaterial( Titanfall2Mount mount, string materialName, IReadOnlyDictionary<string, Material> liveMaterials, string fallbackName )
	{
		if ( liveMaterials is not null && liveMaterials.TryGetValue( materialName, out var cached ) && cached is not null && cached.IsValid )
			return cached;
		if ( mount is null ) return null;

		var normalized = NormalizeMaterialName( materialName );
		if ( !string.IsNullOrWhiteSpace( normalized ) && mount.TryGetMaterialPath( normalized, out var registered ) )
		{
			var loaded = Material.Load( $"mount://{mount.Ident}/{registered}.vmat" );
			if ( loaded is not null && loaded.IsValid ) return loaded;
		}

		var metadata = mount.TryGetMaterialMetadata( normalized, out var known )
			? known
			: MaterialLoader.InferMetadata( normalized );
		return metadata.IsDecal
			? MaterialLoader.CreateDecalMaterial( fallbackName, metadata )
			: MaterialLoader.CreateRuntimeMaterial( fallbackName, metadata );
	}

	static string NormalizeMaterialName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return string.Empty;
		var normalized = string.Join( '/', name.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries ) );
		foreach ( var extension in new[] { ".vmat", ".rpak", ".matl" } )
		{
			if ( normalized.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) )
				normalized = normalized[..^extension.Length];
		}
		return normalized;
	}

	static bool IsOpaqueResidualShader( string shaderName ) =>
		!shaderName.Contains( "translucent", StringComparison.OrdinalIgnoreCase )
		&& !shaderName.Contains( "decal", StringComparison.OrdinalIgnoreCase )
		&& !shaderName.Contains( "additive", StringComparison.OrdinalIgnoreCase )
		&& !shaderName.Contains( "alpha", StringComparison.OrdinalIgnoreCase )
		&& !shaderName.Contains( "water", StringComparison.OrdinalIgnoreCase )
		&& !shaderName.Contains( "godray", StringComparison.OrdinalIgnoreCase )
		&& !shaderName.Contains( "distortion", StringComparison.OrdinalIgnoreCase )
		&& !shaderName.Contains( "hologram", StringComparison.OrdinalIgnoreCase );

	public void SetEnabled( bool enabled )
	{
		if ( _disposed ) return;
		_enabled = enabled;
		if ( !enabled )
		{
			foreach ( var obj in _allObjects )
				if ( obj.IsValid() && !obj.IsDestroyed ) obj.Enabled = false;
			return;
		}

		foreach ( var obj in _allObjects )
			if ( obj.IsValid() && !obj.IsDestroyed ) obj.Enabled = true;

		// Re-apply active PVS + Near/Far tier visibility so both tiers never render simultaneously.
		foreach ( var sector in _sectors )
		{
			foreach ( var obj in sector.NearObjects )
				if ( obj.IsValid() && !obj.IsDestroyed ) obj.Enabled = sector.PvsVisible && sector.IsNear;
			foreach ( var obj in sector.FarObjects )
				if ( obj.IsValid() && !obj.IsDestroyed ) obj.Enabled = sector.PvsVisible && !sector.IsNear;
		}
	}

	public void Dispose()
	{
		if ( _disposed ) return;
		_disposed = true;

		foreach ( var obj in _allObjects )
		{
			if ( obj.IsValid() && !obj.IsDestroyed ) obj.Destroy();
		}
		_allObjects.Clear();
		_sectors.Clear();
		_aggregates.Clear();
		_shadowModels.Clear();
		_residualModels.Clear();

		if ( _scene.IsValid() )
		{
			_scene.RenderAttributes.Set( "T2PropMaterials", (GpuBuffer)null );
			_scene.RenderAttributes.Set( "T2PropLightProbes", (GpuBuffer)null );
		}

		_materialBuffer?.Dispose();
		_materialBuffer = null;
		_probeBuffer?.Dispose();
		_probeBuffer = null;
	}
}
