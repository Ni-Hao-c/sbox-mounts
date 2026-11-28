/// <summary>
/// Owns Pilot wallrun attachment, retail-faithful wall-plane propulsion, tight proximity probing,
/// angular curvature limiting (wallrun_rotateMaxRate), and lateral trim-obstacle gliding.
/// </summary>
internal sealed class Titanfall2PilotWallrunController
{
	const float FootstepInterval = 0.22f;
	const float MinEntrySpeed = 95f;
	const float MinFloorClearance = 8f;
	const float GapGraceSeconds = 0.035f;
	const float MaxTrimStepOut = 10f;
	const float ProbeRadius = 2.5f;

	internal bool IsActive { get; private set; }
	internal Vector3 WallNormal { get; private set; }
	internal Vector3 TravelDir { get; private set; }
	internal Vector3 HugVelocity { get; private set; }
	internal float WallSide { get; private set; }
	internal float Elapsed { get; private set; }

	float _pushAwayTime;
	float _lostTime;
	TimeSince _sinceFootstep = 10f;

	internal void Reset()
	{
		IsActive = false;
		WallNormal = Vector3.Zero;
		TravelDir = Vector3.Zero;
		HugVelocity = Vector3.Zero;
		WallSide = 0f;
		Elapsed = 0f;
		_pushAwayTime = 0f;
		_lostTime = 0f;
	}

	internal bool TryStart( Titanfall2PilotMoveMode host )
	{
		var settings = host.Settings;
		var body = host.Controller.Body;
		var flatVel = body.Velocity.WithZ( 0 );
		var refDir = flatVel.Length >= 40f ? flatVel : (host.WishDir.LengthSquared > 0.01f ? host.WishDir * settings.WalkSpeed : Vector3.Zero);
		if ( flatVel.Length < MinEntrySpeed && refDir.Length < MinEntrySpeed ) return false;

		// Require separation from floor when descending/flat so normal walking beside a wall never attaches.
		if ( body.Velocity.z <= 40f )
		{
			var floorProbe = host.TracePilotHull( host.WorldPosition + Vector3.Up * 2f, host.WorldPosition - Vector3.Up * MinFloorClearance );
			if ( floorProbe.Hit && !floorProbe.StartedSolid && floorProbe.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ )
				return false;
		}

		var eyeForward = host.EyeForwardFlat();
		var eyeRight = Vector3.Cross( eyeForward, Vector3.Up ).Normal;
		var maxWallDist = host.BodyHalfWidth + settings.WallrunAllowedWallDist;
		var traceDist = MathF.Max( 1f, maxWallDist - ProbeRadius );
		var chest = host.WorldPosition + Vector3.Up * (host.Controller.CurrentHeight * 0.55f);

		// Probe sides and 45-degree forward diagonals only (never grab walls behind the pilot).
		ReadOnlySpan<Vector3> probeDirs =
		[
			eyeRight,
			-eyeRight,
			(eyeForward + eyeRight).Normal,
			(eyeForward - eyeRight).Normal
		];

		SceneTraceResult best = default;
		var found = false;
		foreach ( var dir in probeDirs )
		{
			var hit = host.TraceWallSphere( chest, chest + dir * traceDist, ProbeRadius );
			if ( !Titanfall2PilotMoveMode.IsRunnableWallHit( hit ) ) continue;
			if ( host.IsSameWallBlocked( hit.Normal ) ) continue;

			var candidateNormal = hit.Normal;
			if ( candidateNormal.LengthSquared < 0.01f ) continue;
			candidateNormal = candidateNormal.Normal;

			// Never attach onto a wall the pilot is actively pushing away from or moving away from.
			if ( Titanfall2PilotParkourRules.IsPushingAwayFromWall( host.WishDir, candidateNormal ) ) continue;
			var outwardVel = Vector3.Dot( flatVel, candidateNormal );
			var pushingIntoWall = host.WishDir.LengthSquared > 0.01f && Vector3.Dot( host.WishDir, candidateNormal ) < -0.25f;
			if ( outwardVel > 25f && !pushingIntoWall ) continue;

			var tangent = Titanfall2PilotParkourRules.WallTangent( candidateNormal, flatVel.LengthSquared > 900f ? flatVel : eyeForward );
			var velAlong = flatVel.LengthSquared > 900f ? Vector3.Dot( flatVel.Normal, tangent ) : 0f;
			var eyeAlong = Vector3.Dot( eyeForward, tangent );
			if ( velAlong < 0.25f && eyeAlong < 0.35f ) continue;

			if ( !found || hit.Distance < best.Distance )
			{
				best = hit;
				found = true;
			}
		}

		if ( !found ) return false;

		host.Slide.End( host, playStopSound: false );
		IsActive = true;
		WallNormal = best.Normal.Normal;
		TravelDir = Titanfall2PilotParkourRules.WallTangent( WallNormal, flatVel.LengthSquared > 900f ? flatVel : eyeForward );
		HugVelocity = Vector3.Zero;
		WallSide = Titanfall2PilotParkourRules.WallSide( TravelDir, WallNormal );
		Elapsed = 0f;
		_pushAwayTime = 0f;
		_lostTime = 0f;
		_sinceFootstep = 0f;
		host.OnWallAttached();

		// Retail starts this tick's first half-gravity in air, before the wall-attachment transition.
		var entryVelocity = body.Velocity;
		entryVelocity.z -= settings.Gravity * Math.Clamp( Time.Delta, 0.0001f, 0.05f ) * 0.5f;
		var startVel = Titanfall2PilotParkourRules.ComputeWallrunStartVelocity( entryVelocity, WallNormal, settings );
		body.Velocity = startVel;
		host.Controller.PreventGrounding( 0.15f );
		host.Parkour?.TriggerThrusterBurst( 1.0f );
		host.Parkour?.PlayMovementSound( settings.SoundWallrunImpact );
		host.Parkour?.StartWallrunJetSound();
		host.SyncParkourState();
		return true;
	}

