using System.Diagnostics;
using Titanfall2;

/// <summary>
/// On-disk cache for compiled static prop aggregate geometry (Shared/Near/Far LOD sectors,
/// LOD3 depth-only shadow chunks, and batched residual decal/translucent groups).
/// </summary>
sealed partial class Titanfall2StaticPropAggregate
{
	const string CacheKind = "propagg";
	const uint CacheMagic = 0x41503254; // "T2PA"
	// V9 retains forward geometry/authored LODs and authored cutout shadow flags.
	// Earlier payloads could omit blockers or synthesize Far meshes that shrink cover.
	const int CacheVersion = 9;

	internal static string BuildCacheIdentity( Titanfall2Mount mount, string mapPath ) =>
		mount is null || string.IsNullOrWhiteSpace( mapPath )
			? null
			: $"{mount.ContentFingerprint}|{mapPath}|propagg-v{CacheVersion}|shadows:{Titanfall2StreamingSettings.PropShadows}|weak:{Titanfall2StreamingSettings.ShowWeakpointMarkers}|mixed:{Titanfall2StreamingSettings.GpuMixedProps}";

	internal static bool HasCacheEntry( string identity )
	{
		if ( !Titanfall2StreamingSettings.ProcessedCache || string.IsNullOrEmpty( identity ) ) return false;
		try { return Titanfall2Storage.FileExists( Titanfall2ProcessedCache.GetPath( CacheKind, identity ) ); }
		catch ( Exception ) { return false; }
	}

