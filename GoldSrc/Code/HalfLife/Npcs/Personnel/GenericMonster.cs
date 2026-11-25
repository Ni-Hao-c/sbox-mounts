namespace GoldSrc.HalfLife;

[Title( "Generic Monster" ), Category( "Half-Life" )]
[NpcDefinition( "monster_generic", "holo", MonsterClass.PlayerAlly, 8, 16, 72, 90, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class GenericMonster : ScriptedActor
{
}
