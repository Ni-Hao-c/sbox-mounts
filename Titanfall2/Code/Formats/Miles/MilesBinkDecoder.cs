using System.Buffers.Binary;

/// <summary>
/// Decodes the Bink Audio 2 (DCT) streams that Titanfall 2 stores in "1FCB" BCF
/// payloads into interleaved 16-bit PCM. The container has a 24-byte file header,
/// a 16-bit packet-size table and 4-byte-framed audio packets. Each packet holds
/// Bink Audio blocks: per-band exponential quantizers, run-length coded fixed
/// width coefficients and an inverse DCT with overlap-add.
/// Four- and six-channel streams are decoded natively and then folded down to the
/// two channels the mount has always handed to SoundFile.FromPcm. The fold-down is
/// a Miles-approximating mix (not the proprietary Miles mix): 0.7071 centre,
/// 0.5 surrounds, LFE omitted. Four-channel streams are folded as
/// L=(FL+0.7071*BL)*0.7071 and R=(FR+0.7071*BR)*0.7071; the 1/sqrt(2) factor gives
/// equal-power level matching the retail 2ch fold (6ch streams already match it).
/// </summary>
static class MilesBinkDecoder
{
	const int HeaderSize = 24;
	const int MaxChannels = 6;
	const double SurroundMix = 0.70710678118654752440;
	const double RearMix = 0.5;
	const int MaxPackets = 1_000_000;
	const int MaxDecodeSeconds = 15 * 60;

	// Critical band edges from FFmpeg libavcodec/wma_freqs.c (LGPL): the shared
	// WMA/Bink band table. Only the first 25 edges are used by Bink Audio.
	static readonly int[] CriticalFrequencies =
	[
		100, 200, 300, 400, 510, 630, 770, 920,
		1080, 1270, 1480, 1720, 2000, 2320, 2700, 3150,
		3700, 4400, 5300, 6400, 7700, 9500, 12000, 15500,
		24500,
	];

	// Zero-run lengths from FFmpeg libavcodec/binkaudio.c rle_length_tab (LGPL).
	static readonly int[] RunLengths = [2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 32, 64];

	/// <summary>
	/// Decodes one BCF stream to interleaved 16-bit PCM.
	/// <paramref name="gameRoot"/> is accepted for call-site compatibility but is
	/// not used: the managed decoder needs no installed game runtime.
	/// </summary>
	public static bool TryDecode( string gameRoot, byte[] source, out byte[] pcm, out uint rate, out ushort channels, out string error )
	{
		pcm = null;
		rate = 0;
		channels = 0;
		error = null;
		try
		{
			var stream = new BcfStream( source );
			if ( !stream.TryDecode( out pcm, out error ) ) return false;
			rate = (uint)stream.Rate;
			channels = (ushort)stream.OutputChannels;
			return true;
		}
		catch ( Exception e )
		{
			pcm = null;
			rate = 0;
			channels = 0;
			error = e.Message;
			return false;
		}
	}

	sealed class BcfStream
	{
		readonly byte[] _source;
		int _frameLength;
		int _overlapLength;
		int _blockSamples;
		int _bandCount;
		int[] _bands;
		double _root;
		double[] _quantizers;
		int _packetCount;
		int _firstPacketOffset;
		short[][] _previous;
		short[][] _pending;
		int _outputChannels;

		public int Channels { get; private set; }
		public int OutputChannels => _outputChannels;
		public int Rate { get; private set; }
		public int TotalFrames { get; private set; }

		public BcfStream( byte[] source ) => _source = source;

		public bool TryDecode( out byte[] pcm, out string error )
		{
			pcm = null;
			error = null;
			if ( !TryReadHeader() ) return false;
			pcm = Decode( out error );
			return pcm is not null;
		}