	internal static bool TryReadCache( string identity, out PropAggregatePayload payload )
	{
		payload = null;
		if ( !Titanfall2StreamingSettings.ProcessedCache || string.IsNullOrEmpty( identity ) ) return false;

		var timer = Stopwatch.StartNew();
		PropAggregatePayload parsed = null;
		var hit = Titanfall2ProcessedCache.TryRead( CacheKind, identity, reader =>
		{
			if ( reader.ReadUInt32() != CacheMagic || reader.ReadInt32() != CacheVersion )
				throw new InvalidDataException( "Prop aggregate cache header mismatch." );

			var totalPropCount = reader.Count( 1_000_000 );
			var aggregatedPropCount = reader.Count( 1_000_000 );
			var mixedPropCount = reader.Count( 1_000_000 );
			var residualBatchedPropCount = reader.Count( 1_000_000 );
			var sharedTris = reader.Count( 50_000_000 );
			var nearTris = reader.Count( 50_000_000 );
			var farTris = reader.Count( 50_000_000 );
			var shadowTris = reader.Count( 50_000_000 );
			var residualTris = reader.Count( 50_000_000 );

			var matCount = reader.Count( 65536 );
			var materialNames = new string[matCount];
			for ( var i = 0; i < matCount; i++ ) materialNames[i] = reader.Text();

			var probes = reader.Array<Titanfall2ProbeData>( 1_000_000 );

			var bucketCount = reader.Count( 4096 );
			var buckets = new BucketSpec[bucketCount];
			for ( var i = 0; i < bucketCount; i++ )
			{
				var shader = reader.Text();
				var filter = reader.ReadInt32();
				var castShadows = reader.ReadBoolean();
				var templateIdx = reader.Count( matCount );
				buckets[i] = new BucketSpec( shader, filter, castShadows, templateIdx );
			}

			var sectorCount = reader.Count( 16384 );
			var sectors = new SectorData[sectorCount];
			for ( var i = 0; i < sectorCount; i++ )
			{
				var sx = reader.ReadInt32();
				var sy = reader.ReadInt32();
				var bounds = ReadBBox( reader );
				var pvs = Titanfall2BspReader.VisibilityCellMask.Read( reader );
				var hasLandmark = reader.ReadBoolean();
				var sharedModels = ReadAggregateModels( reader, bucketCount );
				var nearModels = ReadAggregateModels( reader, bucketCount );
				var farModels = ReadAggregateModels( reader, bucketCount );
				sectors[i] = new SectorData( sx, sy, bounds, pvs, hasLandmark, sharedModels, nearModels, farModels );
			}

			var shadowCount = reader.Count( 16384 );
			var shadowChunks = new ShadowChunkData[shadowCount];
			for ( var i = 0; i < shadowCount; i++ )
			{
				var cx = reader.ReadInt32();
				var cy = reader.ReadInt32();
				var bounds = ReadBBox( reader );
				var pvs = Titanfall2BspReader.VisibilityCellMask.Read( reader );
				var verts = reader.Array<TitanfallPropShadowVertex>( 8_000_000 );
				var idxs = reader.Array<int>( 24_000_000 );
				shadowChunks[i] = new ShadowChunkData( cx, cy, verts, idxs, bounds, pvs );
			}

			var resGroupCount = reader.Count( 16384 );
			var residualGroups = new ResidualGroupData[resGroupCount];
			for ( var i = 0; i < resGroupCount; i++ )
			{
				var matName = reader.Text();
				var castShadows = reader.ReadBoolean();
				var partCount = reader.Count( 16384 );
				var parts = new ResidualPartData[partCount];
				for ( var p = 0; p < partCount; p++ )
				{
					var bounds = ReadBBox( reader );
					var pvs = Titanfall2BspReader.VisibilityCellMask.Read( reader );
					var verts = reader.Array<TitanfallPropResidualVertex>( 4_000_000 );
					var idxs = reader.Array<int>( 12_000_000 );
					parts[p] = new ResidualPartData( verts, idxs, bounds, pvs );
				}
				residualGroups[i] = new ResidualGroupData( matName, castShadows, parts );
			}

			var fallbackIndices = reader.Array<int>( 1_000_000 );

			parsed = new PropAggregatePayload(
				materialNames,
				probes,
				buckets,
				sectors,
				shadowChunks,
				residualGroups,
				fallbackIndices,
				totalPropCount,
				aggregatedPropCount,
				mixedPropCount,
				residualBatchedPropCount,
				sharedTris,
				nearTris,
				farTris,
				shadowTris,
				residualTris );
		} );

		if ( !hit || parsed is null ) return false;
		payload = parsed;
		Log.Info( $"Titanfall 2 static prop aggregate cache hit: decoded in {timer.Elapsed.TotalSeconds:0.00}s ({payload.Sectors.Length} sectors, {payload.ShadowChunks.Length} shadow chunks, {payload.ResidualGroups.Length} residual groups)." );
		return true;
	}

