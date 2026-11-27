/// <summary>
/// Restores the world material buffer for each cloned scene, using the engine's view attributes.
/// No texture upload, geometry preparation or material capture runs in the frame loop.
/// </summary>
[Library, Hide]
public sealed class Titanfall2WorldMaterialTableController : Component, Component.ExecuteInEditor, Component.DontExecuteOnServer
{
	// Generated materials have no disk loader. The map build keeps their wrappers alive;
	// serializing Material[] would try to reload them through the mount's file loader.
	[Property, Hide] public string SourceKey { get; set; }
	[System.Text.Json.Serialization.JsonIgnore] public Material[] Sources => Titanfall2WorldMaterialBatch.Resolve( SourceKey );
	GpuBuffer<Titanfall2GpuMaterialData> _buffer;

	protected override Task OnLoad( LoadingContext context ) { Restore(); return Task.CompletedTask; }
	protected override void OnEnabled() => Restore();
	protected override void OnStart() => Restore();

	void Restore()
	{
		if ( _buffer is not null || string.IsNullOrEmpty( SourceKey ) ) return;
		var sources = Sources;
		if ( sources.Length == 0 || sources.Any( material => material is null || !material.IsValid ) )
			throw new InvalidOperationException( "World material table source lease is unavailable." );
		// Native aggregate buffers are view attributes. Like the BSP light-page controller,
		// this backend expects one world in a scene; reject overlap instead of binding wrong rows.
		if ( Scene.GetAllComponents<Titanfall2WorldMaterialTableController>().Any( other => other != this && other._buffer is not null ) )
			throw new NotSupportedException( "World material tables currently support one mounted world per scene." );
		_buffer = new( sources.Length );
		try
		{
			_buffer.SetData( sources.Select( Titanfall2GpuMaterialData.Capture ).ToArray() );
			Scene.RenderAttributes.Set( "T2WorldMaterials", _buffer );
		}
		catch { _buffer.Dispose(); _buffer = null; throw; }
	}

	protected override void OnDisabled() => Release();
	protected override void OnDestroy() => Release();

	void Release()
	{
		if ( _buffer is null ) return;
		Scene.RenderAttributes.Set( "T2WorldMaterials", (GpuBuffer)null );
		_buffer.Dispose();
		_buffer = null;
	}
}
