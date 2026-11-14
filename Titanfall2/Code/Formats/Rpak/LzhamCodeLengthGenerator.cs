/// <summary>
/// Code-length generation for the quasi-adaptive models, ported from
/// lzhamdecomp/lzham_huffman_codes.cpp and lzhamdecomp/lzham_polar_codes.cpp.
/// The scratch buffers are owned by one generator instance, which is owned by one decoder
/// instance, so concurrent decodes never share state.
/// </summary>
sealed class LzhamCodeLengthGenerator
{
	public const int MaxSupportedSyms = 1024;

	const int MaxEverCodeSize = 34;

	struct SymbolFrequency
	{
		public uint Frequency;
		public ushort Symbol;
	}

	readonly SymbolFrequency[] _syms0 = new SymbolFrequency[MaxSupportedSyms + 1 + MaxSupportedSyms];
	readonly SymbolFrequency[] _syms1 = new SymbolFrequency[MaxSupportedSyms + 1 + MaxSupportedSyms];
	readonly int[] _minimumRedundancy = new int[MaxSupportedSyms];
	readonly int[] _polarFrequency = new int[MaxSupportedSyms];
	readonly int[] _histogram = new int[512];
	readonly int[] _offsets = new int[256];
	readonly int[] _numCodes = new int[MaxEverCodeSize + 1];
	readonly int[] _nextSortedOfs = new int[MaxEverCodeSize + 1];
	readonly byte[] _newCodeSizes = new byte[MaxSupportedSyms];

	public bool GenerateHuffmanCodes( int numSyms, ushort[] frequencies, ushort[] codeSizes, out int maxCodeSize, out uint totalFreq )
	{
		if ( !CollectUsedSymbols( numSyms, frequencies, codeSizes, out var usedSyms, out totalFreq ) )
		{
			maxCodeSize = 0;
			return false;
		}

		if ( usedSyms == 1 )
		{
			codeSizes[_syms0[0].Symbol] = 1;
			maxCodeSize = 1;
			return true;
		}

		var syms = RadixSortSymbols( usedSyms );

		var x = _minimumRedundancy;
		for ( var i = 0; i < usedSyms; i++ )
			x[i] = (int)syms[i].Frequency;

		CalculateMinimumRedundancy( x, usedSyms );

		var maxLength = 0;
		for ( var i = 0; i < usedSyms; i++ )
		{
			var length = x[i];
			maxLength = Math.Max( length, maxLength );
			codeSizes[syms[i].Symbol] = (ushort)length;
		}

		maxCodeSize = maxLength;
		return true;
	}

	public bool GeneratePolarCodes( int numSyms, ushort[] frequencies, ushort[] codeSizes, out int maxCodeSize, out uint totalFreq )
	{
		if ( !CollectUsedSymbols( numSyms, frequencies, codeSizes, out var usedSyms, out totalFreq ) )
		{
			maxCodeSize = 0;
			return false;
		}

		if ( usedSyms == 1 )
		{
			codeSizes[_syms0[0].Symbol] = 1;
			maxCodeSize = 1;
			return true;
		}

		var syms = RadixSortSymbols( usedSyms );

		var maxCode = 0;
		var tmpFreq = _polarFrequency;

		uint origTotal = 0;
		uint curTotal = 0;
		for ( var i = 0; i < usedSyms; i++ )
		{
			var freq = syms[usedSyms - 1 - i].Frequency;
			origTotal += freq;

			var symLen = TotalBits( freq );
			var adjustedFreq = 1U << (int)(symLen - 1);
			tmpFreq[i] = (int)adjustedFreq;
			curTotal += adjustedFreq;
		}

		var treeTotal = 1U << (int)(TotalBits( origTotal ) - 1);
		if ( treeTotal < origTotal )
			treeTotal <<= 1;

		var startIndex = 0;
		while ( curTotal < treeTotal && startIndex < usedSyms )
		{
			for ( var i = startIndex; i < usedSyms; i++ )
			{
				var freq = (uint)tmpFreq[i];
				if ( curTotal + freq <= treeTotal )
				{
					tmpFreq[i] += (int)freq;
					curTotal += freq;
					if ( curTotal == treeTotal )
						break;
				}
				else
				{
					startIndex = i + 1;
				}
			}
		}

		if ( curTotal != treeTotal )
		{
			maxCodeSize = 0;
			return false;
		}

		var treeTotalBits = TotalBits( treeTotal );
		for ( var i = 0; i < usedSyms; i++ )
		{
			var codeSize = (int)(treeTotalBits - TotalBits( (uint)tmpFreq[i] ));
			maxCode = Math.Max( codeSize, maxCode );
			codeSizes[syms[usedSyms - 1 - i].Symbol] = (ushort)codeSize;
		}

		maxCodeSize = maxCode;
		return true;
	}

