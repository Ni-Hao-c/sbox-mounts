using System.Runtime.CompilerServices;

/// <summary>Raised for structurally invalid LZHAM streams; converted into a TryDecompress error.</summary>
sealed class LzhamDecodeException : Exception
{
	public LzhamDecodeException( string message ) : base( message )
	{
	}
}

/// <summary>
/// Pure managed port of lzhamdecomp/lzham_lzdecomp.cpp, configured exactly like the retired
/// native bridge: dictionary size log2 = 20, output unbuffered into a caller-sized buffer,
/// and the Alpha8 stream header (fast-updating / polar-code flags). No native code is used.
/// </summary>
sealed class LzhamDecompressor
{
	public const int DictionarySizeLog2 = 20;

	const int MinMatchLen = 2;
	const int MaxMatchLen = 257;
	const int LzxNumSecondaryLengths = 249;
	const int NumHugeMatchCodes = 1;
	const int LzxNumSpecialLengths = 2;
	const int LzxLowestUsableMatchSlot = 1;
	const int LzxMaxPositionSlots = 128;

	const int NumStates = 12;
	const int NumLitStates = 7;

	const int NumLitPredBits = 6;
	const int NumDeltaLitPredBits = 6;
	const int NumIsMatchContextBits = 6;
	const int NumLitTables = 1 << NumLitPredBits;

	const int SyncBlock = 0;
	const int CompBlock = 1;
	const int RawBlock = 2;
	const int EofBlock = 3;

	const int BlockFlushTypeBits = 2;
	const int LzxSpecialCodeEndOfBlockCode = 0;

	// Immutable lookup tables (static readonly arrays are never mutated, so they are thread safe).
	static readonly byte[] s_literalNextState =
	{
		0, 0, 0, 0, 1, 2, 3,  // 0-6: literal states
		4, 5, 6, 4, 5,        // 7-11: match states
		7, 7, 7, 7, 7, 7, 7, 10, 10, 10, 10, 10  // 12-23: unused
	};

	static readonly uint[] s_hugeMatchBaseLen = { 258, 514, 1538, 5634 };
	static readonly byte[] s_hugeMatchCodeLen = { 8, 10, 12, 16 };

	readonly byte[] _source;
	readonly int _sourceLength;
	readonly int _destinationLength;
	byte[] _destination;

	readonly uint[] _positionBase = new uint[LzxMaxPositionSlots];
	readonly byte[] _positionExtraBits = new byte[LzxMaxPositionSlots];
	int _numLzxSlots;

	readonly LzhamSymbolCodec _codec;
	readonly LzhamCodeLengthGenerator _generator = new();
	readonly LzhamAdaptiveModel[] _litTables = new LzhamAdaptiveModel[NumLitTables];
	readonly LzhamAdaptiveModel[] _deltaLitTables = new LzhamAdaptiveModel[NumLitTables];
	readonly LzhamAdaptiveModel _mainTable;
	readonly LzhamAdaptiveModel[] _repLenTables = new LzhamAdaptiveModel[2];
	readonly LzhamAdaptiveModel[] _largeLenTables = new LzhamAdaptiveModel[2];
	readonly LzhamAdaptiveModel _distLsbTable;

	readonly LzhamArithBitModel[] _isMatchModels = new LzhamArithBitModel[NumStates << NumIsMatchContextBits];
	readonly LzhamArithBitModel[] _isRepModels = new LzhamArithBitModel[NumStates];
	readonly LzhamArithBitModel[] _isRep0Models = new LzhamArithBitModel[NumStates];
	readonly LzhamArithBitModel[] _isRep0SingleByteModels = new LzhamArithBitModel[NumStates];
	readonly LzhamArithBitModel[] _isRep1Models = new LzhamArithBitModel[NumStates];
	readonly LzhamArithBitModel[] _isRep2Models = new LzhamArithBitModel[NumStates];

