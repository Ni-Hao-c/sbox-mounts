using Chassis = Titanfall2TitanKit.Chassis;

/// <summary>
/// Pure deterministic rules for retail Titanfall 2 Titan-vs-Titan Terminations:
/// Doomed target eligibility, chassis animation/audio pairing, Monarch Energy Thief bonus, and 3rd-person orbit camera.
/// </summary>
internal static class Titanfall2TitanTerminationRules
{
	internal const float TerminationRange = 260f;
	internal const float MinForwardDot = 0.45f;
	internal const float KillEventFraction = 0.82f;
	internal const float MonarchBatteryShieldBonus = 2500f;
	internal const float MonarchBatteryCoreBonus = 0.20f;
	internal const float StandardTerminationCoreBonus = 0.15f;

	/// <summary>
	/// Retail Titan-vs-Titan termination gate: only an active non-busy Titan may terminate
	/// a hostile Doomed (non-dead, non-busy) Titan within range and forward cone.
	/// </summary>
	internal static bool CanTerminate( bool attackerCanAct, bool attackerBusy, bool victimIsTitan,
		bool victimDoomed, bool victimDead, bool victimBusy, bool hostile, float distance, float forwardDot )
	{
		if ( !attackerCanAct || attackerBusy ) return false;
		if ( !victimIsTitan || !victimDoomed || victimDead || victimBusy || !hostile ) return false;
		if ( !float.IsFinite( distance ) || distance < 0f || distance > TerminationRange ) return false;
		return float.IsFinite( forwardDot ) && forwardDot >= MinForwardDot;
	}

	internal static string VictimRoleFor( Chassis attackerChassis ) => attackerChassis switch
	{
		Chassis.Ion => ModelLoader.ExecVictimIonRole,
		Chassis.Tone => ModelLoader.ExecVictimToneRole,
		Chassis.Legion => ModelLoader.ExecVictimLegionRole,
		Chassis.Scorch => ModelLoader.ExecVictimScorchRole,
		Chassis.Northstar => ModelLoader.ExecVictimNorthstarRole,
		Chassis.Ronin => ModelLoader.ExecVictimRoninRole,
		_ => ModelLoader.ExecVictimMonarchRole
	};

	internal static string ExecutionSoundFor( Chassis attackerChassis ) => attackerChassis switch
	{
		Chassis.Monarch => "monarch_battery_execution_part_01_titans",
		Chassis.Ion => "ion_execution",
		Chassis.Tone => "tone_dlc_execution_part_01",
		Chassis.Legion => "legion_execution",
		Chassis.Scorch => "scorch_dlc_execution_intro_01",
		Chassis.Northstar => "northstar_execution",
		Chassis.Ronin => "ronin_execution",
		_ => "ion_execution"
	};

	internal static string FinishImpactSoundFor( Chassis attackerChassis ) => attackerChassis switch
	{
		Chassis.Monarch => "monarch_battery_execution_bone_crush",
		Chassis.Scorch => "scorch_dlc_execution_final_impact",
		_ => "titan_melee_hit"
	};

	internal static float FallbackDurationFor( Chassis attackerChassis ) => attackerChassis switch
	{
		Chassis.Legion => 4.20f,
		Chassis.Tone => 4.33f,
		Chassis.Scorch => 4.47f,
		Chassis.Ion => 4.67f,
		Chassis.Monarch => 4.83f,
		Chassis.Northstar => 4.83f,
		Chassis.Ronin => 4.83f,
		_ => 4.50f
	};

