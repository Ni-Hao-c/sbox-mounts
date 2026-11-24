using System.Buffers.Binary;
using System.Text;

// Miles v13: bank +0x60 is the exact event-name index, +0x68 the action data,
// +0x70 strings, +0x78 controller curves, +0x80 source-selection trees.
// Verified against retail mileswin64.dll (event lookup 0x180026A70,
// action walking 0x180021150, source selection 0x180027FC0).
// Keep only the index in memory; read and cache requested definitions on demand.
sealed class MilesSoundEventCatalog
{
	readonly Func<Stream> _open;
	readonly long _length;
	readonly int _actions, _strings, _curves, _nodes, _nodeEnd;
	readonly Dictionary<string, int> _index = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, (MilesSoundEvent Event, string Error)> _cache = new( StringComparer.OrdinalIgnoreCase );

	internal MilesSoundEventCatalog( byte[] bank, Func<Stream> open )
	{
		_open = open;
		_length = bank.Length;
		int Offset( int at )
		{
			var value = BinaryPrimitives.ReadUInt64LittleEndian( bank.AsSpan( at, 8 ) );
			if ( value > (ulong)bank.Length ) throw new InvalidDataException( "Miles event table is outside the bank." );
			return (int)value;
		}
		var index = Offset( 0x60 );
		_actions = Offset( 0x68 ); _strings = Offset( 0x70 ); _curves = Offset( 0x78 );
		_nodes = Offset( 0x80 ); _nodeEnd = Offset( 0x88 );
		var count = BinaryPrimitives.ReadUInt32LittleEndian( bank.AsSpan( 0xa8, 4 ) );
		if ( index < 0xc0 || _actions < index || _strings < _actions || _curves < _strings
			|| _nodes < _curves || _nodeEnd < _nodes || count > 100_000 || count * 8 != _actions - index )
			throw new InvalidDataException( "Invalid Miles v13 event tables." );
		for ( var i = 0; i < count; i++ )
		{
			var row = index + i * 8;
			var nameOffset = BinaryPrimitives.ReadUInt32LittleEndian( bank.AsSpan( row, 4 ) );
			var actionOffset = BinaryPrimitives.ReadUInt32LittleEndian( bank.AsSpan( row + 4, 4 ) );
			if ( nameOffset >= _curves - _strings || actionOffset >= _strings - _actions )
				throw new InvalidDataException( "Miles event index exceeds its table." );
			var start = _strings + (int)nameOffset;
			var end = Array.IndexOf( bank, (byte)0, start, Math.Min( 1024, _curves - start ) );
			if ( end < 0 ) throw new InvalidDataException( "Unterminated Miles event name." );
			_index[Encoding.UTF8.GetString( bank, start, end - start )] = _actions + (int)actionOffset;
		}
	}

	internal bool TryGet( string name, out MilesSoundEvent result, out string error )
	{
		result = null; error = null;
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		lock ( _cache )
		{
			if ( _cache.TryGetValue( name, out var cached ) ) { result = cached.Event; error = cached.Error; return result is not null; }
			if ( !_index.TryGetValue( name, out var address ) ) { error = "exact event not found"; return false; }
			try
			{
				using var stream = _open();
				var reader = new Reader( this, stream );
				var layers = new List<MilesSoundLayer>();
				for ( var i = 0; i < 64; i++ )
				{
					var header = reader.Bytes( address, 4, _actions, _strings );
					var type = header[0] & 15;
					var size = type == 10 ? 4 : header[1] * 4;
					if ( size < 4 ) throw new InvalidDataException( "Zero-sized Miles action." );
					var data = reader.Bytes( address, size, _actions, _strings );
					// Do not partially play an event with unknown commands.
					if ( type != 0 || size < 140 ) throw new InvalidDataException( $"Unsupported Miles action {type}." );
					var flags = U32( data, 136 );
					MilesSoundValue Value( int offset, uint flag ) => (flags & flag) != 0
						? new( 0, reader.Curve( U32( data, offset ) ) ) : new( F32( data, offset ), null );
					layers.Add( new( reader.Choice( U32( data, 60 ) ), Value( 92, 1 ), Value( 88, 4 ),
						Value( 84, 32 ), reader.Curve( U32( data, 40 ) ), F32( data, 24 ), F32( data, 8 ),
						F32( data, 12 ), F32( data, 16 ), U16( data, 56 ), F32( data, 4 ) ) );
					address += size;
					if ( (header[0] & 16) == 0 ) continue;
					result = new( name, layers.ToArray() );
					break;
				}
				if ( result is null ) throw new InvalidDataException( "Unterminated Miles event action list." );
			}
			catch ( Exception exception ) { error = exception.Message; }
			_cache[name] = (result, error);
			return result is not null;
		}
	}