	int _dstOfs;
	int _matchHist0;
	int _matchHist1;
	int _matchHist2;
	int _matchHist3;
	uint _curState;
	uint _prevChar;
	uint _prevPrevChar;

	public LzhamDecompressor( byte[] source, int destinationLength )
	{
		_source = source;
		_sourceLength = source.Length;
		_destinationLength = destinationLength;
		_destination = new byte[destinationLength];
		_codec = new LzhamSymbolCodec( source );

		for ( var i = 0; i < _litTables.Length; i++ )
		{
			_litTables[i] = new LzhamAdaptiveModel( _generator );
			_deltaLitTables[i] = new LzhamAdaptiveModel( _generator );
		}

		_mainTable = new LzhamAdaptiveModel( _generator );

		for ( var i = 0; i < 2; i++ )
		{
			_repLenTables[i] = new LzhamAdaptiveModel( _generator );
			_largeLenTables[i] = new LzhamAdaptiveModel( _generator );
		}

		_distLsbTable = new LzhamAdaptiveModel( _generator );
	}

	public static bool TryDecompress( byte[] source, int destinationLength, out byte[] destination, out string error )
	{
		destination = null;
		error = null;

		if ( source is null || source.Length == 0 )
		{
			error = "LZHAM source is empty.";
			return false;
		}

		if ( destinationLength <= 0 || destinationLength > Array.MaxLength )
		{
			error = $"Invalid LZHAM destination length {destinationLength}.";
			return false;
		}

		var decompressor = new LzhamDecompressor( source, destinationLength );

		if ( !decompressor.Run( out error ) )
			return false;

		destination = decompressor._destination;
		return true;
	}

	bool Run( out string error )
	{
		error = null;
		try
		{
			Decode();
			if ( _dstOfs != _destination.Length )
				Array.Resize( ref _destination, _dstOfs );
			return true;
		}
		catch ( LzhamDecodeException e )
		{
			error = e.Message;
			return false;
		}
		catch ( Exception e )
		{
			error = $"LZHAM decode failed: {e.Message}";
			return false;
		}
	}

	void Decode()
	{
		InitPositionSlots();

		_codec.StartDecoding();

		// Alpha8 stream header: one bit for fast table updating, one for polar code lengths.
		var config = _codec.GetBits( 2 );
		var fastTableUpdating = (config & 2) != 0;
		var usePolarCodes = (config & 1) != 0;

		for ( var i = 0; i < NumLitTables; i++ )
		{
			if ( !_litTables[i].Init( 256, fastTableUpdating, usePolarCodes ) )
				throw new LzhamDecodeException( "Unable to initialize LZHAM literal model (corrupt stream)." );
			if ( !_deltaLitTables[i].Init( 256, fastTableUpdating, usePolarCodes ) )
				throw new LzhamDecodeException( "Unable to initialize LZHAM delta-literal model (corrupt stream)." );
		}

		var mainSymbols = LzxNumSpecialLengths + (_numLzxSlots - LzxLowestUsableMatchSlot) * 8;
		if ( !_mainTable.Init( mainSymbols, fastTableUpdating, usePolarCodes ) )
			throw new LzhamDecodeException( "Unable to initialize LZHAM main model (corrupt stream)." );

		var repLenSymbols = NumHugeMatchCodes + (MaxMatchLen - MinMatchLen + 1);
		var largeLenSymbols = NumHugeMatchCodes + LzxNumSecondaryLengths;
		for ( var i = 0; i < 2; i++ )
		{
			if ( !_repLenTables[i].Init( repLenSymbols, fastTableUpdating, usePolarCodes ) )
				throw new LzhamDecodeException( "Unable to initialize LZHAM rep-length model (corrupt stream)." );
			if ( !_largeLenTables[i].Init( largeLenSymbols, fastTableUpdating, usePolarCodes ) )
				throw new LzhamDecodeException( "Unable to initialize LZHAM large-length model (corrupt stream)." );
		}

		if ( !_distLsbTable.Init( 16, fastTableUpdating, usePolarCodes ) )
			throw new LzhamDecodeException( "Unable to initialize LZHAM distance-lsb model (corrupt stream)." );

		ClearStateModels();

		DecodeBlocks();
	}

