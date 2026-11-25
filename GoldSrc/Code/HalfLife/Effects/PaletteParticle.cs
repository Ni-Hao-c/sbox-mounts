namespace GoldSrc.HalfLife;

internal static class PaletteParticle
{
	static Sprite _sprite;
	internal static Sprite Sprite => _sprite ??= CreateParticleSprite();

	static Sprite CreateParticleSprite()
	{
		var pixels = new byte[8 * 8 * 4];
		for ( int y = 0; y < 8; y++ )
		for ( int x = 0; x < 8; x++ )
		{
			int i = (y * 8 + x) * 4;
			pixels[i] = pixels[i + 1] = pixels[i + 2] = 255;
			if ( x < 4 && y < 4 && !((x == 0 || x == 3) && (y == 0 || y == 3)) ) pixels[i + 3] = 255;
		}
		return Sprite.FromTexture( Texture.Create( 8, 8 ).WithData( pixels ).Finish() );
	}
}
