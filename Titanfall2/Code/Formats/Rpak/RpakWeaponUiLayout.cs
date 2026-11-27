using System.Text;

internal enum Titanfall2WeaponUiDisplayMode
{
	Numeric,
	NumericWithBar,
	SegmentedBar,
	ContinuousBar,
	DualBar,
	HeatScreen,
	ChargeStack,
	DualNumeric
}

internal readonly record struct Titanfall2WeaponUiBarSpec(
	Rect Rect,
	int Segments,
	int Rows,
	bool Vertical,
	bool ReverseFill = false );

/// <summary>Verified static fields of a retail weapon ammo/heat/charge RUI layout.</summary>
internal sealed record Titanfall2WeaponUiLayout(
	string Name,
	Vector2 CanvasSize,
	int FontId,
	float FontSize,
	Vector2 TextAnchor,
	Vector2 TextPivot,
	string Source,
	Titanfall2WeaponUiDisplayMode Mode = Titanfall2WeaponUiDisplayMode.Numeric,
	Titanfall2WeaponUiBarSpec? PrimaryBar = null,
	Titanfall2WeaponUiBarSpec? SecondaryBar = null,
	Vector2? SecondaryTextAnchor = null );

/// <summary>
/// Static catalog of verified retail Titanfall 2 weapon display layouts extracted from
/// the shipping ui*.rpak assets so players never depend on runtime RUI bytecode decoding.
/// </summary>
internal static class Titanfall2WeaponUiCatalog
{
	static readonly Dictionary<string, Titanfall2WeaponUiLayout> Layouts = new( StringComparer.OrdinalIgnoreCase )
	{
		// Numeric + bar displays
		["ui/r101_ammo_counter"] = new( "ui/r101_ammo_counter", new( 256, 256 ), 9, 120, new( 69.1f, 56.3f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.NumericWithBar, new( new( 10.2f, 117.8f, 233f, 7.7f ), 0, 1, false ) ),
		["ui/car_smg_ammo_counter"] = new( "ui/car_smg_ammo_counter", new( 256, 256 ), 9, 120, new( 128f, 64f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.Numeric ),
		["ui/g2a4_ammo_counter"] = new( "ui/g2a4_ammo_counter", new( 256, 256 ), 9, 145, new( 192f, 64f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.NumericWithBar, new( new( 6f, 16f, 110f, 102f ), 14, 4, false ) ),
		["ui/p2011_ammo_counter"] = new( "ui/p2011_ammo_counter", new( 256, 256 ), 9, 40, new( 209.9f, 10.2f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.NumericWithBar, new( new( 15.4f, 12.8f, 133.1f, 243.2f ), 12, 1, true ) ),
		["ui/volt_ammo_counter"] = new( "ui/volt_ammo_counter", new( 256, 256 ), 9, 60, new( 64f, 25.6f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.Numeric ),
		["ui/hemlok_esaw_ammo_counter"] = new( "ui/hemlok_esaw_ammo_counter", new( 256, 256 ), 2, 120, new( 56.3f, 46.1f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.NumericWithBar, new( new( 0f, 89.6f, 256f, 12.8f ), 0, 1, false ) ),
		["ui/b3wing_ammo_counter"] = new( "ui/b3wing_ammo_counter", new( 256, 256 ), 19, 150, new( 128f, 69.1f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.Numeric ),
		["ui/hemlok_bfr_ammo_counter"] = new( "ui/hemlok_bfr_ammo_counter", new( 256, 256 ), 9, 190, new( 150f, 86f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.NumericWithBar, new( new( 75f, 156f, 116f, 20f ), 8, 1, false ),
			new( new( 0f, 228f, 256f, 28f ), 0, 1, false ) ),
		["ui/mk6_ammo_counter"] = new( "ui/mk6_ammo_counter", new( 256, 256 ), 9, 145, new( 192f, 71.7f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.NumericWithBar, new( new( 4f, 51f, 112f, 85f ), 12, 2, false ) ),
		["ui/doubletake_ammo_counter"] = new( "ui/doubletake_ammo_counter", new( 512, 512 ), 2, 256, new( 384f, 128f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.DualNumeric, SecondaryTextAnchor: new( 128f, 128f ) ),
		["ui/pulse_lmg_panel2"] = new( "ui/pulse_lmg_panel2", new( 256, 256 ), 9, 220, new( 128f, 140.8f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.NumericWithBar, new( new( 24f, 25f, 208f, 28f ), 4, 1, false ) ),
		["ui/arc_launcher_reticle"] = new( "ui/arc_launcher_reticle", new( 256, 256 ), 9, 15, new( 61.4f, 115.2f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.NumericWithBar, new( new( 128f, 12f, 120f, 96f ), 3, 1, false ) ),
		["ui/hcog_lower"] = new( "ui/hcog_lower", new( 256, 256 ), 9, 20, new( 192f, 120.3f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.NumericWithBar, new( new( 134f, 16f, 116f, 84f ), 0, 1, false ) ),

		// Segmented & multi-row bar-only ammo counters
		["ui/car_smg_ammo_counter_small"] = new( "ui/car_smg_ammo_counter_small", new( 100, 100 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.DualBar, new( new( 2f, 52f, 46f, 44f ), 0, 1, false ), new( new( 52f, 4f, 44f, 24f ), 0, 1, false ) ),
		["ui/r97_ammo_counter"] = new( "ui/r97_ammo_counter", new( 160, 240 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.SegmentedBar, new( new( 5f, 40f, 150f, 80f ), 40, 4, false ), new( new( 5f, 4f, 150f, 26f ), 0, 1, false ) ),
		["ui/vinson_bar"] = new( "ui/vinson_bar", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.ContinuousBar, new( new( 4f, 16f, 120f, 96f ), 0, 1, false ) ),
		["ui/re45_ammo_counter"] = new( "ui/re45_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.SegmentedBar, new( new( 25.6f, 12.8f, 204.8f, 236f ), 20, 1, true ) ),
		["ui/alternator_ammo_counter"] = new( "ui/alternator_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.DualBar, new( new( 12f, 8f, 76f, 116f ), 10, 1, true ), new( new( 168f, 8f, 76f, 116f ), 10, 1, true ) ),
		["ui/spitfire_ammo_counter"] = new( "ui/spitfire_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.SegmentedBar, new( new( 6f, 50f, 244f, 200f ), 80, 5, false ), new( new( 6f, 6f, 112f, 28f ), 0, 1, false ) ),
		["ui/rspn101_dmr_ammo_counter"] = new( "ui/rspn101_dmr_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.SegmentedBar, new( new( 12.8f, 6f, 230.4f, 88f ), 6, 1, false ) ),
		["ui/w1128_ammo_counter"] = new( "ui/w1128_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.SegmentedBar, new( new( 4f, 25.6f, 248f, 64f ), 8, 1, false ) ),
		["ui/w1128_upper_ammo_counter"] = new( "ui/w1128_upper_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.ContinuousBar, new( new( 8f, 8f, 240f, 240f ), 0, 1, true ) ),
		["ui/epg_ammo_counter"] = new( "ui/epg_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.DualBar, new( new( 12.8f, 25.6f, 232f, 64f ), 4, 1, false ), new( new( 12f, 140f, 104f, 104f ), 4, 1, false ) ),
		["ui/kraber_ammo_counter"] = new( "ui/kraber_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.SegmentedBar, new( new( 8f, 10f, 240f, 165f ), 4, 1, false ) ),
		["ui/smr_ammo_counter"] = new( "ui/smr_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.ContinuousBar, new( new( 8f, 16f, 236f, 224f ), 0, 1, false ) ),
		["ui/softball_ammo_counter"] = new( "ui/softball_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.SegmentedBar, new( new( 6f, 25.6f, 240f, 115.2f ), 6, 1, false ) ),
		["ui/mastiff_ammo_counter"] = new( "ui/mastiff_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.SegmentedBar, new( new( 8f, 12f, 240f, 228f ), 4, 1, false ) ),
		["ui/sa3_ammo_counter"] = new( "ui/sa3_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.SegmentedBar, new( new( 12.8f, 25.6f, 102.4f, 76.8f ), 4, 1, false ) ),
		["ui/aog_ammo_counter"] = new( "ui/aog_ammo_counter", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.ContinuousBar, new( new( 12f, 32f, 232f, 192f ), 0, 1, false ) ),
		["ui/aog_ammo_counter_lstar"] = new( "ui/aog_ammo_counter_lstar", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.HeatScreen, new( new( 12f, 32f, 232f, 192f ), 0, 1, false ) ),

		// Specialized heat / charge screens
		["ui/lstar_screens"] = new( "ui/lstar_screens", new( 100, 100 ), 9, 28, new( 50f, 44f ), new( 0.5f, 0.5f ), "retail-catalog",
			Titanfall2WeaponUiDisplayMode.HeatScreen, new( new( 12f, 22f, 76f, 56f ), 0, 1, false ), new( new( 2f, 87f, 96f, 6f ), 0, 1, false ) ),
		["ui/pulse_lmg_panel1"] = new( "ui/pulse_lmg_panel1", new( 256, 256 ), 0, 0, default, default, "retail-catalog",
			Titanfall2WeaponUiDisplayMode.ChargeStack, new( new( 5.1f, 10.2f, 102.4f, 236f ), 9, 1, true ), new( new( 134f, 10.2f, 114f, 236f ), 4, 1, true ) )
	};

	internal static bool IsKnownDisplay( string uiPath ) => !string.IsNullOrWhiteSpace( uiPath ) && Layouts.ContainsKey( uiPath );
	internal static bool TryGet( string uiPath, out Titanfall2WeaponUiLayout layout )
	{
		layout = null;
		return !string.IsNullOrWhiteSpace( uiPath ) && Layouts.TryGetValue( uiPath, out layout );
	}
	internal static IReadOnlyCollection<string> KnownPaths => Layouts.Keys;
}

internal static class RpakWeaponUiLayoutReader
{
	static readonly int[] DrawRecordSizes = [18, 42, 26];

	internal static bool TryReadName( RpakArchive archive, RpakAsset asset, out string name )
	{
		name = null;
		if ( asset.Type != "ui" || asset.Version != 30 || !archive.TryReadAsset( asset, out var data, out _ ) || data.Header.Length != 104 ) return false;
		var pointer = new RpakPagePointer( BitConverter.ToInt32( data.Header, 0 ), BitConverter.ToInt32( data.Header, 4 ) );
		if ( !archive.TryReadPagePointer( pointer, 128, out var names, out _ ) ) return false;
		var end = Array.IndexOf( names, (byte)0 );
		if ( end <= 0 || names.AsSpan( 0, end ).ContainsAnyExceptInRange( (byte)32, (byte)126 ) ) return false;
		name = "ui/" + Encoding.ASCII.GetString( names, 0, end ); return true;
	}

	internal static bool TryRead( RpakArchive archive, RpakAsset asset, out Titanfall2WeaponUiLayout layout )
	{
		layout = null;
		if ( asset.Type != "ui" || asset.Version != 30 || !archive.TryReadAsset( asset, out var data, out _ ) || data.Header.Length != 104 ) return false;
		var header = data.Header;
		RpakPagePointer Pointer( int offset ) => new( BitConverter.ToInt32( header, offset ), BitConverter.ToInt32( header, offset + 4 ) );
		if ( !archive.TryReadPagePointer( Pointer( 0 ), 128, out var names, out _ ) ) return false;
		var end = Array.IndexOf( names, (byte)0 );
		if ( end <= 0 || names.AsSpan( 0, end ).ContainsAnyExceptInRange( (byte)32, (byte)126 ) ) return false;
		var name = Encoding.ASCII.GetString( names, 0, end );
		var fullName = "ui/" + name;
		if ( !name.Contains( "ammo_counter", StringComparison.Ordinal ) && !Titanfall2WeaponUiCatalog.IsKnownDisplay( fullName ) ) return false;
		var canvas = new Vector2( BitConverter.ToSingle( header, 24 ), BitConverter.ToSingle( header, 28 ) );
		if ( !float.IsFinite( canvas.x ) || !float.IsFinite( canvas.y ) || canvas.x < 1 || canvas.y < 1 || canvas.x > 4096 || canvas.y > 4096
			|| MathF.Abs( BitConverter.ToSingle( header, 32 ) * canvas.x - 1 ) > 0.001f
			|| MathF.Abs( BitConverter.ToSingle( header, 36 ) * canvas.y - 1 ) > 0.001f ) return false;

		var initialBytes = BitConverter.ToUInt16( header, 70 );
		var styleCount = BitConverter.ToUInt16( header, 72 );
		var drawCount = BitConverter.ToUInt16( header, 76 );
		if ( initialBytes > 32768 || !archive.TryReadPagePointer( Pointer( 8 ), initialBytes, out var defaults, out _ ) ) return false;
		var drawBytes = Math.Max( 18, Math.Min( 4096, drawCount * 42 ) );
		if ( !archive.TryReadPagePointer( Pointer( 88 ), drawBytes, out var draws, out _ ) ) return false;
		var stylesPointer = Pointer( 80 );
		var bytecode = Pointer( 16 );
		byte[] program = null;
		if ( bytecode.Index == stylesPointer.Index && stylesPointer.Offset > bytecode.Offset && stylesPointer.Offset - bytecode.Offset <= 65536 )
			archive.TryReadPagePointer( bytecode, stylesPointer.Offset - bytecode.Offset, out program, out _ );

		// Scan all draw records to locate the first text record (kind == 0).
		var drawOffset = 0;
		for ( var i = 0; i < drawCount && drawOffset + 4 <= draws.Length; ++i )
		{
			var kind = BitConverter.ToUInt16( draws, drawOffset );
			if ( kind >= DrawRecordSizes.Length || drawOffset + DrawRecordSizes[kind] > draws.Length ) break;
			if ( kind == 0 )
			{
				var frame = BitConverter.ToUInt16( draws, drawOffset + 2 );
				var styleIdx = draws[drawOffset + 4];
				var stylePtr = stylesPointer with { Offset = stylesPointer.Offset + styleIdx * 52 };
				if ( styleIdx < styleCount && archive.TryReadPagePointer( stylePtr, 52, out var style, out _ ) )
				{
					var sizeOffset = BitConverter.ToUInt16( style, 40 );
					if ( sizeOffset + 4 <= defaults.Length )
					{
						var fontSize = BitConverter.ToSingle( defaults, sizeOffset );
						if ( float.IsFinite( fontSize ) && fontSize is > 0 and <= 2048
							&& program is not null && TryTextFrame( program, defaults, frame, out var anchor, out var pivot ) )
						{
							var catalogBar = Titanfall2WeaponUiCatalog.TryGet( fullName, out var cat ) ? cat : null;
							layout = new( fullName, canvas, BitConverter.ToUInt16( style, 30 ), fontSize, anchor * canvas, pivot, archive.FilePath,
								catalogBar?.Mode ?? Titanfall2WeaponUiDisplayMode.Numeric, catalogBar?.PrimaryBar, catalogBar?.SecondaryBar, catalogBar?.SecondaryTextAnchor );
							return true;
						}
					}
				}
			}
			drawOffset += DrawRecordSizes[kind];
		}

		// Bar/image-only retail ammo displays have no text record; use the verified static layout.
		if ( Titanfall2WeaponUiCatalog.TryGet( fullName, out var fallback ) )
		{
			layout = fallback with { CanvasSize = canvas, Source = archive.FilePath };
			return true;
		}
		return false;
	}

	// The retail layout VM starts derived frames at index 3. Decode opcodes 4 (anchored text),
	// 7 (two-point quad), and 11 (three-point quad) to resolve root-relative text frames.
	internal static bool TryTextFrame( byte[] program, byte[] defaults, int target, out Vector2 anchor, out Vector2 pivot )
	{
		anchor = pivot = default;
		var cursor = 0;
		var frame = 3;
		while ( cursor + 2 <= program.Length )
		{
			var opcode = program[cursor++];
			var count = program[cursor++];
			var stride = opcode switch { 0 => 4, 1 => 6, 2 => 8, 3 => 8, 4 => 10, 5 => 12, 6 => 16, 7 => 20, 11 => 30, _ => 0 };
			if ( stride == 0 || count == 0 || cursor + count * stride > program.Length ) return false;
			for ( var i = 0; i < count; i++, frame++, cursor += stride )
			{
				if ( frame != target ) continue;
				if ( opcode != 4 || BitConverter.ToUInt16( program, cursor ) != 0 ) return false;
				var values = new float[4];
				for ( var j = 0; j < 4; j++ )
				{
					var offset = BitConverter.ToUInt16( program, cursor + 2 + j * 2 );
					if ( offset + 4 > defaults.Length ) return false;
					values[j] = BitConverter.ToSingle( defaults, offset );
					if ( !float.IsFinite( values[j] ) ) return false;
				}
				anchor = new( values[0], values[1] );
				pivot = new( values[2], values[3] );
				return true;
			}
		}
		return false;
	}
}