		bool TryReadHeader()
		{
			if ( _source.Length < HeaderSize ) throw Invalid( "is shorter than its header" );
			if ( !_source.AsSpan( 0, 4 ).SequenceEqual( "1FCB"u8 ) ) throw Invalid( "does not start with the BCF magic" );
			if ( _source[4] != 2 ) throw Invalid( $"uses unsupported version {_source[4]}" );
			Channels = _source[5];
			Rate = BinaryPrimitives.ReadUInt16LittleEndian( _source.AsSpan( 6 ) );
			var frames = BinaryPrimitives.ReadUInt32LittleEndian( _source.AsSpan( 8 ) );
			var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian( _source.AsSpan( 0x10 ) );
			_packetCount = BinaryPrimitives.ReadUInt16LittleEndian( _source.AsSpan( 0x14 ) );
			if ( Channels is not (1 or 2 or 4 or 6) ) throw Invalid( $"uses unsupported channel layout {Channels}" );
			if ( Rate is < 8000 or > 192000 ) throw Invalid( $"uses unsupported sample rate {Rate}" );
			if ( declaredSize != _source.Length ) throw Invalid( "has a declared size that does not match the source record" );
			if ( _packetCount is 0 or > MaxPackets ) throw Invalid( $"uses an unreasonable packet count {_packetCount}" );
			if ( _packetCount * 2 + HeaderSize > _source.Length ) throw Invalid( "has a packet table that is out of range" );
			if ( frames == 0 || frames > (long)Rate * MaxDecodeSeconds ) throw Invalid( "has an unreasonable decoded length" );

			var frameLengthBits = Rate < 22050 ? 9 : Rate < 44100 ? 10 : 11;
			_frameLength = 1 << frameLengthBits;
			_overlapLength = _frameLength / 16;
			_blockSamples = _frameLength - _overlapLength;
			var sampleRateHalf = (Rate + 1) / 2;
			_root = _frameLength / (Math.Sqrt( _frameLength ) * 32768.0);
			_quantizers = new double[96];
			for ( var i = 0; i < 96; i++ ) _quantizers[i] = Math.Exp( i * 0.15289164787221953823 ) * _root;
			var bandCount = 1;
			for ( ; bandCount < 25; bandCount++ ) if ( sampleRateHalf <= CriticalFrequencies[bandCount - 1] ) break;
			_bandCount = bandCount;
			_bands = new int[_bandCount + 1];
			_bands[0] = 2;
			for ( var i = 1; i < _bandCount; i++ ) _bands[i] = (int)((long)CriticalFrequencies[i - 1] * _frameLength / sampleRateHalf) & ~1;
			_bands[_bandCount] = _frameLength;

			var decodableFrames = (long)_packetCount * _blockSamples;
			if ( frames > decodableFrames + _blockSamples ) throw Invalid( "declares more samples than its packets can hold" );
			TotalFrames = (int)frames;

			_firstPacketOffset = HeaderSize + _packetCount * 2;
			_outputChannels = Math.Min( Channels, 2 );
			_previous = new short[Channels][];
			_pending = new short[Channels][];
			for ( var c = 0; c < Channels; c++ ) _pending[c] = new short[_blockSamples];
			for ( var c = 0; c < Channels; c++ ) _previous[c] = new short[_overlapLength];
			return true;
		}

