using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

/// <summary>
/// Paths in the importer are logical paths into capabilities supplied by the engine.
/// Game capabilities are read-only; only FileSystem.Data can receive derived output.
/// No physical paths, native file handles or process-wide current directory are used.
/// </summary>
static class Titanfall2Storage
{
	sealed record Root( BaseFileSystem FileSystem, bool Writable, string CacheNamespace = null );
	static readonly ConcurrentDictionary<string, Root> Roots = new( StringComparer.Ordinal );
	static readonly ConcurrentDictionary<string, object> Gates = new( StringComparer.Ordinal );
	static readonly ConcurrentDictionary<string, DateTime> AccessTimes = new( StringComparer.Ordinal );
	static int _nextRoot;
	internal const string DataRoot = "@tf2-data";

	internal static string Register( BaseFileSystem fileSystem, string cacheNamespace = null )
	{
		ArgumentNullException.ThrowIfNull( fileSystem );
		var key = $"@tf2-game-{Interlocked.Increment( ref _nextRoot )}";
		Roots[key] = new( fileSystem, false, cacheNamespace ?? key );
		return key;
	}

	// Runtime capabilities stay distinct across remounts. Derived identities use the
	// install's logical namespace so registration order cannot invalidate disk caches.
	internal static string StableIdentity( string identity )
	{
		foreach ( var pair in Roots.OrderByDescending( pair => pair.Key.Length ) )
			identity = identity.Replace( pair.Key, pair.Value.CacheNamespace, StringComparison.Ordinal );
		return identity;
	}

