using System.Globalization;
using Sandbox.Rendering;

public partial class Titanfall2Mount
{
	readonly Dictionary<string, DecalDefinition> _combatDecals = new( StringComparer.OrdinalIgnoreCase );
	internal DecalDefinition GetCombatDecal( string surface, bool scorch, string table )
	{
		var energy = table?.ToLowerInvariant() switch { "lstar" => "lstar", "exp_plasma_lg" or "exp_plasma_sm" => "plasma", "particle_accelerator" => "particle_accelerator", _ => null };
		var name = energy is not null ? $"decals/bullets/{energy}/shot1" : scorch ? "decals/scorch1"
			: surface is "M" or "G" or "V" or "E" ? "decals/bullets/metal/shot1"
			: surface == "W" ? "decals/bullets/wood/shot1" : surface is "Y" or "U" ? "decals/bullets/glass/shot1" : "decals/bullets/concrete/shot1";
		if ( _combatDecals.TryGetValue( name, out var found ) ) return found;
		var material = Material.Load( $"mount://{Ident}/{name}.vmat" );
		if ( material is null || !material.IsValid || !Titanfall2DecalFactory.TryCreateDefinition( material, out var definition, out _ ) ) return _combatDecals[name] = null;
		if ( !TryGetMaterialDescriptor( name, out var descriptor ) ) return _combatDecals[name] = null;
		var subrect = TryGetLegacyMaterialDefinition( name + "_subrect", out var wrapper, out _ ) ? wrapper : null;
		var color = definition.ColorTexture;
		var size = subrect?.GetString( "$size" )?.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		var scale = subrect?.GetFloat( "$decalscale", scorch ? 0.25f : 0.025f ) ?? (energy is not null ? 0.3f : scorch ? 0.25f : 0.025f);
		float Number( int index, int fallback ) => size is not null && size.Length > index && float.TryParse( size[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) ? number : fallback;
		definition.Width = Number( 0, color.Width ) * scale;
		definition.Height = Number( 1, color.Height ) * scale;
		if ( !descriptor.TextureBindings.Any( binding => binding.Semantic == Titanfall2TextureSemantic.Normal ) ) definition.NormalTexture = null;
		if ( !descriptor.TextureBindings.Any( binding => binding.Semantic == Titanfall2TextureSemantic.Emissive ) ) definition.EmissiveTexture = null;
		var opacity = descriptor.TextureBindings.Any( binding => binding.Semantic == Titanfall2TextureSemantic.Opacity ) ? material.GetTexture( "g_tOpacity" ) : null;
		if ( opacity.IsValid() && opacity != Texture.White )
		{
			var pixels = color.GetPixels(); var mask = opacity.GetPixels();
			var rgba = new byte[pixels.Length * 4];
			for ( var y = 0; y < color.Height; y++ )
			for ( var x = 0; x < color.Width; x++ )
			{
				var i = y * color.Width + x; var p = pixels[i];
				var alpha = mask[(y * opacity.Height / color.Height) * opacity.Width + x * opacity.Width / color.Width].r;
				rgba[i * 4] = p.r; rgba[i * 4 + 1] = p.g; rgba[i * 4 + 2] = p.b; rgba[i * 4 + 3] = (byte)(p.a * alpha / 255);
			}
			definition.ColorTexture = Texture.Create( color.Width, color.Height ).WithData( rgba ).Finish();
		}
		definition.BlendMode = energy is not null ? DecalBlendMode.Additive : DecalBlendMode.Multiply;
		if ( energy is not null && TryGetLegacyMaterialDefinition( name, out var energyMaterial, out _ ) )
		{
			var tint = energyMaterial.GetVector( "$layercolor2", Vector4.One );
			definition.Tint = new Color( tint.x, tint.y, tint.z );
		}
		return _combatDecals[name] = definition;
	}
}