	void InitPositionSlots()
	{
		var extraBits = _positionExtraBits;
		for ( int i = 0, bits = 0; i < LzxMaxPositionSlots; i += 2 )
		{
			extraBits[i] = (byte)bits;
			extraBits[i + 1] = (byte)bits;

			if ( i != 0 && bits < 25 )
				bits++;
		}

		var nextBase = 0U;
		for ( var i = 0; i < LzxMaxPositionSlots; i++ )
		{
			_positionBase[i] = nextBase;
			nextBase += 1U << extraBits[i];
		}

		var largestDistance = (1U << DictionarySizeLog2) - 1;
		_numLzxSlots = 0;
		for ( var i = 0; i < LzxMaxPositionSlots; i++ )
		{
			if ( largestDistance >= _positionBase[i] && largestDistance < _positionBase[i] + (1U << extraBits[i]) )
			{
				_numLzxSlots = i + 1;
				break;
			}
		}

		if ( _numLzxSlots == 0 )
			throw new LzhamDecodeException( "Unable to derive LZHAM position slots." );
	}

	void ClearStateModels()
	{
		for ( var i = 0; i < _isMatchModels.Length; i++ )
			_isMatchModels[i].Clear();

		for ( var i = 0; i < NumStates; i++ )
		{
			_isRepModels[i].Clear();
			_isRep0Models[i].Clear();
			_isRep0SingleByteModels[i].Clear();
			_isRep1Models[i].Clear();
			_isRep2Models[i].Clear();
		}
	}

	void DecodeBlocks()
	{
		// Both budgets are far beyond what any valid stream needs; they exist so corrupted
		// input fails with a message instead of looping.
		var blockBudget = (long)_sourceLength + _destinationLength + 4096;
		var blockCount = 0L;

		while ( true )
		{
			if ( ++blockCount > blockBudget )
				throw new LzhamDecodeException( "LZHAM stream has too many blocks (corrupt stream)." );

			var blockType = _codec.GetBits( 2 );
			switch ( blockType )
			{
				case SyncBlock:
					DecodeSyncBlock();
					break;

				case RawBlock:
					DecodeRawBlock();
					break;

				case CompBlock:
					DecodeCompressedBlock();
					break;

				case EofBlock:
					DecodeStreamTrailer();
					return;

				default:
					throw new LzhamDecodeException( "LZHAM stream uses an undefined block type (corrupt stream)." );
			}
		}
	}

	void DecodeSyncBlock()
	{
		var flushType = _codec.GetBits( BlockFlushTypeBits );
		if ( flushType == 1 )
			ResetHuffmanTableUpdateRates();
		else if ( flushType == 2 )
			ResetAllTables();

		_codec.AlignToByte();

		if ( _codec.GetBits( 16 ) != 0 )
			throw new LzhamDecodeException( "LZHAM sync block is corrupt." );
		if ( _codec.GetBits( 16 ) != 0xFFFF )
			throw new LzhamDecodeException( "LZHAM sync block is corrupt." );
	}

