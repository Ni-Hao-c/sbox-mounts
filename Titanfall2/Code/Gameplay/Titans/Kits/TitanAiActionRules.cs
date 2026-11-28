using Chassis = Titanfall2TitanKit.Chassis;

public enum Titanfall2TitanAiAction { None, Offense, Defense, Utility, Smoke, Melee, Core, VortexRelease }
public enum Titanfall2TitanAiPhase { Idle, Prepare, Active, Recover }

/// <summary>Small host action clock. Effects are committed exactly once; cancellation never commits them.</summary>
internal sealed class Titanfall2TitanAiActionClock
{
	internal Titanfall2TitanAiAction Action { get; private set; }
	internal Titanfall2TitanAiPhase Phase { get; private set; }
	internal int Serial { get; private set; }
	internal float Elapsed { get; private set; }
	internal float PrepareSeconds { get; private set; }
	internal float RecoverySeconds { get; private set; }
	internal bool Committed { get; private set; }
	internal bool Busy => Action != Titanfall2TitanAiAction.None;
	internal bool Start( Titanfall2TitanAiAction action, float prepare, float recovery )
	{
		if ( Busy || action == Titanfall2TitanAiAction.None || !float.IsFinite( prepare ) || !float.IsFinite( recovery ) ) return false;
		Action = action; PrepareSeconds = MathF.Max( 0, prepare ); RecoverySeconds = MathF.Max( 0, recovery );
		Elapsed = 0; Committed = false; Phase = Titanfall2TitanAiPhase.Prepare; Serial++;
		return true;
	}
	internal bool Advance( float seconds )
	{
		if ( !Busy || !float.IsFinite( seconds ) || seconds < 0 ) return false;
		Elapsed += seconds;
		if ( Committed || Elapsed < PrepareSeconds ) return false;
		Committed = true; Phase = Titanfall2TitanAiPhase.Active;
		return true;
	}
	internal void Recover() { if ( Busy ) { Phase = Titanfall2TitanAiPhase.Recover; Elapsed = 0; } }
	internal bool RecoveryFinished => Phase == Titanfall2TitanAiPhase.Recover && Elapsed >= RecoverySeconds;
	internal void Cancel() { Action = Titanfall2TitanAiAction.None; Phase = Titanfall2TitanAiPhase.Idle; Elapsed = 0; Committed = false; }
}

internal static class Titanfall2TitanAiRules
{
	internal readonly record struct Timing( float Prepare, float Recover, float VisualDelay, float VisualDuration );
	internal static Timing EventTiming( float prepare, float recovery, float duration, float cycle )
	{
		if ( !float.IsFinite( duration ) || duration <= 0 || !float.IsFinite( cycle ) || cycle is < 0 or > 1 )
			return new( prepare, recovery, 0, prepare + recovery );
		var contact = duration * cycle;
		var delay = MathF.Max( 0, prepare - contact );
		return new( delay + contact, MathF.Max( recovery, duration - contact ), delay, duration );
	}
	internal static bool CoreHasBenefit( Chassis kit, int upgradeLevel, float shieldFraction, float distance,
		float targetHealth, bool targetHeavy, bool groundClear )
	{
		if ( !float.IsFinite( distance ) || distance < 0 || targetHealth <= 0 ) return false;
		return kit switch
		{
			Chassis.Monarch => upgradeLevel < 3 || shieldFraction < 0.8f,
			Chassis.Ronin => targetHeavy && distance < 750,
			Chassis.Scorch => groundClear && distance < 1300 && targetHeavy,
			Chassis.Northstar => targetHeavy && distance is > 400 and < 4500,
			Chassis.Ion or Chassis.Tone or Chassis.Legion => targetHeavy && targetHealth > 700 && distance < 4500,
			_ => false
		};
	}
	internal static bool RearmHasBenefit( float offense, float defense, int rounds, int magazine, int dashes, int maxDashes )
		=> offense > 2 && defense > 2 || rounds <= Math.Max( 1, magazine / 4 ) || dashes == 0 && maxDashes > 0;
	internal static bool ShouldReleaseVortex( float elapsed, float maximum, int energy, int reserve, int captured,
		bool underFire, bool visible ) => elapsed >= maximum || energy <= reserve
		|| elapsed >= 0.4f && captured > 0 && visible && !underFire;
	internal static float PreferredRange( Chassis kit, bool swordCore, bool evasive, float configured )
		=> swordCore && kit == Chassis.Ronin ? 100f : configured * (evasive ? 1.25f : 1f);
	internal static bool BlocksPrimary( Titanfall2TitanAiAction action, Titanfall2TitanAiPhase phase )
		=> phase != Titanfall2TitanAiPhase.Idle && action is Titanfall2TitanAiAction.Offense
			or Titanfall2TitanAiAction.Melee or Titanfall2TitanAiAction.Utility or Titanfall2TitanAiAction.VortexRelease;
}
