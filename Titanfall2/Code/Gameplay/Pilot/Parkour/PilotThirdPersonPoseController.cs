/// <summary>
/// Drives native Citizen 3P body orientation, lean pitch/roll, synthetic climb velocity, and special movement states.
/// </summary>
internal sealed class Titanfall2PilotThirdPersonPoseController
{
	float _bodyPitch;
	float _bodyRoll;

	internal void Reset()
	{
		_bodyPitch = 0f;
		_bodyRoll = 0f;
	}

	internal static Titanfall2ThirdPersonBodyPose ComputePose( Titanfall2PilotMoveMode host )
	{
		var wallrun = host.Wallrun;
		var wallhang = host.Wallhang;
		var mantle = host.Mantle;
		var slide = host.Slide;

		var activeNormal = wallrun.IsActive ? wallrun.WallNormal
			: wallhang.IsActive ? wallhang.WallNormal
			: mantle.IsActive ? mantle.WallNormal
			: Vector3.Zero;
		var wallSide = wallrun.IsActive ? wallrun.WallSide : wallhang.IsActive ? wallhang.WallSide : 0f;

		return Titanfall2PilotParkourRules.ComputeThirdPersonBodyPose(
			mantle.IsActive, mantle.Progress, wallhang.IsActive, wallrun.IsActive,
			wallSide, activeNormal, slide.IsActive, slide.Blend, slide.Direction );
	}

	internal void RotateRenderBody( Titanfall2PilotMoveMode host, SkinnedModelRenderer renderer, Action<SkinnedModelRenderer> baseRotate )
	{
		var pose = ComputePose( host );

		// Strip previous pitch/roll so base yaw logic operates on a clean horizontal heading.
		var flatForward = renderer.WorldRotation.Forward.WithZ( 0 );
		var currentYaw = flatForward.LengthSquared > 0.001f
			? Rotation.LookAt( flatForward.Normal, Vector3.Up ).Yaw()
			: host.Controller.EyeTransform.Rotation.Yaw();
		renderer.WorldRotation = Rotation.FromYaw( currentYaw );

		var dt = Math.Clamp( Time.Delta, 0f, 0.05f );
		if ( pose.FacingDirection is { } facing )
		{
			var targetYaw = Rotation.LookAt( facing, Vector3.Up ).Yaw();
			renderer.WorldRotation = Rotation.Slerp( renderer.WorldRotation, Rotation.FromYaw( targetYaw ), Math.Clamp( dt * 12f, 0f, 1f ) );
		}
		else
		{
			baseRotate( renderer );
		}

		var tiltRate = Math.Clamp( dt * 14f, 0f, 1f );
		_bodyPitch = MathX.Lerp( _bodyPitch, pose.PitchDegrees, tiltRate, true );
		_bodyRoll = MathX.Lerp( _bodyRoll, pose.RollDegrees, tiltRate, true );
		if ( MathF.Abs( _bodyPitch ) > 0.05f || MathF.Abs( _bodyRoll ) > 0.05f )
		{
			var yaw = renderer.WorldRotation.Angles().yaw;
			renderer.WorldRotation = Rotation.FromYaw( yaw ) * Rotation.From( new Angles( _bodyPitch, 0f, _bodyRoll ) );
		}
	}

	internal static void UpdateAnimatorVelocity(
		Titanfall2PilotMoveMode host,
		SkinnedModelRenderer renderer,
		Action<SkinnedModelRenderer, string, float> setFloat )
	{
		if ( host.Mantle.IsActive )
		{
			// Mantle interpolates position with zero rigidbody velocity; feed climb motion so Citizen pulls upward.
			setFloat( renderer, "move_x", 80f );
			setFloat( renderer, "move_y", 0f );
			setFloat( renderer, "move_z", 140f );
			setFloat( renderer, "move_groundspeed", 80f );
		}
		else if ( host.Wallhang.IsActive )
		{
			setFloat( renderer, "move_x", 0f );
			setFloat( renderer, "move_y", 0f );
			setFloat( renderer, "move_z", 0f );
			setFloat( renderer, "move_groundspeed", 0f );
		}
		else if ( host.Wallrun.IsActive )
		{
			var speed = host.Controller.Velocity.WithZ( 0 ).Length;
			setFloat( renderer, "move_x", speed );
			setFloat( renderer, "move_groundspeed", speed );
		}
	}

	internal static void UpdateAnimatorState(
		Titanfall2PilotMoveMode host,
		SkinnedModelRenderer renderer,
		Action<SkinnedModelRenderer, string, int> setInt,
		Action<SkinnedModelRenderer, string, bool> setBool,
		Action<SkinnedModelRenderer, string, float> setFloat )
	{
		var pose = ComputePose( host );
		var attached = host.Wallrun.IsActive || host.Wallhang.IsActive || host.Mantle.IsActive;
		setInt( renderer, "special_movement_states", pose.SpecialMoveState );
		setBool( renderer, "b_climb", pose.Climbing );
		setBool( renderer, "b_grounded", host.Controller.IsOnGround || attached );
		setFloat( renderer, "duck", host.Slide.IsActive ? 1.0f : host.Controller.IsDucking ? 0.85f : 0f );
		setBool( renderer, "b_jump", !host.Controller.IsOnGround && !attached && host.Controller.Velocity.z > 40f );
		setBool( renderer, "b_noclip", false );
	}
}
