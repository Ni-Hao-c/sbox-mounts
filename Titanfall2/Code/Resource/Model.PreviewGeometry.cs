using Titanfall2.Formats;
using static Titanfall2ProcessedCache;

partial class ModelLoader
{
	sealed record PreviewGeometry( StaticMeshGeometry[] Meshes, string Source, string Animation );
	string PreviewCacheIdentity => $"{ImportHost.ContentFingerprint}|{_source.CacheIdentity}|preview-geometry-v2:{heldWeapon}:{skin}:{body}:{Titanfall2StreamingSettings.ShowWeakpointMarkers}";

	bool TryReadPreviewGeometry( out PreviewGeometry geometry )
	{
		PreviewGeometry found = null;
		var hit = Titanfall2ProcessedCache.TryRead( "model", PreviewCacheIdentity, reader =>
		{
			var source = reader.Text();
			var animation = reader.Text();
			var meshes = new StaticMeshGeometry[reader.Count( 4096 )];
			for ( var i = 0; i < meshes.Length; i++ )
			{
				var name = reader.Text();
				var bounds = reader.Array<BBox>( 1 );
				var vertices = reader.Array<TitanfallVertex>( 2_000_000 );
				var indices = reader.Array<int>( 6_000_000 );
				if ( bounds.Length != 1 || indices.Length % 3 != 0 || indices.Any( x => (uint)x >= vertices.Length )
					|| vertices.Any( v => !v.Position.IsFinite || !v.Normal.IsFinite ) ) throw new InvalidDataException( "Invalid cached preview geometry." );
				meshes[i] = new( i, name, vertices, indices, bounds[0] );
			}
			found = new( meshes, source, animation );
		} );
		geometry = hit ? found : null;
		return hit;
	}

