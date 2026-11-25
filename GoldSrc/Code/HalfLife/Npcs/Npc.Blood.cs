namespace GoldSrc.HalfLife;

public partial class Npc
{
	public enum BloodColor { None, Yellow = 195, Red = 247 }

	internal static bool HitStudioModel( DamageInfo info ) => info.Hitbox is not null || info.Tags.Has( "studio_hitbox" );

	internal static ITagSet HitGroups( DamageInfo info ) => info.Hitbox?.Tags ?? info.Tags;

	internal virtual void ShowWound( Component.IDamageable.DamageEvent hit ) =>
		BloodEffects.Wound( GameObject, Definition?.Blood ?? BloodColor.None, hit.DamageInfo, hit.AcceptedDamage );
}
