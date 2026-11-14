using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

// The managed Bink Audio decoder must keep producing identical PCM for these
// captured Titanfall 2 "1FCB" BCF streams. The digests were recorded from a build
// that was cross-checked against the retail Miles/Bink decoder (all samples within
// 2 LSB, 94% byte-identical for mono/stereo), so they guard the pure managed path.
[TestClass]
public class MilesAudioTests
{
	// 372-byte mono BCF stream from r2/sound/general_stream.mstr (48 kHz, 2139 frames).
	const string MonoBcf = "MUZDQgIBgLtbCAAAFAEAAHQBAAACAAEAGAFAAJmZFAEQhJ3goq1epYIRAtEAobCAcUFhEFFwINHA4HBQgPAQkiUAhQoJwVAGQT4VUhACigjUhoBgBqk0TKTQCUWMcgk5piuGdcooIBxERKTyJAJU31GC7XosiEOQH0XKGCBCeYUCgLaKCM16BommupCwyWYxBFUEETcASQgOgxnVCeacSESQH2EwvEQQQRA72igAtcuk6oIEQCoQVUgkJDKVMwHhKxGgFTapGhJzZL5bH8UAASERTWbsc2YIqgCvCBClLUDR0t+aaAsAocyElJCNEUCZFzNjtBG/sahRBFBxDaSEqdCMgKpUqCqGAEnwq6WFugREDEGoSSC4STIG2YsyUmepUebFCIRIbAAMFkMCEBsAPnz4EACZmTwAEAozr4L636WCEdGwoIBwYFBAMBAAAAAAAAAwUIDwEJIVYAi0rW3btm3bdnvi//////////8HHz58+AAA";
	const string MonoPcmSha256 = "D206A8821315E9099D93D5913702F6E2C1B749EAC8BA7F952DF4747633C8F59F";

	// 1916-byte stereo BCF stream from r2/sound/general_stream.mstr (44.1 kHz, 2081 frames).
	const string StereoBcf = "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";
	const string StereoPcmSha256 = "CFFE9FBF20BE444B07B43A617B669D2507FCF764B7D6D2249AC50490A3342C01";

	// Truncated 6-channel BCF stream (44.1 kHz, 1 frame) folded down to stereo.
	const string SixChannelBcf = "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";
	const string SixChannelPcmSha256 = "43C2D07C4C39EA77C861C7E80998C68EA9D95B2A7E064C97D7269CF1AA42C5B8";

	// Truncated 4-channel BCF stream (48 kHz, 1 frame) folded down to stereo.
	const string FourChannelBcf = "MUZDQgIEgLuABwAAoAkAAK4EAAABAAEAlASZmZAEFKXZOZP9eqeCEdGwoIBw8ICQ8JDg0PCg0OEi81KCEZIVBACMgUQ6EUViUDAJHQRpwBBUd3oYHxNVmWRQrEqRcCQAAAMGkFEqCEPoDCwGi0kOslppCSO6AkkGASnIIXdHNRN7qnLX1ZghiuVVjaoAklOgyCxSNKl/KymtMLiFNpEJ7GpQBVGwsYmm/mVUKjMyAhCJ3tQkEYmQTcqWINiBkxQnG4DiCDgUMUO8IALSipIZZREACCEgXxSBFgmXAAl+NdUmQJgQCe3WqQrlTRIliTQGu7ygs9H+ViEaZGoKeA2RlNY2kkJNUZsQrgWvhkjUL5FC24YiboJSQ5SDEJCoDRCFoK8FPwYRqaBoLlOHDTdKVkU9IqlJ2hHqRVYvEAgAYgxI5nkMtbGY2tcazSEJpc4oAJSkrqgFAiVxRPdpjATvULpWraOEVwirQUQBOPEKGcIaIMCkQFKLq2xMCIkyKDQzhWRZM2aCtIVWYWaKspl9OYcCQShKG8zSIgIjJDUhBH8lT6TJpWMSFJEaQdpqrWpCggCJuIVAiHGG+iMkoRZPqtgJQDqsEgcAGYRAIYQMCTNvGUdc8SiJSRGoCwm1m21KW1r7RoZoAqlZ4gqDgaeADx+K1oKZqWxWU8GIaFhQQDhoKDBgMGBAUFDISHFJGbkIyRLAghggEZCASugHiAl0I4gDJkE128e03KwKaaNiVQQOGWAIABg4wIxkwRgSh4BB0UloIdXBagQilAJRUwcFU4oBphcDlSFu/K1hRnhVFVMUy6qBZlBJyhK0VAED0DAcVFXrrDpDlGimeEWg02zmKg0NItFaG48yI1BasYzioCKvTfNSCclABs0wKy/JjMQoLD7Qi4QGSSZnVACBGIpFcIEGJW5SqIg+YTQnFVHrRECmKKMhwU0AajZuAiyMJHeSTTJnAAgkCBET51kUEvTaaWRCRIgCga23wlmKTKzxHOaMkFAlQG80hkigqrs5NJG7CBllELq1wSRyN6UCHRMqzjUUdEiklZqMQFByIKJgjoH5HGGgJGgY90WgLHO4SZZkbbAHhSFkBTR4Vo32uAzWYZErwxBYhNOIOwaKIInsxIx3sVgZk0CmGA+yQtCQYMc19B3J1AxmVEvKHBS6QugkUk+azMgUUQbNKknaUA0ykC1a81fH8A/O5ExQxJSYCk1kyBBciRLckNCitCEKAJmUkNTiKl8mxoQdFZqJKWHWDCEoApEMkm5+MRHKQkhSP8CQkCSleYPxDgCBIIV7a1uU2mAGzCqIGIFpjMUM1FWIAs0bD2NnYixFiLSGUaHBaqbQzEwgiPRiS2eYIJpBqoTELheZ1U+YTF0MIZCNIBKJFQMxJdYZShRHFV4BM1VMoLZiOILKv6riVZDULiTUbmaIJqCapPVvZKpIoKonFICfMOUnngYQgEgywwIAEAYGhAEAAMwLYIACBqACAGAAGAB8+AAAAAAAAAAAAKCCEdGwoIBwYFBAMBAQEBAQEBAwUIDwEJL14cOHDwAAAAAAAACoYEQ0LCggHBgUEAwEBAQEBAQEDBQgPIRkffjw4QMAAA==";
	const string FourChannelPcmSha256 = "985FA21B958817B0674CF06ECFC4C1ACF6FB9B28FC39FB46FEB60BF40856B4B6";

