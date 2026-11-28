using System.Collections.ObjectModel;
using System.Globalization;

/// <summary>Explicit gameplay policy; a friendly enemy-profile Titan is not implicitly an Auto-Titan.</summary>
public enum Titanfall2NpcAiMode { EnemyMp, EnemySp, AutoTitanFollow, AutoTitanGuard, EnhancedAutoTitanFollow, EnhancedAutoTitanGuard }

internal enum Titanfall2NpcAbilityUseCheck { None, TrackerHit, GroundClear, WhileMoving, WhileEvasive, ElectricSmoke, Grapple, SharedEnergy, WideSightClear, EnemyRangeChange, Unsupported }
internal enum Titanfall2NpcAbilityKind { None, Vortex, VortexMelee, Wall, AttachedWall, Unsupported }
internal enum Titanfall2NpcAbilityEligibility { Denied, Allowed, NeedsGeometry }

internal readonly record struct Titanfall2NpcAbilityContext(
	bool Moving = false, bool Evasive = false, bool GroundClear = false, bool WideSightClear = false,
	bool HasTrackerLock = false, float SharedEnergy = 0, bool PhaseActive = false,
	float Range = 0, float PreviousRange = 0, bool HasEnemyRider = false );

internal sealed record Titanfall2NpcAiSource( string Path, string SourceId, string SourceIdentity );
internal sealed record Titanfall2NpcAiSelector( string Name, string Predicate, string Path, int Line );

/// <summary>Immutable resolved values. Fallbacks are explicit in ValueSource and do not claim a retail origin.</summary>
internal abstract record Titanfall2NpcAiValues
{
	internal IReadOnlyDictionary<string, Titanfall2NpcAiKeyValues.Value> Values { get; init; }
		= new ReadOnlyDictionary<string, Titanfall2NpcAiKeyValues.Value>( new Dictionary<string, Titanfall2NpcAiKeyValues.Value>( StringComparer.OrdinalIgnoreCase ) );
	internal IReadOnlyList<Titanfall2NpcAiSource> Sources { get; init; } = Array.Empty<Titanfall2NpcAiSource>();
	internal IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
	internal bool Available { get; init; }
	internal string Text( string key, string fallback = "" ) => Values.TryGetValue( key, out var value ) ? value.Text : fallback;
	internal float Number( string key, float fallback = 0 ) => Values.TryGetValue( key, out var value )
		&& float.TryParse( value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) && float.IsFinite( number ) ? number : fallback;
	internal bool Flag( string key, bool fallback = false ) => Values.TryGetValue( key, out var value )
		? (bool.TryParse( value.Text, out var result ) ? result : Number( key, fallback ? 1 : 0 ) != 0) : fallback;
	internal string ValueSource( string key ) => Values.TryGetValue( key, out var value ) ? value.Path : "fallback";
}

internal sealed record Titanfall2NpcWeaponAiProfile : Titanfall2NpcAiValues
{
	internal string ScriptName { get; init; } = "";
	internal Titanfall2NpcAbilityUseCheck UseCheck { get; init; }
	internal Titanfall2NpcAbilityKind AbilityKind { get; init; }
	internal float MinEngageRange => MathF.Max( 0, Number( "npc_min_engage_range" ) );
	internal float MaxEngageRange => MathF.Max( MinEngageRange, Number( "npc_max_engage_range", Number( "npc_max_range", 4000 ) ) );
	internal float MinHeavyEngageRange => MathF.Max( 0, Number( "npc_min_engage_range_heavy_armor", MinEngageRange ) );
	internal float MaxHeavyEngageRange => MathF.Max( MinHeavyEngageRange, Number( "npc_max_engage_range_heavy_armor", MaxEngageRange ) );
	internal int MinBurst => (int)Math.Clamp( Number( "npc_min_burst", 1 ), 0, 128 );
	internal int MaxBurst => Math.Max( MinBurst, (int)Math.Clamp( Number( "npc_max_burst", MinBurst ), 0, 128 ) );
	internal float MinRest => Math.Clamp( Number( "npc_rest_time_between_bursts_min", 0.5f ), 0, 120 );
	internal float MaxRest => Math.Clamp( Number( "npc_rest_time_between_bursts_max", MinRest ), MinRest, 120 );
	internal float PreFireDelay => Math.Clamp( Number( "npc_pre_fire_delay" ), 0, 30 );
	internal float MinCharge => Math.Clamp( Number( "npc_charge_time_min" ), 0, 30 );
	internal float MaxCharge => Math.Clamp( Number( "npc_charge_time_max", MinCharge ), MinCharge, 30 );
	internal float SharedEnergyCost => Math.Clamp( Number( "shared_energy_cost" ), 0, 10000 );
	internal float SharedEnergyChargeCost => Math.Clamp( Number( "shared_energy_charge_cost" ), 0, 10000 );
	internal float ShortDuration => Math.Clamp( Number( "npc_use_short_duration", 1 ), 0.1f, 30 );
	internal float NormalDuration => Math.Clamp( Number( "npc_use_normal_duration", 2.5f ), 0.1f, 30 );
	internal float MinUseDamage => MathF.Max( 0, Number( "npc_use_min_damage" ) );
	internal float MaxUseDamage => MathF.Max( MinUseDamage, Number( "npc_use_max_damage", MinUseDamage ) );
	internal float MinProjectileDamage => MathF.Max( 0, Number( "npc_use_min_projectile_damage" ) );
	internal float RangeChangeThreshold => MathF.Max( 0, Number( "npc_alt_fire_check_value" ) );
	internal bool AutomaticBurstAllowed => Available && MaxBurst > 0;

