using System.Text;
using Titanfall2.Formats;

/// <summary>Planar RUI surfaces authored inside a retail weapon MDL53.</summary>
[Sandbox.ModelEditor.GameData( "titanfall2_weapon_screens" )]
public sealed class Titanfall2WeaponScreenData
{
	public sealed class Screen
	{
		public string MeshName { get; set; }
		public string BoneName { get; set; }
		/// <summary>Center of the face UV window, in the parent bone's local coordinates.</summary>
		public Vector3 Origin { get; set; }
		/// <summary>Unit vectors toward increasing authored U and V, in bone-local space.</summary>
		public Vector3 Right { get; set; }
		public Vector3 Down { get; set; }
		/// <summary>Bone-local size of the visible face UV window.</summary>
		public float Width { get; set; }
		public float Height { get; set; }
		/// <summary>Bone-local size of the full UV [0,1] canvas, before the face window clips it.</summary>
		public float FullCanvasWidth { get; set; }
		public float FullCanvasHeight { get; set; }
		/// <summary>Numerical reconstruction error for the three mapped affine reference points.</summary>
		public float FitError { get; set; }
		public Vector2 UvStart { get; set; }
		public Vector2 UvEnd { get; set; }
	}
	public Screen[] Screens { get; set; } = [];
	public string[] UnsupportedMeshes { get; set; } = [];
}

partial class ModelLoader
{
	const float MaxStrictSkewDot = 0.002f;
	const float MaxClusterSkewDot = 0.25f;
	const float CoplanarNormalDot = 0.99f;
	const float CoplanarPlaneTolerance = 0.08f;
	const float CoplanarOriginTolerance = 0.12f;
	const float MinPhysicalExtent = 0.015f;

	readonly record struct RuiFacePlane(
		int Bone,
		Vector3 UvOrigin,
		Vector3 Right,
		Vector3 Down,
		Vector3 Normal,
		float PlaneDistance,
		float Area,
		float FitError,
		Vector2 UvStart,
		Vector2 UvEnd,
		Vector3[] Vertices,
		Vector2[] Uvs );

	static void AddWeaponScreens( ModelBuilder builder, Titanfall2Mdl53Reader.ParsedModel model, byte[] bytes )
	{
		if ( model.Header.UiPanelCount <= 0 ) return;
		builder.WithData( ReadWeaponScreens( model, bytes ) );
	}

