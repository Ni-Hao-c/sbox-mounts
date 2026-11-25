namespace GoldSrc.HalfLife;

[Hide]
public sealed class DeadLeech : NpcCorpse
{
	internal override string FinalSequence => "death_end";
	internal override bool WaitForFloor => true;
	bool DiedUnderwater => DeathSequence == "death";
	internal override float GravityScale => DiedUnderwater ? 0.02f : 1;

	float _spin;
	TimeUntil _nextWallCheck;

	protected override void OnStart()
	{
		if ( Networking.IsHost && DiedUnderwater )
		{
			WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() );
			WorldPosition += Vector3.Up;
			if ( Game.Random.Int( 0, 99 ) < 70 ) _spin = Game.Random.Int( -720, 720 );
		}

		base.OnStart();
	}

	protected override void OnFixedUpdate()
	{
		if ( Networking.IsHost && !Landed && _nextWallCheck <= 0 && (Velocity.x != 0 || Velocity.y != 0) )
		{
			_nextWallCheck = 0.1f;
			var wall = Scene.Trace.Ray( WorldPosition, WorldPosition + Velocity * 0.5f )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( wall.Hit ) Velocity = Vector3.Up * Velocity.z;
		}

		base.OnFixedUpdate();
		if ( Networking.IsHost && !Landed && _spin != 0 ) WorldRotation *= Rotation.FromYaw( _spin * Time.Delta );
	}
}
