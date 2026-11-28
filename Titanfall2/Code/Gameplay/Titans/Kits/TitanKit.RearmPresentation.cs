/// <summary>The retail Rearm offhand deploys, charges, then holsters without playing its firing clip.</summary>
internal static class Titanfall2RearmPresentation
{
	internal const string ViewModel = "models/weapons/titan_bt_rocket_pod/atpov_titan_monarch_pods.mdl";
	internal const string DrawRole = "ai_monarch_utility_prepare";
	internal const string ChargeRole = "ai_monarch_utility";
	internal const string HolsterRole = "ai_monarch_utility_recover";
	internal const float DrawSeconds = 16f / 30f;
	internal const float ChargeSeconds = 0.5f;
	internal const float RetractAt = DrawSeconds + ChargeSeconds;
	internal const float PovEnd = RetractAt + 15f / 30f;
	internal const float WorldEnd = RetractAt + 28f / 30f;

	internal static (string Clip, float Cycle) Sample( float elapsed, bool firstPerson )
	{
		if ( !float.IsFinite( elapsed ) || elapsed < 0 || elapsed >= (firstPerson ? PovEnd : WorldEnd) ) return default;
		if ( elapsed < DrawSeconds )
			return (firstPerson ? "draw_seq" : DrawRole, Math.Clamp( elapsed / (firstPerson ? DrawSeconds : 30f / 60f), 0f, 1f ));
		if ( elapsed < RetractAt )
			return (firstPerson ? "charge_seq" : ChargeRole, firstPerson ? 0f : (elapsed - DrawSeconds) / ChargeSeconds);
		return (firstPerson ? "holster_seq" : HolsterRole, (elapsed - RetractAt) / (firstPerson ? 15f / 30f : 28f / 30f));
	}

	// These are the AE_CL_PLAYSOUND keys from the retail sequence descriptors;
	// trigger and fire keys come from mp_titanability_rearm.txt, not Upgrade Core.
	internal static readonly (float Seconds, string Name)[] FirstPersonCues =
	[
		(0f, "rp_monarch_pullout"),
		(DrawSeconds, "Weapon_ChargeRifle_TriggerOn"),
		(RetractAt, "Weapon_ChargeRifle_TriggerOff"),
		(RetractAt, "Titan_Tone_SonarLock_Fired_ShoulderPod_1P"),
		(RetractAt, "rp_monarch_holster")
	];
	internal static readonly (float Seconds, string Name)[] WorldCues =
	[
		(1f / 60f, "Weapon_ShoulderPod_Deploy_Start_3P"),
		(1f / 60f, "Weapon_ShoulderPod_Deploy_Servo_3P"),
		(8f / 60f, "Weapon_ShoulderPod_Deploy_Stop_3P"),
		(RetractAt, "Titan_Tone_SonarLock_Fired_ShoulderPod_3P"),
		(RetractAt + 1f / 30f, "Weapon_ShoulderPod_Retract_Start_3P"),
		(RetractAt + 1f / 30f, "Weapon_ShoulderPod_Retract_Servo_3P"),
		(RetractAt + 8f / 30f, "Weapon_ShoulderPod_Retract_Stop_3P")
	];
}

public sealed partial class Titanfall2TitanKit
{
	// A separate visual clock: Rearm's existing refresh semantics do not depend on assets or rendering.
	[Sync( SyncFlags.FromHost )] double RearmPresentedAt { get; set; } = -1;
	double _lastRearmPresentation = -1;
	float _lastRearmCueTime = -1;
	GameObject _rearmPilot;
	bool _rearmWasFirstPerson;
	int _rearmAiSerial = -1;

	internal float RearmVisualElapsed => RearmPresentedAt >= 0 && IsRearmActorAvailable()
		? (float)Math.Max( 0, Time.NowDouble - RearmPresentedAt ) : float.PositiveInfinity;

	bool IsRearmActorAvailable() => Kit == Chassis.Monarch && Actor is { IsValid: true, Active: true, Dead: false, TitanCanAct: true, IsInTermination: false }
		&& Actor.Seat is not { IsTransitioning: true };

	void BeginRearmPresentation()
	{
		// Autonomous actors already own the retail start/charge/end roles through their AI timeline.
		if ( _aiCommitting ) return;
		RearmPresentedAt = Time.NowDouble;
		_rearmPilot = Actor.Seat?.GetOccupant()?.GameObject;
	}

	void UpdateRearmPresentation()
	{
		if ( Networking.IsHost && !IsProxy && RearmPresentedAt >= 0
			&& (!IsRearmActorAvailable() || RearmVisualElapsed >= Titanfall2RearmPresentation.WorldEnd || _rearmPilot != Actor.Seat?.GetOccupant()?.GameObject) )
			RearmPresentedAt = -1;
		if ( Application.IsHeadless || Application.IsDedicatedServer || !IsRearmActorAvailable() ) return;

		var firstPerson = Actor.Seat?.LocalPilot.IsValid() == true;
		var elapsed = RearmVisualElapsed;
		if ( !float.IsFinite( elapsed ) )
		{
			if ( AiAction != Titanfall2TitanAiAction.Utility || AiPhase == Titanfall2TitanAiPhase.Idle ) return;
			// Map the AI's existing clip clock onto the equivalent world cue interval.
			elapsed = AiAnimationRole switch
			{
				Titanfall2RearmPresentation.DrawRole => AiActionProgress * (30f / 60f),
				Titanfall2RearmPresentation.ChargeRole => Titanfall2RearmPresentation.DrawSeconds + AiActionProgress * (Titanfall2RearmPresentation.ChargeSeconds - 0.001f),
				Titanfall2RearmPresentation.HolsterRole => Titanfall2RearmPresentation.RetractAt + AiActionProgress * (28f / 30f),
				_ => float.PositiveInfinity
			};
			if ( !float.IsFinite( elapsed ) ) return;
		}

		if ( _lastRearmPresentation != RearmPresentedAt || _rearmAiSerial != AiActionSerial )
		{
			_lastRearmPresentation = RearmPresentedAt;
			_rearmAiSerial = AiActionSerial;
			_lastRearmCueTime = elapsed < 0.25f ? -1f : elapsed;
			_rearmWasFirstPerson = firstPerson;
		}
		if ( firstPerson != _rearmWasFirstPerson )
		{
			_lastRearmCueTime = elapsed;
			_rearmWasFirstPerson = firstPerson;
		}
		foreach ( var cue in firstPerson ? Titanfall2RearmPresentation.FirstPersonCues : Titanfall2RearmPresentation.WorldCues )
		{
			// Do not replay stale sounds after a long render/network stall or on late join.
			if ( cue.Seconds <= _lastRearmCueTime || cue.Seconds > elapsed || elapsed - cue.Seconds > 0.25f ) continue;
			if ( firstPerson ) Titanfall2GameplayVisuals.PlayViewSound( Scene, Actor.MountIdent, cue.Name, Eye );
			else Titanfall2GameplayVisuals.PlayFireSound( Scene, Actor.MountIdent, cue.Name, Eye );
		}
		_lastRearmCueTime = MathF.Max( _lastRearmCueTime, elapsed );
	}
}