	internal void StepVelocity( Titanfall2PilotMoveMode host, Rigidbody body, Titanfall2PilotTactical tactical, float dt )
	{
		var settings = host.Settings;
		var stimScale = tactical is { IsValid: true } ? tactical.WallrunSpeedMultiplier : 1f;
		var empScale = host.GetComponent<Titanfall2EmpStatus>() is { IsValid: true } emp ? emp.MoveSpeedScale : 1f;
		var speedScale = stimScale * empScale;
		var wallSettings = MathF.Abs( speedScale - 1f ) > 0.001f
			? settings with { WallrunMaxSpeedHorizontal = settings.WallrunMaxSpeedHorizontal * speedScale }
			: settings;

		var next = Titanfall2PilotParkourRules.StepWallrunVelocity(
			body.Velocity, WallNormal, host.RawInput, host.Controller.EyeTransform.Rotation, Elapsed, wallSettings, dt );
		body.Velocity = next + HugVelocity;
		if ( next.WithZ( 0 ).LengthSquared > 1f )
			TravelDir = Titanfall2PilotParkourRules.WallTangent( WallNormal, next );
		WallSide = Titanfall2PilotParkourRules.WallSide( TravelDir, WallNormal );
	}

	internal void UpdateAttachment( Titanfall2PilotMoveMode host, float dt )
	{
		var settings = host.Settings;
		Elapsed += dt;
		HugVelocity = Vector3.Zero;

		// Retail pilot_mp wallrunAdsType = "ADS": right-click aims freely while wallrunning.
		// Holding Back (S) + ADS deliberately brakes into Wallhang.
		if ( host.WantsWallhang && host.RawInput.x < -0.25f )
		{
			host.Wallhang.Start( host, WallNormal, fromWallrun: true );
			return;
		}

		// Crouch immediately drops cleanly off the wall.
		if ( host.Controller.UseInputControls && Input.Down( host.Controller.DuckButton ) )
		{
			Detach( host, pushAway: true );
			return;
		}

		// Retail 0x18020e7e0: pushing away from wall (looking away + W or strafing away) detaches after 0.05s.
		if ( Titanfall2PilotParkourRules.IsPushingAwayFromWall( host.WishDir, WallNormal ) )
		{
			_pushAwayTime += dt;
			if ( _pushAwayTime >= settings.WallrunPushAwayFallOffTime )
			{
				Detach( host, pushAway: true );
				return;
			}
		}
		else
		{
			_pushAwayTime = 0f;
		}

		// Retail 0x18020e7e0: hard detach once wallrun_timeLimit (1.75s) elapses.
		if ( Elapsed >= settings.WallrunTimeLimit )
		{
			Detach( host, pushAway: true );
			return;
		}

		// Check floor contact below feet to transition cleanly back to ground.
		if ( host.Controller.Body.Velocity.z <= 10f )
		{
			var floorHit = host.TracePilotHull( host.WorldPosition + Vector3.Up * 2f, host.WorldPosition - Vector3.Up * 6f );
			if ( floorHit.Hit && !floorHit.StartedSolid && floorHit.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ )
			{
				End( host );
				return;
			}
		}

		// Retail wallrun_allowed_wall_dist (13u) proximity probe without corner-wrapping or magnetic suction.
		var maxWallDist = host.BodyHalfWidth + settings.WallrunAllowedWallDist;
		if ( !TryProbeAttachedWall( host, maxWallDist, out var hitNormal, out var wallDist ) )
		{
			_lostTime += dt;
			if ( _lostTime >= GapGraceSeconds )
			{
				Detach( host, pushAway: false );
				return;
			}
		}
		else
		{
			_lostTime = 0f;
			// Retail 0x1802068c0: cap wall-normal rotation at wallrun_rotateMaxRate (3 rad/s) so tight pillars detach naturally.
			WallNormal = Titanfall2PilotParkourRules.RotateWallNormal( WallNormal, hitNormal, settings.WallrunRotateMaxRate, dt );
			TravelDir = Titanfall2PilotParkourRules.WallTangent( WallNormal, TravelDir );
			WallSide = Titanfall2PilotParkourRules.WallSide( TravelDir, WallNormal );

			// Only push gently outward if scraping inside minimum hull clearance; never magnetically pull into walls.
			var minStandoff = host.BodyHalfWidth + 0.5f;
			if ( wallDist < minStandoff )
				HugVelocity = WallNormal * Math.Clamp( (minStandoff - wallDist) * 14f, 0f, 80f );
		}

		// Periodic wallrun footsteps while moving along the wall.
		if ( _sinceFootstep >= FootstepInterval && host.Controller.Body.Velocity.WithZ( 0 ).Length > 120f )
		{
			_sinceFootstep = 0f;
			host.Parkour?.PlayMovementSound( settings.SoundWallrunFootstep );
		}
	}

