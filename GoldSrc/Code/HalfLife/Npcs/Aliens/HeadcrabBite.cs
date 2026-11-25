namespace GoldSrc.HalfLife;

[Hide]
public sealed class HeadcrabBite : Component, Component.ICollisionListener
{
	internal Headcrab Crab { get; set; }
	void ICollisionListener.OnCollisionStart( Collision collision ) => Crab?.Touch( collision );
}
