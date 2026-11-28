public sealed partial class Titanfall2TitanKit
{
	bool _aiDashActive, _aiFlightActive, _aiLanding, _aiAgentWasEnabled;
	Vector3 _aiDashDirection;
	float _aiDashDistance, _aiDashSpeed, _aiFlightBaseZ, _aiLandingSeconds;
	internal bool AiControlsMovement => _aiDashActive || _aiFlightActive || AiBlocksMovement;
	internal bool AiBlocksMovement => AiAutonomous && (_aiAction.Busy && (_aiAction.Action == Titanfall2TitanAiAction.Melee
			|| _aiAction.Action == Titanfall2TitanAiAction.Offense && Kit is Chassis.Ronin or Chassis.Scorch
			|| _aiAction.Action == Titanfall2TitanAiAction.Core && Kit is Chassis.Ion or Chassis.Tone or Chassis.Scorch)
			|| Actor.GetComponent<Titanfall2TitanCore>() is { BlocksPrimary: true, SwordCoreActive: false, FlightCoreActive: false });

	bool ConsumeDashCharge()
	{
		EnsureDashInitialized();
		if ( DashCharges <= 0 ) return false;
		DashCharges--;
		if ( _dashChargeTimer <= 0 ) _dashChargeTimer = MathF.Max( 0.5f, DashRechargeSeconds );
		DashRemaining = DashCharges > 0 ? 0 : _dashChargeTimer;
		DashRechargeProgress = Math.Clamp( 1f - _dashChargeTimer / MathF.Max( 0.5f, DashRechargeSeconds ), 0, 1 );
		return true;
	}
	bool HasAiFlightClearance()
	{
		if ( !Actor.IsValid() || Actor.IsTraversing || _aiDashActive || _aiLanding ) return false;
		if ( _aiFlightActive ) return true;
		var ceiling = SweepAiBody( Actor.WorldPosition, Actor.WorldPosition + Vector3.Up * 185 );
		if ( ceiling.StartedSolid || ceiling.Hit ) return false;
		var floor = SweepAiBody( Actor.WorldPosition + Vector3.Up * 8, Actor.WorldPosition - Vector3.Up * 64 );
		return floor.Hit && !floor.StartedSolid && floor.Normal.z >= Titanfall2TitanDriveRules.WalkableNormalZ;
	}
	bool TryTacticalDash( GameObject target, float distance, float health, float shield, bool incoming, Vector3 threat,
		Titanfall2NpcAiProfile profile )
	{
		if ( DashCharges <= 0 || AiControlsMovement || Actor.IsTraversing || IsPhasing || _aiAction.Busy ) return false;
		var period = profile?.DodgePeriod ?? 8;
		while ( _aiDashTimes.Count > 0 && Time.NowDouble - _aiDashTimes.Peek() > period ) _aiDashTimes.Dequeue();
		if ( _aiDashTimes.Count >= (profile?.MaxDodgesPerPeriod ?? 2) ) return false;
		var hazard = Actor.TryFindNearbyHazard( out var hazardPoint, out _ );
		var intent = Titanfall2TacticsRules.ChooseAiDash( Kit, health, shield, Actor.IsUnderFire || incoming,
			hazard || incoming, distance, AiPreferredRangeOverride );
		Vector3 direction = intent switch
		{
			Titanfall2AiDashIntent.EvadeHazard => incoming
				? Vector3.Cross( (threat - Actor.WorldPosition).WithZ( 0 ).Normal, Vector3.Up )
				: (Actor.WorldPosition - hazardPoint).WithZ( 0 ),
			Titanfall2AiDashIntent.KiteBack when target.IsValid() => (Actor.WorldPosition - target.WorldPosition).WithZ( 0 ),
			Titanfall2AiDashIntent.CloseIn when target.IsValid() => (target.WorldPosition - Actor.WorldPosition).WithZ( 0 ),
			Titanfall2AiDashIntent.LateralStrafe when target.IsValid() => Vector3.Cross( (target.WorldPosition - Actor.WorldPosition).WithZ( 0 ).Normal, Vector3.Up ),
			_ => Vector3.Zero
		};
		if ( direction.LengthSquared < 1 || !TryAiDash( direction.Normal ) ) return false;
		_aiDashTimes.Enqueue( Time.NowDouble ); AiDecisionReason = "dash:" + intent;
		return true;
	}

	internal bool TryAiDash( Vector3 direction )
	{
		EnsureDashInitialized();
		if ( !Ready || DashCharges <= 0 || AiControlsMovement || Actor.IsTraversing || Actor.Seat is { IsOccupied: true }
			|| direction.WithZ( 0 ).LengthSquared < 0.01f || !direction.IsFinite ) return false;
		var flat = direction.WithZ( 0 ).Normal;
		var distance = Kit is Chassis.Ronin or Chassis.Northstar ? 240f : Kit is Chassis.Legion or Chassis.Scorch ? 160f : 200f;
		var start = Actor.WorldPosition;
		var sweep = SweepAiBody( start, start + flat * distance );
		if ( sweep.StartedSolid || (sweep.EndPosition - start).WithZ( 0 ).Length < 48 ) return false;
		var ground = SweepAiBody( sweep.EndPosition + Vector3.Up * 12, sweep.EndPosition - Vector3.Up * 64 );
		if ( !ground.Hit || ground.StartedSolid || ground.Normal.z < Titanfall2TitanDriveRules.WalkableNormalZ ) return false;
		if ( !ConsumeDashCharge() ) return false;
		AcquireAiMovement();
		_aiDashActive = true; _aiDashDirection = flat;
		_aiDashDistance = MathF.Min( distance, (sweep.EndPosition - start).WithZ( 0 ).Length );
		_aiDashSpeed = _aiDashDistance / 0.3f;
		return true;
	}

	void TickAiMotion()
	{
		if ( !Actor.IsValid() ) return;
		if ( Actor.Seat is { IsOccupied: true } or { IsEmbarking: true } || Actor.IsInTermination || !Actor.TitanCanAct )
		{
			CancelAiMotion(); return;
		}
		if ( _aiDashActive )
		{
			var step = MathF.Min( _aiDashDistance, _aiDashSpeed * Time.Delta );
			var start = Actor.WorldPosition;
			var displacement = _aiDashDirection * step;
			if ( Actor.GetComponent<Titanfall2TetherConstraint>() is { HasTethers: true } tether )
				displacement = tether.Constrain( Actor, displacement / MathF.Max( Time.Delta, 0.001f ), Time.Delta ) * Time.Delta;
			var sweep = SweepAiBody( start, start + displacement );
			if ( sweep.StartedSolid ) { EndAiDash(); return; }
			var floor = SweepAiBody( sweep.EndPosition + Vector3.Up * 12, sweep.EndPosition - Vector3.Up * 40 );
			if ( !floor.Hit || floor.StartedSolid || floor.Normal.z < Titanfall2TitanDriveRules.WalkableNormalZ ) { EndAiDash(); return; }
			Actor.WorldPosition = floor.EndPosition;
			_aiDashDistance -= step;
			if ( sweep.Hit || _aiDashDistance <= 0.1f ) EndAiDash();
			return;
		}
		var wantsFlight = Kit == Chassis.Northstar && (HoverRemaining > 0 || Actor.GetComponent<Titanfall2TitanCore>() is { FlightCoreActive: true });
		if ( wantsFlight && !_aiFlightActive && !Actor.IsTraversing )
		{
			var ceiling = SweepAiBody( Actor.WorldPosition, Actor.WorldPosition + Vector3.Up * 185 );
			if ( ceiling.StartedSolid || ceiling.EndPosition.z - Actor.WorldPosition.z < 60 )
			{
				HoverRemaining = 0; Actor.GetComponent<Titanfall2TitanCore>()?.CancelForTitanLife();
				AiDecisionReason = "flight-no-headroom"; return;
			}
			_aiFlightBaseZ = Actor.WorldPosition.z; AcquireAiMovement(); _aiFlightActive = true; _aiLanding = false; _aiLandingSeconds = 0;
		}
		if ( !_aiFlightActive ) return;
		if ( !wantsFlight ) _aiLanding = true;
		var targetZ = _aiLanding ? _aiFlightBaseZ - 32 : _aiFlightBaseZ + 185;
		var travel = Math.Clamp( targetZ - Actor.WorldPosition.z, -160 * Time.Delta, 240 * Time.Delta );
		var hit = SweepAiBody( Actor.WorldPosition, Actor.WorldPosition + Vector3.Up * travel );
		if ( !hit.StartedSolid ) Actor.WorldPosition = hit.EndPosition;
		if ( _aiLanding )
		{
			_aiLandingSeconds += Time.Delta;
			if ( _aiLandingSeconds > 6 )
			{
				_aiFlightActive = false; _aiLanding = false; ReleaseAiMovement(); AiDecisionReason = "flight-landing-timeout"; return;
			}
			if ( hit.Hit && hit.Normal.z >= Titanfall2TitanDriveRules.WalkableNormalZ )
			{
				_aiFlightActive = false; _aiLanding = false; ReleaseAiMovement(); AiDecisionReason = "flight-landed";
			}
			else if ( hit.StartedSolid )
			{
				// Do not teleport through an obstacle or declare success; release to the host's normal collision mover.
				_aiFlightActive = false; _aiLanding = false; ReleaseAiMovement(); AiDecisionReason = "flight-landing-blocked";
			}
			else if ( Actor.WorldPosition.z <= targetZ + 0.1f ) _aiFlightBaseZ -= 32;
		}
		else if ( hit.Hit || hit.StartedSolid ) { HoverRemaining = 0; _aiLanding = true; Actor.GetComponent<Titanfall2TitanCore>()?.CancelForTitanLife(); }
	}
	SceneTraceResult SweepAiBody( Vector3 from, Vector3 to )
	{
		var radius = MathF.Max( 8, Actor.BodyRadius - 2 );
		var hull = new BBox( new Vector3( -radius, -radius, 2 ), new Vector3( radius, radius, MathF.Max( 24, Actor.BodyHeight - 2 ) ) );
		const Titanfall2TraceMask mask = (Titanfall2TraceMask)((uint)Titanfall2TraceMask.NpcSolid | (uint)Titanfall2Contents.TitanClip);
		return Titanfall2CollisionQueries.Run( Scene.Trace.Box( hull, from, to ).IgnoreGameObjectHierarchy( Actor.GameObject )
			.WithoutTags( "trigger", "projectile", "tether-trap" ), mask );
	}
	void AcquireAiMovement()
	{
		_aiAgentWasEnabled = Actor.Agent.IsValid() && Actor.Agent.Enabled;
		Actor.Movement?.Stop();
		if ( _aiAgentWasEnabled ) Actor.Agent.Enabled = false;
	}
	void ReleaseAiMovement()
	{
		if ( Actor.IsValid() && Actor.Agent.IsValid() && _aiAgentWasEnabled && !Actor.Dead
			&& !Actor.IsInTermination && Actor.Seat is not ({ IsOccupied: true } or { IsEmbarking: true }) )
		{
			Actor.Agent.Enabled = true;
			Actor.Agent.SetAgentPosition( Actor.WorldPosition );
		}
		_aiAgentWasEnabled = false;
	}
	void EndAiDash() { _aiDashActive = false; _aiDashDistance = 0; ReleaseAiMovement(); }
	void CancelAiMotion()
	{
		_aiDashActive = false; _aiFlightActive = false; _aiLanding = false; _aiDashDistance = 0; ReleaseAiMovement();
	}
}
