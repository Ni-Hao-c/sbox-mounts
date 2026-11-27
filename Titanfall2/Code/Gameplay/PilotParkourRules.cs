/// <summary>
/// Pure deterministic physics, camera-tilt, and viewmodel-pose rules for Titanfall 2 Pilot parkour.
/// All constants default to retail pilot_mp.set + client.dll values so movement feels authentic and testable.
/// </summary>
internal static class Titanfall2PilotParkourRules
{
	// Retail 0x180202090 wall normal Z acceptance bounds.
	internal const float WallNormalMinZ = -0.35f;
	internal const float WallNormalMaxZ = 0.7f;
	internal const float WalkableNormalZ = 0.7f;
	internal const float MaxHorizontalSpeed = 1200f;
	internal const float FallCushionTriggerSpeed = 480f;
	internal const float FallCushionMaxLandingSpeed = 440f;

	/// <summary>Initial vertical speed to reach <paramref name="height"/> under <paramref name="gravity"/>.</summary>
	internal static float JumpSpeedForHeight( float height, float gravity ) =>
		height > 0f && gravity > 0f ? MathF.Sqrt( 2f * gravity * height ) : 0f;

	/// <summary>Source / TF2 air-strafe acceleration: preserves speeds above AirSpeed while allowing crisp turning.</summary>
	internal static Vector3 ApplyAirAcceleration( Vector3 velocity, Vector3 wishDir, float airSpeed, float airAccel, float dt )
	{
		if ( dt <= 0f || wishDir.LengthSquared < 0.0001f ) return velocity;
		var dir = wishDir.WithZ( 0 ).Normal;
		var flat = velocity.WithZ( 0 );
		var currentInWish = Vector3.Dot( flat, dir );
		var addSpeed = airSpeed - currentInWish;
		if ( addSpeed <= 0f ) return velocity;

		var accelSpeed = MathF.Min( airAccel * dt, addSpeed );
		var next = flat + dir * accelSpeed;
		return next.WithZ( velocity.z );
	}

	/// <summary>Applies retail double-jump vertical impulse and horizontal directional burst while preserving momentum.</summary>
	internal static Vector3 ComputeDoubleJumpVelocity( Vector3 velocity, Vector3 wishDir, in Titanfall2PilotSettings settings )
	{
		var jumpZ = JumpSpeedForHeight( settings.DoubleJumpHeight, settings.Gravity );
		var nextZ = MathF.Max( velocity.z, 0f ) * 0.2f + jumpZ;
		var flat = velocity.WithZ( 0 );

		if ( wishDir.LengthSquared > 0.01f )
		{
			var dir = wishDir.WithZ( 0 ).Normal;
			var speed = flat.Length;
			// Redirect momentum cleanly when turning in mid-air without losing high wallrun/slide speed.
			var targetSpeed = MathF.Max( speed, MathF.Max( settings.SprintSpeed, settings.DoubleJumpHorzSpeed ) );
			var aligned = Vector3.Dot( flat, dir );
			if ( aligned < settings.DoubleJumpHorzSpeed )
			{
				var redirected = Vector3.Lerp( flat, dir * targetSpeed, 0.55f );
				var boost = MathF.Min( settings.DoubleJumpHorzSpeed * 0.45f, MathF.Max( 0f, settings.WallrunMaxSpeedHorizontal - redirected.Length ) );
				flat = redirected + dir * boost;
			}
			else
			{
				var bonus = MathF.Min( 55f, MathF.Max( 0f, settings.WallrunMaxSpeedHorizontal - speed ) );
				flat += dir * bonus;
			}
		}

		return ClampHorizontal( flat.WithZ( nextZ ), MaxHorizontalSpeed );
	}

	/// <summary>Checks whether a surface normal qualifies as a runnable wall (retail 0x180202090 bounds).</summary>
	internal static bool IsValidWallNormal( Vector3 normal ) =>
		normal.LengthSquared > 0.5f && normal.z >= WallNormalMinZ && normal.z <= WallNormalMaxZ;

