using System.Text;

/// <summary>Verified static fields of the retail RUI v30 header and first text style.</summary>
internal sealed record Titanfall2WeaponUiLayout( string Name, Vector2 CanvasSize, int FontId, float FontSize,
	Vector2 TextAnchor, Vector2 TextPivot, string Source );

internal static class RpakWeaponUiLayoutReader
{
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
		if ( !name.Contains( "ammo_counter", StringComparison.Ordinal ) ) return false;
		var canvas = new Vector2( BitConverter.ToSingle( header, 24 ), BitConverter.ToSingle( header, 28 ) );
		if ( !float.IsFinite( canvas.x ) || !float.IsFinite( canvas.y ) || canvas.x < 1 || canvas.y < 1 || canvas.x > 4096 || canvas.y > 4096
			|| MathF.Abs( BitConverter.ToSingle( header, 32 ) * canvas.x - 1 ) > 0.001f
			|| MathF.Abs( BitConverter.ToSingle( header, 36 ) * canvas.y - 1 ) > 0.001f ) return false;
		// Text record zero selects four style records using bytes 4..7. Each
		// style is 52 bytes; +30 is the native font ID and +40 is the parameter
		// byte offset for its font size (engine.dll text callback 0x1800F6980).
		if ( !archive.TryReadPagePointer( Pointer( 88 ), 18, out var text, out _ ) || BitConverter.ToUInt16( text, 0 ) != 0 ) return false;
		var stylePointer = Pointer( 80 );
		stylePointer = stylePointer with { Offset = stylePointer.Offset + text[4] * 52 };
		if ( !archive.TryReadPagePointer( stylePointer, 52, out var style, out _ ) ) return false;
		var initialBytes = BitConverter.ToUInt16( header, 70 );
		var sizeOffset = BitConverter.ToUInt16( style, 40 );
		if ( initialBytes > 32768 || sizeOffset + 4 > initialBytes
			|| !archive.TryReadPagePointer( Pointer( 8 ), initialBytes, out var defaults, out _ ) ) return false;
		var fontSize = BitConverter.ToSingle( defaults, sizeOffset );
		if ( !float.IsFinite( fontSize ) || fontSize <= 0 || fontSize > 2048 ) return false;
		var bytecode = Pointer( 16 );
		var styles = Pointer( 80 );
		if ( bytecode.Index != styles.Index || styles.Offset <= bytecode.Offset || styles.Offset - bytecode.Offset > 65536
			|| !archive.TryReadPagePointer( bytecode, styles.Offset - bytecode.Offset, out var program, out _ )
			|| !TryTextFrame( program, defaults, BitConverter.ToUInt16( text, 2 ), out var anchor, out var pivot ) ) return false;
		layout = new( "ui/" + name, canvas, BitConverter.ToUInt16( style, 30 ), fontSize, anchor * canvas, pivot, archive.FilePath );
		return true;
	}

	// The retail layout VM starts derived frames at index 3. The lower R-201
	// screen's text is frame 3; CAR's is frame 6 after three opcode-7 quads.
	// Decode only proven root-relative text frames, rejecting other programs.
	internal static bool TryTextFrame( byte[] program, byte[] defaults, int target, out Vector2 anchor, out Vector2 pivot )
	{
		anchor = pivot = default;
		var cursor = 0;
		var frame = 3;
		while ( cursor + 2 <= program.Length )
		{
			var opcode = program[cursor++];
			var count = program[cursor++];
			var stride = opcode switch { 4 => 10, 7 => 20, _ => 0 };
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

public partial class Titanfall2Mount
{
	Dictionary<string, Titanfall2WeaponUiLayout> _weaponUiLayouts;
	string _weaponUiRevision;
	internal void ClearWeaponUiLayouts() { _weaponUiLayouts = null; _weaponUiRevision = null; }
	internal Titanfall2WeaponUiLayout GetWeaponUiLayout( string name )
	{
		if ( _contentSnapshot is null || string.IsNullOrWhiteSpace( name ) ) return null;
		if ( _weaponUiRevision != _contentSnapshot.Revision ) { ClearWeaponUiLayouts(); _weaponUiRevision = _contentSnapshot.Revision; }
		return _weaponUiLayouts?.GetValueOrDefault( name );
	}

	static Titanfall2WeaponUiLayout ReadWeaponUiLayout( Titanfall2ContentSnapshot snapshot, string name, System.Threading.CancellationToken cancellation )
	{
		ulong? matchedGuid = null;
		foreach ( var entry in snapshot.Entries.Where( x => x.Kind == "ui" && x.Archive is not null ) )
		{
			cancellation.ThrowIfCancellationRequested();
			if ( matchedGuid.HasValue && entry.Asset.Guid != matchedGuid.Value ) continue;
			if ( !RpakWeaponUiLayoutReader.TryReadName( entry.Archive, entry.Asset, out var candidate )
				|| !string.Equals( candidate, name, StringComparison.OrdinalIgnoreCase ) ) continue;
			matchedGuid = entry.Asset.Guid;
			// Only parse the requested layout; a patch can refer to absent inherited
			// pages, so continue through the same GUID until the first complete copy.
			if ( RpakWeaponUiLayoutReader.TryRead( entry.Archive, entry.Asset, out var layout ) ) return layout;
		}
		return null;
	}

	void PublishWeaponUiLayout( Titanfall2WeaponUiLayout layout )
	{
		if ( _weaponUiRevision != _contentSnapshot.Revision ) { ClearWeaponUiLayouts(); _weaponUiRevision = _contentSnapshot.Revision; }
		_weaponUiLayouts ??= new( StringComparer.OrdinalIgnoreCase );
		_weaponUiLayouts[layout.Name] = layout;
	}
}