	[TestMethod]
	public void DecodesMonoBcfStream()
	{
		Assert.IsTrue( MilesBinkDecoder.TryDecode( null, Convert.FromBase64String( MonoBcf ), out var pcm, out var rate, out var channels, out var error ), error );
		Assert.AreEqual( 48000u, rate );
		Assert.AreEqual( (ushort)1, channels );
		Assert.AreEqual( 4278, pcm.Length );
		Assert.AreEqual( MonoPcmSha256, Sha256Hex( pcm ) );
	}

	[TestMethod]
	public void DecodesStereoBcfStream()
	{
		Assert.IsTrue( MilesBinkDecoder.TryDecode( null, Convert.FromBase64String( StereoBcf ), out var pcm, out var rate, out var channels, out var error ), error );
		Assert.AreEqual( 44100u, rate );
		Assert.AreEqual( (ushort)2, channels );
		Assert.AreEqual( 8324, pcm.Length );
		Assert.AreEqual( StereoPcmSha256, Sha256Hex( pcm ) );
	}

	[TestMethod]
	public void DecodesSixChannelBcfStreamAndFoldsToStereo()
	{
		Assert.IsTrue( MilesBinkDecoder.TryDecode( null, Convert.FromBase64String( SixChannelBcf ), out var pcm, out var rate, out var channels, out var error ), error );
		Assert.AreEqual( 44100u, rate );
		Assert.AreEqual( (ushort)2, channels );
		Assert.AreEqual( 7680, pcm.Length );
		Assert.AreEqual( SixChannelPcmSha256, Sha256Hex( pcm ) );
	}

	[TestMethod]
	public void DecodesFourChannelBcfStreamAndFoldsToStereo()
	{
		Assert.IsTrue( MilesBinkDecoder.TryDecode( null, Convert.FromBase64String( FourChannelBcf ), out var pcm, out var rate, out var channels, out var error ), error );
		Assert.AreEqual( 48000u, rate );
		Assert.AreEqual( (ushort)2, channels );
		Assert.AreEqual( 7680, pcm.Length );
		Assert.AreEqual( FourChannelPcmSha256, Sha256Hex( pcm ) );
	}

	[TestMethod]
	public void RejectsMalformedBcfStreams()
	{
		var mono = Convert.FromBase64String( MonoBcf );
		AssertRejected( null );
		AssertRejected( [] );
		AssertRejected( mono[..23] );
		AssertRejected( Mutate( mono, 0, 0x30 ) );
		AssertRejected( Mutate( mono, 4, 1 ) );
		AssertRejected( Mutate( mono, 0x10, (byte)(mono[0x10] ^ 0x55) ) );
		AssertRejected( Mutate( mono, 11, 0xFF ) );
		AssertRejected( Mutate( mono, 24, (byte)(mono[24] ^ 0xFF) ) );
		AssertRejected( mono[..^1] );
	}

	[TestMethod]
	public void RejectsUnsupportedChannelLayoutsWithClearMessage()
	{
		var mono = Convert.FromBase64String( MonoBcf );
		foreach ( var channels in new byte[] { 3, 5, 7 } )
		{
			Assert.IsFalse( MilesBinkDecoder.TryDecode( null, Mutate( mono, 5, channels ), out var pcm, out _, out _, out var error ) );
			Assert.IsNull( pcm );
			StringAssert.Contains( error, "channel layout" );
		}
	}

	[TestMethod]
	public void CorruptPayloadsNeverThrow()
	{
		var mono = Convert.FromBase64String( MonoBcf );
		for ( var offset = 24; offset < mono.Length; offset += 17 )
		{
			var corrupt = Mutate( mono, offset, (byte)(mono[offset] ^ 0xFF) );
			MilesBinkDecoder.TryDecode( null, corrupt, out var pcm, out _, out _, out _ );
			if ( pcm is not null ) Assert.AreEqual( 4278, pcm.Length );
		}
	}

	[TestMethod]
	public void ConcurrentDecodesAreIsolated()
	{
		var mono = Convert.FromBase64String( MonoBcf );
		var digests = new string[32];
		Parallel.For( 0, digests.Length, index =>
		{
			Assert.IsTrue( MilesBinkDecoder.TryDecode( null, mono, out var pcm, out _, out _, out var error ), error );
			digests[index] = Sha256Hex( pcm );
		} );
		Assert.IsTrue( digests.All( digest => digest == MonoPcmSha256 ) );
	}

	static void AssertRejected( byte[] source )
	{
		Assert.IsFalse( MilesBinkDecoder.TryDecode( null, source, out var pcm, out _, out _, out var error ) );
		Assert.IsNull( pcm );
		Assert.IsFalse( string.IsNullOrWhiteSpace( error ) );
	}

	static byte[] Mutate( byte[] source, int offset, byte value )
	{
		var copy = (byte[])source.Clone();
		copy[offset] = value;
		return copy;
	}

	static string Sha256Hex( byte[] data ) => Convert.ToHexString( SHA256.HashData( data ) );
}

