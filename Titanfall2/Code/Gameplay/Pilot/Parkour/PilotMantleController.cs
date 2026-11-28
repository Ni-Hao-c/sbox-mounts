/// <summary>
/// Owns Pilot ledge-mantle detection, smooth interpolation, vault-jump cancel, and completion boost.
/// </summary>
internal sealed class Titanfall2PilotMantleController
{
	internal bool IsActive { get; private set; }
	internal Vector3 WallNormal { get; private set; }
	internal float Progress => IsActive && _duration > 0.01f ? Math.Clamp( _elapsed / _duration, 0f, 1f ) : 0f;

	Vector3 _start;
	Vector3 _end;
	float _duration;
	float _elapsed;
	TimeUntil _cooldownUntil;

	internal void Reset()
	{
		IsActive = false;
		WallNormal = Vector3.Zero;
		_start = Vector3.Zero;
		_end = Vector3.Zero;
		_duration = 0f;
		_elapsed = 0f;
	}

	internal bool TryStart( Titanfall2PilotMoveMode host )
	{
		if ( _cooldownUntil > 0f || host.Slide.IsActive ) return false;
		var eyeForward = host.EyeForwardFlat();
		// Require pushing toward the ledge or actively pressing jump against it.
		var pushingForward = host.WishDir.LengthSquared > 0.01f && Vector3.Dot( host.WishDir, eyeForward ) > 0.35f;
		if ( !pushingForward && !host.HasJumpBuffered ) return false;

		var halfWidth = host.BodyHalfWidth;
		var reach = halfWidth + 18f;
		var wallProbeOrigin = host.WorldPosition + Vector3.Up * 38f;
		var wallHit = host.TraceWallRay( wallProbeOrigin, wallProbeOrigin + eyeForward * reach );
		if ( !wallHit.Hit || wallHit.StartedSolid || wallHit.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ || Titanfall2PilotMoveMode.IsNoClimb( wallHit ) )
			return false;

		// Must be facing reasonably into the ledge wall.
		var wallNormalFlat = wallHit.Normal.WithZ( 0 );
		if ( wallNormalFlat.LengthSquared < 0.01f || Vector3.Dot( eyeForward, -wallNormalFlat.Normal ) < 0.55f )
			return false;

		// Probe downward onto the ledge top from above head height down to waist height.
		var ledgePointXY = wallHit.HitPosition - wallNormalFlat.Normal * (halfWidth + 6f);
		var topStart = new Vector3( ledgePointXY.x, ledgePointXY.y, host.WorldPosition.z + 92f );
		var topEnd = new Vector3( ledgePointXY.x, ledgePointXY.y, host.WorldPosition.z + 22f );
		var ledgeHit = host.TraceWallRay( topStart, topEnd );
		if ( !ledgeHit.Hit || ledgeHit.StartedSolid || ledgeHit.Normal.z < Titanfall2PilotParkourRules.WalkableNormalZ || Titanfall2PilotMoveMode.IsNoClimb( ledgeHit ) )
			return false;

		var climbHeight = ledgeHit.HitPosition.z - host.WorldPosition.z;
		if ( climbHeight < 22f || climbHeight > 88f ) return false;

		// Verify standing headroom at the destination so we never mantle into a solid ceiling.
		var destPos = new Vector3( ledgePointXY.x, ledgePointXY.y, ledgeHit.HitPosition.z + 2f );
		var hullCheck = host.TracePilotHull( destPos + Vector3.Up * 2f, destPos + Vector3.Up * 2f );
		if ( hullCheck.StartedSolid || hullCheck.Hit ) return false;

		host.Wallrun.End( host );
		host.Wallhang.End( host, playDetachSound: false );
		host.Slide.End( host, playStopSound: false );
		IsActive = true;
		_start = host.WorldPosition;
		_end = destPos;
		WallNormal = wallNormalFlat.Normal;
		_duration = Titanfall2PilotParkourRules.ComputeMantleDuration( climbHeight );
		_elapsed = 0f;
		host.OnMantleStarted();
		host.Controller.Body.Velocity = Vector3.Zero;
		host.Controller.PreventGrounding( _duration + 0.05f );
		host.Parkour?.PlayMovementSound( climbHeight > 54f ? host.Settings.SoundMantleMedium : host.Settings.SoundMantleLow, host.Settings.SoundMantle3P );
		host.SyncParkourState();
		return true;
	}

	internal bool TryVaultCancel( Titanfall2PilotMoveMode host, Rigidbody body )
	{
		if ( !host.HasJumpBuffered || _duration <= 0f || (_elapsed / _duration) < 0.25f )
			return false;

		var settings = host.Settings;
		host.ConsumeJumpBuffer();
		IsActive = false;
		host.ResetAirJumpAfterVault();
		var dir = host.EyeForwardFlat();
		body.Velocity = dir * MathF.Max( settings.SprintSpeed, 280f )
			+ Vector3.Up * Titanfall2PilotParkourRules.JumpSpeedForHeight( settings.JumpHeight, settings.Gravity );
		host.Controller.PreventGrounding( 0.15f );
		host.Parkour?.TriggerThrusterBurst( 0.9f );
		host.Parkour?.PlayJumpJetBurst();
		host.SyncParkourState();
		return true;
	}

	internal void Step( Titanfall2PilotMoveMode host, float dt )
	{
		_elapsed += dt;
		var progress = _duration > 0.01f ? Math.Clamp( _elapsed / _duration, 0f, 1f ) : 1f;
		var nextPos = Titanfall2PilotParkourRules.EvaluateMantlePosition( _start, _end, progress );
		host.Controller.Body.WorldPosition = nextPos;
		host.WorldPosition = nextPos;
		host.Controller.Body.Velocity = Vector3.Zero;

		if ( progress >= 1f )
		{
			IsActive = false;
			_cooldownUntil = host.Settings.TraversalCooldown;
			host.OnMantleCompleted();
			// Preserve snappy forward momentum cresting the ledge.
			var forward = (_end - _start).WithZ( 0 );
			if ( forward.LengthSquared > 0.01f )
				host.Controller.Body.Velocity = forward.Normal * MathF.Min( host.Settings.SprintSpeed, 210f );
		}
	}
}
