using System;

namespace GoldSrc.HalfLife;

internal sealed partial class NpcAudio
{
	readonly Dictionary<(SoundFile, int), Task<SoundFile>> _compressedWords = new();

	Task<SoundFile> CompressWord( SoundFile sound, int percent )
	{
		var key = (sound, percent);
		if ( !_compressedWords.TryGetValue( key, out var result ) )
			_compressedWords.Add( key, result = Shorten( sound, percent ) );
		return result;
	}

	// Overlap short sections of the original recording so faster speech keeps its voice pitch.
	static async Task<SoundFile> Shorten( SoundFile sound, int percent )
	{
		var pcm = await sound.GetSamplesAsync();
		if ( pcm is not { Length: > 0 } ) return sound;
		int keep = 100 - percent;
		var result = new short[Math.Max( 1, (int)((long)pcm.Length * keep / 100) )];
		var hop = Math.Clamp( sound.Rate / 100, 1, result.Length );
		pcm.AsSpan( 0, hop ).CopyTo( result );
		for ( int dst = hop; dst < result.Length; dst += hop )
		{
			int count = Math.Min( hop, result.Length - dst );
			int expected = (int)((long)dst * 100 / keep);
			int next = Math.Clamp( expected, 0, pcm.Length - count );
			int previous = Math.Min( (int)((long)(dst - hop) * 100 / keep) + hop, pcm.Length - count );
			for ( int i = 0; i < count; i++ )
				result[dst + i] = (short)((pcm[previous + i] * (hop - i) + pcm[next + i] * i) / hop);
		}
		var bytes = new byte[result.Length * sizeof( short )];
		Buffer.BlockCopy( result, 0, bytes, 0, bytes.Length );
		return SoundFile.FromPcm( $"{sound.ResourcePath}.t{percent}.vsnd", bytes,
			new SoundFile.PcmOptions { Rate = (uint)sound.Rate } );
	}
}
