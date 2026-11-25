namespace GoldSrc.HalfLife;

public sealed partial class Houndeye
{
	[Sync( SyncFlags.FromHost )] Houndeye PackLeader { get; set; }
	bool IsPackFollower => PackLeader.IsValid() && PackLeader != this;
	int PackSize => PackLeader.IsValid()
		? Scene.GetAllComponents<Houndeye>().Count( h => h.Health > 0 && h.PackLeader == PackLeader )
		: 1;

	void JoinPack()
	{
		if ( PackLeader.IsValid() ) return;
		foreach ( var other in Scene.GetAllComponents<Houndeye>() )
		{
			if ( other == this || other.Health <= 0 || other.IsPackFollower || WorldPosition.DistanceSquared( other.WorldPosition ) > 1024 * 1024 ) continue;
			if ( other.PackSize >= 4 ) continue;
			var sight = Scene.Trace.Ray( EyePosition, other.EyePosition ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "npc", "player", "playercontroller", "trigger" ).Run();
			if ( sight.Hit ) continue;
			other.PackLeader = other;
			PackLeader = other;
			return;
		}
	}

	internal override void OnEnemySpotted( GameObject target )
	{
		if ( !IsPackFollower ) Call( Sounds.Alert );
	}
}