	/// <summary>Port of prefix_coding::limit_max_code_size (the LHArc tree adjustment).</summary>
	public bool LimitMaxCodeSize( int numSyms, ushort[] codeSizes, int maxCodeSize )
	{
		if ( numSyms == 0 || numSyms > MaxSupportedSyms || maxCodeSize < 1 || maxCodeSize > MaxEverCodeSize )
			return false;

		var numCodes = _numCodes;
		Array.Clear( numCodes );

		var shouldLimit = false;
		for ( var i = 0; i < numSyms; i++ )
		{
			var c = codeSizes[i];
			if ( c > MaxEverCodeSize )
				return false;

			numCodes[c]++;
			if ( c > maxCodeSize )
				shouldLimit = true;
		}

		if ( !shouldLimit )
			return true;

		var nextSortedOfs = _nextSortedOfs;
		var ofs = 0;
		for ( var i = 1; i <= MaxEverCodeSize; i++ )
		{
			nextSortedOfs[i] = ofs;
			ofs += numCodes[i];
		}

		if ( ofs < 2 || ofs > MaxSupportedSyms )
			return true;

		if ( ofs > 1 << maxCodeSize )
			return false;

		for ( var i = maxCodeSize + 1; i <= MaxEverCodeSize; i++ )
			numCodes[maxCodeSize] += numCodes[i];

		var total = 0;
		for ( var i = maxCodeSize; i != 0; --i )
			total += numCodes[i] << (maxCodeSize - i);

		if ( total == 1 << maxCodeSize )
			return true;

		int adjustedIndex;
		do
		{
			numCodes[maxCodeSize]--;

			for ( adjustedIndex = maxCodeSize - 1; adjustedIndex != 0; --adjustedIndex )
			{
				if ( numCodes[adjustedIndex] == 0 )
					continue;

				numCodes[adjustedIndex]--;
				numCodes[adjustedIndex + 1] += 2;
				break;
			}

			if ( adjustedIndex == 0 )
				return false;

			total--;
		}
		while ( total != 1 << maxCodeSize );

		var newCodeSizes = _newCodeSizes;
		var position = 0;
		for ( var i = 1; i <= maxCodeSize; i++ )
		{
			var n = numCodes[i];
			for ( var j = 0; j < n; j++ )
				newCodeSizes[position++] = (byte)i;
		}

		for ( var i = 0; i < numSyms; i++ )
		{
			var c = codeSizes[i];
			if ( c == 0 )
				continue;

			var nextOfs = nextSortedOfs[c]++;
			codeSizes[i] = newCodeSizes[nextOfs];
		}

		return true;
	}

	bool CollectUsedSymbols( int numSyms, ushort[] frequencies, ushort[] codeSizes, out int usedSyms, out uint totalFreq )
	{
		usedSyms = 0;
		totalFreq = 0;
		if ( numSyms == 0 || numSyms > MaxSupportedSyms )
			return false;

		for ( var i = 0; i < numSyms; i++ )
		{
			var freq = frequencies[i];
			if ( freq == 0 )
			{
				codeSizes[i] = 0;
				continue;
			}

			totalFreq += freq;
			_syms0[usedSyms].Frequency = freq;
			_syms0[usedSyms].Symbol = (ushort)i;
			usedSyms++;
		}

		return usedSyms != 0;
	}

