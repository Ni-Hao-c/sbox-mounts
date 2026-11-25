namespace GoldSrc.HalfLife;

[Title( "Bloater" ), Category( "Half-Life" )]
[NpcDefinition( "monster_bloater", "floater", MonsterClass.AlienMonster, 40, 16, 72, 0, Movement = NpcMovement.Flying, IdleSequence = "idle1", Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class Bloater : Npc
{
	internal override void OnModelLoaded( ModelLoader loader )
	{
		EyeHeight = loader.EyePosition.z;
	}

	internal override void IdleMovement() { }

	// The shipped model supplies just one sequence, including for an ordinary death.
	protected override GameObject CreateCorpse() => Find( "idle1" ) is { } pose
		? NpcCorpse.Spawn( this, pose ) : base.CreateCorpse();
}