	internal static Titanfall2WeaponScreenData ReadWeaponScreens( Titanfall2Mdl53Reader.ParsedModel model, byte[] bytes )
	{
		var screens = new List<Titanfall2WeaponScreenData.Screen>();
		var unsupported = new List<string>();
		var count = model.Header.UiPanelCount;
		var table = model.Header.UiPanelOffset;
		if ( count <= 0 ) return new();
		if ( count > 256 || !RuiRange( bytes, table, (long)count * 8 ) )
			return new() { UnsupportedMeshes = ["Invalid MDL53 RUI mesh table"] };

		for ( var i = 0; i < count; ++i )
		{
			var name = $"RUI[{i}]";
			try
			{
				// MDL53's RUI entry is 8 bytes. The mesh pointer is relative to its
				// own entry, not to the start of the complete mesh table.
				var entry = table + i * 8;
				var mesh = RuiOffset( bytes, entry, RuiInt( bytes, entry + 4 ) );
				if ( !RuiRange( bytes, mesh, 32 ) ) throw new InvalidDataException( "mesh header outside file" );
				var parents = RuiInt( bytes, mesh );
				var vertices = RuiInt( bytes, mesh + 4 );
				var faces = RuiInt( bytes, mesh + 8 );
				var parentOffset = RuiInt( bytes, mesh + 12 );
				if ( parentOffset < 33 || parentOffset > 2048 || !RuiRange( bytes, mesh + 32, parentOffset - 32 ) )
					throw new InvalidDataException( "invalid mesh name range" );
				var nameBytes = bytes.AsSpan( mesh + 32, parentOffset - 32 );
				var terminator = nameBytes.IndexOf( (byte)0 );
				if ( terminator <= 0 ) throw new InvalidDataException( "unterminated mesh name" );
				name = Encoding.UTF8.GetString( nameBytes[..terminator] ).Replace( '\\', '/' );

				if ( parents is < 1 or > 32 || vertices is < 3 or > 4096 || faces is < 1 or > 4096 )
					throw new InvalidDataException( "invalid RUI mesh counts" );

				var parentTable = RuiOffset( bytes, mesh, parentOffset );
				var vertexTable = RuiOffset( bytes, mesh, RuiInt( bytes, mesh + 16 ) );
				var mapTable = RuiOffset( bytes, mesh, RuiInt( bytes, mesh + 20 ) );
				var faceTable = RuiOffset( bytes, mesh, RuiInt( bytes, mesh + 24 ) );
				if ( !RuiRange( bytes, parentTable, (long)parents * 2 )
					|| !RuiRange( bytes, vertexTable, (long)vertices * 16 )
					|| !RuiRange( bytes, mapTable, (long)faces * 6 )
					|| !RuiRange( bytes, faceTable, (long)faces * 32 ) )
					throw new InvalidDataException( "mesh payload outside file" );

				var bones = new int[parents];
				for ( var p = 0; p < parents; ++p )
				{
					bones[p] = BitConverter.ToInt16( bytes, parentTable + p * 2 );
					if ( bones[p] < 0 || bones[p] >= model.Bones.Length ) throw new InvalidDataException( "invalid parent bone" );
				}

				var points = new Vector3[vertices];
				var vertexParents = new int[vertices];
				for ( var v = 0; v < vertices; ++v )
				{
					var pos = vertexTable + v * 16;
					var parentIdx = RuiInt( bytes, pos );
					if ( parentIdx < 0 || parentIdx >= parents ) throw new InvalidDataException( "vertex uses invalid parent" );
					vertexParents[v] = parentIdx;
					points[v] = new( RuiFloat( bytes, pos + 4 ), RuiFloat( bytes, pos + 8 ), RuiFloat( bytes, pos + 12 ) );
				}

				var extracted = ExtractFacePlanes( bytes, bones, points, vertexParents, mapTable, faceTable, faces );
				if ( extracted.Count == 0 )
					throw new InvalidDataException( "no valid planar RUI faces" );

				var clusters = ClusterCoplanarFaces( extracted );
				var added = 0;
				foreach ( var cluster in clusters )
				{
					var screen = BuildScreenFromCluster( name, model.Bones[cluster[0].Bone].Name, cluster );
					if ( screen is null ) continue;
					screens.Add( screen );
					added++;
				}
				if ( added == 0 )
					throw new InvalidDataException( "surface is degenerate or skewed" );
			}
			catch ( Exception error ) when ( error is InvalidDataException or ArgumentException )
			{
				unsupported.Add( $"{name}: {error.Message}" );
			}
		}
		return new() { Screens = screens.ToArray(), UnsupportedMeshes = unsupported.ToArray() };
	}