	/// <summary>Port of lzham_huffman_codes.cpp radix_sort_syms (stable, descending frequency order reversed).</summary>
	SymbolFrequency[] RadixSortSymbols( int numSyms )
	{
		const int MaxPasses = 2;
		var hist = _histogram;
		Array.Clear( hist );

		var pairEnd = (numSyms >> 1) * 2;
		for ( var p = 0; p != pairEnd; p += 2 )
		{
			var freq0 = _syms0[p].Frequency;
			var freq1 = _syms0[p + 1].Frequency;

			hist[freq0 & 0xFF]++;
			hist[256 + ((freq0 >> 8) & 0xFF)]++;

			hist[freq1 & 0xFF]++;
			hist[256 + ((freq1 >> 8) & 0xFF)]++;
		}

		if ( (numSyms & 1) != 0 )
		{
			var freq = _syms0[pairEnd].Frequency;
			hist[freq & 0xFF]++;
			hist[256 + ((freq >> 8) & 0xFF)]++;
		}

		var curSyms = _syms0;
		var newSyms = _syms1;

		var totalPasses = hist[256] == numSyms ? 1 : MaxPasses;
		for ( var pass = 0; pass < totalPasses; pass++ )
		{
			var histogram = pass << 8;
			var offsets = _offsets;

			var curOfs = 0;
			for ( var i = 0; i < 256; i += 2 )
			{
				offsets[i] = curOfs;
				curOfs += hist[histogram + i];

				offsets[i + 1] = curOfs;
				curOfs += hist[histogram + i + 1];
			}

			var passShift = pass << 3;

			for ( var p = 0; p != pairEnd; p += 2 )
			{
				var c0 = curSyms[p].Frequency;
				var c1 = curSyms[p + 1].Frequency;

				if ( pass != 0 )
				{
					c0 >>= 8;
					c1 >>= 8;
				}

				c0 &= 0xFF;
				c1 &= 0xFF;

				if ( c0 == c1 )
				{
					var dstOffset0 = offsets[c0];
					offsets[c0] = dstOffset0 + 2;

					newSyms[dstOffset0] = curSyms[p];
					newSyms[dstOffset0 + 1] = curSyms[p + 1];
				}
				else
				{
					var dstOffset0 = offsets[c0]++;
					var dstOffset1 = offsets[c1]++;

					newSyms[dstOffset0] = curSyms[p];
					newSyms[dstOffset1] = curSyms[p + 1];
				}
			}

			if ( (numSyms & 1) != 0 )
			{
				var c = (curSyms[pairEnd].Frequency >> passShift) & 0xFF;

				var dstOffset = offsets[c];
				offsets[c] = dstOffset + 1;

				newSyms[dstOffset] = curSyms[pairEnd];
			}

			var swap = curSyms;
			curSyms = newSyms;
			newSyms = swap;
		}

		return curSyms;
	}

	/// <summary>
	/// calculate_minimum_redundancy() written by Alistair Moffat and Jyrki Katajainen, November 1996.
	/// Reproduces the exact code lengths the native decoder generates, so table updates stay in sync.
	/// </summary>
	static void CalculateMinimumRedundancy( int[] a, int n )
	{
		if ( n == 0 ) return;
		if ( n == 1 )
		{
			a[0] = 0;
			return;
		}

		a[0] += a[1];
		int root = 0;
		int leaf = 2;
		for ( var next = 1; next < n - 1; next++ )
		{
			if ( leaf >= n || a[root] < a[leaf] )
			{
				a[next] = a[root];
				a[root++] = next;
			}
			else
			{
				a[next] = a[leaf++];
			}

			if ( leaf >= n || (root < next && a[root] < a[leaf]) )
			{
				a[next] += a[root];
				a[root++] = next;
			}
			else
			{
				a[next] += a[leaf++];
			}
		}

		a[n - 2] = 0;
		for ( var next = n - 3; next >= 0; next-- )
			a[next] = a[a[next]] + 1;

		var avbl = 1;
		var used = 0;
		var dpth = 0;
		root = n - 2;
		var nextIndex = n - 1;
		while ( avbl > 0 )
		{
			while ( root >= 0 && a[root] == dpth )
			{
				used++;
				root--;
			}

			while ( avbl > used )
			{
				a[nextIndex--] = dpth;
				avbl--;
			}

			avbl = 2 * used;
			dpth++;
			used = 0;
		}
	}

	public static uint TotalBits( uint v )
	{
		uint bits = 0;
		while ( v > 0 )
		{
			v >>= 1;
			bits++;
		}

		return bits;
	}
}
