using System;

namespace GoldSrc.HalfLife;

public abstract partial class Personnel
{
	const int TurnLeft = 15, TurnRight = 16;
	StudioSequence _turnLeft, _turnRight;
	[Sync( SyncFlags.FromHost )] int TurnActivity { get; set; }
	protected virtual float StandingYawSpeed => 70;
	protected virtual bool HasSpecialFacing => false;

	internal override void OnModelLoaded( ModelLoader loader )
	{
		base.OnModelLoaded( loader );
		if ( Definition.Movement != NpcMovement.Ground ) return;
		_turnLeft = loader.Sequences.FirstOrDefault( s => s.Description.Activity == TurnLeft );
		_turnRight = loader.Sequences.FirstOrDefault( s => s.Description.Activity == TurnRight );
		if ( _turnLeft is not null || _turnRight is not null ) Animation.TurnsBody = false;
	}

	internal override IEnumerable<StudioSequence> ExtraSequences => [.. base.ExtraSequences, _turnLeft, _turnRight];

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && (_turnLeft is not null || _turnRight is not null) && !HasSpecialFacing )
		{
			if ( Health > 0 && !PhysicsCarrier.IsValid() && !IsAirborne && !_previousTurnsBody.HasValue &&
				Movement?.IsMoving != true && Animation.IsLooking )
				FaceStandingTarget( Animation.CalculateLookPosition(), StandingYawSpeed, Time.Delta );
			else TurnActivity = 0;
		}
		base.OnUpdate();
	}

	// GoldSrc SetTurnActivity and ChangeYaw. Keep the chosen clip until the body reaches ideal yaw.
	protected bool FaceStandingTarget( Vector3 target, float yawSpeed, float elapsed )
	{
		var direction = (target - WorldPosition).WithZ( 0 );
		var yaw = WorldRotation.Yaw();
		var delta = direction.IsNearZeroLength ? 0 : Angles.NormalizeAngle( direction.EulerAngles.yaw - yaw );
		if ( delta <= -45 ) TurnActivity = TurnRight;
		else if ( delta > 45 ) TurnActivity = TurnLeft;
		var step = 2 * yawSpeed * MathF.Min( elapsed, 0.25f );
		WorldRotation = Rotation.FromYaw( yaw + Math.Clamp( delta, -step, step ) );
		if ( MathF.Abs( delta ) > step ) return false;
		TurnActivity = 0;
		return true;
	}

	internal override StudioSequence MovementSequence( float speed ) =>
		base.MovementSequence( speed ) ?? (TurnActivity == TurnLeft ? _turnLeft : TurnActivity == TurnRight ? _turnRight : null);
}