	static List<RuiFacePlane> ExtractFacePlanes( byte[] bytes, int[] bones, Vector3[] points, int[] vertexParents,
		int mapTable, int faceTable, int faces )
	{
		var planes = new List<RuiFacePlane>( faces );
		var vertices = points.Length;
		for ( var fi = 0; fi < faces; ++fi )
		{
			var i0 = (int)BitConverter.ToInt16( bytes, mapTable + fi * 6 );
			var i1 = (int)BitConverter.ToInt16( bytes, mapTable + fi * 6 + 2 );
			var i2 = (int)BitConverter.ToInt16( bytes, mapTable + fi * 6 + 4 );
			if ( i0 < 0 || i0 >= vertices || i1 < 0 || i1 >= vertices || i2 < 0 || i2 >= vertices
				|| i0 == i1 || i1 == i2 || i0 == i2 )
			{
				if ( faces == 1 ) throw new InvalidDataException( "invalid affine face vertex map" );
				continue;
			}
			var parentIdx = vertexParents[i0];
			if ( vertexParents[i1] != parentIdx || vertexParents[i2] != parentIdx ) continue;

			var faceOffset = faceTable + fi * 32;
			var uvs = new Vector2[4];
			for ( var k = 0; k < 4; ++k )
				uvs[k] = new( RuiFloat( bytes, faceOffset + k * 4 ), RuiFloat( bytes, faceOffset + 16 + k * 4 ) );

			var ab = points[i1] - points[i0];
			var bc = points[i2] - points[i1];
			var uvAb = uvs[1] - uvs[0];
			var uvBc = uvs[2] - uvs[1];
			var determinant = uvAb.x * uvBc.y - uvAb.y * uvBc.x;
			if ( !float.IsFinite( determinant ) || MathF.Abs( determinant ) < 0.000001f )
			{
				if ( faces == 1 ) throw new InvalidDataException( "degenerate affine UV mapping" );
				continue;
			}

			var right = (uvBc.y * ab - uvAb.y * bc) / determinant;
			var down = (uvAb.x * bc - uvBc.x * ab) / determinant;
			var fullWidth = right.Length;
			var fullHeight = down.Length;
			if ( !float.IsFinite( fullWidth ) || !float.IsFinite( fullHeight ) || fullWidth < 0.001f || fullHeight < 0.001f )
			{
				if ( faces == 1 ) throw new InvalidDataException( "surface is degenerate or skewed" );
				continue;
			}

			var skewDot = MathF.Abs( Vector3.Dot( right / fullWidth, down / fullHeight ) );
			if ( skewDot > MaxClusterSkewDot )
			{
				if ( faces == 1 ) throw new InvalidDataException( "surface is degenerate or skewed" );
				continue;
			}

			var uvOrigin = points[i0] - right * uvs[0].x - down * uvs[0].y;
			var start = new Vector2( MathF.Min( MathF.Min( uvs[0].x, uvs[1].x ), MathF.Min( uvs[2].x, uvs[3].x ) ),
				MathF.Min( MathF.Min( uvs[0].y, uvs[1].y ), MathF.Min( uvs[2].y, uvs[3].y ) ) );
			var end = new Vector2( MathF.Max( MathF.Max( uvs[0].x, uvs[1].x ), MathF.Max( uvs[2].x, uvs[3].x ) ),
				MathF.Max( MathF.Max( uvs[0].y, uvs[1].y ), MathF.Max( uvs[2].y, uvs[3].y ) ) );
			var span = end - start;
			if ( span.x < 0.001f || span.y < 0.001f )
			{
				if ( faces == 1 ) throw new InvalidDataException( "empty face UV window" );
				continue;
			}

			var normal = Vector3.Cross( right, down ).Normal;
			if ( normal.LengthSquared < 0.5f ) continue;
			var area = Vector3.Cross( ab, bc ).Length * 0.5f;
			var error = 0f;
			Vector3[] faceVerts = [points[i0], points[i1], points[i2]];
			for ( var k = 0; k < 3; ++k )
				error = MathF.Max( error, faceVerts[k].Distance( uvOrigin + right * uvs[k].x + down * uvs[k].y ) );

			planes.Add( new( bones[parentIdx], uvOrigin, right, down, normal, Vector3.Dot( normal, uvOrigin ),
				MathF.Max( area, 0.00001f ), error, start, end, faceVerts, [uvs[0], uvs[1], uvs[2]] ) );
		}
		return planes;
	}

	static List<List<RuiFacePlane>> ClusterCoplanarFaces( List<RuiFacePlane> faces )
	{
		var clusters = new List<List<RuiFacePlane>>();
		foreach ( var face in faces )
		{
			List<RuiFacePlane> matched = null;
			foreach ( var cluster in clusters )
			{
				var seed = cluster[0];
				if ( seed.Bone != face.Bone ) continue;
				if ( Vector3.Dot( seed.Normal, face.Normal ) < CoplanarNormalDot ) continue;
				if ( MathF.Abs( seed.PlaneDistance - face.PlaneDistance ) > CoplanarPlaneTolerance ) continue;
				var basisScale = MathF.Max( seed.Right.Length, seed.Down.Length );
				if ( seed.Right.Distance( face.Right ) > basisScale * 0.25f
					|| seed.Down.Distance( face.Down ) > basisScale * 0.25f ) continue;
				if ( seed.UvOrigin.Distance( face.UvOrigin ) > CoplanarOriginTolerance ) continue;
				matched = cluster;
				break;
			}
			if ( matched is not null ) matched.Add( face );
			else clusters.Add( [face] );
		}
		clusters.Sort( ( a, b ) => b.Sum( f => f.Area ).CompareTo( a.Sum( f => f.Area ) ) );
		return clusters;
	}

