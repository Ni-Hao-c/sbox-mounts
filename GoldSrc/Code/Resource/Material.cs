namespace GoldSrc;

class MaterialLoader( string texturePath, string textureName ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var material = Material.Create( Path, "goldsrc" );
		material?.Set( "Color", Texture.Load( texturePath ) );
		if ( material is not null )
			Host.BindSurface( material, textureName );

		return material;
	}
}
