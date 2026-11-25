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
				// A WorldPanel cannot reproduce curved, clipped, multi-bone RUI
				// geometry. Import only the authored single-quad topology here.
				if ( parents != 1 || vertices != 4 || faces != 1 || RuiInt( bytes, mesh + 28 ) != 0 )
					throw new InvalidDataException( "requires more general RUI mesh rendering" );
				var parentTable = RuiOffset( bytes, mesh, parentOffset );
				var vertexTable = RuiOffset( bytes, mesh, RuiInt( bytes, mesh + 16 ) );
				var mapTable = RuiOffset( bytes, mesh, RuiInt( bytes, mesh + 20 ) );
				var faceTable = RuiOffset( bytes, mesh, RuiInt( bytes, mesh + 24 ) );
				if ( !RuiRange( bytes, parentTable, 2 ) || !RuiRange( bytes, vertexTable, 64 )
					|| !RuiRange( bytes, mapTable, 6 ) || !RuiRange( bytes, faceTable, 32 ) )
					throw new InvalidDataException( "mesh payload outside file" );
				var bone = BitConverter.ToInt16( bytes, parentTable );
				if ( bone < 0 || bone >= model.Bones.Length ) throw new InvalidDataException( "invalid parent bone" );
				var map = Enumerable.Range( 0, 3 ).Select( index => (int)BitConverter.ToInt16( bytes, mapTable + index * 2 ) ).ToArray();
				if ( map.Any( index => index < 0 || index >= 4 ) || map.Distinct().Count() != 3 )
					throw new InvalidDataException( "invalid affine face vertex map" );
				var points = new Vector3[4];
				for ( var vertex = 0; vertex < 4; ++vertex )
				{
					var position = vertexTable + vertex * 16;
					if ( RuiInt( bytes, position ) != 0 ) throw new InvalidDataException( "vertex uses another parent" );
					points[vertex] = new( RuiFloat( bytes, position + 4 ), RuiFloat( bytes, position + 8 ), RuiFloat( bytes, position + 12 ) );
				}
				// Retail engine!0x1800FF440: the 32-byte face is U[4], V[4].
				// Its map's first three vertices define the UV-to-bone affine plane.
				// In particular, weapon screens often expose only V [0,~0.5] of
				// a square UI canvas. Treating those floats as two UV corners
				// stretches half of the canvas into the complete physical screen.
				var uvs = Enumerable.Range( 0, 4 ).Select( index => new Vector2(
					RuiFloat( bytes, faceTable + index * 4 ), RuiFloat( bytes, faceTable + 16 + index * 4 ) ) ).ToArray();
				var ab = points[map[1]] - points[map[0]];
				var bc = points[map[2]] - points[map[1]];
				var uvAb = uvs[1] - uvs[0];
				var uvBc = uvs[2] - uvs[1];
				var determinant = uvAb.x * uvBc.y - uvAb.y * uvBc.x;
				if ( !float.IsFinite( determinant ) || MathF.Abs( determinant ) < 0.000001f )
					throw new InvalidDataException( "degenerate affine UV mapping" );
				var right = (uvBc.y * ab - uvAb.y * bc) / determinant;
				var down = (uvAb.x * bc - uvBc.x * ab) / determinant;
				var uvOrigin = points[map[0]] - right * uvs[0].x - down * uvs[0].y;
				var fullWidth = right.Length;
				var fullHeight = down.Length;
				if ( !float.IsFinite( fullWidth ) || !float.IsFinite( fullHeight ) || fullWidth < 0.001f || fullHeight < 0.001f
					|| MathF.Abs( Vector3.Dot( right / fullWidth, down / fullHeight ) ) > 0.002f )
					throw new InvalidDataException( "surface is degenerate or skewed" );
				var start = new Vector2( uvs.Min( uv => uv.x ), uvs.Min( uv => uv.y ) );
				var end = new Vector2( uvs.Max( uv => uv.x ), uvs.Max( uv => uv.y ) );
				var span = end - start;
				if ( span.x < 0.001f || span.y < 0.001f ) throw new InvalidDataException( "empty face UV window" );
				var uvCenter = (start + end) * 0.5f;
				var origin = uvOrigin + right * uvCenter.x + down * uvCenter.y;
				var error = 0f;
				for ( var vertex = 0; vertex < 3; ++vertex )
					error = MathF.Max( error, points[map[vertex]].Distance( uvOrigin + right * uvs[vertex].x + down * uvs[vertex].y ) );
				screens.Add( new() { MeshName = name, BoneName = model.Bones[bone].Name, Origin = origin, Right = right / fullWidth, Down = down / fullHeight,
					Width = fullWidth * span.x, Height = fullHeight * span.y, FullCanvasWidth = fullWidth, FullCanvasHeight = fullHeight,
					FitError = error, UvStart = start, UvEnd = end } );
			}
			catch ( Exception error ) when ( error is InvalidDataException or ArgumentException )
			{
				unsupported.Add( $"{name}: {error.Message}" );
			}
		}
		return new() { Screens = screens.ToArray(), UnsupportedMeshes = unsupported.ToArray() };
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
