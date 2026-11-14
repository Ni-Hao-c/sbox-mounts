using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests for the pure managed LZHAM decoder. The golden vectors are three small real VPK chunk
/// payloads (compressed bytes only, base64) plus the SHA-256 of their expected output, so the
/// tests are self-contained and never touch the retail game installation.
/// </summary>
[TestClass]
public sealed class LzhamDecoderTests
{
	sealed record Vector( string Name, string InputBase64, int OutputLength, string OutputSha256 );

	static readonly Vector[] Vectors =
	{
		// Raw-block dominated chunk (21 compressed -> 24 bytes).
		new( "raw", "1B/TMAAEAAAQAywDvRIxgMABOAAc", 24, "3c1c3a70ce278a243684aa0b7ba0eb2903ddab0d1e55a9f6cda9e9d18e0ee543" ),

		// Small arithmetic-coded BSP lump (208 -> 1332 bytes).
		new( "lump-small", "1zOyrzAALgNSFwaABgBBCQH/UPdFEhAQKUgMPBHULAQ+AWQsH1kIBfeACgkWChflGC+/yVAAwD2kiPn/jH9+p19vuNVAAISBSRj+DAJpGAowL/gj/olmDIIr+iVT+jH1ofqKaK0MEmcESMBQky/MEUH/om4806MDRgbBDA3690e9YUADAIH7Y59R2P0jFQM736FCbBgwEcRgZkhJjwBHQwOMpAxwDgCLf9CDogKiTPxtoC4Z7koMvviMDfEGRAAMfAEMkAokwAAnovjAA8AaKA==", 1332, "76d545e0586dc814dae2886fcca1e1a3ec56e9015cd9ec71349ba27a3cfd6537" ),

		// Larger arithmetic-coded BSP lump (441 -> 2048 bytes).
		new( "lump-2k", "1/+maJABWAdaHJwD3YAgZaDgYDrgYGBgKABh4GALAYIDAWwAwAGQAQAEQAyAAEACAAAHHgAACABOACAACHCAAAwA5gEAAMF8AEAMAEGYHABADABlADwAAEAAdwMAABDYVkIgDlAMgCdAAAGAFT4gBh+rQP4H6AIAgDAABACcBuACAMAIB2IAIAwAgPAGB+mGuYDGGMMYYwt6wExP/gPokBICQ/IAgACAABwIAAIAA5exrEUxlGQGgaBoCOsAIA5r9UDRADQNA0fil5MDwHR4DQGgDDp/I+4FQA/8f0fsfcfjrH3HqPcfI9x6/IBQDgFAWA0B9aA0CwAAQAC3CDANAwCwAAIAEQEAwIAwEAAI0ABWFoXhaFYC+ADgKBYGgeBoFgCUAIABYGAaBgFkgAAEAAIBgQBgL+AAHxDNFYVhYF5YAwGdQNA4EgWBIxyUF/gCgOAp+CoLk8MAwfw/gIfw/gEAQBAEH8zj+H8P4FYVhWFZqAgCe9S8NH6P4foKiQLAuCwKj9H8P0Pg0DgPA4DR+j+I5+gkhsNBUFwVODMeSf0IA4T+Ai+GAAAIACAAEAAAMABF+MDA3nnj", 2048, "8b99c515ec54bd074d7dcd6b6aa99f8b079017be1206c644631ea6dd34f5ca28" )
	};

	static byte[] Decode( Vector vector )
	{
		var input = Convert.FromBase64String( vector.InputBase64 );
		Assert.IsTrue(
			LzhamDecoder.TryDecompress( input, vector.OutputLength, out var output, out var error ),
			$"{vector.Name}: {error}" );
		Assert.IsNotNull( output );
		Assert.AreEqual( vector.OutputLength, output.Length, vector.Name );
		return output;
	}

	static string Hash( byte[] data ) => Convert.ToHexString( SHA256.HashData( data ) ).ToLowerInvariant();

	[TestMethod]
	public void DecodesGoldenVectors()
	{
		foreach ( var vector in Vectors )
			Assert.AreEqual( vector.OutputSha256, Hash( Decode( vector ) ), vector.Name );
	}

	[TestMethod]
	public void RejectsTruncatedStreams()
	{
		foreach ( var vector in Vectors )
		{
			var input = Convert.FromBase64String( vector.InputBase64 );
			for ( var length = 0; length < input.Length; length++ )
			{
				var truncated = input.AsSpan( 0, length ).ToArray();
				Assert.IsFalse(
					LzhamDecoder.TryDecompress( truncated, vector.OutputLength, out var output, out var error ),
					$"{vector.Name}: truncated to {length} bytes was accepted ({output?.Length} bytes)" );
				Assert.IsNull( output, $"{vector.Name}: truncated to {length} bytes produced output" );
				Assert.IsFalse( string.IsNullOrEmpty( error ), $"{vector.Name}: truncated to {length} bytes gave no message" );
			}
		}
	}