	/// <summary>Computes a dramatic 3rd-person orbit camera around the paired termination pair.</summary>
	internal static (Vector3 Position, Rotation Rotation) ComputeCinematicCamera( Vector3 focusCenter, Rotation refRotation, float progress, float radius = 320f )
	{
		var t = Math.Clamp( progress, 0f, 1f );
		// Sweep smoothly from rear-right flank across the side profile to capture both Titans cleanly.
		var azimuthDeg = float.Lerp( -128f, -48f, t * t * (3f - 2f * t) );
		var elevation = float.Lerp( 96f, 128f, MathF.Sin( t * MathF.PI ) );
		var dynamicRadius = radius * (1f - 0.12f * MathF.Sin( t * MathF.PI ));

		var orbitRot = refRotation * Rotation.FromYaw( azimuthDeg );
		var camPos = focusCenter + orbitRot.Forward * dynamicRadius + Vector3.Up * elevation;
		var lookTarget = focusCenter + Vector3.Up * 112f;

		// Subtle camera punch right around the killing blow.
		var shake = t is >= 0.78f and <= 0.92f
			? MathF.Sin( (t - 0.78f) * 85f ) * (1f - (t - 0.78f) / 0.14f) * 2.8f
			: 0f;
		var lookRot = Rotation.LookAt( (lookTarget - camPos).Normal, Vector3.Up ) * Rotation.From( new Angles( shake * 0.6f, 0f, shake ) );
		return (camPos, lookRot);
	}
}

/// <summary>
/// Host-authoritative paired Titan-vs-Titan Termination session matching retail _melee_synced_titan.gnut:
/// locks both Titans in invulnerable paired choreography, plays exact retail MDL53 attacker/victim execution clips
/// and Miles audio, drives a collision-safe 3rd-person orbit camera, kills the doomed victim, and restores controls safely.
/// </summary>
[Hide]
public sealed class Titanfall2TitanTermination : Component, ICameraModifier
{
	[Sync( SyncFlags.FromHost )] public Titanfall2Npc Attacker { get; private set; }
	[Sync( SyncFlags.FromHost )] public Titanfall2Npc Victim { get; private set; }
	[Sync( SyncFlags.FromHost )] public Chassis AttackerChassis { get; private set; }
	[Sync( SyncFlags.FromHost )] public float Duration { get; private set; } = 4.5f;
	[Sync( SyncFlags.FromHost )] public float Elapsed { get; private set; }
	[Sync( SyncFlags.FromHost )] public Vector3 RefPosition { get; private set; }
	[Sync( SyncFlags.FromHost )] public Rotation RefRotation { get; private set; }

	GameObject _attackerPilot;
	Vector3 _attackerStartPos;
	bool _killTriggered;
	bool _finished;
	bool _animsStarted;

	public float Progress => Duration > 0.01f ? Math.Clamp( Elapsed / Duration, 0f, 1f ) : 1f;

	// Higher than TitanCockpitView (250) so cinematic 3P camera takes clean precedence during termination.
	int ICameraModifier.CameraOrder => 280;

