/// <summary>
/// Port of lzhamdecomp/lzham_prefix_coding.cpp. Builds the canonical prefix-code decode
/// tables used by <see cref="LzhamSymbolCodec"/>.
/// </summary>
static class LzhamPrefixCoding
{
	public static bool GenerateDecoderTables( int numSyms, ushort[] codeSizes, LzhamDecoderTables tables, int tableBits )
	{
		if ( numSyms == 0 || tableBits > LzhamDecoderTables.MaxTableBits )
			return false;

		tables.NumSyms = numSyms;

		var minCodes = new int[LzhamDecoderTables.MaxExpectedCodeSize];
		var numCodes = new int[LzhamDecoderTables.MaxExpectedCodeSize + 1];
		var sortedPositions = new int[LzhamDecoderTables.MaxExpectedCodeSize + 1];
		Array.Clear( numCodes );
		Array.Clear( sortedPositions );

		for ( var i = 0; i < numSyms; i++ )
			numCodes[codeSizes[i]]++;

		uint nextCode = 0;
		uint totalUsedSyms = 0;
		uint maxCodeSize = 0;
		var minCodeSize = int.MaxValue;

		// Canonical code assignment: symbols arrive sorted by (code size, symbol index).
		for ( var i = 1; i <= LzhamDecoderTables.MaxExpectedCodeSize; i++ )
		{
			var n = (uint)numCodes[i];
			if ( n == 0 )
			{
				tables.MaxCodes[i - 1] = 0;
			}
			else
			{
				minCodeSize = Math.Min( minCodeSize, i );
				maxCodeSize = Math.Max( maxCodeSize, (uint)i );

				minCodes[i - 1] = (int)nextCode;

				tables.MaxCodes[i - 1] = nextCode + n - 1;
				tables.MaxCodes[i - 1] = 1 + ((tables.MaxCodes[i - 1] << (16 - i)) | ((1U << (16 - i)) - 1));

				tables.ValPtrs[i - 1] = (int)totalUsedSyms;

				sortedPositions[i] = (int)totalUsedSyms;

				nextCode += n;
				totalUsedSyms += n;
			}

			nextCode <<= 1;
		}

		tables.TotalUsedSyms = (int)totalUsedSyms;

		tables.MinCodeSize = minCodeSize;
		tables.MaxCodeSize = (int)maxCodeSize;

		for ( var i = 0; i < numSyms; i++ )
		{
			var c = codeSizes[i];
			if ( c == 0 )
				continue;

			var sortedPos = sortedPositions[c]++;
			tables.SortedSymbolOrder[sortedPos] = (ushort)i;
		}

		if ( tableBits <= tables.MinCodeSize )
			tableBits = 0;
		tables.TableBits = tableBits;

		if ( tableBits != 0 )
		{
			var tableSize = 1 << tableBits;
			if ( tables.Lookup is null || tables.Lookup.Length < tableSize )
				tables.Lookup = new uint[tableSize];

			Array.Fill( tables.Lookup, uint.MaxValue, 0, tableSize );

			for ( var codeSize = 1; codeSize <= tableBits; codeSize++ )
			{
				if ( numCodes[codeSize] == 0 )
					continue;

				var fillSize = tableBits - codeSize;
				var fillNum = 1U << fillSize;

				var minCode = minCodes[codeSize - 1];
				var maxCode = tables.GetUnshiftedMaxCode( codeSize );
				var valPtr = tables.ValPtrs[codeSize - 1];

				for ( var code = minCode; code <= maxCode; code++ )
				{
					var symIndex = tables.SortedSymbolOrder[valPtr + code - minCode];

					for ( var j = 0; j < fillNum; j++ )
					{
						var t = (uint)j + ((uint)code << fillSize);
						tables.Lookup[t] = symIndex | ((uint)codeSize << 16);
					}
				}
			}
		}

		for ( var i = 0; i < LzhamDecoderTables.MaxExpectedCodeSize; i++ )
			tables.ValPtrs[i] -= minCodes[i];

		tables.TableMaxCode = 0;
		tables.DecodeStartCodeSize = tables.MinCodeSize;

		if ( tableBits != 0 )
		{
			var i = tableBits;
			for ( ; i >= 1; i-- )
			{
				if ( numCodes[i] != 0 )
				{
					tables.TableMaxCode = tables.MaxCodes[i - 1];
					break;
				}
			}

			if ( i >= 1 )
			{
				tables.DecodeStartCodeSize = tableBits + 1;
				for ( i = tableBits + 1; i <= maxCodeSize; i++ )
				{
					if ( numCodes[i] != 0 )
					{
						tables.DecodeStartCodeSize = i;
						break;
					}
				}
			}
		}

		// Sentinels: guarantee the decode loops terminate on corrupted streams.
		tables.MaxCodes[LzhamDecoderTables.MaxExpectedCodeSize] = uint.MaxValue;
		tables.ValPtrs[LzhamDecoderTables.MaxExpectedCodeSize] = 0xFFFFF;

		return true;
	}
}
