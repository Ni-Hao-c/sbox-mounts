namespace GoldSrc;

internal sealed class AmmoLoader( string title, int maxReserve ) : ResourceLoader<GameMount>
{
	protected override object Load() => new BaseAmmoResource { Title = title, MaxReserve = maxReserve };
}