	/// <summary>Returns +1 when the wall is on the pilot's right, -1 when on the left, 0 if degenerate.</summary>
	internal static float WallSide( Vector3 lookForward, Vector3 wallNormal )
	{
		var fwd = lookForward.WithZ( 0 );
		var n = wallNormal.WithZ( 0 );
		if ( fwd.LengthSquared < 0.0001f || n.LengthSquared < 0.0001f ) return 0f;
		// Cross(Up, Forward) = Left in s&box (X forward, Y left, Z up).
		// Wall on RIGHT -> wallNormal points LEFT -> Dot(Left, wallNormal) > 0 -> +1.
		var left = Vector3.Cross( Vector3.Up, fwd.Normal );
		var dot = Vector3.Dot( left, n.Normal );
		return dot >= 0f ? 1f : -1f;
	}

	/// <summary>Computes unit horizontal tangent along the wall aligned with travel/look direction.</summary>
	internal static Vector3 WallTangent( Vector3 wallNormal, Vector3 referenceDirection )
	{
		var n = wallNormal.WithZ( 0 );
		if ( n.LengthSquared < 0.0001f ) return Vector3.Forward;
		n = n.Normal;
		var tangent = new Vector3( -n.y, n.x, 0f );
		var refFlat = referenceDirection.WithZ( 0 );
		if ( refFlat.LengthSquared > 0.0001f && Vector3.Dot( tangent, refFlat ) < 0f )
			tangent = -tangent;
		return tangent;
	}

	/// <summary>Prevents reattaching higher onto the same wall plane right after jumping off (retail 0x180202090).</summary>
	internal static bool IsSameWallBlocked( bool hasPreviousWall, Vector3 prevNormal, Vector3 prevPos,
		Vector3 candidateNormal, Vector3 candidatePos, in Titanfall2PilotSettings settings )
	{
		if ( !hasPreviousWall ) return false;
		var prevFlat = prevNormal.WithZ( 0 );
		var candFlat = candidateNormal.WithZ( 0 );
		if ( prevFlat.LengthSquared < 0.0001f || candFlat.LengthSquared < 0.0001f ) return false;
		if ( Vector3.Dot( prevFlat.Normal, candFlat.Normal ) <= settings.WallrunSameWallDot ) return false;

		var distSq = (candidatePos - prevPos).WithZ( 0 ).LengthSquared;
		if ( distSq <= settings.WallrunSameWallDist * settings.WallrunSameWallDist )
			return candidatePos.z >= prevPos.z + settings.WallrunSameWallHeight - 12f;

		return false;
	}

	/// <summary>
	/// Wallrun gravity multiplier over time (retail 0x18020e6f0 + slip ramp):
	/// starts at 0, ramps to 1 across WallrunGravityRampUpTime, and increases toward full gravity when slipping or idle.
	/// </summary>
	internal static float WallrunGravityFraction( float elapsed, bool hasForwardInput, in Titanfall2PilotSettings settings )
	{
		var t = MathF.Max( 0f, elapsed );
		var ramp = settings.WallrunGravityRampUpTime > 0.01f
			? Math.Clamp( t / settings.WallrunGravityRampUpTime, 0f, 1f )
			: 1f;

		// Early wallrun stays crisp and nearly weightless while pushing forward; late wallrun slips down.
		var baseFrac = ramp * ramp * 0.45f;
		if ( t > settings.WallrunSlipStartTime )
		{
			var slipT = settings.WallrunSlipDuration > 0.01f
				? Math.Clamp( (t - settings.WallrunSlipStartTime) / settings.WallrunSlipDuration, 0f, 1f )
				: 1f;
			baseFrac = float.Lerp( baseFrac, 1f, slipT );
		}

		if ( !hasForwardInput )
			baseFrac = MathF.Max( baseFrac, settings.WallrunNoInputSlipFrac );

		return Math.Clamp( baseFrac, 0f, 1f );
	}