	void DecodeRawBlock()
	{
		var numRawBytesRemaining = _codec.GetBits( 24 );
		var checkBits = _codec.GetBits( 8 );

		var expectedCheck = (numRawBytesRemaining & 0xFF) ^ ((numRawBytesRemaining >> 8) & 0xFF) ^ ((numRawBytesRemaining >> 16) & 0xFF);
		if ( checkBits != expectedCheck )
			throw new LzhamDecodeException( "LZHAM raw block length check failed (corrupt stream)." );

		numRawBytesRemaining++;

		// Discard any partial byte, then drain whole bytes that are already in the bit buffer.
		_codec.AlignToByte();
		do
		{
			var nextByte = _codec.RemoveByteFromBitBuffer();
			if ( nextByte < 0 )
				break;

			if ( _dstOfs >= _destinationLength )
				throw new LzhamDecodeException( "LZHAM raw block overflows the destination buffer." );

			_destination[_dstOfs++] = (byte)nextByte;
			numRawBytesRemaining--;
		}
		while ( numRawBytesRemaining != 0 );

		while ( numRawBytesRemaining != 0 )
		{
			var available = _sourceLength - _codec.Next;
			if ( available <= 0 )
				throw new LzhamDecodeException( "LZHAM raw block is truncated." );

			var copy = (int)Math.Min( numRawBytesRemaining, (uint)available );
			if ( _dstOfs + copy > _destinationLength )
				throw new LzhamDecodeException( "LZHAM raw block overflows the destination buffer." );

			Buffer.BlockCopy( _source, _codec.Next, _destination, _dstOfs, copy );
			_codec.AdvanceInput( copy );
			_dstOfs += copy;
			numRawBytesRemaining -= (uint)copy;
		}
	}

	void DecodeCompressedBlock()
	{
		_codec.StartArithmeticDecoding();

		_matchHist0 = 1;
		_matchHist1 = 1;
		_matchHist2 = 1;
		_matchHist3 = 1;
		_curState = 0;
		_prevChar = 0;
		_prevPrevChar = 0;

		var flushType = _codec.GetBits( BlockFlushTypeBits );
		if ( flushType == 1 )
			ResetHuffmanTableUpdateRates();
		else if ( flushType == 2 )
			ResetAllTables();

		var symbolBudget = 2L * _destinationLength + (1 << 22);
		var symbolCount = 0L;
		var endOfBlock = false;
		while ( !endOfBlock )
		{
			if ( ++symbolCount > symbolBudget )
				throw new LzhamDecodeException( "LZHAM compressed block produces too many symbols (corrupt stream)." );

			var matchModelIndex = (int)(_prevChar >> (8 - NumIsMatchContextBits)) + ((int)_curState << NumIsMatchContextBits);
			var isMatch = _codec.DecodeBit( ref _isMatchModels[matchModelIndex] );

			if ( isMatch == 0 )
				DecodeLiteral();
			else
				endOfBlock = DecodeMatch();
		}

		_codec.AlignToByte();
	}

	void DecodeLiteral()
	{
		if ( _dstOfs >= _destinationLength )
			throw new LzhamDecodeException( "LZHAM literal overflows the destination buffer." );

		uint value;
		if ( _curState < NumLitStates )
		{
			var predictor = (int)(_prevChar >> (8 - NumLitPredBits / 2))
				| ((int)(_prevPrevChar >> (8 - NumLitPredBits / 2)) << (NumLitPredBits / 2));

			value = _codec.DecodeSymbol( _litTables[predictor] );
		}
		else
		{
			var matchHist0Offset = _dstOfs - _matchHist0;
			var repLit0 = ReadOutputByte( matchHist0Offset );
			var repLit1 = ReadOutputByte( matchHist0Offset - 1 );

			var predictor = (int)(repLit0 >> (8 - NumDeltaLitPredBits / 2))
				| ((int)(repLit1 >> (8 - NumDeltaLitPredBits / 2)) << (NumDeltaLitPredBits / 2));

			value = _codec.DecodeSymbol( _deltaLitTables[predictor] ) ^ repLit0;
		}

		_destination[_dstOfs] = (byte)value;
		_prevPrevChar = _prevChar;
		_prevChar = value;
		_curState = s_literalNextState[_curState];
		_dstOfs++;
	}