	internal void TryGlideObstacle( Titanfall2PilotMoveMode host )
	{
		if ( TravelDir.LengthSquared < 0.5f || WallNormal.LengthSquared < 0.5f ) return;
		var speed = host.Controller.Body.Velocity.WithZ( 0 ).Length;
		if ( speed < 60f ) return;

		var dt = Math.Clamp( Time.Delta, 0.008f, 0.035f );
		var lookAhead = Math.Clamp( speed * dt + 8f, 10f, 24f );
		var forwardHit = host.TracePilotHull( host.WorldPosition, host.WorldPosition + TravelDir * lookAhead );
		if ( !forwardHit.Hit || forwardHit.StartedSolid ) return;
		if ( Vector3.Dot( forwardHit.Normal.WithZ( 0 ), TravelDir ) > -0.25f ) return;

		ReadOnlySpan<float> stepOuts = [4f, 8f, MaxTrimStepOut];
		foreach ( var step in stepOuts )
		{
			var shifted = host.WorldPosition + WallNormal * step;
			var sideCheck = host.TracePilotHull( host.WorldPosition, shifted );
			if ( sideCheck.Hit || sideCheck.StartedSolid ) continue;

			var clearCheck = host.TracePilotHull( shifted, shifted + TravelDir * lookAhead );
			if ( !clearCheck.Hit && !clearCheck.StartedSolid || clearCheck.Fraction > forwardHit.Fraction + 0.35f )
			{
				host.Controller.Body.WorldPosition = shifted;
				host.WorldPosition = shifted;
				return;
			}
		}
	}

	internal void Detach( Titanfall2PilotMoveMode host, bool pushAway )
	{
		var wallNormal = WallNormal;
		host.RecordPreviousWall( wallNormal );
		End( host );
		host.SetWallRepel( host.Settings.WallrunRepelTime );
		if ( pushAway )
			host.Controller.Body.Velocity += wallNormal * host.Settings.WallrunFallAwaySpeed;
	}

	internal void End( Titanfall2PilotMoveMode host )
	{
		if ( !IsActive ) return;
		IsActive = false;
		TravelDir = Vector3.Zero;
		HugVelocity = Vector3.Zero;
		Elapsed = 0f;
		_pushAwayTime = 0f;
		_lostTime = 0f;
		host.Parkour?.StopWallrunJetSound();
	}

	internal void ClearForWallhangTransition()
	{
		IsActive = false;
		Elapsed = 0f;
		_pushAwayTime = 0f;
		_lostTime = 0f;
		TravelDir = Vector3.Zero;
		HugVelocity = Vector3.Zero;
	}

	bool TryProbeAttachedWall( Titanfall2PilotMoveMode host, float maxWallDist, out Vector3 normal, out float wallDistance )
	{
		normal = WallNormal;
		wallDistance = maxWallDist;
		var minCos = host.Settings.WallrunAngleChangeMinCos;
		var h = host.Controller.CurrentHeight;
		var traceDist = MathF.Max( 1f, maxWallDist - ProbeRadius );

		ReadOnlySpan<Vector3> origins =
		[
			host.WorldPosition + Vector3.Up * (h * 0.58f),
			host.WorldPosition + Vector3.Up * (h * 0.35f)
		];

		var bestDist = float.MaxValue;
		var found = false;
		foreach ( var origin in origins )
		{
			var hit = host.TraceWallSphere( origin, origin - WallNormal * traceDist, ProbeRadius );
			if ( !Titanfall2PilotMoveMode.IsRunnableWallHit( hit ) ) continue;
			var candidate = hit.Normal;
			if ( candidate.LengthSquared < 0.01f ) continue;
			candidate = candidate.Normal;
			if ( Vector3.Dot( WallNormal, candidate ) < minCos ) continue;

			var dist = hit.Distance + ProbeRadius;
			if ( dist > maxWallDist ) continue;
			if ( !found || dist < bestDist )
			{
				bestDist = dist;
				normal = candidate;
				found = true;
			}
		}

		if ( found ) wallDistance = bestDist;
		return found;
	}

}
