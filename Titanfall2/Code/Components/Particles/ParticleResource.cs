/// <summary>
/// Reconstructs the PCF converter's runtime controllers for each prefab instance.
/// Serializing the converter's private simulation state would lose ramps, ropes and children.
/// </summary>
[Library, Hide]
public sealed class Titanfall2ParticleResource : Component
{
	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public string EffectName { get; set; }
	public bool Prepared { get; private set; }
	GameObject _runtime;

	protected override async Task OnLoad( LoadingContext context )
	{
		Prepared = false;
		if ( _runtime.IsValid() ) _runtime.Destroy();
		var mount = Sandbox.Mounting.Directory.Get( MountIdent ) as Titanfall2Mount;
		if ( mount is null || !mount.IsMounted ) { Titanfall2Log.Warning( $"Particle mount '{MountIdent}' is unavailable." ); return; }
		while ( !mount.IsParticleCatalogComplete )
		{
			mount.IndexParticleFiles( 1, 3 );
			await Task.Yield();
			if ( !this.IsValid() || GameObject.IsDestroyed || !mount.IsMounted ) return;
		}
		if ( !mount.TryGetParticleDefinition( EffectName, out var definition ) )
		{ Titanfall2Log.Warning( $"Titanfall 2 particle '{EffectName}' does not exist." ); return; }
		_runtime = new GameObject( GameObject, true, "PCF runtime" );
		_runtime.LocalTransform = global::Transform.Zero;
		_runtime.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Prepared = Titanfall2ParticleFactory.TryCreate( _runtime, mount, definition, out _, out var error );
		if ( !Prepared ) { _runtime.Destroy(); _runtime = null; Titanfall2Log.Warning( error ); }
		else _runtime.Enabled = Active;
	}

	protected override void OnEnabled() { if ( _runtime.IsValid() ) _runtime.Enabled = true; }
	protected override void OnDisabled() { if ( _runtime.IsValid() ) _runtime.Enabled = false; }
	protected override void OnDestroy() { if ( _runtime.IsValid() ) _runtime.Destroy(); _runtime = null; Prepared = false; }
}