	/// <summary>Initial velocity upon attaching to a wall (retail 0x18020b390 upward wall boost + wall plane projection).</summary>
	internal static Vector3 ComputeWallrunStartVelocity( Vector3 velocity, Vector3 wallNormal, Vector3 lookForward, in Titanfall2PilotSettings settings )
	{
		var tangent = WallTangent( wallNormal, velocity.WithZ( 0 ).LengthSquared > 900f ? velocity : lookForward );
		var alongSpeed = MathF.Max( Vector3.Dot( velocity.WithZ( 0 ), tangent ), settings.SprintSpeed * 0.9f );
		alongSpeed = MathF.Max( alongSpeed, MathF.Min( velocity.WithZ( 0 ).Length, settings.WallrunMaxSpeedHorizontal ) );

		// Retail 0x18020b390 applies an upward boost capped by wallrun_upWallBoost (250) when entering from a fall or flat jump.
		var upBoost = settings.WallrunUpWallBoost * 0.55f;
		var nextZ = velocity.z < upBoost ? MathF.Min( upBoost, MathF.Max( 40f, velocity.z * 0.35f + upBoost ) ) : MathF.Min( velocity.z, settings.WallrunMaxSpeedVertical );
		return ClampHorizontal( tangent * alongSpeed, MaxHorizontalSpeed ).WithZ( nextZ );
	}

	/// <summary>Advances velocity along the wall plane with retail acceleration, auto-push lift, and ramped gravity.</summary>
	internal static Vector3 StepWallrunVelocity( Vector3 velocity, Vector3 wallNormal, Vector3 wishDir, Vector3 lookForward,
		float elapsed, in Titanfall2PilotSettings settings, float dt )
	{
		var tangent = WallTangent( wallNormal, velocity.WithZ( 0 ).LengthSquared > 900f ? velocity : lookForward );
		var wishAlong = wishDir.LengthSquared > 0.01f ? Vector3.Dot( wishDir.WithZ( 0 ).Normal, tangent ) : 0f;
		var hasInput = wishAlong > 0.15f;

		var alongSpeed = Vector3.Dot( velocity.WithZ( 0 ), tangent );
		if ( hasInput )
		{
			if ( alongSpeed < settings.WallrunMaxSpeedHorizontal )
				alongSpeed = MathF.Min( settings.WallrunMaxSpeedHorizontal, alongSpeed + settings.WallrunAccelerateHorizontal * dt );
		}
		else
		{
			alongSpeed = MathF.Max( 0f, alongSpeed - settings.WallrunAccelerateHorizontal * 0.35f * dt );
		}

		var gravFrac = WallrunGravityFraction( elapsed, hasInput, settings );
		var nextZ = velocity.z - settings.Gravity * gravFrac * dt;
		if ( hasInput && elapsed < settings.WallrunSlipStartTime && nextZ < 20f )
			nextZ = MathF.Min( 20f, nextZ + settings.WallrunAccelerateVertical * settings.WallrunUpwardAutoPush * dt );

		if ( elapsed > settings.WallrunSlipStartTime )
			nextZ = MathF.Min( nextZ, -settings.WallrunSlipVelocity );

		nextZ = Math.Clamp( nextZ, -settings.WallrunMaxSpeedVertical * 1.6f, settings.WallrunMaxSpeedVertical );
		return ClampHorizontal( tangent * alongSpeed, MaxHorizontalSpeed ).WithZ( nextZ );
	}

