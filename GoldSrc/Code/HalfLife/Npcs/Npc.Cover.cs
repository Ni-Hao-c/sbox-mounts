using Sandbox.Navigation;

namespace GoldSrc.HalfLife;

public partial class Npc
{
	protected bool CanReach( Vector3 point ) => Scene.NavMesh.CalculatePath( new()
	{
		Start = WorldPosition, Target = point, Agent = GetComponent<NavMeshAgent>()
	} ).Status == NavMeshPathStatus.Complete;

	protected Vector3? FindCover( Vector3 danger, GameObject source, bool allowOpenGround )
	{
		var away = (WorldPosition - danger).WithZ( 0 ).Normal;
		if ( away.LengthSquared < 0.01f ) away = WorldRotation.Backward;
		Vector3? fallback = null;
		var bestDistance = WorldPosition.DistanceSquared( danger );

		for ( int radius = 128; radius <= 384; radius += 128 )
		{
			for ( int i = 0; i < 7; i++ )
			{
				var yaw = ((i + 1) / 2) * (i % 2 == 0 ? -45 : 45);
				var offset = Rotation.FromYaw( yaw ) * away * radius;
				if ( Movement.ProjectPoint( WorldPosition + offset ) is not { } point || point.DistanceSquared( WorldPosition ) < 32 * 32 ) continue;
				var blocked = Scene.Trace.Ray( danger, point + Vector3.Up * EyeHeight ).IgnoreGameObjectHierarchy( source )
					.WithoutTags( "npc", "player", "playercontroller", "trigger", "debris" ).Run();
				var distance = point.DistanceSquared( danger );
				if ( blocked.Hit && distance > 64 * 64 && CanReach( point ) ) return point;
				if ( allowOpenGround && radius == 384 && distance > bestDistance && CanReach( point ) )
				{
					fallback = point;
					bestDistance = distance;
				}
			}
		}
		return fallback;
	}
}
