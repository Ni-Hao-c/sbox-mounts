namespace GoldSrc.HalfLife;

public sealed partial class Nihilanth
{
	/// <summary>Destination for the teleport orb. A scene may also handle the hit through OnTeleport.</summary>
	[Property] public GameObject TeleportDestination { get; set; }
	[Property] public System.Action<GameObject> OnTeleport { get; set; }
	bool CanTeleport => TeleportDestination.IsValid() || OnTeleport is not null;
	readonly List<Npc> _reinforcements = [];

	void FireTeleport( GameObject enemy )
	{
		var hand = Renderer.GetAttachment( "attachment3" )?.Position ?? WorldPosition;
		var velocity = WorldPosition - hand;
		velocity = velocity.WithZ( velocity.z * 0.2f );
		EmitSound( "x/x_attack", WorldPosition, attenuation: 0.2f );
		NpcProjectile.Fire<NihilanthTeleport>( GameObject, null, 0, enemy, hand, velocity.Normal, velocity.Length );
	}

	internal void SummonReinforcement( Vector3 contact, Vector3 normal )
	{
		_reinforcements.RemoveAll( friend => !friend.IsValid() || friend.Health <= 0 );
		if ( _reinforcements.Count >= 3 ) return;
		var outside = contact + normal * 40;
		if ( Game.Random.Int( 0, 1 ) == 0 )
		{
			SpawnFriend<AlienController>( outside + Vector3.Up * 64 );
			return;
		}
		if ( Scene.NavMesh.GetClosestPoint( outside ) is { } floor ) SpawnFriend<Vortigaunt>( floor );
	}

	void SpawnFriend<T>( Vector3 origin ) where T : Npc
	{
		var definition = Game.TypeLibrary.GetType( typeof( T ) ).GetAttribute<NpcDefinitionAttribute>();
		var half = new Vector3( definition.Radius, definition.Radius, definition.Height * 0.5f );
		var center = origin + Vector3.Up * (half.z + 1);
		var trace = Scene.Trace.Box( new BBox( -half, half ), center, center + Vector3.Up ).WithoutTags( "trigger" ).Run();
		if ( trace.StartedSolid || trace.Hit ) return;
		var prefab = GameObject.GetPrefab( $"mount://hl1/entities/npcs/{definition.ClassName}.prefab" );
		if ( !prefab.IsValid() ) return;
		var friend = prefab.Clone( new Transform( origin, WorldRotation ) );
		_reinforcements.Add( friend.GetComponent<T>() );
		friend.NetworkSpawn( true, null );
		EmitSound( "debris/beamstart7.wav", origin );
	}
}