	/// <summary>Wallkick jump-off burst (pilot_mp.set: Outward 205, Up 230, InputDir 80) preserving wallrun speed.</summary>
	internal static Vector3 ComputeWallkickVelocity( Vector3 velocity, Vector3 wallNormal, Vector3 wishDir, in Titanfall2PilotSettings settings )
	{
		var n = wallNormal.WithZ( 0 );
		n = n.LengthSquared > 0.0001f ? n.Normal : Vector3.Forward;

		var flat = velocity.WithZ( 0 );
		// Ensure outward component is at least wallrunJumpOutwardSpeed.
		var outward = Vector3.Dot( flat, n );
		if ( outward < settings.WallrunJumpOutwardSpeed )
			flat += n * (settings.WallrunJumpOutwardSpeed - outward);

		if ( wishDir.LengthSquared > 0.01f )
		{
			var inputFlat = wishDir.WithZ( 0 ).Normal;
			// Do not push back into the wall we just kicked off.
			var intoWall = Vector3.Dot( inputFlat, n );
			if ( intoWall < 0f ) inputFlat = (inputFlat - n * intoWall).Normal;
			flat += inputFlat * settings.WallrunJumpInputDirSpeed;
		}

		var nextZ = MathF.Max( settings.WallrunJumpUpSpeed, velocity.z * 0.35f + settings.WallrunJumpUpSpeed );
		return ClampHorizontal( flat, MaxHorizontalSpeed ).WithZ( nextZ );
	}

	/// <summary>Checks whether horizontal velocity and facing qualify to enter a slide (retail 0x18020af70).</summary>
	internal static bool CanStartSlide( Vector3 horizontalVelocity, Vector3 lookForward, in Titanfall2PilotSettings settings )
	{
		var flat = horizontalVelocity.WithZ( 0 );
		var speedSq = flat.LengthSquared;
		if ( speedSq < settings.SlideRequiredStartSpeed * settings.SlideRequiredStartSpeed ) return false;
		var fwd = lookForward.WithZ( 0 );
		if ( fwd.LengthSquared < 0.0001f ) return true;
		return Vector3.Dot( flat.Normal, fwd.Normal ) >= settings.SlideMaxAngleDot;
	}

	/// <summary>Applies retail slide initiation boost (0x18020af70: min(speed + 150, 400) when off cooldown).</summary>
	internal static (Vector3 Velocity, bool Boosted) ApplySlideBoost( Vector3 velocity, bool canBoost, in Titanfall2PilotSettings settings )
	{
		if ( !canBoost ) return (velocity, false);
		var flat = velocity.WithZ( 0 );
		var speed = flat.Length;
		if ( speed < 1f ) return (velocity, false);
		var boosted = MathF.Min( speed + settings.SlideSpeedBoost, settings.SlideSpeedBoostCap );
		if ( boosted <= speed ) return (velocity, false);
		return (ClampHorizontal( flat * (boosted / speed), MaxHorizontalSpeed ).WithZ( velocity.z ), true);
	}

	/// <summary>
	/// Steps ground sliding with retail fixed decel, exponential decay, slight player steering,
	/// and downhill slope acceleration (projecting gravity along the ground plane).
	/// </summary>
	internal static (Vector3 Velocity, bool ShouldContinue) StepSlideVelocity( Vector3 velocity, Vector3 groundNormal,
		Vector3 wishDir, bool wantsStop, in Titanfall2PilotSettings settings, float dt )
	{
		var flat = velocity.WithZ( 0 );
		var speed = flat.Length;
		if ( speed < 0.01f ) return (Vector3.Zero.WithZ( velocity.z ), false);

		var dir = flat / speed;
		var n = groundNormal.LengthSquared > 0.5f ? groundNormal.Normal : Vector3.Up;

		// Downhill slope acceleration: steeper downhill adds velocity along slope; uphill slows down faster.
		var slopeAccel = Vector3.Zero;
		if ( n.z < 0.999f )
		{
			var downhill = (Vector3.Down - n * Vector3.Dot( Vector3.Down, n )).WithZ( 0 );
			if ( downhill.LengthSquared > 0.0001f )
				slopeAccel = downhill.Normal * (settings.Gravity * (1f - n.z) * 1.85f);
		}

		// Retail fixed deceleration + exponential decay (pilot_base: slidedecel=50, slidevelocitydecay=0.7).
		var decel = wantsStop ? settings.SlideWantToStopDecel : settings.SlideDecel;
		var decayed = MathF.Max( 0f, speed - decel * dt ) * MathF.Pow( settings.SlideVelocityDecay, dt );
		var nextFlat = dir * decayed + slopeAccel * dt;

		// Subtle lateral steering while sliding (slideAccel = 50).
		if ( wishDir.LengthSquared > 0.01f )
		{
			var steer = wishDir.WithZ( 0 ).Normal;
			var lateral = steer - dir * Vector3.Dot( steer, dir );
			if ( lateral.LengthSquared > 0.001f )
				nextFlat += lateral.Normal * (settings.SlideAccel * 2.2f * dt);
		}

		nextFlat = ClampHorizontal( nextFlat, MaxHorizontalSpeed );
		var nextSpeed = nextFlat.Length;
		var keepSliding = nextSpeed >= settings.SlideStopSpeed && (!wantsStop || nextSpeed >= settings.SlideMaxStopSpeed);
		return (nextFlat.WithZ( velocity.z ), keepSliding);
	}