		byte[] Decode( out string error )
		{
			error = null;
			var byteCount = checked((long)TotalFrames * _outputChannels * sizeof( short ));
			if ( byteCount > Array.MaxLength ) throw Invalid( "would need more PCM than one array can hold" );
			var output = new byte[(int)byteCount];

			var coeffs = new double[_frameLength];
			var quant = new double[_bandCount];
			var block = new double[_frameLength];
			var fftReal = new double[_frameLength * 2];
			var fftImaginary = new double[_frameLength * 2];
			var cosines = new double[_frameLength];
			var sines = new double[_frameLength];
			FillTwiddles( cosines, sines );

			var groupsPerFrame = (Channels + 1) / 2;
			var packetTableOffset = HeaderSize;
			var packetIndex = 0;
			var packetOffset = _firstPacketOffset;
			BitReader reader = null;
			var decodedFrames = 0;
			var framesNeeded = (TotalFrames + _blockSamples - 1) / _blockSamples;
			var blocksNeeded = framesNeeded * groupsPerFrame;
			for ( var blockIndex = 0; blockIndex < blocksNeeded; blockIndex++ )
			{
				if ( reader is null )
				{
					reader = OpenPacket( ref packetIndex, ref packetOffset, packetTableOffset );
					if ( reader is null ) break;
				}
				var channelOffset = (blockIndex % groupsPerFrame) * 2;
				var channelCount = Math.Min( 2, Channels - channelOffset );
				if ( blockIndex % groupsPerFrame == 0 ) decodedFrames++;
				var frameBase = (decodedFrames - 1) * _blockSamples;
				var first = blockIndex == 0;
				try
				{
					DecodeBlock( reader, coeffs, quant, block, fftReal, fftImaginary, cosines, sines, channelOffset, channelCount, first );
				}
				catch ( InvalidDataException e )
				{
					throw Invalid( $"has a malformed bitstream in packet {packetIndex}: {e.Message}" );
				}
				reader.Align32();
				if ( reader.BitsLeft <= 0 ) reader = null;
				if ( blockIndex % groupsPerFrame == groupsPerFrame - 1 ) WriteFrame( output, frameBase );
			}

			var decodedSamples = decodedFrames * _blockSamples;
			if ( decodedSamples < TotalFrames )
			{
				// The final packet only has to cover the tail of the declared duration.
				Array.Clear( output, decodedSamples * _outputChannels * sizeof( short ), output.Length - decodedSamples * _outputChannels * sizeof( short ) );
			}
			return output;
		}

		BitReader OpenPacket( ref int packetIndex, ref int packetOffset, int tableOffset )
		{
			if ( packetIndex >= _packetCount ) return null;
			if ( packetOffset + 4 > _source.Length ) throw Invalid( "has a truncated packet header" );
			var payloadSize = BinaryPrimitives.ReadUInt16LittleEndian( _source.AsSpan( packetOffset + 2 ) );
			var packetSize = payloadSize + 4;
			var tableEntry = BinaryPrimitives.ReadUInt16LittleEndian( _source.AsSpan( tableOffset + packetIndex * 2 ) );
			if ( tableEntry != packetSize ) throw Invalid( "has a packet table that does not match its packet headers" );
			if ( packetOffset + packetSize > _source.Length ) throw Invalid( "has a truncated packet payload" );
			var reader = new BitReader( _source, packetOffset + 4, packetOffset + packetSize );
			packetOffset += packetSize;
			packetIndex++;
			return reader;
		}

		void DecodeBlock( BitReader reader, double[] coeffs, double[] quant, double[] block, double[] fftReal, double[] fftImaginary,
			double[] cosines, double[] sines, int channelOffset, int channelCount, bool first )
		{
			reader.Skip( 2 );
			for ( var ch = 0; ch < channelCount; ch++ )
			{
				Array.Clear( coeffs );
				coeffs[0] = ReadFloat( reader ) * _root;
				coeffs[1] = ReadFloat( reader ) * _root;
				for ( var i = 0; i < _bandCount; i++ )
				{
					var index = reader.ReadBits( 8 );
					quant[i] = _quantizers[Math.Min( index, 95 )];
				}

				var band = 0;
				var scale = quant[0];
				var position = 2;
				while ( position < _frameLength )
				{
					int end;
					if ( reader.ReadBit() != 0 )
					{
						var lengthCode = reader.ReadBits( 4 );
						end = position + RunLengths[lengthCode] * 8;
					}
					else
					{
						end = position + 8;
					}
					end = Math.Min( end, _frameLength );
					var width = reader.ReadBits( 4 );
					if ( width == 0 )
					{
						position = end;
						while ( band < _bandCount && _bands[band] < position ) scale = quant[band++];
						continue;
					}
					while ( position < end )
					{
						if ( band < _bandCount && _bands[band] == position ) scale = quant[band++];
						var value = reader.ReadBits( width );
						coeffs[position] = value != 0 ? (reader.ReadBit() != 0 ? -scale : scale) * value : 0.0;
						position++;
					}
				}

				coeffs[0] /= 0.5;
				InverseDct( coeffs, block, fftReal, fftImaginary, cosines, sines );
				var channel = channelOffset + ch;
				var previous = _previous[channel];
				for ( var i = 0; i < _blockSamples; i++ )
				{
					var value = RoundSample( block[i] );
					if ( i < _overlapLength && !first )
					{
						value = BlendOverlap( previous[i], value, i, _overlapLength, ch, channelCount );
					}
					_pending[channel][i] = value;
				}
				for ( var i = 0; i < _overlapLength; i++ ) previous[i] = RoundSample( block[_frameLength - _overlapLength + i] );
			}
		}

