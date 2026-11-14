/// <summary>Copies packed POD records through the engine's validated ByteStream API.</summary>
static class Titanfall2Binary
{
	internal static T[] ReadArray<T>( ReadOnlySpan<byte> bytes ) where T : unmanaged
	{
		if ( bytes.IsEmpty ) return [];
		var size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
		if ( bytes.Length % size != 0 ) throw new InvalidDataException( "Truncated packed record array." );
		var values = new T[bytes.Length / size];
		using var stream = ByteStream.CreateReader( bytes );
		for ( var i = 0; i < values.Length; ++i ) values[i] = stream.Read<T>();
		return values;
	}

	internal static byte[] WriteArray<T>( T[] values ) where T : unmanaged
	{
		// Optional BSP/model streams legitimately contain no records. ByteStream
		// rejects a zero-sized allocation; the cache format still writes count=0.
		if ( values.Length == 0 ) return [];
		using var stream = ByteStream.Create( checked(values.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>()) );
		stream.WriteArray( values, includeCount: false );
		return stream.ToArray();
	}
}
