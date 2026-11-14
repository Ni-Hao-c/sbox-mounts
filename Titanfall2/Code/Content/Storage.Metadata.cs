/// <summary>Metadata available without exposing a physical filesystem.</summary>
sealed class Titanfall2FileInfo( string path )
{
	internal string FullName => Titanfall2Storage.Normalize( path );
	internal string DirectoryName => Path.GetDirectoryName( FullName )?.Replace( '\\', '/' );
	internal long Length => Titanfall2Storage.Length( path );
	internal bool Exists => Titanfall2Storage.FileExists( path );
	internal long Revision => Titanfall2Storage.Revision( path );
	internal DateTime AccessedAt => Titanfall2Storage.AccessedAt( path );
	internal void Delete() => Titanfall2Storage.Delete( path );
}

sealed class Titanfall2DirectoryInfo( string path )
{
	internal Titanfall2FileInfo[] GetFiles( string pattern, bool recursive = false ) =>
		Titanfall2Storage.Files( path, pattern, recursive ).Select( file => new Titanfall2FileInfo( file ) ).ToArray();
}