	/// <summary>Returns true when the block's end-of-block code was decoded.</summary>
	bool DecodeMatch()
	{
		var matchLen = 1U;

		var isRep = _codec.DecodeBit( ref _isRepModels[_curState] );
		if ( isRep != 0 )
		{
			var isRep0 = _codec.DecodeBit( ref _isRep0Models[_curState] );
			if ( isRep0 != 0 )
			{
				var isRep0Len1 = _codec.DecodeBit( ref _isRep0SingleByteModels[_curState] );
				if ( isRep0Len1 != 0 )
				{
					_curState = _curState < NumLitStates ? 9U : 11U;
				}
				else
				{
					matchLen = DecodeRepLength();
					_curState = _curState < NumLitStates ? 8U : 11U;
				}
			}
			else
			{
				matchLen = DecodeRepLength();

				var isRep1 = _codec.DecodeBit( ref _isRep1Models[_curState] );
				if ( isRep1 != 0 )
				{
					var temp = _matchHist1;
					_matchHist1 = _matchHist0;
					_matchHist0 = temp;
				}
				else
				{
					var isRep2 = _codec.DecodeBit( ref _isRep2Models[_curState] );
					if ( isRep2 != 0 )
					{
						var temp = _matchHist2;
						_matchHist2 = _matchHist1;
						_matchHist1 = _matchHist0;
						_matchHist0 = temp;
					}
					else
					{
						var temp = _matchHist3;
						_matchHist3 = _matchHist2;
						_matchHist2 = _matchHist1;
						_matchHist1 = _matchHist0;
						_matchHist0 = temp;
					}
				}

				_curState = _curState < NumLitStates ? 8U : 11U;
			}
		}
		else
		{
			var symbol = _codec.DecodeSymbol( _mainTable );
			var adjustedSymbol = (int)symbol - LzxNumSpecialLengths;
			if ( adjustedSymbol < 0 )
			{
				if ( adjustedSymbol == LzxSpecialCodeEndOfBlockCode - LzxNumSpecialLengths )
					return true;

				// cLZXSpecialCodePartialStateReset: reset match history and continue.
				_matchHist0 = 1;
				_matchHist1 = 1;
				_matchHist2 = 1;
				_matchHist3 = 1;
				_curState = 0;
				return false;
			}

			symbol = (uint)adjustedSymbol;
			matchLen = (symbol & 7) + MinMatchLen;

			var matchSlot = (int)(symbol >> 3) + LzxLowestUsableMatchSlot;

			if ( matchLen == 9 )
			{
				matchLen += _codec.DecodeSymbol( _largeLenTables[_curState >= NumLitStates ? 1 : 0] );
				if ( matchLen == MaxMatchLen + 1 )
					matchLen = DecodeHugeMatchLength();
			}

			var numExtraBits = (int)_positionExtraBits[matchSlot];
			uint extraBits;
			if ( numExtraBits < 3 )
			{
				extraBits = _codec.GetBits( numExtraBits );
			}
			else
			{
				extraBits = 0;
				if ( numExtraBits > 4 )
				{
					extraBits = _codec.GetBits( numExtraBits - 4 );
					extraBits <<= 4;
				}

				extraBits += _codec.DecodeSymbol( _distLsbTable );
			}

			_matchHist3 = _matchHist2;
			_matchHist2 = _matchHist1;
			_matchHist1 = _matchHist0;
			_matchHist0 = (int)(_positionBase[matchSlot] + extraBits);

			_curState = _curState < NumLitStates ? (uint)NumLitStates : (uint)NumLitStates + 3;
		}

		CopyMatch( matchLen );
		return false;
	}

	uint DecodeHugeMatchLength()
	{
		var length = 0U;
		do
		{
			var more = _codec.GetBits( 1 );
			if ( more == 0 )
				break;
			length++;
		}
		while ( length < 3 );

		return s_hugeMatchBaseLen[length] + _codec.GetBits( s_hugeMatchCodeLen[length] );
	}

	uint DecodeRepLength()
	{
		var length = _codec.DecodeSymbol( _repLenTables[_curState >= NumLitStates ? 1 : 0] ) + MinMatchLen;
		if ( length == MaxMatchLen + 1 )
			length = DecodeHugeMatchLength();

		return length;
	}