	internal static string Normalize( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || path.Contains( ':' ) || path.Contains( '\0' ) || path[0] is '/' or '\\' )
			throw new ArgumentException( "A path must belong to an engine-provided filesystem.", nameof( path ) );
		var parts = new List<string>();
		foreach ( var part in path.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( part == "." ) continue;
			if ( part == ".." )
			{
				if ( parts.Count <= 1 ) throw new ArgumentException( "Path escapes its filesystem root.", nameof( path ) );
				parts.RemoveAt( parts.Count - 1 );
			}
			else parts.Add( part );
		}
		if ( parts.Count == 0 || (parts[0] != DataRoot && !Roots.ContainsKey( parts[0] )) )
			throw new ArgumentException( "Unknown filesystem capability.", nameof( path ) );
		return string.Join( '/', parts );
	}

	internal static (BaseFileSystem FileSystem, string Path) Resolve( string path, bool write = false )
	{
		path = Normalize( path );
		var separator = path.IndexOf( '/' );
		var key = separator < 0 ? path : path[..separator];
		var relative = separator < 0 ? "" : path[(separator + 1)..];
		var root = key == DataRoot ? new Root( FileSystem.Data, true ) : Roots[key];
		if ( write && !root.Writable ) throw new UnauthorizedAccessException( "Installed game content is read-only." );
		return (root.FileSystem, relative);
	}

	internal static bool FileExists( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return false;
		var (fs, relative) = Resolve( path );
		return fs.FileExists( relative );
	}
	internal static bool DirectoryExists( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return false;
		var (fs, relative) = Resolve( path );
		return fs.DirectoryExists( relative );
	}
	internal static Stream OpenRead( string path )
	{
		var (fs, relative) = Resolve( path );
		if ( !Normalize( path ).StartsWith( DataRoot + "/", StringComparison.Ordinal ) ) return fs.OpenRead( relative );
		var gate = Gates.GetOrAdd( Normalize( path ), static _ => new object() );
		Monitor.Enter( gate );
		try { return new CacheReadStream( fs.OpenRead( relative ), gate ); }
		catch { Monitor.Exit( gate ); throw; }
	}
	internal static Stream OpenWrite( string path )
	{
		var (fs, relative) = Resolve( path, true );
		fs.CreateDirectory( Path.GetDirectoryName( relative ) );
		return fs.OpenWrite( relative );
	}
	internal static long Length( string path )
	{
		var (fs, relative) = Resolve( path );
		return fs.FileSize( relative );
	}
	internal static byte[] ReadAllBytes( string path )
	{
		using var stream = OpenRead( path );
		if ( stream.Length > int.MaxValue ) throw new InvalidDataException( "Source exceeds the managed array size limit." );
		var bytes = new byte[checked((int)stream.Length)];
		stream.ReadExactly( bytes );
		return bytes;
	}
	internal static string ReadAllText( string path ) => Encoding.UTF8.GetString( ReadAllBytes( path ) );
	internal static void WriteAllText( string path, string contents, Encoding encoding = null )
	{
		using var stream = OpenWrite( path );
		stream.Write( (encoding ?? Encoding.UTF8).GetBytes( contents ) );
	}
	internal static void CreateDirectory( string path )
	{
		var (fs, relative) = Resolve( path, true );
		fs.CreateDirectory( relative );
	}
	internal static void Delete( string path )
	{
		var (fs, relative) = Resolve( path, true );
		fs.DeleteFile( relative );
	}
	internal static void Publish( string temporary, string destination )
	{
		var (source, sourcePath) = Resolve( temporary, true );
		var (target, targetPath) = Resolve( destination, true );
		var gate = Gates.GetOrAdd( Normalize( destination ), static _ => new object() );
		lock ( gate )
		{
			using ( var input = source.OpenRead( sourcePath ) )
			using ( var output = target.OpenWrite( targetPath ) ) input.CopyTo( output );
			source.DeleteFile( sourcePath );
			AccessTimes[Normalize( destination )] = DateTime.UtcNow;
		}
	}
	internal static IEnumerable<string> Files( string path, string pattern = "*", bool recursive = false )
	{
		var root = Normalize( path );
		var (fs, relative) = Resolve( root );
		return fs.FindFile( relative, pattern, recursive ).Select( file => Normalize( root + "/" + file ) ).ToArray();
	}
	internal static IEnumerable<string> Directories( string path )
	{
		var root = Normalize( path );
		var (fs, relative) = Resolve( root );
		return fs.FindDirectory( relative ).Select( directory => Normalize( root + "/" + directory ) ).ToArray();
	}
	internal static void Touch( string path ) => AccessTimes[Normalize( path )] = DateTime.UtcNow;
	internal static DateTime AccessedAt( string path ) => AccessTimes.TryGetValue( Normalize( path ), out var value ) ? value : DateTime.MinValue;

	/// <summary>
	/// VFS has no public mtime API. Pin size plus bounded header/footer fingerprints for
	/// containers; loose overrides additionally retain their complete SHA-256 digest.
	/// This is a revision hint, not a claim to hash an entire multi-gigabyte archive.
	/// </summary>
	internal static long Revision( string path )
	{
		using var stream = OpenRead( path );
		using var hash = SHA256.Create();
		var buffer = new byte[(int)Math.Min( 4096, stream.Length )];
		stream.ReadExactly( buffer );
		hash.TransformBlock( buffer, 0, buffer.Length, null, 0 );
		if ( stream.Length > buffer.Length )
		{
			stream.Position = Math.Max( buffer.Length, stream.Length - buffer.Length );
			var count = checked((int)(stream.Length - stream.Position));
			stream.ReadExactly( buffer.AsSpan( 0, count ) );
			hash.TransformBlock( buffer, 0, count, null, 0 );
		}
		hash.TransformFinalBlock( BitConverter.GetBytes( stream.Length ), 0, sizeof( long ) );
		return BitConverter.ToInt64( hash.Hash, 0 );
	}

	sealed class CacheReadStream( Stream inner, object gate ) : Stream
	{
		bool _disposed;
		public override bool CanRead => inner.CanRead;
		public override bool CanSeek => inner.CanSeek;
		public override bool CanWrite => false;
		public override long Length => inner.Length;
		public override long Position { get => inner.Position; set => inner.Position = value; }
		public override int Read( byte[] buffer, int offset, int count ) => inner.Read( buffer, offset, count );
		public override int Read( Span<byte> buffer ) => inner.Read( buffer );
		public override long Seek( long offset, SeekOrigin origin ) => inner.Seek( offset, origin );
		public override void Flush() => inner.Flush();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
		protected override void Dispose( bool disposing )
		{
			if ( !_disposed && disposing )
			{
				_disposed = true;
				try { inner.Dispose(); } finally { Monitor.Exit( gate ); }
			}
			base.Dispose( disposing );
		}
	}
}
