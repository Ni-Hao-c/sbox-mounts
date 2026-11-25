namespace GoldSrc.HalfLife;

[Title( "Dead HEV Suit" ), Category( "Half-Life" )]
[NpcDefinition( "monster_hevsuit_dead", "player", MonsterClass.HumanPassive, 8, 0, 0, 0 )]
public sealed class DeadHevSuit : PosedCorpse
{
	public enum Position { Back, Sitting, Stomach, Table }
	[Property] public Position Pose { get; set; }
	internal override string PoseName => Pose switch
	{
		Position.Sitting => "deadsitting",
		Position.Stomach => "deadstomach",
		Position.Table => "deadtable",
		_ => "deadback"
	};

	internal override void SetBody() => Renderer.SetBodyGroup( "head", 1 );
}
