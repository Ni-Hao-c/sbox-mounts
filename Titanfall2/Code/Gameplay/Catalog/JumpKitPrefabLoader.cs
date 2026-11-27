/// <summary>Builds the spawnable Pilot Jump Kit equipment prefab.</summary>
internal sealed class Titanfall2JumpKitPrefabLoader( string title, string worldModel, string packModel, Titanfall2PilotSettings settings ) : ResourceLoader<Titanfall2Mount>
{
	PrefabFile _prefab;

	protected override object Load()
	{
		var visualPath = $"mount://{Host.Ident}/{worldModel}.visual.vmdl";
		var packPath = packModel is null ? null : $"mount://{Host.Ident}/{packModel}.visual.vmdl";
		var builder = new PrefabBuilder().WithName( Path )
			.WithMetadata( "Title", title )
			.WithMetadata( "PreviewModel", visualPath );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, title );
			root.Tags.Add( "item" );
			var item = root.AddComponent<Titanfall2JumpKitItem>();
			item.MountIdent = Host.Ident;
			item.WorldModelPath = visualPath;
			item.PackModelPath = packPath;
			item.Settings = settings;
			var visualGo = new GameObject( root, true, "JumpKit Visual" );
			item.Visual = visualGo.AddComponent<ModelRenderer>();
			var glowGo = new GameObject( root, true, "JumpKit Accent Light" ) { LocalPosition = Vector3.Up * 8f };
			var light = glowGo.AddComponent<PointLight>();
			light.LightColor = new Color( 0.18f, 0.72f, 1f ) * 0.85f;
			light.Radius = 48f;
			light.Shadows = false;
			var hull = root.AddComponent<BoxCollider>();
			hull.Scale = new Vector3( 16f, 16f, 16f );
			hull.Center = Vector3.Up * 8f;
			root.AddComponent<Rigidbody>();
		}
		return _prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
	}
}
