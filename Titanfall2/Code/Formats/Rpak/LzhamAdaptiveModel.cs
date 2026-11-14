/// <summary>
/// Port of the quasi-adaptive Huffman/polar data model from lzhamdecomp/lzham_symbol_codec.h/.cpp
/// (the LZHAM alpha revision, which schedules updates via fast-updating / max-cycle and can
/// switch between Huffman and polar code lengths, both signalled in the stream header).
/// </summary>
sealed class LzhamAdaptiveModel
{
	const int MaxUpdateCycle = 32767;

	readonly LzhamCodeLengthGenerator _generator;
	readonly LzhamDecoderTables _tables = new();

	ushort[] _symFreq;
	ushort[] _codeSizes;

	int _totalSyms;
	int _maxCycle;
	int _updateCycle;
	int _symbolsUntilUpdate;
	int _totalCount;
	int _decoderTableBits;
	bool _fastUpdating;
	bool _usePolarCodes;

	public LzhamAdaptiveModel( LzhamCodeLengthGenerator generator )
	{
		_generator = generator;
	}

	public LzhamDecoderTables Tables => _tables;
	public int TotalSyms => _totalSyms;

	public bool Init( int totalSyms, bool fastUpdating, bool usePolarCodes )
	{
		_totalSyms = totalSyms;
		_fastUpdating = fastUpdating;
		_usePolarCodes = usePolarCodes;
		_symbolsUntilUpdate = 0;

		if ( _symFreq is null || _symFreq.Length < totalSyms )
		{
			_symFreq = new ushort[totalSyms];
			_codeSizes = new ushort[totalSyms];
		}

		_decoderTableBits = totalSyms <= 16
			? 0
			: Math.Min( 1 + CeilLog2( (uint)totalSyms ), LzhamDecoderTables.MaxTableBits );

		_tables.Init( totalSyms, _decoderTableBits );

		if ( fastUpdating )
			_maxCycle = (Math.Max( 64, totalSyms ) + 6) << 5;
		else
			_maxCycle = (Math.Max( 32, totalSyms ) + 6) * 16;

		_maxCycle = Math.Min( _maxCycle, MaxUpdateCycle );

		return Reset();
	}

	public bool Reset()
	{
		if ( _totalSyms == 0 )
			return true;

		for ( var i = 0; i < _totalSyms; i++ )
			_symFreq[i] = 1;
		_updateCycle = _totalSyms;

		_totalCount = 0;
		_symbolsUntilUpdate = 0;

		if ( !Update() )
			return false;

		_symbolsUntilUpdate = _updateCycle = 8;
		return true;
	}

	public void Rescale()
	{
		var totalFreq = 0;
		for ( var i = 0; i < _totalSyms; i++ )
		{
			var freq = (_symFreq[i] + 1) >> 1;
			totalFreq += freq;
			_symFreq[i] = (ushort)freq;
		}

		_totalCount = totalFreq;
	}

	public void ResetUpdateRate()
	{
		_totalCount += _updateCycle - _symbolsUntilUpdate;

		if ( _totalCount > _totalSyms )
			Rescale();

		_symbolsUntilUpdate = _updateCycle = Math.Min( 8, _updateCycle );
	}

	public bool Update()
	{
		_totalCount += _updateCycle;

		while ( _totalCount >= 32768 )
			Rescale();

		bool status;
		int maxCodeSize;
		uint totalFreq;
		if ( _usePolarCodes )
			status = _generator.GeneratePolarCodes( _totalSyms, _symFreq, _codeSizes, out maxCodeSize, out totalFreq );
		else
			status = _generator.GenerateHuffmanCodes( _totalSyms, _symFreq, _codeSizes, out maxCodeSize, out totalFreq );

		if ( !status || totalFreq != (uint)_totalCount )
			return false;

		if ( maxCodeSize > LzhamDecoderTables.MaxExpectedCodeSize
			&& !_generator.LimitMaxCodeSize( _totalSyms, _codeSizes, LzhamDecoderTables.MaxExpectedCodeSize ) )
			return false;

		if ( !LzhamPrefixCoding.GenerateDecoderTables( _totalSyms, _codeSizes, _tables, _decoderTableBits ) )
			return false;

		if ( _fastUpdating )
			_updateCycle = 2 * _updateCycle;
		else
			_updateCycle = (5 * _updateCycle) >> 2;

		if ( _updateCycle > _maxCycle )
			_updateCycle = _maxCycle;

		_symbolsUntilUpdate = _updateCycle;

		return true;
	}

	/// <summary>Equivalent to the frequency bump plus conditional table update in LZHAM_SYMBOL_CODEC_DECODE_ADAPTIVE_HUFFMAN.</summary>
	public void OnSymbolDecoded( int symbol )
	{
		_symFreq[symbol] = (ushort)(_symFreq[symbol] + 1);

		if ( --_symbolsUntilUpdate == 0 && !Update() )
			throw new LzhamDecodeException( "LZHAM adaptive model update failed (corrupt stream)." );
	}

	static int CeilLog2( uint v )
	{
		var floor = 0;
		var value = v;
		while ( value > 1 )
		{
			value >>= 1;
			floor++;
		}

		if ( v > 1U << floor )
			floor++;

		return floor;
	}
}
