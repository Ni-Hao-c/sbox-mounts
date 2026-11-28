/// <summary>
/// Owns Pilot wallhang attachment, slip-warning vertical drift, and detachment.
/// </summary>
internal sealed class Titanfall2PilotWallhangController
{
	const float MinFloorClearance = 8f;

	internal bool IsActive { get; private set; }
	internal Vector3 WallNormal { get; private set; }
	internal float WallSide { get; private set; }
	internal float Elapsed { get; private set; }
	internal Vector3 HugVelocity { get; private set; }

	internal void Reset()
	{
		IsActive = false;
		WallNormal = Vector3.Zero;
		WallSide = 0f;
		Elapsed = 0f;
		HugVelocity = Vector3.Zero;
	}

	internal bool TryStart( Titanfall2PilotMoveMode host )
	{
		// Retail pilot_mp wallrunAdsType = "ADS": mid-air ADS stays free; require explicit S + ADS to wallhang.
		if ( !host.WantsWallhang || host.RawInput.x >= -0.25f ) return false;
		var settings = host.Settings;
		var body = host.Controller.Body;

		// Require separation from floor so ADSing next to a wall while standing/walking never clings.
		if ( body.Velocity.z <= 40f )
		{
			var floorProbe = host.TracePilotHull( host.WorldPosition + Vector3.Up * 2f, host.WorldPosition - Vector3.Up * MinFloorClearance );
			if ( floorProbe.Hit && !floorProbe.StartedSolid && floorProbe.Normal.z >= Titanfall2PilotParkourRules.WalkableNormalZ )
				return false;
		}

		var eyeForward = host.EyeForwardFlat();
		var eyeRight = Vector3.Cross( eyeForward, Vector3.Up ).Normal;
		var probeDist = host.BodyHalfWidth + settings.WallrunAllowedWallDist;
		var chest = host.WorldPosition + Vector3.Up * (host.Controller.CurrentHeight * 0.55f);

		ReadOnlySpan<Vector3> probeDirs =
		[
			eyeForward,
			eyeRight,
			-eyeRight,
			(eyeForward + eyeRight).Normal,
			(eyeForward - eyeRight).Normal
		];

		SceneTraceResult best = default;
		var found = false;
		foreach ( var dir in probeDirs )
		{
			var hit = host.TraceWallRay( chest, chest + dir * probeDist );
			if ( !Titanfall2PilotMoveMode.IsRunnableWallHit( hit ) ) continue;
			if ( host.IsSameWallBlocked( hit.Normal ) ) continue;
			if ( !found || hit.Distance < best.Distance )
			{
				best = hit;
				found = true;
			}
		}

		if ( !found ) return false;
		Start( host, best.Normal.WithZ( 0 ).Normal, fromWallrun: false );
		return true;
	}

	internal void Start( Titanfall2PilotMoveMode host, Vector3 wallNormal, bool fromWallrun )
	{
		host.Slide.End( host, playStopSound: false );
		if ( fromWallrun )
			host.Wallrun.ClearForWallhangTransition();
		else
			host.Parkour?.StartWallrunJetSound();

		IsActive = true;
		WallNormal = wallNormal;
		WallSide = Titanfall2PilotParkourRules.WallSide( host.EyeForwardFlat(), WallNormal );
		Elapsed = 0f;
		HugVelocity = Vector3.Zero;
		host.OnWallAttached();
		host.Controller.Body.Velocity = Vector3.Zero;
		host.Controller.PreventGrounding( 0.15f );
		host.Parkour?.TriggerThrusterBurst( 0.85f );
		host.Parkour?.PlayMovementSound( host.Settings.SoundWallhangStart, fallbackEvent: host.Settings.SoundWallrunImpact );
		host.SyncParkourState();
	}

	internal void StepVelocity( Titanfall2PilotMoveMode host, Rigidbody body )
	{
		var slipZ = Titanfall2PilotParkourRules.ComputeWallhangVerticalVelocity( Elapsed, host.Settings.WallhangTimeLimit );
		body.Velocity = HugVelocity.WithZ( slipZ );
	}

	internal void UpdateAttachment( Titanfall2PilotMoveMode host, float dt )
	{
		var settings = host.Settings;
		Elapsed += dt;
		HugVelocity = Vector3.Zero;

		if ( !host.WantsWallhang )
		{
			Detach( host, pushAway: false );
			return;
		}

		if ( host.Controller.UseInputControls && Input.Down( host.Controller.DuckButton ) )
		{
			Detach( host, pushAway: true );
			return;
		}

		if ( Elapsed >= settings.WallhangTimeLimit )
		{
			Detach( host, pushAway: true );
			return;
		}

		var chest = host.WorldPosition + Vector3.Up * (host.Controller.CurrentHeight * 0.55f);
		var probeDist = host.BodyHalfWidth + settings.WallrunAllowedWallDist;
		var hit = host.TraceWallRay( chest, chest - WallNormal * probeDist );
		if ( !Titanfall2PilotMoveMode.IsRunnableWallHit( hit ) )
		{
			Detach( host, pushAway: false );
			return;
		}

		WallNormal = hit.Normal.WithZ( 0 ).Normal;
		WallSide = Titanfall2PilotParkourRules.WallSide( host.EyeForwardFlat(), WallNormal );

		var desiredDist = host.BodyHalfWidth + 1.5f;
		var error = hit.Distance - desiredDist;
		if ( MathF.Abs( error ) > 0.5f && MathF.Abs( error ) < 18f )
			HugVelocity = -WallNormal * Math.Clamp( error * 10f, -120f, 120f );
	}

	internal void Detach( Titanfall2PilotMoveMode host, bool pushAway )
	{
		var wallNormal = WallNormal;
		host.RecordPreviousWall( wallNormal );
		End( host, playDetachSound: true );
		host.SetWallRepel( 0.18f );
		if ( pushAway )
			host.Controller.Body.Velocity += wallNormal * host.Settings.WallrunFallAwaySpeed;
	}

	internal void End( Titanfall2PilotMoveMode host, bool playDetachSound )
	{
		if ( !IsActive ) return;
		IsActive = false;
		Elapsed = 0f;
		HugVelocity = Vector3.Zero;
		host.Parkour?.StopWallrunJetSound();
		if ( playDetachSound )
			host.Parkour?.PlayMovementSound( host.Settings.SoundWallhangFall );
	}
}
