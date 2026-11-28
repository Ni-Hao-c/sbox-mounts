public enum Titanfall2RodeoState { None, Mounting, StealingBattery, ThrowingGrenade, InsertingBattery, FriendlyIdle }

/// <summary>Retail multiplayer battery-style rodeo rules; physical placement belongs to the adapter.</summary>
internal static class Titanfall2RodeoRules
{
	internal const float MountRange = 180f;
	internal const float JumpOffDelay = 0.6f;
	internal const float SegmentHealth = 2500f;
	internal const float BatteryHealth = SegmentHealth * 0.5f;
	internal const float BatteryCore = 0.2f;
	internal static Titanfall2RodeoState SelectAction( bool friendly, bool carryingBattery ) => friendly
		? carryingBattery ? Titanfall2RodeoState.InsertingBattery : Titanfall2RodeoState.FriendlyIdle
		: carryingBattery ? Titanfall2RodeoState.ThrowingGrenade : Titanfall2RodeoState.StealingBattery;
	internal static float Damage( bool doomed, float health, bool grenade ) => doomed
		? MathF.Max( 0, float.IsFinite( health ) ? health : 0 ) + 1
		: SegmentHealth * (grenade ? 0.5f : 1f);
	internal static bool CanJumpOff( float elapsed, bool pastPointOfNoReturn ) =>
		float.IsFinite( elapsed ) && elapsed >= JumpOffDelay && !pastPointOfNoReturn;
	internal static float HealedHealth( float health, float maximum, bool doomed, float doomedMaximum = 2500f )
	{
		var limit = doomed ? doomedMaximum : maximum;
		return !float.IsFinite( health ) || !float.IsFinite( limit ) || health <= 0
			? health : MathF.Min( limit, health + BatteryHealth );
	}
}

/// <summary>A host action owns one commit token, independent of visual animation callbacks.</summary>
internal sealed class Titanfall2RodeoCommitGate
{
	internal int Serial { get; private set; }
	internal bool Committed { get; private set; }
	internal void Begin( int serial ) { Serial = serial; Committed = false; }
	internal bool TryCommit( int serial, bool valid, float elapsed, float commitAt )
	{
		if ( serial != Serial || Committed || !valid || !float.IsFinite( elapsed ) || !float.IsFinite( commitAt ) || elapsed < commitAt ) return false;
		Committed = true;
		return true;
	}
}
