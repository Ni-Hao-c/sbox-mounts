/// <summary>
/// Adler-32 implementation ported from lzhamdecomp/lzham_checksum.cpp (itself derived from
/// the public domain stb.h). LZHAM memory streams end with the Adler-32 of the source data,
/// so this doubles as the corruption check the native bridge performed.
/// </summary>
static class LzhamChecksum
{
	public const uint InitialAdler32 = 1;

	const uint AdlerMod = 65521;
	const int MaxBlockLength = 5552;

	public static uint Adler32( byte[] buffer, int length )
	{
		uint s1 = InitialAdler32 & 0xFFFF;
		uint s2 = InitialAdler32 >> 16;
		var offset = 0;
		while ( length > 0 )
		{
			var blockLength = Math.Min( length, MaxBlockLength );
			var blockEnd = offset + blockLength;
			while ( offset < blockEnd )
			{
				s1 += buffer[offset++];
				s2 += s1;
			}

			s1 %= AdlerMod;
			s2 %= AdlerMod;
			length -= blockLength;
		}

		return (s2 << 16) | s1;
	}
}