	static uint U32( byte[] data, int at ) => BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( at, 4 ) );
	static ushort U16( byte[] data, int at ) => BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( at, 2 ) );
	static float F32( byte[] data, int at )
	{
		var value = BitConverter.Int32BitsToSingle( (int)U32( data, at ) );
		if ( !float.IsFinite( value ) ) throw new InvalidDataException( "Non-finite Miles sound setting." );
		return value;
	}

	sealed class Reader( MilesSoundEventCatalog owner, Stream stream )
	{
		readonly Dictionary<uint, MilesSoundCurve> _curves = [];
		int _visited;
		internal byte[] Bytes( long address, int count, int begin, int end )
		{
			if ( count < 0 || address < begin || address > end - count || address > owner._length - count )
				throw new InvalidDataException( "Miles sound definition exceeds its table." );
			var bytes = new byte[count];
			stream.Position = address; stream.ReadExactly( bytes );
			return bytes;
		}
		string String( uint offset )
		{
			var at = owner._strings + (long)offset;
			var data = Bytes( at, (int)Math.Min( 1024, owner._curves - at ), owner._strings, owner._curves );
			var end = Array.IndexOf( data, (byte)0 );
			if ( end < 0 ) throw new InvalidDataException( "Unterminated Miles source name." );
			return Encoding.UTF8.GetString( data, 0, end );
		}
		internal MilesSoundCurve Curve( uint offset )
		{
			if ( offset == uint.MaxValue ) return null;
			if ( _curves.TryGetValue( offset, out var curve ) ) return curve;
			var at = owner._curves + (long)offset;
			var header = Bytes( at, 8, owner._curves, owner._nodes );
			var count = header[0];
			if ( count is < 2 or > 64 ) throw new InvalidDataException( "Invalid Miles controller point count." );
			var data = Bytes( at, 8 + count * 4 + count * 20, owner._curves, owner._nodes );
			var points = Enumerable.Range( 0, count ).Select( i => F32( data, 8 + i * 4 ) ).ToArray();
			if ( points.Zip( points.Skip( 1 ), (a, b) => b < a ).Any( bad => bad ) )
				throw new InvalidDataException( "Unordered Miles controller points." );
			var segments = Enumerable.Range( 0, count ).Select( i => {
				var row = 8 + count * 4 + i * 20;
				return new MilesSoundCurve.Segment( F32( data, row ), F32( data, row + 4 ), F32( data, row + 8 ), F32( data, row + 12 ), data[row + 16] );
			} ).ToArray();
			return _curves[offset] = new( String( U32( header, 4 ) ), points, segments );
		}
		internal MilesSoundChoice Choice( uint baseOffset, uint offset = 0, int depth = 0 )
		{
			if ( depth >= 16 || ++_visited > 2048 ) throw new InvalidDataException( "Miles source tree limit exceeded." );
			var at = owner._nodes + (long)baseOffset + offset;
			var header = Bytes( at, 8, owner._nodes, owner._nodeEnd );
			if ( header[0] == 1 )
			{
				var data = Bytes( at, 40, owner._nodes, owner._nodeEnd );
				if ( data[4] != 0 || data[5] != 0 || data[6] != 0 || U32( data, 36 ) != uint.MaxValue )
					throw new InvalidDataException( "Unsupported Miles source cropping or name controller." );
				var name = String( U32( data, 32 ) );
				if ( string.IsNullOrWhiteSpace( name ) || name.Contains( ':' ) || name.Contains( ".." ) || name.Any( char.IsControl ) )
					throw new InvalidDataException( "Invalid Miles event source name." );
				return new( "sound/" + name.TrimStart( '/' ).Replace( '\\', '/' ), data[1], 0, [] );
			}
			if ( header[0] != 0 || header[3] == 0 ) throw new InvalidDataException( $"Unsupported Miles source node {header[0]}." );
			var children = Bytes( at + 8, header[3] * 4, owner._nodes, owner._nodeEnd );
			return new( null, header[1], header[2] & 15,
				Enumerable.Range( 0, header[3] ).Select( i => Choice( baseOffset, U32( children, i * 4 ), depth + 1 ) ).ToArray() );
		}
	}
}