	[TestMethod]
	public void RejectsByteCorruption()
	{
		// Flipping a whole byte destroys the arithmetic/prefix-code synchronization or the
		// stream's own Adler-32 trailer, so every position must be rejected with a message.
		foreach ( var vector in Vectors )
		{
			var input = Convert.FromBase64String( vector.InputBase64 );
			for ( var position = 0; position < input.Length; position++ )
			{
				var mutated = (byte[])input.Clone();
				mutated[position] ^= 0xFF;

				Assert.IsFalse(
					LzhamDecoder.TryDecompress( mutated, vector.OutputLength, out var output, out var error ),
					$"{vector.Name}: corruption at {position} was accepted" );
				Assert.IsNull( output, $"{vector.Name}: corruption at {position} produced output" );
				Assert.IsFalse( string.IsNullOrEmpty( error ), $"{vector.Name}: corruption at {position} gave no message" );
			}
		}
	}

	[TestMethod]
	public void SingleBitCorruptionIsSafe()
	{
		// Single-bit flips must never throw, overrun, or change the reported output size. Most are
		// rejected outright. A small fraction of flips can still be accepted because LZHAM's stream
		// trailer is a 32-bit Adler-32 checksum, which can collide for structured data; the retired
		// native bridge accepted those same inputs and produced the same bytes (verified at porting
		// time), so the decoder intentionally mirrors that behaviour rather than inventing a stricter
		// contract. What it must never do is return a truncated buffer without an error.
		foreach ( var vector in Vectors )
		{
			var input = Convert.FromBase64String( vector.InputBase64 );
			for ( var position = 0; position < input.Length; position++ )
			{
				for ( var bit = 0; bit < 8; bit++ )
				{
					var mutated = (byte[])input.Clone();
					mutated[position] ^= (byte)(1 << bit);

					if ( LzhamDecoder.TryDecompress( mutated, vector.OutputLength, out var output, out var error ) )
					{
						Assert.IsNotNull( output, $"{vector.Name}: accepted flip {position}:{bit} without output" );
						Assert.AreEqual( vector.OutputLength, output.Length, $"{vector.Name}: accepted flip {position}:{bit} with a short output" );
						continue;
					}

					Assert.IsNull( output, $"{vector.Name}: rejected flip {position}:{bit} but returned output" );
					Assert.IsFalse( string.IsNullOrEmpty( error ), $"{vector.Name}: rejected flip {position}:{bit} without a message" );
				}
			}
		}
	}

	[TestMethod]
	public void RejectsInvalidArguments()
	{
		Assert.IsFalse( LzhamDecoder.TryDecompress( null, 1024, out var output, out var error ) );
		Assert.IsNull( output );
		Assert.IsFalse( string.IsNullOrEmpty( error ) );

		Assert.IsFalse( LzhamDecoder.TryDecompress( Array.Empty<byte>(), 1024, out output, out error ) );
		Assert.IsNull( output );
		Assert.IsFalse( string.IsNullOrEmpty( error ) );

		var vector = Convert.FromBase64String( Vectors[0].InputBase64 );
		foreach ( var destinationLength in new[] { 0, -1, int.MinValue } )
		{
			Assert.IsFalse( LzhamDecoder.TryDecompress( vector, destinationLength, out output, out error ) );
			Assert.IsNull( output );
			Assert.IsFalse( string.IsNullOrEmpty( error ) );
		}
	}

	[TestMethod]
	public void RejectsGarbageAndLyingLengths()
	{
		var random = new Random( 20261113 );
		for ( var i = 0; i < 32; i++ )
		{
			var garbage = new byte[1 + random.Next( 4096 )];
			random.NextBytes( garbage );
			Assert.IsFalse(
				LzhamDecoder.TryDecompress( garbage, 1 + random.Next( 8192 ), out var output, out var error ),
				$"garbage {i} was accepted" );
			Assert.IsNull( output );
			Assert.IsFalse( string.IsNullOrEmpty( error ) );
		}

		foreach ( var vector in Vectors )
		{
			var input = Convert.FromBase64String( vector.InputBase64 );

			// A destination smaller than the real output must be rejected.
			Assert.IsFalse( LzhamDecoder.TryDecompress( input, vector.OutputLength - 1, out _, out _ ), $"{vector.Name}: short destination accepted" );

			// A larger destination is legal for the memory decoder (it reports the produced
			// length), but it must never produce more bytes than the stream actually has.
			if ( LzhamDecoder.TryDecompress( input, vector.OutputLength + 64, out var output, out var error ) )
			{
				Assert.AreEqual( vector.OutputLength, output.Length, $"{vector.Name}: oversized destination changed the output length" );
				Assert.AreEqual( vector.OutputSha256, Hash( output ), vector.Name );
			}
			else
			{
				Assert.IsFalse( string.IsNullOrEmpty( error ) );
			}
		}
	}

	[TestMethod]
	public void ConcurrentDecodesAreDeterministic()
	{
		var expected = new Dictionary<string, string>();
		foreach ( var vector in Vectors )
			expected[vector.Name] = vector.OutputSha256;

		var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
		Parallel.For( 0, 96, iteration =>
		{
			var vector = Vectors[iteration % Vectors.Length];
			var input = Convert.FromBase64String( vector.InputBase64 );

			if ( !LzhamDecoder.TryDecompress( input, vector.OutputLength, out var output, out var error ) )
			{
				failures.Add( $"{vector.Name}: {error}" );
				return;
			}

			var hash = Hash( output );
			if ( hash != expected[vector.Name] )
				failures.Add( $"{vector.Name}: nondeterministic output" );
		} );

		Assert.AreEqual( 0, failures.Count, string.Join( "; ", failures ) );
	}
}