	internal static void WriteCache( string identity, PropAggregatePayload payload )
	{
		if ( !Titanfall2StreamingSettings.ProcessedCache || string.IsNullOrEmpty( identity ) || payload is null ) return;

		var timer = Stopwatch.StartNew();
		var written = Titanfall2ProcessedCache.Write( CacheKind, identity, writer =>
		{
			writer.Write( CacheMagic );
			writer.Write( CacheVersion );
			writer.Write( payload.TotalPropCount );
			writer.Write( payload.AggregatedPropCount );
			writer.Write( payload.MixedPropCount );
			writer.Write( payload.ResidualBatchedPropCount );
			writer.Write( payload.SharedTriangles );
			writer.Write( payload.NearTriangles );
			writer.Write( payload.FarTriangles );
			writer.Write( payload.ShadowTriangles );
			writer.Write( payload.ResidualTriangles );

			writer.Write( payload.MaterialNames.Length );
			foreach ( var name in payload.MaterialNames ) Titanfall2ProcessedCache.WriteString( writer, name );

			Titanfall2ProcessedCache.WriteArray( writer, payload.Probes );

			writer.Write( payload.Buckets.Length );
			foreach ( var b in payload.Buckets )
			{
				Titanfall2ProcessedCache.WriteString( writer, b.ShaderName );
				writer.Write( b.TextureFiltering );
				writer.Write( b.CastShadows );
				writer.Write( b.TemplateMaterialIndex );
			}

			writer.Write( payload.Sectors.Length );
			foreach ( var s in payload.Sectors )
			{
				writer.Write( s.SectorX );
				writer.Write( s.SectorY );
				WriteBBox( writer, s.Bounds );
				s.PvsCells.Write( writer );
				writer.Write( s.HasLandmark );
				WriteAggregateModels( writer, s.SharedModels );
				WriteAggregateModels( writer, s.NearModels );
				WriteAggregateModels( writer, s.FarModels );
			}

			writer.Write( payload.ShadowChunks.Length );
			foreach ( var c in payload.ShadowChunks )
			{
				writer.Write( c.CellX );
				writer.Write( c.CellY );
				WriteBBox( writer, c.Bounds );
				c.PvsCells.Write( writer );
				Titanfall2ProcessedCache.WriteArray( writer, c.Vertices );
				Titanfall2ProcessedCache.WriteArray( writer, c.Indices );
			}

			writer.Write( payload.ResidualGroups.Length );
			foreach ( var g in payload.ResidualGroups )
			{
				Titanfall2ProcessedCache.WriteString( writer, g.MaterialName );
				writer.Write( g.CastShadows );
				writer.Write( g.Parts.Length );
				foreach ( var p in g.Parts )
				{
					WriteBBox( writer, p.Bounds );
					p.PvsCells.Write( writer );
					Titanfall2ProcessedCache.WriteArray( writer, p.Vertices );
					Titanfall2ProcessedCache.WriteArray( writer, p.Indices );
				}
			}

			Titanfall2ProcessedCache.WriteArray( writer, payload.FallbackPropIndices );
		} );

		if ( written )
			Log.Info( $"Titanfall 2 static prop aggregate cache written in {timer.Elapsed.TotalSeconds:0.00}s." );
	}

	static void WriteAggregateModels( BinaryWriter writer, AggregateModelData[] models )
	{
		writer.Write( models.Length );
		foreach ( var m in models )
		{
			writer.Write( m.BucketIndex );
			writer.Write( m.Parts.Length );
			foreach ( var p in m.Parts )
			{
				WriteBBox( writer, p.Bounds );
				Titanfall2ProcessedCache.WriteArray( writer, p.Vertices );
				Titanfall2ProcessedCache.WriteArray( writer, p.Indices );
			}
		}
	}

	static AggregateModelData[] ReadAggregateModels( Titanfall2ProcessedCache.CacheReader reader, int bucketCount )
	{
		var count = reader.Count( 4096 );
		var models = new AggregateModelData[count];
		for ( var i = 0; i < count; i++ )
		{
			var bucketIndex = reader.Count( Math.Max( 0, bucketCount - 1 ) );
			var partCount = reader.Count( 4096 );
			var parts = new AggregatePartData[partCount];
			for ( var p = 0; p < partCount; p++ )
			{
				var bounds = ReadBBox( reader );
				var verts = reader.Array<TitanfallPropAggregateVertex>( 4_000_000 );
				var idxs = reader.Array<int>( 12_000_000 );
				parts[p] = new AggregatePartData( verts, idxs, bounds );
			}
			models[i] = new AggregateModelData( bucketIndex, parts );
		}
		return models;
	}

	static void WriteBBox( BinaryWriter writer, BBox bounds )
	{
		writer.Write( bounds.Mins.x ); writer.Write( bounds.Mins.y ); writer.Write( bounds.Mins.z );
		writer.Write( bounds.Maxs.x ); writer.Write( bounds.Maxs.y ); writer.Write( bounds.Maxs.z );
	}

	static BBox ReadBBox( Titanfall2ProcessedCache.CacheReader reader ) => new(
		new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ),
		new Vector3( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() ) );
}
