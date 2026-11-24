/// <summary>Bounded lazy PCF preparation shared with the map particle catalog.</summary>
[Hide]
public sealed class Titanfall2PlasmaFlight : Component
{
	[Property] public string MountIdent { get; set; }
	[Property] public string EffectName { get; set; }
	[Property] public bool CombatImpact { get; set; }
	bool _finished;
	protected override void OnUpdate()
	{
		if ( _finished ) return;
		if ( Sandbox.Mounting.Directory.Get( MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount || !mount.IsMounted ) return;
		if ( !mount.TryPrepareWeaponParticle( EffectName, out var definition ) )
		{
			if ( mount.IsParticleCatalogComplete )
			{
				_finished = true;
				Titanfall2Log.Warning( $"Weapon particle '{EffectName}' was not found in mount '{mount.Ident}'." );
			}
			return;
		}
		_finished = true;
		if ( definition is not null )
		{
			var systems = CombatImpact ? new List<Titanfall2ParticleFactory.CreatedSystem>() : null;
			if ( !Titanfall2ParticleFactory.TryCreate( GameObject, mount, definition, out _, out var error, systems ) )
				Titanfall2Log.Warning( $"Weapon particle '{EffectName}' could not be created: {error}" );
			if ( systems is null ) return;
			foreach ( var system in systems )
			{
				if ( !mount.TryGetLegacyMaterialDefinition( system.Definition.MaterialName, out var material, out _ ) ) continue;
				var metadata = material.GetMetadata( system.Definition.MaterialName );
				// Native sprite rendering cannot reproduce a projected decal, scene refraction,
				// multiplicative blending or a second opacity texture. Keep supported children;
				// the combat presenter owns the projected mark using the original decal assets.
				if ( !metadata.IsDecal && !material.ShaderName.Contains( "Refract", StringComparison.OrdinalIgnoreCase )
					&& !material.ShaderName.Contains( "Decal", StringComparison.OrdinalIgnoreCase )
					&& !material.ShaderName.Contains( "Modulate", StringComparison.OrdinalIgnoreCase )
					&& !material.IsUnlitTwoTexture && string.IsNullOrEmpty( material.GetTexture( "$opacitytexture", "$alphamask" ) ) ) continue;
				foreach ( var renderer in system.Effect.GameObject.GetComponents<ParticleSpriteRenderer>() ) renderer.Enabled = false;
				foreach ( var renderer in system.Effect.GameObject.GetComponents<Titanfall2ParticleRampRenderer>() ) renderer.Enabled = false;
			}
		}
	}
}
