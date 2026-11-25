namespace GoldSrc.HalfLife;

[Title( "Rat" ), Category( "Half-Life" )]
[Alias( "GoldSrc.HalfLifeRat" )]
[NpcDefinition( "monster_rat", "bigrat", MonsterClass.Insect, 8, 5, 8, 55, Gibs = GibSet.Alien, Blood = BloodColor.Red )]
public sealed class Rat : Npc
{
	float? _alertYaw;

	protected override void Think()
	{
		if ( AdvanceAction() || Movement?.MoveTarget.HasValue == true )
		{
			_alertYaw = null;
			return;
		}

		if ( Senses.Disturbance is { Kind: "world" or "player" or "combat" or "explosion" or Sandbox.Npcs.StimulusKinds.Gunshot } sound )
		{
			var direction = (sound.Position - WorldPosition).WithZ( 0 );
			if ( !direction.IsNearlyZero() ) _alertYaw = direction.EulerAngles.yaw;
		}

		if ( _alertYaw is not { } target ) return;
		var yaw = WorldRotation.Yaw();
		var remaining = Angles.NormalizeAngle( target - yaw );
		var step = 90 * Time.Delta;
		WorldRotation = Rotation.FromYaw( yaw + System.Math.Clamp( remaining, -step, step ) );
		if ( System.MathF.Abs( remaining ) <= step ) _alertYaw = null;
	}

	internal override void OnModelLoaded( ModelLoader loader ) => EyeHeight = loader.EyePosition.z;
}