	/// <summary>
	/// Mirrors the distinction between a failed use check, a qualifying special condition, and an additional geometry check.
	/// Inputs must be observed host state; this does not read hidden enemies or spend resources.
	/// </summary>
	internal Titanfall2NpcAbilityEligibility EvaluateUseCheck( Titanfall2NpcAbilityContext context, out string reason )
	{
		reason = "";
		if ( !Available ) return Denied( "missing-ability-data", out reason );
		if ( AbilityKind == Titanfall2NpcAbilityKind.Unsupported ) return Denied( "unsupported-ability-kind", out reason );
		if ( context.PhaseActive && ScriptName == "mp_titanweapon_arc_wave" ) return Denied( "phase-blocks-arc-wave", out reason );
		switch ( UseCheck )
		{
			case Titanfall2NpcAbilityUseCheck.TrackerHit:
				return context.HasTrackerLock ? Titanfall2NpcAbilityEligibility.Allowed : Denied( "tracker-lock-required", out reason );
			case Titanfall2NpcAbilityUseCheck.WhileMoving:
				return context.Moving ? Titanfall2NpcAbilityEligibility.Allowed : Denied( "movement-required", out reason );
			case Titanfall2NpcAbilityUseCheck.WhileEvasive:
				return context.Evasive ? Titanfall2NpcAbilityEligibility.NeedsGeometry : Denied( "evasive-state-required", out reason );
			case Titanfall2NpcAbilityUseCheck.SharedEnergy:
				return context.SharedEnergy >= MathF.Max( SharedEnergyCost, SharedEnergyChargeCost )
					? Titanfall2NpcAbilityEligibility.NeedsGeometry : Denied( "shared-energy-required", out reason );
			case Titanfall2NpcAbilityUseCheck.ElectricSmoke:
				return context.HasEnemyRider || context.Evasive || context.Range < 500
					? Titanfall2NpcAbilityEligibility.Allowed : Denied( "no-close-threat", out reason );
			case Titanfall2NpcAbilityUseCheck.EnemyRangeChange:
				var threshold = RangeChangeThreshold;
				return threshold > 0 && (context.Range < threshold) != (context.PreviousRange < threshold)
					? Titanfall2NpcAbilityEligibility.Allowed : Denied( "range-threshold-not-crossed", out reason );
			case Titanfall2NpcAbilityUseCheck.Unsupported:
			case Titanfall2NpcAbilityUseCheck.Grapple:
				return Denied( "unsupported-use-check:" + Text( "npc_use_check_type" ), out reason );
			default:
				return Titanfall2NpcAbilityEligibility.NeedsGeometry;
		}
	}

	internal bool CanUse( Titanfall2NpcAbilityContext context, out string reason )
	{
		var result = EvaluateUseCheck( context, out reason );
		if ( result == Titanfall2NpcAbilityEligibility.Denied ) return false;
		if ( result == Titanfall2NpcAbilityEligibility.Allowed ) return true;
		var clear = UseCheck == Titanfall2NpcAbilityUseCheck.GroundClear ? context.GroundClear : context.WideSightClear;
		if ( !clear ) reason = UseCheck == Titanfall2NpcAbilityUseCheck.GroundClear ? "ground-path-blocked" : "sight-blocked";
		return clear;
	}
	static Titanfall2NpcAbilityEligibility Denied( string value, out string reason ) { reason = value; return Titanfall2NpcAbilityEligibility.Denied; }
}

