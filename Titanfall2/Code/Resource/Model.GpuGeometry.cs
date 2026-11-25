using System.Runtime.CompilerServices;

partial class ModelLoader
{
	internal readonly record struct GpuGeometryVertex( Vector3 Position, Vector3 Normal, Vector2 TexCoord, Color32 Color );
	internal sealed record GpuGeometryMesh( Material Material, GpuGeometryVertex[] Vertices, int[] Indices, string MaterialName = null );
	internal sealed record GpuGeometry(
		GpuGeometryMesh[][] Lods,
		ulong ResidualMask = 0,
		bool ResidualHasOpaque = false,
		GpuGeometryMesh[] ResidualMeshes = null )
	{
		public bool IsMixed => ResidualMask != 0;
		public bool HasGpuMeshes => Lods.Length > 0 && Lods[0] is { Length: > 0 };
		public bool HasResidualMeshes => ResidualMeshes is { Length: > 0 };
	}
	internal const string GpuMeshGroup = "titanfall2_gpu";
	internal const string ResidualMeshGroup = "titanfall2_residual";
	internal sealed record CollisionMeshData( Vector3[] Vertices, int[] Indices, Surface Surface );
	static readonly ConditionalWeakTable<Model, GpuGeometry> SharedGpuGeometry = new();
	static readonly ConditionalWeakTable<Model, CollisionMeshData> SharedCollisionMesh = new();

	internal static bool TryGetGpuGeometry( Model model, out GpuGeometry geometry ) => SharedGpuGeometry.TryGetValue( model, out geometry );
	internal static bool TryGetCollisionMesh( Model model, out CollisionMeshData mesh ) => SharedCollisionMesh.TryGetValue( model, out mesh );

	internal static void RegisterCollisionMesh( Model model, CollisionMeshData mesh )
	{
		if ( !IsUsableModel( model ) || mesh is null || mesh.Vertices.Length == 0 || mesh.Indices.Length < 3 ) return;
		SharedCollisionMesh.AddOrUpdate( model, mesh );
	}

	internal static void RegisterGpuGeometry( Model model, StaticLodGeometry[] levels )
	{
		if ( !Titanfall2StreamingSettings.GpuStaticScene && !Titanfall2StreamingSettings.PropAggregate ) return;
		var meshes = levels.SelectMany( level => level.Meshes ).ToArray();
		if ( meshes.Length == 0 ) return;

		var hasGpu = meshes.Any( mesh => mesh.GpuCompatible );
		var mixed = hasGpu && meshes.Any( mesh => !mesh.GpuCompatible );
		if ( !hasGpu )
		{
			if ( !Titanfall2StreamingSettings.PropAggregate || levels.Length == 0 || levels[0].Meshes.Length == 0 ) return;
			var emptyLods = new GpuGeometryMesh[StaticInstanceBandCount][];
			for ( var lod = 0; lod < emptyLods.Length; ++lod ) emptyLods[lod] = [];
			var pureResidual = ConvertGeometryMeshes( levels[0].Meshes );
			var pureHasOpaque = levels[0].Meshes.Any( mesh => IsOpaqueResidualMaterial( mesh.RuntimeMaterial ) );
			SharedGpuGeometry.AddOrUpdate( model, new( emptyLods, 0, pureHasOpaque, pureResidual ) );
			return;
		}

		var residualMask = mixed ? model.Parts.Get( ResidualMeshGroup )?.Mask ?? 0 : 0;
		// A model already handled by the old CPU instancer must stay there if some
		// opaque custom shader cannot enter this backend. Partial ownership below is
		// only for the individual-object path, which can retain a residual mask.
		if ( mixed && (CanInstance( model ) || !Titanfall2StreamingSettings.GpuMixedProps || residualMask == 0) ) return;
		var result = new GpuGeometryMesh[StaticInstanceBandCount][];
		var resolved = 0;
		for ( var lod = 0; lod < result.Length; ++lod )
		{
			if ( levels[lod].Meshes.Length == 0 ) { result[lod] = result[resolved]; continue; }
			resolved = lod;
			// Resolve missing original LODs before partitioning. An existing LOD with no
			// compatible meshes is deliberately empty, not an alias of a finer partition.
			result[lod] = ConvertGeometryMeshes( levels[lod].Meshes.Where( source => source.GpuCompatible ) );
		}
		if ( result.SelectMany( lod => lod ).Any( mesh => !IsGpuSceneMaterial( mesh.Material ) ) ) return;
		var residualMeshes = mixed && levels.Length > 0
			? ConvertGeometryMeshes( levels[0].Meshes.Where( source => !source.GpuCompatible ) )
			: null;
		var residualHasOpaque = mixed && meshes.Any( mesh => !mesh.GpuCompatible && IsOpaqueResidualMaterial( mesh.RuntimeMaterial ) );
		SharedGpuGeometry.AddOrUpdate( model, new( result, residualMask, residualHasOpaque, residualMeshes ) );
	}

	static GpuGeometryMesh[] ConvertGeometryMeshes( IEnumerable<StaticMeshGeometry> sources ) =>
		sources.Select( source => new GpuGeometryMesh(
			source.RuntimeMaterial,
			source.Vertices.Select( vertex => new GpuGeometryVertex( vertex.Position, vertex.Normal, vertex.TexCoord, vertex.Color ) ).ToArray(),
			source.Indices,
			source.MaterialName ) ).ToArray();

	static bool IsOpaqueResidualMaterial( Material material )
	{
		if ( material is null ) return false;
		var name = material.ShaderName.Replace( '\\', '/' );
		return !name.Contains( "translucent", StringComparison.OrdinalIgnoreCase )
			&& !name.Contains( "decal", StringComparison.OrdinalIgnoreCase )
			&& !name.Contains( "additive", StringComparison.OrdinalIgnoreCase )
			&& !name.Contains( "alpha", StringComparison.OrdinalIgnoreCase )
			&& !name.Contains( "water", StringComparison.OrdinalIgnoreCase )
			&& !name.Contains( "godray", StringComparison.OrdinalIgnoreCase )
			&& !name.Contains( "distortion", StringComparison.OrdinalIgnoreCase )
			&& !name.Contains( "hologram", StringComparison.OrdinalIgnoreCase );
	}

	internal static void AddStaticRenderMesh( ModelBuilder builder, Mesh mesh, int lod, bool gpuCompatible, bool mixed )
	{
		// Both choices are enabled by default, preserving the complete CPU model. The
		// GPU path selects only the residual group on the same native object. Keeping
		// all original LOD registrations also preserves genuinely empty residual LODs,
		// bounds and shared buffers without constructing another model or dummy mesh.
		if ( mixed ) builder.AddMesh( mesh, lod, gpuCompatible ? GpuMeshGroup : ResidualMeshGroup, 0 );
		else builder.AddMesh( mesh, lod );
	}

	static bool IsGpuSceneMaterial( Material material )
	{
		if ( material is null ) return false;
		var name = material.ShaderName.Replace( '\\', '/' );
		return name.EndsWith( "/titanfall2_default.shader", StringComparison.OrdinalIgnoreCase )
			|| name.EndsWith( "/titanfall2_cutout.shader", StringComparison.OrdinalIgnoreCase );
	}
}
