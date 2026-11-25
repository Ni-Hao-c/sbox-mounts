namespace GoldSrc.HalfLife;

[Title( "Dead Security Guard" ), Category( "Half-Life" )]
[NpcDefinition( "monster_barney_dead", "barney", MonsterClass.HumanPassive, 8, 0, 0, 0 )]
public sealed class DeadSecurityGuard : PosedCorpse
{
	public enum Position { Back, Side, Stomach }
	[Property] public Position Pose { get; set; }
	internal override string PoseName => Pose switch
	{
		Position.Side => "lying_on_side",
		Position.Stomach => "lying_on_stomach",
		_ => "lying_on_back"
	};
}