	/// <summary>Slide-hop jump velocity: preserves slide momentum and applies crisp jump lift.</summary>
	internal static Vector3 ComputeSlideJumpVelocity( Vector3 velocity, in Titanfall2PilotSettings settings )
	{
		var jumpZ = JumpSpeedForHeight( settings.SlideJumpHeight, settings.Gravity );
		// Keep full horizontal momentum for authentic TF2 slide-hopping.
		return ClampHorizontal( velocity.WithZ( 0 ), MaxHorizontalSpeed ).WithZ( MathF.Max( jumpZ, velocity.z ) );
	}

	/// <summary>Computes crisp mantle duration (0.24..0.40s) proportional to climb height.</summary>
	internal static float ComputeMantleDuration( float climbHeight ) =>
		Math.Clamp( 0.22f + MathF.Max( 0f, climbHeight ) * 0.0022f, 0.24f, 0.42f );

	/// <summary>Two-phase ledge climb curve: clear the vertical lip first, then pull horizontally onto the platform.</summary>
	internal static Vector3 EvaluateMantlePosition( Vector3 startPos, Vector3 endPos, float progress )
	{
		var t = Math.Clamp( progress, 0f, 1f );
		// Phase 1 (0..0.55): rise cleanly above the ledge lip before pushing into the platform.
		var tz = Math.Clamp( t / 0.55f, 0f, 1f );
		var ez = 1f - MathF.Pow( 1f - tz, 3f );
		// Phase 2 (0.45..1.0): pull smoothly forward onto the cleared platform.
		var tx = Math.Clamp( (t - 0.45f) / 0.55f, 0f, 1f );
		var ex = tx * tx * (3f - 2f * tx);
		return new Vector3(
			float.Lerp( startPos.x, endPos.x, ex ),
			float.Lerp( startPos.y, endPos.y, ex ),
			float.Lerp( startPos.z, endPos.z, ez )
		);
	}

	/// <summary>
	/// Retail wallrun camera roll (0x1802c1970):
	/// Tilts away from the wall up to wallrun_maxViewTilt (15 deg), projected against camera facing
	/// so looking toward/away from the wall stays natural, and attenuated by (1 - adsBlend).
	/// </summary>
	internal static float ComputeWallrunCameraRoll( Vector3 cameraRight, Vector3 wallNormal, float adsBlend, in Titanfall2PilotSettings settings )
	{
		var rightFlat = cameraRight.WithZ( 0 );
		var normalFlat = wallNormal.WithZ( 0 );
		if ( rightFlat.LengthSquared < 0.0001f || normalFlat.LengthSquared < 0.0001f ) return 0f;

		// In s&box, positive roll tilts the right edge down (clockwise around +X).
		// Wall on RIGHT -> wallNormal points LEFT -> Dot(right, wallNormal) < 0 -> negative roll (head leans left, away from wall).
		// Wall on LEFT  -> wallNormal points RIGHT -> Dot(right, wallNormal) > 0 -> positive roll (head leans right, away from wall).
		var projection = Vector3.Dot( rightFlat.Normal, normalFlat.Normal );
		var adsScale = 1f - Math.Clamp( adsBlend, 0f, 1f ) * 0.85f;
		return projection * settings.WallrunMaxViewTilt * adsScale;
	}

