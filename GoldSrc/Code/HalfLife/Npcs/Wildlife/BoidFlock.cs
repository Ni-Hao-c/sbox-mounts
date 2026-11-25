namespace GoldSrc.HalfLife;

[Title( "Boid Flock" ), Category( "Half-Life" )]
[NpcDefinition( "monster_flyer_flock", "boid", MonsterClass.None, 0, 0, 0, 0 )]
public sealed class BoidFlock : Component
{
	[Property, Range( 1, 64 )] public int Count { get; set; } = 6;
	[Property] public float Radius { get; set; } = 96;

	readonly List<Boid> _members = [];
	internal IReadOnlyList<Boid> Members => _members;

	internal void Join( Boid bird ) => _members.Insert( _members.Count == 0 ? 0 : 1, bird );
	internal void Leave( Boid bird ) => _members.Remove( bird );

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;
		var prefab = PrefabFile.Load( "mount://hl1/entities/npcs/monster_flyer.prefab" );
		if ( prefab is null ) return;
		for ( int i = 0; i < Count; ++i )
		{
			var offset = new Vector3( Game.Random.Float( -Radius, Radius ), Game.Random.Float( -Radius, Radius ), Game.Random.Float( 0, 16 ) );
			var member = GameObject.Clone( prefab, new Transform( WorldPosition + offset, WorldRotation ), startEnabled: false );
			member.SetParent( GameObject, true );
			member.Enabled = true;
			member.NetworkSpawn( true, null );
		}
	}
}
