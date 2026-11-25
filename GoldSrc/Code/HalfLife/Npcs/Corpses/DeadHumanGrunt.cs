namespace GoldSrc.HalfLife;

[Title( "Dead Human Grunt" ), Category( "Half-Life" )]
[NpcDefinition( "monster_hgrunt_dead", "hgrunt", MonsterClass.HumanPassive, 8, 0, 0, 0 )]
public sealed class DeadHumanGrunt : PosedCorpse
{
	public enum Position { Stomach, Side, Sitting }
	[Property] public Position Pose { get; set; }
	internal override string PoseName => Pose switch
	{
		Position.Side => "deadside",
		Position.Sitting => "deadsitting",
		_ => "deadstomach"
	};

	[Property] public bool Commander { get; set; }
	[Property] public bool HasGun { get; set; } = true;
	internal override void SetBody()
	{
		Renderer.SetBodyGroup( "heads", Commander ? 1 : 0 );
		Renderer.SetBodyGroup( "weapons", HasGun ? 0 : 2 );
	}
}
