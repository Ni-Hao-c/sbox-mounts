using System.Text.Json;

/// <summary>Discoverable PCF effects, exposed as ordinary cloneable PrefabFiles.</summary>
sealed class Titanfall2ParticleCatalogLoader : ResourceLoader<Titanfall2Mount>
{
	protected override object Load()
	{
		while ( !Host.IsParticleCatalogComplete ) Host.IndexParticleFiles( 1, 1000 );
		return JsonSerializer.Serialize( Host.GetParticleDefinitionNames().Select( name => new { Name = name, Prefab = $"{Path}/{Uri.EscapeDataString( name )}.prefab" } ) );
	}

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		if ( !name.EndsWith( ".prefab", StringComparison.OrdinalIgnoreCase ) || name.Contains( '/' ) || name.Contains( '\\' ) ) return null;
		var effect = Uri.UnescapeDataString( name[..^7] );
		if ( string.IsNullOrWhiteSpace( effect ) ) return null;
		type = ResourceType.PrefabFile;
		return new ParticlePrefabLoader( effect );
	}

	sealed class ParticlePrefabLoader( string effect ) : ResourceLoader<Titanfall2Mount>
	{
		PrefabFile _prefab;
		protected override object Load()
		{
			// Resolve the complete child graph before publishing a prefab. Indexing is lazy:
			// it happens only when explicitly browsing/loading particle resources.
			while ( !Host.IsParticleCatalogComplete ) Host.IndexParticleFiles( 1, 1000 );
			if ( !Host.TryGetParticleDefinition( effect, out _ ) ) return null;
			var builder = new PrefabBuilder().WithName( Path );
			using ( builder.Scope() )
			{
				Game.ActiveScene.Enabled = false;
				var root = new GameObject( true, effect );
				root.AddComponent<Titanfall2ParticleControlPoints>();
				var resource = root.AddComponent<Titanfall2ParticleResource>();
				resource.MountIdent = Host.Ident;
				resource.EffectName = effect;
			}
			return _prefab = builder.Create();
		}
		protected override void Shutdown()
		{
			PrefabBuilder.Destroy( _prefab );
			_prefab = null;
		}
	}
}