		// Folds one fully decoded frame of native channels into the stereo output.
		// Channel order follows the default FFmpeg/WAV layouts: 2ch = FL FR,
		// 4ch = FL FR BL BR, 6ch = FL FR FC LFE BL BR (LFE is intentionally omitted).
		void WriteFrame( byte[] output, int frameBase )
		{
			var target = output.AsSpan();
			for ( var i = 0; i < _blockSamples; i++ )
			{
				var frame = frameBase + i;
				if ( frame >= TotalFrames ) break;
				if ( _outputChannels == 1 )
				{
					BinaryPrimitives.WriteInt16LittleEndian( target.Slice( frame * 2 ), _pending[0][i] );
				}
				else
				{
					short left, right;
					if ( Channels == 2 )
					{
						left = _pending[0][i];
						right = _pending[1][i];
					}
					else if ( Channels == 4 )
					{
						left = Mix( _pending[0][i], _pending[2][i] );
						right = Mix( _pending[1][i], _pending[3][i] );
					}
					else
					{
						// 5.1: FL FR FC LFE BL BR; LFE is omitted in the fold-down.
						left = Mix3( _pending[0][i], _pending[2][i], _pending[4][i] );
						right = Mix3( _pending[1][i], _pending[2][i], _pending[5][i] );
					}
					BinaryPrimitives.WriteInt16LittleEndian( target.Slice( frame * 4 ), left );
					BinaryPrimitives.WriteInt16LittleEndian( target.Slice( frame * 4 + 2 ), right );
				}
			}
		}

		// 4ch uses the ITU front/surround pair, then a 1/sqrt(2) equal-power scale so the
		// folded level matches the retail 2ch fold (measured RMS ratio ~1.41 without it).
		static short Mix( short front, short surround ) => Clamp( (front + surround * SurroundMix) * SurroundMix );

		static short Mix3( short front, short center, short surround ) => Clamp( front + center * SurroundMix + surround * RearMix );

		static short Clamp( double value )
		{
			var rounded = Math.Round( value, MidpointRounding.AwayFromZero );
			if ( rounded > 32767 ) return 32767;
			if ( rounded < -32767 ) return -32767;
			return (short)rounded;
		}

		static short RoundSample( double sample )
		{
			var value = Math.Round( sample * 32768.0, MidpointRounding.AwayFromZero );
			if ( value > 32767 ) return 32767;
			if ( value < -32767 ) return -32767;
			return (short)value;
		}

		static short BlendOverlap( short previous, short current, int index, int overlapLength, int channel, int channels )
		{
			// Miles crossfades already rounded 16-bit block samples with a linear ramp
			// and integer division (floor), i.e. an arithmetic shift in fixed point.
			var weight = channel + index * channels;
			var count = overlapLength * channels;
			var numerator = (long)(count - weight) * previous + (long)weight * current;
			var value = numerator / count;
			if ( numerator % count != 0 && numerator < 0 ) value--;
			return (short)Math.Clamp( value, -32767, 32767 );
		}

		static double ReadFloat( BitReader reader )
		{
			var power = reader.ReadBits( 5 );
			var mantissa = reader.ReadBits( 23 );
			var value = Math.ScaleB( (double)mantissa, power - 23 );
			if ( reader.ReadBit() != 0 ) value = -value;
			return value;
		}

		static void FillTwiddles( double[] cosines, double[] sines )
		{
			var length = cosines.Length * 2;
			for ( var i = 0; i < cosines.Length; i++ )
			{
				var angle = Math.PI * i / length;
				cosines[i] = Math.Cos( angle );
				sines[i] = Math.Sin( angle );
			}
		}

