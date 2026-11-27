// SceneFile and resource caches are owned by the official engine. Scene-local
// buffers/collision are disposed by their components; parsed mount caches can
// be trimmed between builds without destroying resources used by live scenes.
public partial class Titanfall2Mount
{
	int _mapSceneLeases;
	internal void BeginMapBuild( Scene buildScene ) { }
	internal void AddMapSceneLease() => _mapSceneLeases++;
	internal void OnSceneOwnerDestroyed( Scene scene ) { if ( _mapSceneLeases > 0 ) _mapSceneLeases--; }
	void ResetMapSceneLeases() => _mapSceneLeases = 0;
	internal void ReleaseSceneResourcesBeforeBuild( MapLoader building )
	{
		if ( _mapSceneLeases == 0 ) ReleaseMountCaches();
	}
}