	Model CreatePreviewGeometry( Titanfall2Mdl53Reader.ParsedModel model, Titanfall2MeshExtractor.ExtractedLod lod, byte[] data )
	{
		var skeleton = AddSkeleton( null, model );
		var pose = skeleton.LocalTransforms;
		var poseSource = _source.Description;
		var poseAnimation = "bindpose";
		if ( infantryAnimations )
		{
			var sources = GetAnimationSources( data, model );
			var targetBones = skeleton.Names.Select( (name, index) => (name, index) ).ToDictionary( x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase );
			foreach ( var candidate in GetInfantryCandidates( skeleton, sources, targetBones, idleOnly: true )
				.OrderByDescending( c => c.Score ).DistinctBy( c => (c.Source, c.Index) ).Take( 12 ) )
			{
				try
				{
					var frame = Titanfall2Mdl53AnimationReader.DecodeFrame( candidate.Source.Data, candidate.Source.Model, candidate.Index, 0, candidate.Flags );
					pose = new Transform[skeleton.LocalTransforms.Length];
					Array.Copy( skeleton.LocalTransforms, pose, pose.Length );
					for ( var i = 0; i < frame.Length && i < candidate.Remap.Length; i++ )
					{
						if ( candidate.Remap[i] < 0 ) continue;
						pose[candidate.Remap[i]] = new( ToSandbox( frame[i].Position ), ToSandbox( frame[i].Rotation ), ToSandbox( frame[i].Scale ) );
					}
					poseSource = candidate.Source.Description;
					poseAnimation = candidate.Source.Model.Animations[candidate.Index].Name;
					break;
				}
				catch ( Exception error ) { Titanfall2Log.Trace( $"TF2 preview idle skipped for '{ImportPath}': {error.Message}" ); }
			}
		}
		var posedBones = new Transform[pose.Length];
		for ( var i = 0; i < pose.Length; i++ )
		{
			var parent = GetBoneParent( model, i );
			posedBones[i] = parent >= 0 && parent < i ? posedBones[parent].ToWorld( pose[i] ) : pose[i];
		}
		var rotation = heldWeapon ? Titanfall2GameplayVisuals.WeaponAxisCorrection : Rotation.Identity;
		var meshes = new List<StaticMeshGeometry>();
		foreach ( var mesh in lod.Meshes )
		{
			if ( mesh.Vertices.Length == 0 || mesh.Indices.Length < 3 || ShouldSkipRenderMaterial( mesh.MaterialName ) ) continue;
			var name = NormalizeMaterialName( mesh.MaterialName );
			if ( !Titanfall2StreamingSettings.ShowWeakpointMarkers && IsWeakpointMarker( name ) ) continue;
			var vertices = new TitanfallVertex[mesh.Vertices.Length];
			var bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
			for ( var i = 0; i < vertices.Length; i++ )
			{
				var vertex = mesh.Vertices[i];
				var position = ToSandbox( vertex.Position );
				var normal = ToSandbox( vertex.Normal );
				var posedPosition = Vector3.Zero;
				var posedNormal = Vector3.Zero;
				var total = 0f;
				var ids = vertex.BoneIds;
				var weights = vertex.BoneWeights;
				for ( var j = 0; j < Math.Min( ids.Length, weights.Length ); j++ )
				{
					var bone = ids[j]; var weight = weights[j];
					if ( bone >= posedBones.Length || !float.IsFinite( weight ) || weight <= 0 ) continue;
					posedPosition += posedBones[bone].PointToWorld( skeleton.ModelTransforms[bone].PointToLocal( position ) ) * weight;
					posedNormal += (posedBones[bone].Rotation * (skeleton.ModelTransforms[bone].Rotation.Inverse * normal)) * weight;
					total += weight;
				}
				if ( total > 0 ) { position = posedPosition / total; normal = posedNormal.Normal; }
				position = rotation * position;
				normal = (rotation * normal).Normal;
				vertices[i] = new() { Position = position, Normal = normal, TexCoord = ToSandbox( vertex.TexCoord ), Color = GetVertexColor( vertex ) };
				bounds = bounds.AddPoint( position );
			}
			meshes.Add( new( meshes.Count, name, vertices, ReverseWinding( mesh.Indices ), bounds ) );
		}
		var geometry = new PreviewGeometry( meshes.ToArray(), poseSource, poseAnimation );
		Titanfall2ProcessedCache.Write( "model", PreviewCacheIdentity, writer =>
		{
			WriteString( writer, geometry.Source ); WriteString( writer, geometry.Animation ); writer.Write( geometry.Meshes.Length );
			foreach ( var mesh in geometry.Meshes )
			{
				WriteString( writer, mesh.MaterialName ); WriteArray( writer, new[] { mesh.Bounds } );
				WriteArray( writer, mesh.Vertices ); WriteArray( writer, mesh.Indices );
			}
		} );
		return BuildPreviewGeometry( geometry, cached: false );
	}

	Model BuildPreviewGeometry( PreviewGeometry geometry, bool cached )
	{
		var builder = Model.Builder.WithName( ImportPath );
		foreach ( var source in geometry.Meshes )
		{
			var material = LoadMaterial( source.MaterialName ) ?? MaterialLoader.CreateRuntimeMaterial( $"{ImportPath}/preview_fallback_{source.Index}", GetMaterialMetadata( source.MaterialName ) );
			var mesh = new Mesh( $"{ImportPath}_{source.Index}", material );
			mesh.CreateVertexBuffer( source.Vertices.Length, source.Vertices );
			mesh.CreateIndexBuffer( source.Indices.Length, source.Indices );
			mesh.Bounds = source.Bounds;
			builder.AddMesh( mesh );
		}
		builder.WithData( new Titanfall2PreviewModelData { PoseSource = geometry.Source, PoseAnimation = geometry.Animation, GeometryCacheHit = cached } );
		return builder.Create();
	}
}

/// <summary>Diagnostics for a static menu model, independent of gameplay skeletons and animation resources.</summary>
[Sandbox.ModelEditor.GameData( "titanfall2_preview" )]
public sealed class Titanfall2PreviewModelData
{
	public string PoseSource { get; set; }
	public string PoseAnimation { get; set; }
	public bool GeometryCacheHit { get; set; }
}
