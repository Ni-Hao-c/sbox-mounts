namespace GoldSrc.HalfLife;

[Hide]
public sealed class DeadBarnacle : NpcCorpse
{
	internal override void HandleEvent( int id )
	{
		if ( id == 2 ) Barnacle.CoughGib( WorldPosition );
	}
}
