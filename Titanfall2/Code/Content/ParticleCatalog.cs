using System.Diagnostics;
using Titanfall2;

public partial class Titanfall2Mount
{
	readonly List<ParticleFileSource> _particleFiles = [];
	readonly Dictionary<string, Titanfall2ParticleDefinition> _particleDefinitions = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> _indexedParticleFiles = new( StringComparer.OrdinalIgnoreCase );
	readonly object _particleCatalogLock = new();
	readonly object _particleIndexLock = new();
	int _nextParticleFile;
	int _particleFileFailures;
	int _particleFailureLogs;
	float _weaponWarmTime = -1;
	internal void WarmWeaponParticles()
	{
		if ( IsParticleCatalogComplete || _weaponWarmTime == Time.Now ) return;
		_weaponWarmTime = Time.Now;
		IndexParticleFiles( 2, 2 );
	}

	internal int ParticleFileCount
	{
		get { lock ( _particleCatalogLock ) return _particleFiles.Count; }
	}

	internal int IndexedParticleFileCount
	{
		get { lock ( _particleCatalogLock ) return _indexedParticleFiles.Count; }
	}

	internal int ParticleDefinitionCount
	{
		get { lock ( _particleCatalogLock ) return _particleDefinitions.Count; }
	}

	internal bool IsParticleCatalogComplete
	{
		get { lock ( _particleIndexLock ) lock ( _particleCatalogLock ) return _nextParticleFile >= _particleFiles.Count; }
	}

	internal void ResetParticleCatalog()
	{
		lock ( _particleIndexLock )
		lock ( _particleCatalogLock )
		{
			_particleFiles.Clear();
			_particleDefinitions.Clear();
			_readyShotParticles.Clear();
			_indexedParticleFiles.Clear();
			_nextParticleFile = 0;
			_particleFileFailures = 0;
			_particleFailureLogs = 0;
			_weaponWarmTime = -1;
		}
	}

	internal void RegisterParticleFile( string path, ITitanfall2AssetSource source )
	{
		if ( source is null || string.IsNullOrWhiteSpace( path ) ) return;
		lock ( _particleCatalogLock ) _particleFiles.Add( new ParticleFileSource( path, source ) );
	}

	internal bool TryGetParticleDefinition( string name, out Titanfall2ParticleDefinition definition )
	{
		definition = null;
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		lock ( _particleCatalogLock ) return _particleDefinitions.TryGetValue( name.Trim(), out definition );
	}

	/// <summary>
	/// Decompresses and parses a bounded number of PCFs. This is called by the
	/// runtime map streamer so opening a map never parses all particle libraries
	/// on the loading screen.
	/// </summary>
	internal ParticleCatalogProgress IndexParticleFiles( int maximumFiles, float maximumMilliseconds )
	{
		// Do not publish completion before another consumer has merged the last PCF.
		// Map streaming and standalone prefab discovery share this cursor.
		lock ( _particleIndexLock ) return IndexParticleFilesCore( maximumFiles, maximumMilliseconds );
	}

	ParticleCatalogProgress IndexParticleFilesCore( int maximumFiles, float maximumMilliseconds )
	{
		maximumFiles = Math.Max( 1, maximumFiles );
		maximumMilliseconds = MathF.Max( 0.1f, maximumMilliseconds );
		var timer = Stopwatch.StartNew();
		var processed = 0;
		var added = 0;
		while ( processed < maximumFiles && timer.Elapsed.TotalMilliseconds < maximumMilliseconds )
		{
			ParticleFileSource file;
			lock ( _particleCatalogLock )
			{
				if ( _nextParticleFile >= _particleFiles.Count ) break;
				file = _particleFiles[_nextParticleFile++];
				if ( !_indexedParticleFiles.Add( file.Path ) ) continue;
			}
			processed++;
			added += IndexParticleFile( file );
		}

		return new ParticleCatalogProgress(
			processed,
			added,
			IndexedParticleFileCount,
			ParticleFileCount,
			ParticleDefinitionCount,
			_particleFileFailures,
			IsParticleCatalogComplete );
	}

	int IndexParticleFile( ParticleFileSource file )
	{
		if ( !file.Source.TryReadAllBytes( out var bytes, out var readError ) )
		{ RecordParticleFailure( file.Path, readError ); return 0; }
		if ( !Titanfall2PcfReader.TryReadDefinitions( bytes, file.Path, out var definitions, out var parseError ) )
		{ RecordParticleFailure( file.Path, parseError ); return 0; }
		var added = 0;
		lock ( _particleCatalogLock )
			foreach ( var definition in definitions )
				if ( _particleDefinitions.TryAdd( definition.Name, definition ) ) added++;
		return added;
	}

	void IndexPriorityParticleLibrary( string path )
	{
		if ( path is null ) return;
		lock ( _particleIndexLock )
		{
			ParticleFileSource file;
			lock ( _particleCatalogLock )
			{
				if ( _indexedParticleFiles.Contains( path ) ) return;
				file = _particleFiles.FirstOrDefault( file => file.Path.Equals( path, StringComparison.OrdinalIgnoreCase ) );
				if ( file.Source is null || !_indexedParticleFiles.Add( file.Path ) ) return;
			}
			IndexParticleFile( file );
		}
	}

	void RecordParticleFailure( string path, string error )
	{
		bool shouldLog;
		lock ( _particleCatalogLock )
		{
			_particleFileFailures++;
			shouldLog = _particleFailureLogs++ < 16;
		}
		if ( shouldLog )
			Titanfall2Log.Warning( $"Unable to index Titanfall 2 particle library '{path}': {error}" );
	}

	internal string[] GetParticleDefinitionNames()
	{
		lock ( _particleCatalogLock ) return _particleDefinitions.Keys.OrderBy( name => name, StringComparer.OrdinalIgnoreCase ).ToArray();
	}

	readonly record struct ParticleFileSource( string Path, ITitanfall2AssetSource Source );
}

readonly record struct ParticleCatalogProgress(
	int ProcessedFiles,
	int AddedDefinitions,
	int IndexedFiles,
	int TotalFiles,
	int TotalDefinitions,
	int FailedFiles,
	bool IsComplete );
