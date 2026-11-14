/// <summary>
/// Prefix code decode tables ported from lzhamdecomp/lzham_prefix_coding.h and
/// lzham_prefix_coding.cpp. One instance backs each adaptive model and is rebuilt whenever
/// the model's Huffman/polar code lengths change.
/// </summary>
sealed class LzhamDecoderTables
{
	public const int MaxExpectedCodeSize = 16;
	public const int MaxTableBits = 11;

	// DO NOT reorder: the decode paths index these exactly like the native struct.
	public int NumSyms;
	public int TotalUsedSyms;
	public int TableBits;
	public uint TableMaxCode;
	public int DecodeStartCodeSize;

	public int MinCodeSize;
	public int MaxCodeSize;

	public readonly uint[] MaxCodes = new uint[MaxExpectedCodeSize + 1];
	public readonly int[] ValPtrs = new int[MaxExpectedCodeSize + 1];

	public uint[] Lookup;
	public ushort[] SortedSymbolOrder;

	public void Init( int numSyms, int decoderTableBits )
	{
		NumSyms = numSyms;
		if ( SortedSymbolOrder is null || SortedSymbolOrder.Length < numSyms )
			SortedSymbolOrder = new ushort[numSyms];

		var lookupSize = decoderTableBits > 0 ? 1 << decoderTableBits : 0;
		if ( lookupSize > 0 && (Lookup is null || Lookup.Length < lookupSize) )
			Lookup = new uint[lookupSize];
	}

	public uint GetUnshiftedMaxCode( int length )
	{
		var k = MaxCodes[length - 1];
		if ( k == 0 ) return uint.MaxValue;
		return (k - 1) >> (16 - length);
	}
}
