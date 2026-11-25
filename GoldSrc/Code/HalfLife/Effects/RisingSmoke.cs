namespace GoldSrc.HalfLife;

[Hide]
public sealed class RisingSmoke : Component
{
	protected override void OnUpdate() => WorldPosition += Vector3.Up * (30 * Time.Delta);
}
