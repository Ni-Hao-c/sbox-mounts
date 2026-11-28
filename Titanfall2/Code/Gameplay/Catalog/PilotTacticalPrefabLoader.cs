/// <summary>Builds a Pilot tactical pickup from a mounted retail model.</summary>
internal sealed class Titanfall2PilotTacticalPrefabLoader( Titanfall2PilotTacticalKind kind, string worldModel ) : ResourceLoader<Titanfall2Mount>
{
	PrefabFile _prefab;

	protected override object Load()
	{
		var visualPath = $"mount://{Host.Ident}/{worldModel}.visual.vmdl";
		var title = Titanfall2PilotTacticalRules.ShortLabel( kind );
		var builder = new PrefabBuilder().WithName( Path )
			.WithMetadata( "Title", $"Pilot Tactical · {title}" )
			.WithMetadata( "PreviewModel", visualPath );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject( true, title );
			root.Tags.Add( "item" );
			root.Tags.Add( "equipment" );
			var item = root.AddComponent<Titanfall2PilotTacticalItem>();
			item.MountIdent = Host.Ident;
			item.Kind = kind;
			item.WorldModelPath = visualPath;
			item.Visual = new GameObject( root, true, "Tactical Pickup Visual" ).AddComponent<ModelRenderer>();
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