sealed record MilesSoundEvent( string Name, MilesSoundLayer[] Layers );
sealed record MilesSoundLayer( MilesSoundChoice Choice, MilesSoundValue Volume, MilesSoundValue Pitch,
	MilesSoundValue Delay, MilesSoundCurve Distance, float Gain, float VolumeRandom, float PitchRandom, float DelayRandom,
	int Repeats, float Priority );
readonly record struct MilesSoundValue( float Constant, MilesSoundCurve Curve )
{
	internal float Evaluate( MilesSoundContext context ) => Curve?.Evaluate( context.Value( Curve.Controller ) ) ?? Constant;
}
readonly record struct MilesSoundContext( float Distance, float AmmoClipPercent )
{
	// Direction/occlusion routing remains a dry, on-axis approximation. Never use a
	// guessed event or sample when an environmental controller is unavailable.
	internal float Value( string name ) => name switch
	{
		"AutoDistance" or "ManualDistance" => Distance,
		"AmmoClipPct" => AmmoClipPercent,
		_ => 0
	};
}
sealed record MilesSoundChoice( string Source, int Weight, int Mode, MilesSoundChoice[] Children )
{
	internal IEnumerable<string> Sources => Source is not null ? [Source] : Children.SelectMany( child => child.Sources );
}

sealed record MilesSoundCurve( string Controller, float[] Points, MilesSoundCurve.Segment[] Segments )
{
	internal readonly record struct Segment( float Start, float End, float A, float B, int Shape );
	internal float Evaluate( float value )
	{
		if ( value <= Points[0] ) return Segments[0].Start;
		if ( value >= Points[^1] ) return Segments[^1].End;
		var i = 0;
		while ( i < Points.Length - 2 && value > Points[i + 1] ) i++;
		var s = Segments[i];
		var width = Points[i + 1] - Points[i];
		if ( width < 0.0000024f || s.Shape == 2 ) return s.Start;
		var t = Math.Clamp( (value - Points[i]) / width, 0, 1 );
		if ( s.Shape == 1 )
		{
			var m0 = MathF.Tan( s.A ) * width;
			var m1 = MathF.Tan( s.B ) * width;
			return (2*t*t*t - 3*t*t + 1) * s.Start + (t*t*t - 2*t*t + t) * m0
				+ (-2*t*t*t + 3*t*t) * s.End + (t*t*t - t*t) * m1;
		}
		if ( s.Shape != 0 )
		{
			var ranged = s.A != 0 || s.B != 0;
			t = Shape( s.Shape, ranged ? s.A + (s.B - s.A) * t : t );
			if ( ranged )
			{
				var start = Shape( s.Shape, s.A ); var end = Shape( s.Shape, s.B );
				t = MathF.Abs( end - start ) > 0.000001f ? (t - start) / (end - start) : 0;
			}
		}
		return s.Start + (s.End - s.Start) * t;
	}
	static float Shape( int kind, float t )
	{
		var reverse = (kind & 16) != 0;
		if ( reverse ) t = 1 - t;
		t = (kind & 15) switch
		{
			3 => t * t, 6 => t * t * t,
			4 => ((t <= 0.0001f ? -96f : MathF.Log( t ) * 14.476483f) + 96f) / 96f,
			5 => ((t <= 0.0001f ? -96f : 20 * MathF.Log10( t )) + 96f) / 96f,
			_ => t
		};
		t = Math.Clamp( t, 0, 1 );
		return reverse ? 1 - t : t;
	}
}
