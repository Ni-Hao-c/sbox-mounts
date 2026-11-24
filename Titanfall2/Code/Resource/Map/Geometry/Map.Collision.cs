using System.Runtime.InteropServices;
using Titanfall2;

partial class MapLoader
{
	// World collision is split into XY cells so the broad phase and tile queries touch few triangles.
	const float CollisionCellSize = 512f;
	readonly List<PhysicsGroupDescription> _worldPhysics = [];

	PhysicsCollider[] CreateClassifiedWorldCollision( GameObject world, Titanfall2BspReader.ParsedBsp parsed )
	{
		var result = new List<PhysicsCollider>();
		foreach ( var group in parsed.WorldCollision.Parts.GroupBy( part => part.Contents ) )
		{
			if ( group.Key == 0 ) continue;
			var geometry = new Titanfall2BspReader.BspWorldCollision
			{
				Vertices = parsed.WorldCollision.Vertices,
				Indices = group.SelectMany( part => parsed.WorldCollision.Indices.AsSpan( part.FirstIndex, part.IndexCount ).ToArray() ).ToArray()
			};
			var body = new PhysicsBodyBuilder();
			foreach ( var chunk in BuildCollisionChunks( geometry, CollisionCellSize ).Values )
				body.AddMesh( CollectionsMarshal.AsSpan( chunk.Vertices ), chunk.Indices.Select( index => (uint)index ).ToArray(), [] );
			// CM contents are authoritative. They do not identify a surfaceprop: do not infer
			// a physical material from a render face or from Source 1's collision layout.
			var physics = PhysicsGroupDescription.Create( $"{Path}/contents_{group.Key:x8}.vphys", [body] );
			_worldPhysics.Add( physics );
			var child = new GameObject( world, true, $"titanfall2_world_contents_{group.Key:x8}" ) { IsStatic = true };
			var collider = child.AddComponent<PhysicsCollider>();
			collider.Physics = physics;
			collider.Static = true;
			Titanfall2CollisionQueries.Classify( child, collider, group.Key );
			// Tagged unconditionally: the tag only matters once a navigation filter selects it.
			if ( !collider.IsTrigger ) child.Tags.Add( Titanfall2DeferredNavMeshBuilder.NavMeshBodyTag );
			var source = child.AddComponent<Titanfall2CollisionSource>();
			source.MountIdent = Host.Ident; source.MapPath = Path; source.GeometrySource = "rBSP CM brush/tricoll";
			source.HasEffectiveContents = true; source.EffectiveContents = group.Key;
			result.Add( collider );
		}
		return result.ToArray();
	}

	static Dictionary<CollisionCell, CollisionChunk> BuildCollisionChunks( Titanfall2BspReader.BspWorldCollision collision, float cellSize )
	{
		var chunks = new Dictionary<CollisionCell, CollisionChunk>();
		for ( var index = 0; index + 2 < collision.Indices.Length; index += 3 )
		{
			var sourceA = collision.Indices[index];
			var sourceB = collision.Indices[index + 1];
			var sourceC = collision.Indices[index + 2];
			if ( sourceA < 0 || sourceA >= collision.Vertices.Length
				|| sourceB < 0 || sourceB >= collision.Vertices.Length
				|| sourceC < 0 || sourceC >= collision.Vertices.Length ) continue;
			var center = (collision.Vertices[sourceA] + collision.Vertices[sourceB] + collision.Vertices[sourceC]) / 3f;
			var cell = new CollisionCell( (int)MathF.Floor( center.X / cellSize ), (int)MathF.Floor( center.Y / cellSize ) );
			if ( !chunks.TryGetValue( cell, out var chunk ) )
			{
				chunk = new CollisionChunk();
				chunks.Add( cell, chunk );
			}
			chunk.Indices.Add( chunk.RemapVertex( sourceA, collision.Vertices ) );
			chunk.Indices.Add( chunk.RemapVertex( sourceB, collision.Vertices ) );
			chunk.Indices.Add( chunk.RemapVertex( sourceC, collision.Vertices ) );
		}
		return chunks;
	}

	IReadOnlyDictionary<int, byte[]> ReadCollisionLumpOverrides()
	{
		var overrides = new Dictionary<int, byte[]>();
		if ( _source is not ITitanfall2SiblingAssetSource siblingSource ) return overrides;
		long totalBytes = 0;
		foreach ( var lumpId in Titanfall2BspReader.RequiredCollisionLumpIds )
		{
			byte[] lumpBytes;
			if ( !siblingSource.TryReadSibling( $".{lumpId:X4}.bsp_lump.client", out lumpBytes, out _ )
				&& !siblingSource.TryReadSibling( $".{lumpId:X4}.bsp_lump", out lumpBytes, out _ ) ) continue;
			overrides[lumpId] = lumpBytes;
			totalBytes += lumpBytes.LongLength;
		}

		Log.Info( $"Titanfall 2 BSP collision lumps loaded: {Path} ({overrides.Count}, "
			+ $"{totalBytes / (1024.0 * 1024.0):0.00} MB)" );
		return overrides;
	}
}