internal sealed record Titanfall2NpcAiProfile : Titanfall2NpcAiValues
{
	internal string SettingsName { get; init; } = "";
	internal Titanfall2NpcAiMode Mode { get; init; }
	internal string BehaviorSelector { get; init; } = "";
	internal IReadOnlyDictionary<string, Titanfall2NpcAiSelector[]> Selectors { get; init; }
		= new ReadOnlyDictionary<string, Titanfall2NpcAiSelector[]>( new Dictionary<string, Titanfall2NpcAiSelector[]>( StringComparer.OrdinalIgnoreCase ) );
	internal Titanfall2NpcWeaponAiProfile Primary { get; init; } = new();
	internal Titanfall2NpcWeaponAiProfile Offense { get; init; } = new();
	internal Titanfall2NpcWeaponAiProfile Defense { get; init; } = new();
	internal Titanfall2NpcWeaponAiProfile Utility { get; init; } = new();
	internal bool Singleplayer => Mode == Titanfall2NpcAiMode.EnemySp;
	internal bool IsAutoTitan => Mode is Titanfall2NpcAiMode.AutoTitanFollow or Titanfall2NpcAiMode.AutoTitanGuard;
	internal bool IsEnhancedAutoTitan => Mode is Titanfall2NpcAiMode.EnhancedAutoTitanFollow or Titanfall2NpcAiMode.EnhancedAutoTitanGuard;
	// _ai_lethality disables ordinary Auto-Titan ordnance and center weapons. Enhanced is a distinct policy.
	internal bool AllowOffense => !IsAutoTitan;
	internal bool AllowUtility => !IsAutoTitan;
	internal bool AllowCore => !IsAutoTitan;
	internal float ChaseStopDistance => Math.Clamp( Number( "chaseStopDist", 200 ), 0, 16000 );
	internal float ChaseStopHeavyDistance => Math.Clamp( Number( "chaseStopDistHeavyArmor", 1000 ), 0, 16000 );
	internal float CircleStrafeDistance => Math.Clamp( Number( "circleStrafeDist", 1000 ), 0, 16000 );
	internal float EvasiveShieldFraction => Math.Clamp( Number( "evasiveCombatShieldPct", 0.8f ), 0, 1 );
	internal float EvasiveHealthRateDifference => Number( "evasiveCombatHealthChangeRateDiff", -200 );
	internal float AggressiveHealthRateDifference => Number( "aggresiveCombatHealthChangeRateDiff", 100 );
	internal float EvasiveTotalHealthDifference => Math.Clamp( Number( "evasiveCombatTotalHealthDiffPct", Singleplayer ? 0.2f : 1f ), 0, 10 );
	internal float AggressiveTotalHealthDifference => Math.Clamp( Number( "aggressiveCombatTotalHealthDiffPct", 0.3f ), 0, 10 );
	internal float DodgePeriod => Math.Clamp( Number( "dodgePeriod", 8 ), 0.1f, 120 );
	internal int MaxDodgesPerPeriod => (int)Math.Clamp( Number( "maxDodgePerPeriod", 2 ), 0, 16 );
	internal float SharedEnergyTotal => Math.Clamp( Number( "sharedEnergyTotal", 1000 ), 0, 10000 );
	internal float SharedEnergyRegenRate => Math.Clamp( Number( "sharedEnergyRegenRate", 100 ), 0, 10000 );
	internal float SharedEnergyRegenDelay => Math.Clamp( Number( "sharedEnergyRegenDelay", 0.2f ), 0, 30 );
	internal float MeleeInterval => Math.Clamp( Number( "meleeInterval", 2 ), 0.1f, 30 );
	internal int MaxMeleeCombo => (int)Math.Clamp( Number( "meleeMaxCombo", 2 ), 1, 32 );
	internal string TraverseAnimType => Text( "TraverseAnimType" );
	internal float TraverseCostFactor => Math.Clamp( Number( "traverseCostFactor", 1 ), 0.01f, 100 );
	internal bool HasSelector( string state, string name ) => Selectors.TryGetValue( state, out var list )
		&& list.Any( entry => entry.Name.Equals( name, StringComparison.Ordinal ) || entry.Name.EndsWith( "::" + name, StringComparison.Ordinal ) );
	internal IEnumerable<Titanfall2NpcAiSelector> UnhandledSelectors( IEnumerable<string> implemented )
	{
		var supported = new HashSet<string>( implemented, StringComparer.Ordinal );
		return Selectors.Values.SelectMany( entries => entries ).Where( entry => !supported.Contains( entry.Name ) ).Distinct();
	}
}