	void CopyMatch( uint matchLen )
	{
		if ( _matchHist0 > _dstOfs || _dstOfs + (long)matchLen > _destinationLength )
			throw new LzhamDecodeException( "LZHAM match points outside the output buffer (corrupt stream)." );

		var sourceOffset = _dstOfs - _matchHist0;
		var destinationOffset = _dstOfs;

		if ( _matchHist0 == 1 )
		{
			// Byte runs, including overlapping ones.
			var value = _destination[sourceOffset];
			if ( matchLen < 8 )
			{
				for ( var i = 0; i < (int)matchLen; i++ )
					_destination[destinationOffset++] = value;

				_prevPrevChar = matchLen == 1 ? _prevChar : value;
			}
			else
			{
				Array.Fill( _destination, value, destinationOffset, (int)matchLen );
				destinationOffset += (int)matchLen;
				_prevPrevChar = value;
			}

			_prevChar = value;
		}
		else if ( matchLen == 1 )
		{
			_prevPrevChar = _prevChar;
			_prevChar = _destination[sourceOffset];
			_destination[destinationOffset++] = (byte)_prevChar;
		}
		else
		{
			var bytesToCopy = (int)matchLen - 2;
			if ( bytesToCopy < 8 || bytesToCopy > _matchHist0 )
			{
				for ( var i = 0; i < bytesToCopy; i++ )
					_destination[destinationOffset++] = _destination[sourceOffset++];
			}
			else
			{
				Buffer.BlockCopy( _destination, sourceOffset, _destination, destinationOffset, bytesToCopy );
				destinationOffset += bytesToCopy;
				sourceOffset += bytesToCopy;
			}

			// The last two output bytes are always tracked for match context.
			_prevPrevChar = _destination[sourceOffset++];
			_destination[destinationOffset++] = (byte)_prevPrevChar;

			_prevChar = _destination[sourceOffset++];
			_destination[destinationOffset++] = (byte)_prevChar;
		}

		_dstOfs += (int)matchLen;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	byte ReadOutputByte( int index )
	{
		// The native unbuffered decoder reads raw memory here; clamping to the already produced
		// bytes keeps corrupted streams inside the destination buffer without unbounded reads.
		if ( (uint)index >= (uint)_dstOfs )
			return 0;

		return _destination[index];
	}

	void ResetAllTables()
	{
		for ( var i = 0; i < _litTables.Length; i++ )
		{
			ResetModel( _litTables[i] );
			ResetModel( _deltaLitTables[i] );
		}

		ResetModel( _mainTable );

		for ( var i = 0; i < 2; i++ )
		{
			ResetModel( _repLenTables[i] );
			ResetModel( _largeLenTables[i] );
		}

		ResetModel( _distLsbTable );

		ClearStateModels();
	}

	void ResetHuffmanTableUpdateRates()
	{
		for ( var i = 0; i < _litTables.Length; i++ )
		{
			_litTables[i].ResetUpdateRate();
			_deltaLitTables[i].ResetUpdateRate();
		}

		_mainTable.ResetUpdateRate();

		for ( var i = 0; i < 2; i++ )
		{
			_repLenTables[i].ResetUpdateRate();
			_largeLenTables[i].ResetUpdateRate();
		}

		_distLsbTable.ResetUpdateRate();
	}

	static void ResetModel( LzhamAdaptiveModel model )
	{
		if ( !model.Reset() )
			throw new LzhamDecodeException( "Unable to reset LZHAM adaptive model (corrupt stream)." );
	}

	void DecodeStreamTrailer()
	{
		_codec.AlignToByte();

		var expectedAdler = (_codec.GetBits( 16 ) << 16) | _codec.GetBits( 16 );
		var computedAdler = LzhamChecksum.Adler32( _destination, _dstOfs );
		if ( expectedAdler != computedAdler )
			throw new LzhamDecodeException( $"LZHAM adler32 mismatch (stream 0x{expectedAdler:X8}, decoded 0x{computedAdler:X8})." );
	}
}
