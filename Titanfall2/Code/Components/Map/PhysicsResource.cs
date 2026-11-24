/// <summary>Rebinds model physics per prefab instance, avoiding anonymous native resource-name collisions.</summary>
[Library, Hide]
public sealed class Titanfall2PhysicsResource : Component
{
	[Property] public string MountIdent { get; set; } = "titanfall2";
	[Property] public string ModelPath { get; set; }
	[Property] public int Skin { get; set; }
	[Property] public int Body { get; set; }
	[Property] public bool Static { get; set; }
	[Property] public bool IsTrigger { get; set; }
	public bool Prepared { get; private set; }
	GameObject _runtime;
	Model _model;

	protected override Task OnLoad( LoadingContext context )
	{
		Prepared = false;
		if ( _runtime.IsValid() ) _runtime.Destroy();
		var mount = Sandbox.Mounting.Directory.Get( MountIdent ) as Titanfall2Mount;
		if ( mount is null || !mount.IsMounted || mount.GetByPath( ModelPath ) is not ModelLoader loader ) return Task.CompletedTask;
		var result = loader.GetCollisionVariant( Skin, Body );
		_model = result.Model;
		if ( !ModelLoader.IsUsableModel( _model ) || _model.Physics is not { Parts.Count: > 0 } physics ) return Task.CompletedTask;
		_runtime = new GameObject( GameObject, true, "Model physics runtime" );
		_runtime.LocalTransform = global::Transform.Zero;
		_runtime.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var collider = _runtime.AddComponent<PhysicsCollider>();
		collider.Physics = physics;
		collider.Static = Static;
		collider.IsTrigger = IsTrigger;
		var source = _runtime.AddComponent<Titanfall2CollisionSource>();
		source.MountIdent = MountIdent;
		source.ModelPath = ModelPath;
		source.GeometrySource = result.GeometrySource;
		// A model's raw contents are not an sprp instance's effective contents.
		_runtime.Enabled = Active;
		Prepared = true;
		return Task.CompletedTask;
	}

	protected override void OnEnabled() { if ( _runtime.IsValid() ) _runtime.Enabled = true; }
	protected override void OnDisabled() { if ( _runtime.IsValid() ) _runtime.Enabled = false; }
	protected override void OnDestroy()
	{
		if ( _runtime.IsValid() ) _runtime.Destroy();
		_runtime = null;
		_model = null;
		Prepared = false;
	}
}
