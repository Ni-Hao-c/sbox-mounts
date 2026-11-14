/// <summary>
/// Pure managed LZHAM decompressor for Titanfall 2 VPK chunks. It replaces the retired native
/// LZHAM bridge with a byte-exact C# port of the LZHAM Alpha8 decoder (the revision the shipped
/// VPK data was produced with). The profile matches what the game data uses: dictionary size
/// log2 = 20, unbuffered output, output length known up front.
///
/// This is safe to call concurrently from multiple loader threads: all decoder state lives in
/// the per-call <see cref="LzhamDecompressor"/> instance and no native code is involved.
/// </summary>
static class LzhamDecoder
{
	public static bool TryDecompress( byte[] source, int destinationLength, out byte[] destination, out string error )
	{
		return LzhamDecompressor.TryDecompress( source, destinationLength, out destination, out error );
	}
}