	/// <summary>Attempts to start a host-authoritative Titan-vs-Titan Termination on a doomed enemy Titan.</summary>
	internal static bool TryStart( Titanfall2Npc attacker, Titanfall2Npc victim, GameObject attackerPilot, Ray aim )
	{
		if ( !Networking.IsHost || !attacker.IsValid() || !victim.IsValid() || attacker == victim ) return false;

		var delta = (victim.WorldPosition - attacker.WorldPosition).WithZ( 0 );
		var dist = delta.Length;
		var fwd = aim.Forward.WithZ( 0 );
		var forwardDot = dist > 0.01f && fwd.LengthSquared > 0.01f ? Vector3.Dot( fwd.Normal, delta / dist ) : 1f;
		var hostile = attacker.IsHostileToFaction( victim.Faction );

		if ( !Titanfall2TitanTerminationRules.CanTerminate(
			attacker.TitanCanAct, attacker.IsInTermination, victim.IsTitan, victim.IsDoomed, victim.Dead,
			victim.IsInTermination, hostile, dist, forwardDot ) )
			return false;

		var kit = attacker.GetComponent<Titanfall2TitanKit>();
		var chassis = kit?.Kit ?? Chassis.Monarch;

		// Lock both Titans immediately (invulnerable + AI/weapon/core suspended, matching retail _melee_synced_titan.gnut).
		attacker.BeginTerminationLock( isAttacker: true, ModelLoader.ExecAttackerRole );
		victim.BeginTerminationLock( isAttacker: false, Titanfall2TitanTerminationRules.VictimRoleFor( chassis ) );

		// Retail alignment: victim stays planted facing attacker; attacker aligns to victim's "ref" attachment (or origin).
		var attackDir = dist > 1f ? (delta / dist) : attacker.WorldRotation.Forward.WithZ( 0 ).Normal;
		if ( attackDir.LengthSquared < 0.01f ) attackDir = Vector3.Forward;
		var refRot = Rotation.LookAt( attackDir, Vector3.Up );
		var victimFaceAttacker = Rotation.LookAt( -attackDir, Vector3.Up );

		var attackerStart = attacker.WorldPosition;
		victim.WorldRotation = victimFaceAttacker;
		var refPos = victim.WorldPosition;
		if ( victim.Renderer.IsValid() && victim.Renderer.GetAttachment( "ref" ) is { } refAttach )
		{
			attacker.WorldPosition = refAttach.Position;
			attacker.WorldRotation = refAttach.Rotation;
		}
		else
		{
			attacker.WorldPosition = refPos;
			attacker.WorldRotation = victimFaceAttacker;
		}

		// Resolve duration from decoded retail sequence if available, else verified retail fallback duration.
		var duration = attacker.GetActorClipDuration( ModelLoader.ExecAttackerRole );
		var victimDur = victim.GetActorClipDuration( Titanfall2TitanTerminationRules.VictimRoleFor( chassis ) );
		if ( victimDur > 0.5f && (duration <= 0.5f || victimDur < duration) ) duration = victimDur;
		if ( duration <= 0.5f ) duration = Titanfall2TitanTerminationRules.FallbackDurationFor( chassis );

		var session = attacker.GameObject.AddComponent<Titanfall2TitanTermination>();
		session.Attacker = attacker;
		session.Victim = victim;
		session.AttackerChassis = chassis;
		session.Duration = Math.Clamp( duration, 1.5f, 6.5f );
		session.Elapsed = 0f;
		session.RefPosition = refPos;
		session.RefRotation = refRot;
		session._attackerPilot = attackerPilot.IsValid() ? attackerPilot : attacker.GameObject;
		session._attackerStartPos = attackerStart;

		session.PlayExecutionAudioRpc( chassis, refPos );
		return true;
	}

	protected override void OnUpdate()
	{
		if ( _finished ) return;

		if ( !_animsStarted && Attacker.IsValid() && Victim.IsValid() )
		{
			_animsStarted = true;
			Attacker.StartTerminationAnimation( ModelLoader.ExecAttackerRole );
			Victim.StartTerminationAnimation( Titanfall2TitanTerminationRules.VictimRoleFor( AttackerChassis ) );
		}

		if ( !Networking.IsHost ) return;

		if ( !Attacker.IsValid() || Attacker.Dead || !Victim.IsValid() )
		{
			CompleteTermination( killedVictim: false );
			return;
		}

		Elapsed += Math.Clamp( Time.Delta, 0f, 0.05f );

		if ( !_killTriggered && Progress >= Titanfall2TitanTerminationRules.KillEventFraction )
		{
			_killTriggered = true;
			PlayFinishEffectsRpc( AttackerChassis, RefPosition + Vector3.Up * 110f );
		}

		if ( Elapsed >= Duration )
			CompleteTermination( killedVictim: true );
	}

	void CompleteTermination( bool killedVictim )
	{
		if ( _finished ) return;
		_finished = true;

		if ( Networking.IsHost )
		{
			if ( Victim.IsValid() )
			{
				Victim.EndTerminationLock();
				if ( killedVictim && !Victim.Dead )
				{
					// Release any seated pilot safely before killing the doomed victim chassis.
					Victim.Seat?.ReleaseSeat();
					Victim.ForceKillTitan( _attackerPilot.IsValid() ? _attackerPilot : Attacker?.GameObject, Attacker?.GameObject );
				}
			}

			if ( Attacker.IsValid() )
			{
				Attacker.EndTerminationLock();
				if ( !Attacker.Dead )
				{
					Attacker.WorldPosition = ResolveSafeExitPosition( Attacker, RefPosition, _attackerStartPos );
					Attacker.WorldRotation = RefRotation;

					if ( killedVictim )
					{
						Attacker.NotifyHitMarker( kill: true );
						var core = Attacker.GetComponent<Titanfall2TitanCore>();
						if ( AttackerChassis == Chassis.Monarch )
						{
							// Retail Monarch Battery Thief bonus: restores shield + grants 20% Upgrade Core charge!
							Attacker.RestoreTitanShield( Titanfall2TitanTerminationRules.MonarchBatteryShieldBonus );
							core?.GrantTerminationCharge( Titanfall2TitanTerminationRules.MonarchBatteryCoreBonus );
						}
						else
						{
							core?.GrantTerminationCharge( Titanfall2TitanTerminationRules.StandardTerminationCoreBonus );
						}
					}
				}
			}
		}

		Destroy();
	}