	static Titanfall2WeaponScreenData.Screen BuildScreenFromCluster( string meshName, string boneName, List<RuiFacePlane> cluster )
	{
		Vector3 uvOrigin, right, down;
		Vector2 start, end;
		if ( cluster.Count == 1 )
		{
			var f = cluster[0];
			uvOrigin = f.UvOrigin;
			right = f.Right;
			down = f.Down;
			start = f.UvStart;
			end = f.UvEnd;
		}
		else
		{
			var totalWeight = 0f;
			uvOrigin = right = down = Vector3.Zero;
			start = new( float.MaxValue, float.MaxValue );
			end = new( float.MinValue, float.MinValue );
			foreach ( var f in cluster )
			{
				totalWeight += f.Area;
				uvOrigin += f.UvOrigin * f.Area;
				right += f.Right * f.Area;
				down += f.Down * f.Area;
				start = new( MathF.Min( start.x, f.UvStart.x ), MathF.Min( start.y, f.UvStart.y ) );
				end = new( MathF.Max( end.x, f.UvEnd.x ), MathF.Max( end.y, f.UvEnd.y ) );
			}
			uvOrigin /= totalWeight;
			right /= totalWeight;
			down /= totalWeight;
		}

		var fullWidth = right.Length;
		var fullHeight = down.Length;
		if ( !float.IsFinite( fullWidth ) || !float.IsFinite( fullHeight ) || fullWidth < 0.001f || fullHeight < 0.001f )
			return null;

		var rightDir = right / fullWidth;
		var downDir = down / fullHeight;
		var skew = Vector3.Dot( rightDir, downDir );
		if ( MathF.Abs( skew ) > MaxStrictSkewDot )
		{
			// Slightly trapezoidal screens (e.g. R-101 OG, EVA-8, Volt) are projected onto
			// their orthogonal plane while keeping center and U/V extents accurate.
			var orthoDown = downDir - rightDir * skew;
			if ( orthoDown.LengthSquared < 0.5f ) return null;
			downDir = orthoDown.Normal;
		}

		var span = end - start;
		var width = fullWidth * span.x;
		var height = fullHeight * span.y;
		if ( span.x < 0.001f || span.y < 0.001f || width < MinPhysicalExtent || height < MinPhysicalExtent )
			return null;

		var uvCenter = (start + end) * 0.5f;
		var origin = uvOrigin + right * uvCenter.x + down * uvCenter.y;
		var fitError = 0f;
		foreach ( var f in cluster )
			for ( var k = 0; k < 3; ++k )
				fitError = MathF.Max( fitError, f.Vertices[k].Distance( uvOrigin + right * f.Uvs[k].x + down * f.Uvs[k].y ) );

		return new()
		{
			MeshName = meshName,
			BoneName = boneName,
			Origin = origin,
			Right = rightDir,
			Down = downDir,
			Width = width,
			Height = height,
			FullCanvasWidth = fullWidth,
			FullCanvasHeight = fullHeight,
			FitError = fitError,
			UvStart = start,
			UvEnd = end
		};
	}

	static int RuiOffset( byte[] bytes, int origin, int relative )
	{
		var offset = (long)origin + relative;
		if ( offset < 0 || offset > bytes.LongLength || offset > int.MaxValue ) throw new InvalidDataException( "RUI relative offset outside file" );
		return (int)offset;
	}
	static bool RuiRange( byte[] bytes, int offset, long length ) => offset >= 0 && length >= 0 && offset + length <= bytes.LongLength;
	static int RuiInt( byte[] bytes, int offset ) => BitConverter.ToInt32( bytes, offset );
	static float RuiFloat( byte[] bytes, int offset )
	{
		var value = BitConverter.ToSingle( bytes, offset );
		return float.IsFinite( value ) ? value : throw new InvalidDataException( "non-finite RUI coordinate" );
	}
}
