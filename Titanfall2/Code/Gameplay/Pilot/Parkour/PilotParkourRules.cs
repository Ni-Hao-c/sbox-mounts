/// <summary>
/// Pure deterministic physics, camera-tilt, and viewmodel-pose rules for Titanfall 2 Pilot parkour.
/// Ground, air, wallrun and slide motor rules use verified retail evidence; remaining pose/jump adapters are noted below.
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

	/// <summary>Retail 0x1801f8090 / 0x1801f0ac0 ordinary ground friction and acceleration, without obstacle/status modifiers.</summary>
	internal static Vector3 StepGroundVelocity( Vector3 velocity, Vector3 wishVelocity, float acceleration, float deceleration, float dt )
	{
		if ( dt <= 0f ) return velocity;
		var flat = velocity.WithZ( 0 );
		var wish = wishVelocity.WithZ( 0 );
		var wishSpeed = wish.Length;
		var direction = wishSpeed > 0.0001f ? wish / wishSpeed : Vector3.Zero;
		var along = Vector3.Dot( flat, direction );
		var protectedVelocity = Vector3.Zero;
		if ( along > 0f && flat.LengthSquared > 0.0001f )
			protectedVelocity = direction * along * MathF.Min( along * wishSpeed / flat.LengthSquared, 1f );
		var remaining = flat - protectedVelocity;
		var remainingSpeed = remaining.Length;
		if ( remainingSpeed > 0.0001f )
			remaining *= MathF.Max( 0f, remainingSpeed - deceleration * dt ) / remainingSpeed;
		flat = remaining + protectedVelocity;
		if ( wishSpeed > 0.0001f )
			flat = ApplyMovementAcceleration( flat, direction, wishSpeed, acceleration, dt );
		return ClampHorizontal( flat, MaxHorizontalSpeed ).WithZ( velocity.z );
	}

	/// <summary>Retail 0x1801f19a0 air acceleration, including the extra 2 u/s² turn branch above wish speed.</summary>
	internal static Vector3 ApplyAirAcceleration( Vector3 velocity, Vector3 wishDir, float airSpeed, float airAccel, float dt )
	{
		if ( dt <= 0f || wishDir.LengthSquared < 0.0001f ) return velocity;
		var wish = wishDir.WithZ( 0 );
		var dir = wish.Normal;
		var flat = velocity.WithZ( 0 );
		var currentInWish = Vector3.Dot( flat, dir );
		var addSpeed = airSpeed * MathF.Min( wish.Length, 1f ) - currentInWish;
		if ( addSpeed <= 0f )
		{
			var redirected = velocity + dir * (2f * dt); // player_extraairaccelleration default
			if ( redirected.LengthSquared > velocity.LengthSquared )
				redirected *= MathF.Sqrt( velocity.LengthSquared / redirected.LengthSquared );
			return redirected;
		}

		var accelSpeed = MathF.Min( airAccel * dt, addSpeed );
		var next = flat + dir * accelSpeed;
		return next.WithZ( velocity.z );
	}

	/// <summary>Current double-jump adapter; retail state-dependent redirect/boost branches are not fully reproduced yet.</summary>
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
	/// Retail 0x18020e6f0: ordinary wallrun gravity ramps linearly to full gravity.
	/// Input-dependent slipping affects friction and upward propulsion, not this multiplier.
	/// </summary>
	internal static float WallrunGravityFraction( float elapsed, in Titanfall2PilotSettings settings )
	{
		return settings.WallrunGravityRampUpTime > 0f
			? Math.Clamp( elapsed / settings.WallrunGravityRampUpTime, 0f, 1f )
			: 1f;
	}

	/// <summary>Initial velocity upon attaching to a wall (retail 0x18020b390 upward wall boost + wall plane projection).</summary>
	internal static Vector3 ComputeWallrunStartVelocity( Vector3 velocity, Vector3 wallNormal, in Titanfall2PilotSettings settings )
	{
		var normal = wallNormal.Normal;
		var wallUp = (Vector3.Up - normal * normal.z).Normal;
		var upward = Vector3.Dot( velocity, wallUp );
		if ( upward < settings.WallrunUpWallBoost )
			velocity += wallUp * (settings.WallrunUpWallBoost - MathF.Max( upward, 0f ));
		// 0x18020cc30 projects onto the actual wall plane; it does not convert impact speed into travel speed.
		return ClampHorizontal( velocity - normal * Vector3.Dot( velocity, normal ), MaxHorizontalSpeed );
	}

	/// <summary>
	/// Retail 0x18020e7e0 push-away check: returns true when world wish direction points away from the wall
	/// (Dot(wishDir, wallNormal) > 0.7, ~45.6 deg), whether from looking away with W or strafing away.
	/// </summary>
	internal static bool IsPushingAwayFromWall( Vector3 wishDir, Vector3 wallNormal )
	{
		var normalFlat = wallNormal;
		var wishFlat = wishDir.WithZ( 0 );
		if ( normalFlat.LengthSquared < 0.0001f || wishFlat.LengthSquared < 0.01f ) return false;
		return Vector3.Dot( wishFlat, normalFlat.Normal ) > 0.7f;
	}

	/// <summary>
	/// Retail 0x1802068c0 angular rate cap (wallrun_rotateMaxRate = 3.0 rad/s): rotates current wall normal
	/// toward target normal by at most maxRateRadPerSec * dt so tight pillars cannot trap the pilot in orbit.
	/// </summary>
	internal static Vector3 RotateWallNormal( Vector3 currentNormal, Vector3 targetNormal, float maxRateRadPerSec, float dt )
	{
		var cur = currentNormal;
		var tgt = targetNormal;
		if ( cur.LengthSquared < 0.0001f ) return tgt.LengthSquared > 0.0001f ? tgt.Normal : Vector3.Forward;
		if ( tgt.LengthSquared < 0.0001f || dt <= 0f || maxRateRadPerSec <= 0f ) return cur.Normal;

		cur = cur.Normal;
		tgt = tgt.Normal;
		var maxAngle = MathF.Min( maxRateRadPerSec * dt, MathF.PI );
		var dot = Math.Clamp( Vector3.Dot( cur, tgt ), -1f, 1f );
		if ( dot >= MathF.Cos( maxAngle ) )
			return tgt;

		var toward = tgt - cur * dot;
		if ( toward.LengthSquared < 0.0001f )
			toward = Vector3.Cross( cur, MathF.Abs( cur.z ) < 0.9f ? Vector3.Up : Vector3.Forward );
		return (cur * MathF.Cos( maxAngle ) + toward.Normal * MathF.Sin( maxAngle )).Normal;
	}

	/// <summary>
	/// Retail 0x18020cc30 wall-relative input basis. W into a wall drives upward; S may reverse along it.
	/// Source's positive sidemove is right, whereas s&box raw input Y is left.
	/// </summary>
	internal static Vector3 ComputeWallrunWish( Vector3 rawInput, Rotation eyes, Vector3 wallNormal,
		in Titanfall2PilotSettings settings, out bool isIdle )
	{
		var normal = wallNormal.Normal;
		Vector3 forward, right;
		if ( MathF.Abs( Vector3.Dot( eyes.Right, normal ) ) < 0.7f )
		{
			forward = Vector3.Cross( normal, eyes.Right ).Normal;
			right = Vector3.Cross( forward, normal );
		}
		else
		{
			right = Vector3.Cross( eyes.Forward, normal ).Normal;
			forward = Vector3.Cross( normal, right );
		}
		var tangent = new Vector3( normal.y, -normal.x, 0f ).Normal;
		var alignment = Vector3.Dot( forward, tangent );
		var sideInput = -rawInput.y;
		var side = sideInput + MathF.Max( rawInput.x, 0f ) * alignment * settings.WallrunUpwardAutoPush;
		var ahead = rawInput.x * (1f + MathF.Max( alignment * sideInput, 0f ));
		if ( right.z < -0.01f && side > 0f || right.z > 0.01f && side < 0f ) side = 0f;
		isIdle = MathF.Abs( ahead ) < 0.1f && MathF.Abs( side ) < 0.1f;
		var wish = forward * ahead + right * side;
		return wish - normal * Vector3.Dot( wish, normal );
	}

	/// <summary>Retail 0x1801f0ac0 acceleration, including its total-speed energy limit.</summary>
	internal static Vector3 ApplyMovementAcceleration( Vector3 velocity, Vector3 direction, float wishSpeed, float acceleration, float dt )
	{
		var addSpeed = wishSpeed - Vector3.Dot( velocity, direction );
		if ( addSpeed <= 0f || dt <= 0f ) return velocity;
		var maxSpeedSquared = MathF.Max( velocity.LengthSquared, wishSpeed * wishSpeed );
		var next = velocity + direction * MathF.Min( addSpeed, acceleration * dt );
		if ( next.LengthSquared > maxSpeedSquared )
			next *= MathF.Sqrt( maxSpeedSquared / next.LengthSquared );
		return next;
	}

	/// <summary>
	/// Ordinary retail wallrun: half gravity, wall-plane projection, full friction, vertical then horizontal
	/// acceleration, and half gravity. Slipping preserves downward velocity lost to friction and reduces lift.
	/// </summary>
	internal static Vector3 StepWallrunVelocity( Vector3 velocity, Vector3 wallNormal, Vector3 rawInput, Rotation eyes,
		float elapsed, in Titanfall2PilotSettings settings, float dt )
	{
		if ( dt <= 0f ) return velocity;
		var normal = wallNormal.Normal;
		var halfGravity = settings.Gravity * WallrunGravityFraction( elapsed, settings ) * dt * 0.5f;
		velocity.z -= halfGravity;
		velocity -= normal * Vector3.Dot( velocity, normal );
		var wish = ComputeWallrunWish( rawInput, eyes, normal, settings, out var isIdle );
		var slip = settings.WallrunSlipDuration > 0f
			? Math.Clamp( (elapsed - settings.WallrunSlipStartTime) / settings.WallrunSlipDuration, 0f, 1f )
			: elapsed >= settings.WallrunSlipStartTime ? 1f : 0f;
		if ( isIdle ) slip = MathF.Max( slip, settings.WallrunNoInputSlipFrac );

		var beforeFrictionZ = velocity.z;
		velocity *= MathF.Max( 0f, 1f - settings.WallrunFriction * dt );
		if ( beforeFrictionZ < 0f ) velocity.z = float.Lerp( velocity.z, beforeFrictionZ, slip );
		if ( MathF.Abs( wish.z ) > 0.0001f )
			velocity = ApplyMovementAcceleration( velocity, Vector3.Up * MathF.Sign( wish.z ),
				MathF.Min( MathF.Abs( wish.z ), 1f ) * settings.WallrunMaxSpeedVertical,
				settings.WallrunAccelerateVertical * (1f - slip), dt );
		var horizontal = wish.WithZ( 0 );
		if ( horizontal.LengthSquared > 0.0001f )
		{
			var direction = horizontal.Normal;
			var speed = Vector3.Dot( direction, eyes.Forward ) <= -0.6f
				? settings.WallrunMaxSpeedHorizontalBackward : settings.WallrunMaxSpeedHorizontal;
			velocity = ApplyMovementAcceleration( velocity, direction, MathF.Min( horizontal.Length, 1f ) * speed,
				settings.WallrunAccelerateHorizontal, dt );
		}
		velocity.z -= halfGravity;
		velocity -= normal * Vector3.Dot( velocity, normal );
		return ClampHorizontal( velocity, MaxHorizontalSpeed );
	}

	/// <summary>
	/// Retail 0x180201170 ordinary wallkick: add the outward/input impulse, then supplement upward speed.
	/// Looking and pushing into the wall attenuates the outward impulse (flat eye forward from 0x1802d8b40).
	/// </summary>
	internal static Vector3 ComputeWallkickVelocity( Vector3 velocity, Vector3 wallNormal, Vector3 wishDir,
		Vector3 eyeForward, in Titanfall2PilotSettings settings )
	{
		var n = wallNormal;
		n = n.LengthSquared > 0.0001f ? n.Normal : Vector3.Forward;
		var input = wishDir.WithZ( 0 );
		if ( input.LengthSquared > 1f ) input = input.Normal;
		var impulse = input * settings.WallrunJumpInputDirSpeed;
		var inwardFacing = MathF.Min( Vector3.Dot( n, eyeForward.WithZ( 0 ).Normal ), 0f );
		var inwardInput = MathF.Min( Vector3.Dot( n, input ), 0f );
		var outwardSpeed = settings.WallrunJumpOutwardSpeed * (1f - inwardFacing * inwardInput * 0.8f);
		impulse += n * MathF.Max( 0f, outwardSpeed - Vector3.Dot( impulse, n ) );
		var next = velocity + impulse;
		if ( next.z < settings.WallrunJumpUpSpeed )
			next.z = MathF.Min( settings.WallrunJumpUpSpeed, next.z + settings.WallrunJumpUpSpeed * 1.5f );
		return ClampHorizontal( next, MaxHorizontalSpeed );
	}

	/// <summary>
	/// Retail 0x18020af70: require horizontal speed; an already-grounded start also requires forward input.
	/// Landing calls this before assigning the ground entity, bypassing the input-facing gate (0x1801fa160).
	/// </summary>
	internal static bool CanStartSlide( Vector3 horizontalVelocity, Vector3 lookForward, Vector3 wishDir,
		bool isLanding, in Titanfall2PilotSettings settings )
	{
		var flat = horizontalVelocity.WithZ( 0 );
		var speedSq = flat.LengthSquared;
		if ( speedSq < settings.SlideRequiredStartSpeed * settings.SlideRequiredStartSpeed ) return false;
		if ( isLanding ) return true;
		var fwd = lookForward.WithZ( 0 );
		if ( fwd.LengthSquared < 0.0001f ) return false;
		return Vector3.Dot( wishDir.WithZ( 0 ), fwd.Normal ) >= settings.SlideMaxAngleDot;
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
	/// Ordinary retail slide: half slope gravity, fixed deceleration and per-second retention,
	/// steering through the shared acceleration rule, then half slope gravity.
	/// </summary>
	internal static (Vector3 Velocity, bool ShouldContinue) StepSlideVelocity( Vector3 velocity, Vector3 groundNormal,
		Vector3 wishDir, bool wantsStop, in Titanfall2PilotSettings settings, float dt, float maxWishSpeed )
	{
		var flat = velocity.WithZ( 0 );
		var n = groundNormal.LengthSquared > 0.5f ? groundNormal.Normal : Vector3.Up;
		var step = MathF.Max( dt, 0f );
		// 0x1801f93c0 / 0x1801f7410: the ground-normal correction in each half-gravity step.
		var halfSlope = n.WithZ( 0 ) * (settings.Gravity * n.z * step * 0.5f);
		flat += halfSlope;
		var speed = flat.Length;
		var wish = wishDir.WithZ( 0 );
		var along = Vector3.Dot( wish, flat );
		var steering = wish;
		if ( along > 0f && flat.LengthSquared > 0.0001f )
			steering -= flat * (along / flat.LengthSquared);

		// 0x1801fcf10: reverse input brakes even while crouch is held. It is not just an uncrouch flag.
		var decel = wantsStop || along < 0f ? settings.SlideWantToStopDecel : settings.SlideDecel;
		var decayed = MathF.Max( 0f, speed - decel * step ) * MathF.Pow( settings.SlideVelocityDecay, step );
		var nextFlat = speed > 0.0001f ? flat * (decayed / speed) : Vector3.Zero;
		if ( steering.LengthSquared > 0.0001f )
		{
			// 0x18020cc30: keep reverse input, remove only its forward component, and do not amplify lateral input.
			nextFlat = ApplyMovementAcceleration( nextFlat, steering.Normal,
				MathF.Min( steering.Length, 1f ) * MathF.Max( maxWishSpeed, 0f ), settings.SlideAccel, step );
		}
		var nextSpeed = nextFlat.Length;
		var keepSliding = nextSpeed >= settings.SlideStopSpeed && (!wantsStop || nextSpeed >= settings.SlideMaxStopSpeed);
		// Native exits a slow slide before FinishGravity; an ended slide receives no second slope impulse.
		nextFlat = ClampHorizontal( nextFlat + (keepSliding ? halfSlope : Vector3.Zero), MaxHorizontalSpeed );
		return (nextFlat.WithZ( velocity.z ), keepSliding);
	}

	/// <summary>Current slide-hop adapter; retail early boost cancellation and max-jump-speed branches remain unmodeled.</summary>
	internal static Vector3 ComputeSlideJumpVelocity( Vector3 velocity, in Titanfall2PilotSettings settings )
	{
		var jumpZ = JumpSpeedForHeight( settings.SlideJumpHeight, settings.Gravity );
		// Keep the existing adapter's momentum until the retail conditional boost cancellation is mapped.
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
	/// Simplified wallrun camera roll inspired by 0x1802c1970, not the complete native filtering pipeline:
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
	/// Adapted slide camera roll, not a verified reproduction of the full native camera pipeline:
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

	/// <summary>
	/// Vertical slip warning during the final 0.6s of a wallhang before automatic detach.
	/// </summary>
	internal static float ComputeWallhangVerticalVelocity( float elapsed, float timeLimit )
	{
		var limit = MathF.Max( 0.5f, timeLimit );
		var slipStart = MathF.Max( 0f, limit - 0.6f );
		if ( elapsed < slipStart ) return 0f;
		var t = Math.Clamp( (elapsed - slipStart) / 0.6f, 0f, 1f );
		return -35f * t;
	}

	/// <summary>Subtle wallhang camera tilt away from the wall, heavily suppressed while ADSing.</summary>
	internal static float ComputeWallhangCameraRoll( Vector3 cameraRight, Vector3 wallNormal, float adsBlend )
	{
		var rightFlat = cameraRight.WithZ( 0 );
		var normalFlat = wallNormal.WithZ( 0 );
		if ( rightFlat.LengthSquared < 0.0001f || normalFlat.LengthSquared < 0.0001f ) return 0f;
		var projection = Vector3.Dot( rightFlat.Normal, normalFlat.Normal );
		var adsScale = 1f - Math.Clamp( adsBlend, 0f, 1f ) * 0.88f;
		return projection * 6.5f * adsScale;
	}

	/// <summary>
	/// Drives s&amp;box's native Citizen/Human AnimGraph special_movement_states (LedgeGrab=1, Slide=3)
	/// and subtle 3P body lean without replacing the player's Citizen model or avatar clothing.
	/// </summary>
	internal static Titanfall2ThirdPersonBodyPose ComputeThirdPersonBodyPose(
		bool isMantling,
		float mantleProgress,
		bool isWallhanging,
		bool isWallrunning,
		float wallSide,
		Vector3 wallNormal,
		bool isSliding,
		float slideBlend,
		Vector3 slideDirection )
	{
		const int SpecialNone = 0;
		const int SpecialLedgeGrab = 1;
		const int SpecialSlide = 3;

		var wallFacing = wallNormal.WithZ( 0 ).LengthSquared > 0.01f ? (Vector3?)(-wallNormal.WithZ( 0 ).Normal) : null;

		if ( isMantling )
		{
			var arc = MathF.Sin( Math.Clamp( mantleProgress, 0f, 1f ) * MathF.PI );
			return new( SpecialLedgeGrab, Climbing: true, PitchDegrees: 18f * arc, RollDegrees: 0f, FacingDirection: wallFacing );
		}

		if ( isWallhanging )
		{
			var side = Math.Clamp( wallSide, -1f, 1f );
			return new( SpecialLedgeGrab, Climbing: true, PitchDegrees: -6f, RollDegrees: -side * 8f, FacingDirection: wallFacing );
		}

		if ( isWallrunning )
		{
			var side = Math.Clamp( wallSide, -1f, 1f );
			return new( SpecialNone, Climbing: false, PitchDegrees: 6f, RollDegrees: -side * 26f, FacingDirection: null );
		}

		var clampedSlide = Math.Clamp( slideBlend, 0f, 1f );
		if ( isSliding || clampedSlide > 0.01f )
		{
			var slideFacing = isSliding && slideDirection.WithZ( 0 ).LengthSquared > 0.01f
				? (Vector3?)slideDirection.WithZ( 0 ).Normal
				: null;
			return new( isSliding || clampedSlide > 0.35f ? SpecialSlide : SpecialNone, Climbing: false,
				PitchDegrees: -18f * clampedSlide, RollDegrees: 0f, FacingDirection: slideFacing );
		}

		return default;
	}
}

internal readonly record struct Titanfall2ThirdPersonBodyPose(
	int SpecialMoveState,
	bool Climbing,
	float PitchDegrees,
	float RollDegrees,
	Vector3? FacingDirection );
