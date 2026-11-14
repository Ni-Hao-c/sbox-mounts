using System.Runtime.CompilerServices;

/// <summary>
/// Adaptive binary arithmetic model used by the LZHAM LZ stream, ported from
/// lzhamdecomp/lzham_symbol_codec.h (adaptive_bit_model).
/// </summary>
struct LzhamArithBitModel
{
	public const int ProbabilityBits = 11;
	public const int ProbabilityScale = 1 << ProbabilityBits;
	public const int ProbabilityMoveBits = 5;
	public const ushort InitialProbability0 = 1 << (ProbabilityBits - 1);

	public ushort Bit0Prob;

	public void Clear() => Bit0Prob = InitialProbability0;
}

/// <summary>
/// Bit-buffer, arithmetic decoder and adaptive-symbol decoder ported from
/// lzhamdecomp/lzham_symbol_codec.h/.cpp. It consumes one complete input buffer; reading past
/// the end behaves exactly like the native decoder with the end-of-input flag set (zero bytes),
/// which keeps truncated streams from throwing or reading out of bounds.
/// </summary>
sealed class LzhamSymbolCodec
{
	public const uint ArithMinLen = 0x01000000;
	public const uint ArithMaxLen = 0xFFFFFFFF;
	public const uint ArithProbBits = 11;
	public const uint ArithProbScale = 1 << (int)ArithProbBits;
	public const uint ArithProbMoveBits = 5;

	const int BitBufSize = 64;
	const int HuffmanFillTarget = BitBufSize - 8;

	readonly byte[] _source;
	readonly int _sourceLength;
	int _next;

	ulong _bitBuf;
	int _bitCount;

	uint _arithValue;
	uint _arithLength;

	public LzhamSymbolCodec( byte[] source )
	{
		_source = source;
		_sourceLength = source.Length;
	}

	/// <summary>Bytes consumed from the input, including bytes buffered in the bit accumulator.</summary>
	public int Next => _next;

	public void StartDecoding()
	{
		_next = 0;
		_bitBuf = 0;
		_bitCount = 0;
		_arithValue = 0;
		_arithLength = 0;
	}

	public void AdvanceInput( int count ) => _next += count;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public uint GetBits( int numBits )
	{
		if ( numBits == 0 )
			return 0;

		while ( _bitCount < numBits )
		{
			uint next = 0;
			if ( _next < _sourceLength )
				next = _source[_next++];

			_bitCount += 8;
			_bitBuf |= (ulong)next << (BitBufSize - _bitCount);
		}

		var result = (uint)(_bitBuf >> (BitBufSize - numBits));
		_bitBuf <<= numBits;
		_bitCount -= numBits;
		return result;
	}

	public void AlignToByte()
	{
		var bits = _bitCount & 7;
		if ( bits != 0 )
			GetBits( bits );
	}

	public int RemoveByteFromBitBuffer()
	{
		if ( _bitCount < 8 )
			return -1;

		var result = (int)(_bitBuf >> (BitBufSize - 8));
		_bitBuf <<= 8;
		_bitCount -= 8;
		return result;
	}

	public void StartArithmeticDecoding()
	{
		_arithLength = ArithMaxLen;
		_arithValue = GetBits( 8 ) << 24;
		_arithValue |= GetBits( 8 ) << 16;
		_arithValue |= GetBits( 8 ) << 8;
		_arithValue |= GetBits( 8 );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public uint DecodeBit( ref LzhamArithBitModel model )
	{
		while ( _arithLength < ArithMinLen )
		{
			var c = GetBits( 8 );
			_arithValue = (_arithValue << 8) | c;
			_arithLength <<= 8;
		}

		var x = (uint)model.Bit0Prob * (_arithLength >> (int)ArithProbBits);
		if ( _arithValue >= x )
		{
			model.Bit0Prob = (ushort)(model.Bit0Prob - (model.Bit0Prob >> LzhamArithBitModel.ProbabilityMoveBits));
			_arithValue -= x;
			_arithLength -= x;
			return 1;
		}

		model.Bit0Prob = (ushort)(model.Bit0Prob + ((LzhamArithBitModel.ProbabilityScale - model.Bit0Prob) >> LzhamArithBitModel.ProbabilityMoveBits));
		_arithLength = x;
		return 0;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public uint DecodeSymbol( LzhamAdaptiveModel model )
	{
		while ( _bitCount < HuffmanFillTarget )
		{
			uint next = 0;
			if ( _next < _sourceLength )
				next = _source[_next++];

			_bitCount += 8;
			_bitBuf |= (ulong)next << (BitBufSize - _bitCount);
		}

		var tables = model.Tables;
		var k = (uint)(_bitBuf >> (BitBufSize - 16)) + 1;

		uint symbol;
		uint length;
		if ( k <= tables.TableMaxCode )
		{
			var entry = tables.Lookup[_bitBuf >> (BitBufSize - tables.TableBits)];
			symbol = entry & 0xFFFF;
			length = entry >> 16;
		}
		else
		{
			length = (uint)tables.DecodeStartCodeSize;
			if ( length >= 1 && length <= LzhamDecoderTables.MaxExpectedCodeSize + 1 )
			{
				for (; ; )
				{
					if ( k <= tables.MaxCodes[length - 1] )
						break;
					length++;
				}
			}
			else
			{
				throw new LzhamDecodeException( "LZHAM prefix-code table is invalid (corrupt stream)." );
			}

			var value = tables.ValPtrs[length - 1] + (int)(_bitBuf >> (BitBufSize - (int)length));
			if ( (uint)value >= (uint)model.TotalSyms )
				value = 0;
			symbol = tables.SortedSymbolOrder[value];
		}

		_bitBuf <<= (int)length;
		_bitCount -= (int)length;

		model.OnSymbolDecoded( (int)symbol );
		return symbol;
	}
}