		static void InverseDct( double[] coeffs, double[] output, double[] real, double[] imaginary, double[] cosines, double[] sines )
		{
			// out[n] = 2/N * Re[ sum(c[k] * e^(i*pi*k*(n+0.5)/N)) ], evaluated with a
			// 2N-point complex FFT: FFT(conj(g))[n] has the required real part.
			var length = coeffs.Length;
			var fftLength = length * 2;
			Array.Clear( real, 0, fftLength );
			Array.Clear( imaginary, 0, fftLength );
			real[0] = coeffs[0] * 0.5;
			for ( var k = 1; k < length; k++ )
			{
				real[k] = coeffs[k] * cosines[k];
				imaginary[k] = -coeffs[k] * sines[k];
			}
			Fft( real, imaginary, fftLength );
			var scale = 2.0 / length;
			for ( var i = 0; i < length; i++ ) output[i] = real[i] * scale;
		}

		static void Fft( double[] real, double[] imaginary, int length )
		{
			for ( int i = 1, j = 0; i < length; i++ )
			{
				var bit = length >> 1;
				for ( ; (j & bit) != 0; bit >>= 1 ) j ^= bit;
				j ^= bit;
				if ( i < j )
				{
					(real[i], real[j]) = (real[j], real[i]);
					(imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
				}
			}
			for ( var span = 2; span <= length; span <<= 1 )
			{
				var angle = -2 * Math.PI / span;
				var stepReal = Math.Cos( angle );
				var stepImaginary = Math.Sin( angle );
				for ( var start = 0; start < length; start += span )
				{
					var twiddleReal = 1.0;
					var twiddleImaginary = 0.0;
					for ( var k = 0; k < span / 2; k++ )
					{
						var evenReal = real[start + k];
						var evenImaginary = imaginary[start + k];
						var oddReal = real[start + k + span / 2] * twiddleReal - imaginary[start + k + span / 2] * twiddleImaginary;
						var oddImaginary = real[start + k + span / 2] * twiddleImaginary + imaginary[start + k + span / 2] * twiddleReal;
						real[start + k] = evenReal + oddReal;
						imaginary[start + k] = evenImaginary + oddImaginary;
						real[start + k + span / 2] = evenReal - oddReal;
						imaginary[start + k + span / 2] = evenImaginary - oddImaginary;
						var nextReal = twiddleReal * stepReal - twiddleImaginary * stepImaginary;
						twiddleImaginary = twiddleReal * stepImaginary + twiddleImaginary * stepReal;
						twiddleReal = nextReal;
					}
				}
			}
		}

		static InvalidDataException Invalid( string message ) => new( "The BCF stream " + message + "." );
	}

	/// <summary>Little-endian bit reader: the first bit read is the low bit of a value.</summary>
	sealed class BitReader
	{
		readonly byte[] _data;
		readonly int _startBit;
		readonly int _endBit;
		int _bit;

		public BitReader( byte[] data, int startByte, int endByte )
		{
			_data = data;
			_startBit = startByte * 8;
			_bit = _startBit;
			_endBit = endByte * 8;
		}

		public int BitsLeft => _endBit - _bit;

		public int ReadBits( int count )
		{
			if ( count is < 0 or > 32 ) throw new InvalidDataException( "invalid bit count" );
			if ( _bit + count > _endBit ) throw new InvalidDataException( "the bitstream is truncated" );
			var value = 0;
			for ( var i = 0; i < count; i++ )
			{
				value |= ((_data[_bit >> 3] >> (_bit & 7)) & 1) << i;
				_bit++;
			}
			return value;
		}

		public int ReadBit() => ReadBits( 1 );

		public void Skip( int count )
		{
			if ( count < 0 || _bit + count > _endBit ) throw new InvalidDataException( "the bitstream is truncated" );
			_bit += count;
		}

		public void Align32() => _bit = _startBit + ((_bit - _startBit + 31) & ~31);
	}
}












