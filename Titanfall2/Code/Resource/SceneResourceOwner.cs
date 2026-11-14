/// <summary>
/// Releases resources owned exclusively by this scene. Shared mount caches have
/// the mount's lifetime and must survive destruction of an editor or play clone.
/// </summary>
[Library, Hide]
public sealed class Titanfall2SceneResourceOwner : Component, Component.DontExecuteOnServer
{
	readonly List<IDisposable> _ownedResources = new();

	[Property, Hide]
	public string MountIdent { get; set; }

	bool _leased;

	internal void Configure( string mountIdent, IEnumerable<IDisposable> ownedResources = null )
	{
		MountIdent = mountIdent;
		if ( ownedResources is not null )
			_ownedResources.AddRange( ownedResources.Where( static resource => resource is not null ) );
	}

	/// <summary>
	/// One lease per live map scene prevents trimming shared parsed data while scenes use it.
	/// Engine resource caches and the registered SceneFile have their own official lifetime.
	/// </summary>
	protected override void OnAwake()
	{
		if ( _leased || string.IsNullOrWhiteSpace( MountIdent ) ) return;
		if ( Sandbox.Mounting.Directory.Get( MountIdent ) is not Titanfall2Mount mount ) return;
		_leased = true;
		mount.AddMapSceneLease();
	}

	protected override void OnDestroy()
	{
		foreach ( var resource in _ownedResources )
		{
			try
			{
				resource.Dispose();
			}
			catch ( Exception exception )
			{
				// One failing native resource must not prevent the remaining releases or
				// strand this scene's mount lease forever.
				Titanfall2Log.Warning( exception, $"Titanfall 2 scene resource release failed ({resource.GetType().Name})." );
			}
		}
		_ownedResources.Clear();
		// Only a component that took a lease returns one. This does not invalidate
		// official loader caches or destroy resources retained by another scene.
		if ( !_leased ) return;
		_leased = false;
		if ( Sandbox.Mounting.Directory.Get( MountIdent ) is Titanfall2Mount mount )
			mount.OnSceneOwnerDestroyed( Scene );
	}
}
