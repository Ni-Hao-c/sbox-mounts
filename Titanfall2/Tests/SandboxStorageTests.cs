using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Titanfall2;
using Titanfall2.Formats;

[TestClass]
public class SandboxStorageTests
{
	[TestMethod]
	public void GameCapabilityRejectsWritesAndEscapes()
	{
		var disk = Path.Combine( Path.GetTempPath(), "tf2-vfs-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( disk );
		try
		{
			File.WriteAllText( Path.Combine( disk, "original.txt" ), "original" );
			var root = Titanfall2Storage.Register( FixtureFiles.CreateFileSystem( disk ) );
			Assert.AreEqual( "original", Titanfall2Storage.ReadAllText( root + "/original.txt" ) );
			Assert.ThrowsException<UnauthorizedAccessException>( () => Titanfall2Storage.OpenWrite( root + "/original.txt" ) );
			Assert.ThrowsException<UnauthorizedAccessException>( () => Titanfall2Storage.Delete( root + "/original.txt" ) );
			Assert.ThrowsException<ArgumentException>( () => Titanfall2Storage.Normalize( root + "/../outside.txt" ) );
			Assert.ThrowsException<ArgumentException>( () => Titanfall2Storage.OpenRead( Path.Combine( disk, "original.txt" ) ) );
			Assert.AreEqual( "original", File.ReadAllText( Path.Combine( disk, "original.txt" ) ) );
		}
		finally { Directory.Delete( disk, true ); }
	}

	[TestMethod]
	public void CacheIdentitySurvivesCapabilityRegistrationOrder()
	{
		var disk = Path.Combine( Path.GetTempPath(), "tf2-id-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( disk );
		try
		{
			var fs = FixtureFiles.CreateFileSystem( disk );
			var first = Titanfall2Storage.Register( fs, "test-install" );
			var second = Titanfall2Storage.Register( fs, "test-install" );
			Assert.AreNotEqual( first, second );
			Assert.AreEqual( Titanfall2ProcessedCache.Key( first + "/map:version" ), Titanfall2ProcessedCache.Key( second + "/map:version" ) );
		}
		finally { Directory.Delete( disk, true ); }
	}

	[TestMethod]
	public void PackedMdlAndVtxFieldsKeepTheirOnDiskOffsets()
	{
		Assert.AreEqual( 9, Marshal.SizeOf<VtxVertex>() );
		Assert.AreEqual( 80, Marshal.OffsetOf<Mdl53StudioHeader>( nameof( Mdl53StudioHeader.Length ) ).ToInt32() );
		var bytes = new byte[] { 10, 20, 30, 3, 0x34, 0x12, 0xff, 2, 3 };
		var vertex = Titanfall2Binary.ReadArray<VtxVertex>( bytes ).Single();
		Assert.AreEqual( (byte)30, vertex.BoneWeightIndex[2] );
		Assert.AreEqual( (ushort)0x1234, vertex.OriginalMeshVertexId );
		Assert.AreEqual( (sbyte)-1, vertex.BoneId[0] );
		CollectionAssert.AreEqual( bytes, Titanfall2Binary.WriteArray( new[] { vertex } ) );
		Assert.ThrowsException<InvalidDataException>( () => Titanfall2Binary.ReadArray<VtxVertex>( bytes.AsSpan( 1 ) ) );
	}

	[TestMethod]
	public void ConcurrentStarPakRangesUseIndependentSeekPositions()
	{
		var disk = Path.Combine( Path.GetTempPath(), "tf2-starpak-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( disk );
		try
		{
			var bytes = Enumerable.Range( 0, 65536 ).Select( i => (byte)(i * 17) ).ToArray();
			File.WriteAllBytes( Path.Combine( disk, "test.starpak" ), bytes );
			var root = Titanfall2Storage.Register( FixtureFiles.CreateFileSystem( disk ) );
			Parallel.For( 0, 64, i =>
			{
				var offset = i * 701;
				Assert.IsTrue( StarPakIndex.TryRead( root + "/test.starpak", offset, 1024, out var actual, out var error ), error );
				CollectionAssert.AreEqual( bytes.AsSpan( offset, 1024 ).ToArray(), actual );
			} );
		}
		finally { StarPakIndex.Clear(); Directory.Delete( disk, true ); }
	}

	[TestMethod]
	public void RetailMdlGeometryStillDecodesWithSandboxPackedFields()
	{
		var disk = Environment.GetEnvironmentVariable( "TF2_SKY_TEST_GAME" );
		if ( string.IsNullOrWhiteSpace( disk ) ) Assert.Inconclusive( "Set TF2_SKY_TEST_GAME for local model verification." );
		var root = Titanfall2Storage.Register( FixtureFiles.CreateFileSystem( disk ) );
		Assert.IsTrue( VpkArchive.TryOpen( root + "/vpk/englishclient_mp_common.bsp.pak000_dir.vpk", out var archive, out var error ), error );
		var entries = archive.Entries.Where( entry => entry.Path.EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase ) ).Take( 3 ).ToArray();
		Assert.AreEqual( 3, entries.Length, "Expected retail common model fixtures." );
		foreach ( var entry in entries )
		{
			Assert.IsTrue( archive.TryReadAllBytes( entry, out var data, out error ), error );
			var model = Titanfall2Mdl53Reader.Parse( data );
			Assert.AreEqual( 53, model.Header.Version );
			var geometry = Titanfall2MeshExtractor.ExtractLod( model, 0, allBodyGroupChoices: true );
			Assert.IsTrue( geometry.Meshes.Length > 0, entry.Path );
		}
	}
}
