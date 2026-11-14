using Titanfall2;

/// <summary>Scene-local render lifetime, separate from compiled map data and collision work.</summary>
sealed class Titanfall2StaticPropRenderResources : IDisposable
{
	public Titanfall2LocalLightBuffer Lights { get; private set; }
	public bool IsPrepared { get; private set; }
	readonly List<SceneObject> _nativeObjects = new();
	public int NativeObjectCount => _nativeObjects.Count;
	bool _disposed;

	// Clusters and instances borrow these objects for visibility updates. Only this
	// scene owner deletes them, including partial-load and native fallback objects.
	public void Track( SceneObject sceneObject )
	{
		if ( _disposed )
		{
			sceneObject?.Delete();
			throw new ObjectDisposedException( nameof( Titanfall2StaticPropRenderResources ) );
		}
		if ( sceneObject is not null ) _nativeObjects.Add( sceneObject );
	}

	// Called only by the loading transaction. Headless/server/data-only scenes allocate no GPU buffers.
	public void Prepare( string encodedLights )
	{
		ObjectDisposedException.ThrowIf( _disposed, this );
		if ( IsPrepared ) return;
		if ( !Application.IsDedicatedServer && Graphics.IsAvailable )
			Lights = Titanfall2LocalLightBuffer.Restore( encodedLights );
		IsPrepared = true;
	}

	public void Dispose()
	{
		if ( _disposed ) return;
		_disposed = true;
		foreach ( var sceneObject in _nativeObjects )
			if ( sceneObject.IsValid() ) sceneObject.Delete();
		_nativeObjects.Clear();
		Lights?.Dispose();
		Lights = null;
		IsPrepared = false;
	}
}
