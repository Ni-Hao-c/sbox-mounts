/// <summary>
/// Owns Pilot slide initiation, boost cooldown, slope-projected friction, and visual blend factor.
/// </summary>
internal sealed class Titanfall2PilotSlideController
{
	internal bool IsActive { get; private set; }
	internal Vector3 Direction { get; private set; }
	internal float Blend { get; set; }

	TimeSince _sinceStart = 10f;

	internal void Reset()
	{
		IsActive = false;
		Direction = Vector3.Zero;
		Blend = 0f;
	}

	internal void Start( Titanfall2PilotMoveMode host )
	{
		var settings = host.Settings;
		var body = host.Controller.Body;
		var canBoost = _sinceStart > settings.SlideBoostCooldown;
		var (nextVel, boosted) = Titanfall2PilotParkourRules.ApplySlideBoost( body.Velocity, canBoost, settings );
		// Retail starts the cooldown on every successful slide entry, including unboosted landings.
		_sinceStart = 0f;
		body.Velocity = nextVel;
		IsActive = true;
		host.ClearDuckBuffer();
		Direction = nextVel.WithZ( 0 ).LengthSquared > 1f ? nextVel.WithZ( 0 ).Normal : host.EyeForwardFlat();
		if ( boosted )
		{
			host.Parkour?.TriggerThrusterBurst( 1.0f );
			host.Parkour?.PlayMovementSound( settings.SoundSlideBoost1P, settings.SoundSlideBoost3P );
		}
		host.Parkour?.PlayMovementSound( settings.SoundSlideStart1P );
	}

	internal void End( Titanfall2PilotMoveMode host, bool playStopSound )
	{
		if ( !IsActive ) return;
		IsActive = false;
		if ( playStopSound ) host.Parkour?.PlayMovementSound( host.Settings.SoundSlideStop1P );
	}

	internal bool StepVelocity( Titanfall2PilotMoveMode host, Rigidbody body, bool grounded, bool duckHeld, float dt )
	{
		if ( !IsActive ) return false;
		if ( !grounded )
		{
			End( host, playStopSound: false );
			return false;
		}

		var wantsStop = !duckHeld;
		// Retail supplies a general movement wish-speed limit here. Keep the host's limit and input magnitude separate.
		var wishMagnitude = host.WishDir.Length;
		var maxWishSpeed = wishMagnitude > 0.0001f
			? host.Controller.WishVelocity.WithZ( 0 ).Length / wishMagnitude : host.Settings.CrouchSpeed;
		var (nextVel, keepSliding) = Titanfall2PilotParkourRules.StepSlideVelocity(
			body.Velocity - host.Controller.GroundVelocity, host.Controller.GroundNormal, host.WishDir, wantsStop, host.Settings, dt, maxWishSpeed );
		body.Velocity = host.ProjectToGroundPlane( nextVel );
		if ( nextVel.WithZ( 0 ).LengthSquared > 1f )
			Direction = nextVel.WithZ( 0 ).Normal;
		if ( !keepSliding )
			End( host, playStopSound: true );
		return true;
	}

	internal void UpdateBlend( float dt )
	{
		var target = IsActive ? 1f : 0f;
		Blend = MathX.Lerp( Blend, target, Math.Clamp( dt * (IsActive ? 14f : 10f), 0f, 1f ), true );
	}
}