	/// <summary>
	/// Retail slide camera roll (0x1802c1970):
	/// Tilts up to slide_viewTiltSide (15 deg) when looking across the slide direction, scaled by speed/400,
	/// plus a subtle -2.5 deg tactical lean when sliding straight forward so the slide feels immediately grounded.
	/// </summary>
	internal static float ComputeSlideCameraRoll( Vector3 cameraRight, Vector3 slideDir, float speed, float adsBlend, in Titanfall2PilotSettings settings )
	{
		var rightFlat = cameraRight.WithZ( 0 );
		var slideFlat = slideDir.WithZ( 0 );
		if ( rightFlat.LengthSquared < 0.0001f || slideFlat.LengthSquared < 0.0001f ) return 0f;

		var speedFactor = settings.SlideViewTiltPlayerSpeed > 1f
			? Math.Clamp( speed / settings.SlideViewTiltPlayerSpeed, 0f, 1f )
			: 1f;
		var adsScale = 1f - Math.Clamp( adsBlend, 0f, 1f ) * 0.85f;
		var sideDot = Vector3.Dot( rightFlat.Normal, slideFlat.Normal );
		var sideTilt = -sideDot * settings.SlideViewTiltSide * speedFactor;
		var forwardBias = -2.5f * (1f - MathF.Abs( sideDot )) * speedFactor;
		return (sideTilt + forwardBias) * adsScale;
	}

	/// <summary>Computes dynamic FOV multiplier for sliding (pilot_mp slideFOVScale = 1.1) and high-speed parkour.</summary>
	internal static float ComputeTargetFovScale( bool isSliding, bool isWallrunning, float horizontalSpeed, float adsBlend, in Titanfall2PilotSettings settings )
	{
		var adsScale = 1f - Math.Clamp( adsBlend, 0f, 1f );
		var target = 1f;
		if ( isSliding )
		{
			var speedT = Math.Clamp( horizontalSpeed / MathF.Max( 1f, settings.SlideSpeedBoostCap ), 0.4f, 1f );
			target = float.Lerp( 1f, settings.SlideFovScale, speedT );
		}
		else if ( isWallrunning )
		{
			var speedT = Math.Clamp( (horizontalSpeed - settings.SprintSpeed) / MathF.Max( 1f, settings.WallrunMaxSpeedHorizontal - settings.SprintSpeed ), 0f, 1f );
			target = 1f + 0.06f * speedT;
		}
		else if ( horizontalSpeed > settings.SprintSpeed + 40f )
		{
			var speedT = Math.Clamp( (horizontalSpeed - settings.SprintSpeed - 40f) / 300f, 0f, 1f );
			target = 1f + 0.05f * speedT;
		}
		return float.Lerp( 1f, target, adsScale );
	}

	/// <summary>Cushions high-speed downward velocity just before touchdown so Jump Kit pilots take no fall damage.</summary>
	internal static float CushionFallSpeed( float verticalVelocity ) =>
		verticalVelocity < -FallCushionTriggerSpeed ? -FallCushionMaxLandingSpeed : verticalVelocity;

	internal static Vector3 ClampHorizontal( Vector3 velocity, float maxSpeed )
	{
		var flat = velocity.WithZ( 0 );
		var lenSq = flat.LengthSquared;
		if ( lenSq <= maxSpeed * maxSpeed || lenSq < 0.0001f ) return velocity;
		return (flat * (maxSpeed / MathF.Sqrt( lenSq ))).WithZ( velocity.z );
	}
}
