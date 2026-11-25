namespace GoldSrc.HalfLife;

[Title( "Furniture" ), Category( "Half-Life" )]
[NpcDefinition( "monster_furniture", "filecabinet", MonsterClass.None, 80000, 16, 64, 0, Movement = NpcMovement.Stationary )]
public sealed class Furniture : Npc
{
	StudioSequence _first;
	internal override StudioSequence RestSequence => _first;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		_first = loader.Sequences.FirstOrDefault();
		if ( _first is null ) return;
		var bounds = new BBox( _first.Description.Min, _first.Description.Max );
		var hull = GetComponentInChildren<BoxCollider>();
		hull.Center = bounds.Center;
		hull.Scale = bounds.Size;
	}

	protected override void Think() => AdvanceAction();
	protected override GameObject CreateCorpse() => null;
}
