namespace GoldSrc.HalfLife;

[Hide]
public sealed class DeadGrunt : NpcCorpse
{
	[Property] public bool CarriesLauncher { get; set; }
	[Sync( SyncFlags.FromHost ), Change( nameof( DropChanged ) )] bool Dropped { get; set; }

	internal override void HandleEvent( int id )
	{
		if ( id != 11 || Dropped ) return;
		Dropped = HumanGrunt.DropWeapon( Renderer, CarriesLauncher );
	}

	internal void TossWeapon()
	{
		if ( !Dropped ) Dropped = HumanGrunt.DropWeapon( Renderer, CarriesLauncher, true );
	}

	void DropChanged( bool before, bool after ) => SetBody();

	internal override void SetBody()
	{
		if ( Dropped && Renderer.IsValid() && Renderer.Model.IsValid() ) Renderer.SetBodyGroup( "weapons", 2 );
	}
}
