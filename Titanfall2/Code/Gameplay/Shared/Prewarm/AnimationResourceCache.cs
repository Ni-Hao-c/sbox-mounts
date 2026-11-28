using Sandbox.Animation;

/// <summary>Mount-lifetime immutable assets only; never retains a scene, renderer or playback graph.</summary>
internal sealed class Titanfall2AnimationResourceCache( Func<Titanfall2WeaponPrewarmKind, string, object> load )
{
	readonly Dictionary<(Titanfall2WeaponPrewarmKind Kind, string Path), (object Resource, string Error)> _entries = [];
	internal int Count => _entries.Count;
	internal int LoadAttempts { get; private set; }
	internal static string Normalize( string path ) => (path ?? "").Trim().Replace( '\\', '/' ).ToLowerInvariant();
	internal object Get( Titanfall2WeaponPrewarmKind kind, string path )
	{
		var key = (kind, Normalize( path ));
		if ( key.Item2.Length == 0 ) throw new InvalidDataException( "Empty animation resource path." );
		if ( !_entries.TryGetValue( key, out var entry ) )
		{
			LoadAttempts++;
			try { entry = (load( kind, key.Item2 ) ?? throw new InvalidDataException( "Loader returned null." ), null); }
			catch ( Exception error ) { entry = (null, $"{kind} unavailable: {key.Item2}: {error.Message}"); }
			_entries.Add( key, entry );
		}
		// A missing asset is attempted once per mount, too; retries from every NPC must not repeat IO.
		if ( entry.Error is not null ) throw new InvalidDataException( entry.Error );
		return entry.Resource;
	}
	internal void Clear() { _entries.Clear(); LoadAttempts = 0; }
}

public partial class Titanfall2Mount
{
	internal readonly Titanfall2AnimationResourceCache InteractionResources = new( LoadInteractionResource );
	internal Model GetInteractionModel( string path ) => (Model)InteractionResources.Get( Titanfall2WeaponPrewarmKind.InteractionModel, path );
	internal Skeleton GetInteractionSkeleton( string path ) => (Skeleton)InteractionResources.Get( Titanfall2WeaponPrewarmKind.AnimationSkeleton, path );
	internal Clip GetInteractionClip( string path ) => (Clip)InteractionResources.Get( Titanfall2WeaponPrewarmKind.AnimationClip, path );
	static object LoadInteractionResource( Titanfall2WeaponPrewarmKind kind, string path )
	{
		switch ( kind )
		{
			case Titanfall2WeaponPrewarmKind.InteractionModel:
				var model = Model.Load( path );
				return model is { IsError: false } ? model : throw new InvalidDataException( "Missing model." );
			case Titanfall2WeaponPrewarmKind.AnimationSkeleton:
				var skeleton = Skeleton.Load( path );
				return skeleton.IsValid() ? skeleton : throw new InvalidDataException( "Missing skeleton." );
			case Titanfall2WeaponPrewarmKind.AnimationClip:
				var clip = Clip.Load( path );
				return clip is not null && float.IsFinite( clip.Duration ) && clip.Duration > 0 && !clip.IsAdditive
					? clip : throw new InvalidDataException( "Missing or non-body animation clip." );
			default: throw new InvalidDataException( "Unsupported animation resource kind." );
		}
	}
}
