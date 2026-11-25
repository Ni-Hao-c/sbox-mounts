/// <summary>
/// Fast offline spatial vertex-clustering simplifier for baked Far-tier static props and
/// depth-only shadow meshes. Runs once during offline aggregate cache generation (zero runtime cost).
/// Preserves UV islands and sharp normal creases so Far props keep clean textures and silhouettes.
/// </summary>
static class Titanfall2PropMeshSimplifier
{
	readonly record struct ClusterKey( int X, int Y, int Z, int NormalBucket, int UvBucket );

	struct ClusterAccumulator
	{
		public Vector3 PositionSum;
		public Vector3 NormalSum;
		public Vector2 TexCoordSum;
		public int R, G, B, A;
		public int Count;
	}

	/// <summary>
	/// Simplifies a static prop mesh for Far-tier (>2048u) rendering. Returns null if the mesh is
	/// already simple or does not shrink by at least 25%.
	/// </summary>
	internal static ModelLoader.GpuGeometryMesh TrySimplifyForFar( ModelLoader.GpuGeometryMesh source, float modelRadius )
	{
		if ( source is null || source.Vertices.Length < 24 || source.Indices.Length < 48 * 3 ) return null;
		var cellSize = Math.Clamp( modelRadius * 0.085f, 2.5f, 24f );
		return Simplify( source, cellSize, preserveSeams: true, minReductionRatio: 0.75f );
	}

	/// <summary>
	/// Aggressively simplifies an opaque shadow-caster mesh (position/normal only, no UV seams)
	/// for depth-only LOD3 shadow chunks. Returns the original mesh if reduction is negligible.
	/// </summary>
	internal static ModelLoader.GpuGeometryMesh SimplifyForShadow( ModelLoader.GpuGeometryMesh source, float modelRadius )
	{
		if ( source is null || source.Vertices.Length < 24 || source.Indices.Length < 64 * 3 ) return source;
		var cellSize = Math.Clamp( modelRadius * 0.12f, 6f, 32f );
		return Simplify( source, cellSize, preserveSeams: false, minReductionRatio: 0.80f ) ?? source;
	}

	static ModelLoader.GpuGeometryMesh Simplify(
		ModelLoader.GpuGeometryMesh source,
		float cellSize,
		bool preserveSeams,
		float minReductionRatio )
	{
		var verts = source.Vertices;
		var indices = source.Indices;
		var sourceTris = indices.Length / 3;
		var invCell = 1f / MathF.Max( cellSize, 0.5f );

		var clusterIds = new Dictionary<ClusterKey, int>( verts.Length / 2 );
		var accumulators = new List<ClusterAccumulator>( verts.Length / 2 );
		var remap = new int[verts.Length];

		for ( var i = 0; i < verts.Length; i++ )
		{
			var v = verts[i];
			var gx = (int)MathF.Floor( v.Position.x * invCell );
			var gy = (int)MathF.Floor( v.Position.y * invCell );
			var gz = (int)MathF.Floor( v.Position.z * invCell );

			var normalBucket = 0;
			var uvBucket = 0;
			if ( preserveSeams )
			{
				// Quantize normal into 6 dominant directions (keeps hard box corners crisp)
				// and UV into 0.25-tile bins (prevents merging across atlas/UV seams).
				normalBucket = DominantNormalBucket( v.Normal );
				var ux = (int)MathF.Floor( v.TexCoord.x * 4f );
				var uy = (int)MathF.Floor( v.TexCoord.y * 4f );
				uvBucket = (ux * 397) ^ uy;
			}

			var key = new ClusterKey( gx, gy, gz, normalBucket, uvBucket );
			if ( !clusterIds.TryGetValue( key, out var id ) )
			{
				id = accumulators.Count;
				clusterIds.Add( key, id );
				accumulators.Add( default );
			}

			remap[i] = id;
			var acc = accumulators[id];
			acc.PositionSum += v.Position;
			acc.NormalSum += v.Normal;
			acc.TexCoordSum += v.TexCoord;
			acc.R += v.Color.r;
			acc.G += v.Color.g;
			acc.B += v.Color.b;
			acc.A += v.Color.a;
			acc.Count++;
			accumulators[id] = acc;
		}

		// Rebuild non-degenerate triangles.
		var outIndices = new List<int>( indices.Length / 2 );
		var usedVerts = new int[accumulators.Count];
		Array.Fill( usedVerts, -1 );
		var outVerts = new List<ModelLoader.GpuGeometryVertex>( accumulators.Count );

		int EmitVertex( int clusterId )
		{
			var mapped = usedVerts[clusterId];
			if ( mapped >= 0 ) return mapped;

			var acc = accumulators[clusterId];
			var inv = 1f / acc.Count;
			var n = acc.NormalSum;
			var lenSq = n.LengthSquared;
			var normal = lenSq > 1e-8f ? n * (1f / MathF.Sqrt( lenSq )) : Vector3.Up;
			var color = new Color32(
				(byte)Math.Clamp( acc.R / acc.Count, 0, 255 ),
				(byte)Math.Clamp( acc.G / acc.Count, 0, 255 ),
				(byte)Math.Clamp( acc.B / acc.Count, 0, 255 ),
				(byte)Math.Clamp( acc.A / acc.Count, 0, 255 ) );

			mapped = outVerts.Count;
			usedVerts[clusterId] = mapped;
			outVerts.Add( new ModelLoader.GpuGeometryVertex(
				acc.PositionSum * inv,
				normal,
				acc.TexCoordSum * inv,
				color ) );
			return mapped;
		}

		for ( var i = 0; i + 2 < indices.Length; i += 3 )
		{
			var ia = indices[i];
			var ib = indices[i + 1];
			var ic = indices[i + 2];
			if ( (uint)ia >= (uint)verts.Length || (uint)ib >= (uint)verts.Length || (uint)ic >= (uint)verts.Length )
				continue;

			var ca = remap[ia];
			var cb = remap[ib];
			var cc = remap[ic];
			if ( ca == cb || cb == cc || cc == ca ) continue;

			outIndices.Add( EmitVertex( ca ) );
			outIndices.Add( EmitVertex( cb ) );
			outIndices.Add( EmitVertex( cc ) );
		}

		var outTris = outIndices.Count / 3;
		if ( outTris < 4 || outTris >= sourceTris * minReductionRatio ) return null;

		return new ModelLoader.GpuGeometryMesh( source.Material, outVerts.ToArray(), outIndices.ToArray(), source.MaterialName );
	}

	static int DominantNormalBucket( Vector3 n )
	{
		var ax = MathF.Abs( n.x );
		var ay = MathF.Abs( n.y );
		var az = MathF.Abs( n.z );
		if ( ax >= ay && ax >= az ) return n.x >= 0f ? 0 : 1;
		if ( ay >= ax && ay >= az ) return n.y >= 0f ? 2 : 3;
		return n.z >= 0f ? 4 : 5;
	}
}
