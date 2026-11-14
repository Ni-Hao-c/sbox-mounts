using System.Text;
// Physical disk access is confined to the test host, outside mount Code/.
static class FixtureFiles
{
	internal static Sandbox.BaseFileSystem CreateFileSystem( string root ) => (Sandbox.BaseFileSystem)Activator.CreateInstance( typeof( Sandbox.BaseFileSystem ).Assembly.GetType( "Sandbox.LocalFileSystem", true )!, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, [root, false], null )!;
	internal static string Physical( string path )
	{
		if ( !path.StartsWith( "@tf2-", StringComparison.Ordinal ) ) return path;
		var (fs, relative) = Titanfall2Storage.Resolve( path ); return fs.GetFullPath( relative );
	}
	internal static void WriteAllText( string path, string value ) => File.WriteAllText( Physical( path ), value );
	internal static void WriteAllText( string path, string value, Encoding encoding ) => File.WriteAllText( Physical( path ), value, encoding );
	internal static void WriteAllBytes( string path, byte[] value ) => File.WriteAllBytes( Physical( path ), value );
	internal static string ReadAllText( string path ) => File.ReadAllText( Physical( path ) );
	internal static bool Exists( string path ) => File.Exists( Physical( path ) );
	internal static FileStream Create( string path ) => File.Create( Physical( path ) );
	internal static DateTime GetLastWriteTimeUtc( string path ) => File.GetLastWriteTimeUtc( Physical( path ) );
	internal static void SetLastWriteTimeUtc( string path, DateTime time ) => File.SetLastWriteTimeUtc( Physical( path ), time );
}

