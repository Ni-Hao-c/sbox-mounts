using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class PackedArrayTests
{
	[TestMethod]
	public void EmptyOptionalStreamsRoundTripWithoutAZeroSizedByteStream()
	{
		Assert.AreEqual( 0, Titanfall2Binary.WriteArray( Array.Empty<int>() ).Length );
		Assert.AreEqual( 0, Titanfall2Binary.ReadArray<int>( ReadOnlySpan<byte>.Empty ).Length );
		Assert.AreEqual( 0, Titanfall2Binary.WriteArray( Array.Empty<byte>() ).Length );
	}

	[TestMethod]
	public void PackedRecordsKeepTheirBytesAndRejectTruncation()
	{
		var source = new[] { int.MinValue, -1, 0, 42, int.MaxValue };
		var packed = Titanfall2Binary.WriteArray( source );
		Assert.AreEqual( source.Length * sizeof( int ), packed.Length );
		CollectionAssert.AreEqual( source, Titanfall2Binary.ReadArray<int>( packed ) );
		Assert.ThrowsException<InvalidDataException>( () => Titanfall2Binary.ReadArray<int>( packed.AsSpan( 0, packed.Length - 1 ) ) );
	}
}