	Vector3 ResolveSafeExitPosition( Titanfall2Npc attacker, Vector3 victimPos, Vector3 originalStart )
	{
		var radius = MathF.Max( 12f, attacker.BodyRadius - 2f );
		var hull = new BBox( new Vector3( -radius, -radius, 4f ), new Vector3( radius, radius, MathF.Max( 32f, attacker.BodyHeight - 4f ) ) );
		const Titanfall2TraceMask mask = (Titanfall2TraceMask)((uint)Titanfall2TraceMask.NpcSolid | (uint)Titanfall2Contents.TitanClip);

		// Prefer the attacker's pre-execution ground spot if unobstructed, else victim's spot.
		var checkStart = Titanfall2CollisionQueries.Run( Scene.Trace.Box( hull, originalStart + Vector3.Up * 8f, originalStart + Vector3.Up * 8f )
			.IgnoreGameObjectHierarchy( attacker.GameObject ).WithoutTags( "trigger", "projectile" ), mask );
		if ( !checkStart.StartedSolid && !checkStart.Hit ) return originalStart;

		var checkVictim = Titanfall2CollisionQueries.Run( Scene.Trace.Box( hull, victimPos + Vector3.Up * 8f, victimPos + Vector3.Up * 8f )
			.IgnoreGameObjectHierarchy( attacker.GameObject ).WithoutTags( "trigger", "projectile" ), mask );
		if ( !checkVictim.StartedSolid && !checkVictim.Hit ) return victimPos;

		return originalStart;
	}

	protected override void OnDisabled()
	{
		if ( !_finished && Networking.IsHost )
			CompleteTermination( killedVictim: false );
	}

	protected override void OnDestroy()
	{
		if ( !_finished && Networking.IsHost )
			CompleteTermination( killedVictim: false );
	}

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( _finished || camera != Scene.Camera || !Attacker.IsValid() ) return;
		var localPilot = Attacker.Seat?.LocalPilot;
		if ( !localPilot.IsValid() || localPilot.IsProxy ) return;

		var (desiredPos, desiredRot) = Titanfall2TitanTerminationRules.ComputeCinematicCamera( RefPosition, RefRotation, Progress );
		var focus = RefPosition + Vector3.Up * 112f;
		var trace = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( focus, desiredPos )
			.Radius( 10f )
			.IgnoreGameObjectHierarchy( Attacker.GameObject )
			.WithoutTags( "trigger", "projectile", "player" ), Titanfall2TraceMask.PlayerSolid );
		var finalPos = trace.Hit && !trace.StartedSolid ? trace.EndPosition + trace.Normal * 6f : desiredPos;

		view.Position = finalPos;
		view.Rotation = desiredRot;
		view.FieldOfView = 76f;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayExecutionAudioRpc( Chassis chassis, Vector3 origin )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var ident = Attacker.IsValid() ? (Attacker.MountIdent ?? "titanfall2") : "titanfall2";
		Titanfall2GameplayVisuals.PlayFireSound( Scene, ident, Titanfall2TitanTerminationRules.ExecutionSoundFor( chassis ), origin + Vector3.Up * 100f );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayFinishEffectsRpc( Chassis chassis, Vector3 point )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !Scene.IsValid() ) return;
		var ident = Attacker.IsValid() ? (Attacker.MountIdent ?? "titanfall2") : "titanfall2";
		var go = new GameObject( Scene, false, "Titan Termination Burst" ) { WorldPosition = point };
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		Titanfall2CombatEffects.Burst( go, new Color( 1f, 0.45f, 0.14f ), 48, 220, 34, 1.1f, smoke: true );
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 1.4f;
		go.Enabled = true;
		Titanfall2GameplayVisuals.PlayFireSound( Scene, ident, Titanfall2TitanTerminationRules.FinishImpactSoundFor( chassis ), point );
	}
}
