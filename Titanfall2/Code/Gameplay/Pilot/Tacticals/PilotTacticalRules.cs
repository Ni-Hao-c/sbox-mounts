using System.Globalization;

/// <summary>Core Titanfall 2 Pilot Tactical ability selection.</summary>
public enum Titanfall2PilotTacticalKind
{
	Stim = 0,
	Grapple = 1,
	PhaseShift = 2,
	PulseBlade = 3
}

/// <summary>
/// Retail Pilot tactical parameters parsed from scripts/players/mp/pilot_base.set (grapple_*)
/// and mp_ability_heal / mp_ability_grapple / mp_ability_shifter / mp_weapon_grenade_sonar.
/// </summary>
public readonly record struct Titanfall2PilotTacticalSettings
{
	internal const string PilotBaseScript = "scripts/players/mp/pilot_base.set";
	internal const string StimScript = "scripts/weapons/mp_ability_heal.txt";
	internal const string GrappleScript = "scripts/weapons/mp_ability_grapple.txt";
	internal const string PhaseScript = "scripts/weapons/mp_ability_shifter.txt";
	internal const string PulseScript = "scripts/weapons/mp_weapon_grenade_sonar.txt";

	// Stim (mp_ability_heal.txt).
	public float StimDuration { get; init; }
	public float StimSpeedScale { get; init; }
	public float StimWallrunSpeedScale { get; init; }
	public float StimHealPerSecond { get; init; }
	public float StimFovScale { get; init; }
	public float StimCooldownSeconds { get; init; }

	// Grapple (pilot_base.set grapple_* + mp_ability_grapple.txt).
	public float GrappleMaxRange { get; init; }
	public float GrapplePullAccel { get; init; }
	public float GrappleMaxPullSpeed { get; init; }
	public float GrappleDetachLengthMin { get; init; }
	public float GrappleDetachLengthMax { get; init; }
	public float GrappleDetachAwaySpeed { get; init; }
	public float GrappleGravityFracMin { get; init; }
	public float GrappleGravityFracMax { get; init; }
	public float GrappleDetachVerticalBoost { get; init; }
	public float GrappleDetachVerticalMaxSpeed { get; init; }
	public float GrappleImpactVerticalBoost { get; init; }
	public float GrappleImpactVerticalMaxSpeed { get; init; }
	public float GrappleAirSpeedMax { get; init; }
	public float GrappleAirAccel { get; init; }
	public float GrappleMaxActiveSeconds { get; init; }
	public float GrappleChargeRechargeSeconds { get; init; }
	public float GrappleLowSpeedThreshold { get; init; }
	public float GrappleLowSpeedTime { get; init; }
	public float GrappleLowSpeedGroundTime { get; init; }

	// Phase Shift (mp_ability_shifter.txt).
	public float PhaseDuration { get; init; }
	public float PhaseSpeedScale { get; init; }
	public float PhaseFovScale { get; init; }
	public float PhaseCooldownSeconds { get; init; }

	// Pulse Blade (mp_weapon_grenade_sonar.txt).
	public float PulseThrowSpeed { get; init; }
	public float PulseGravityScale { get; init; }
	public float PulseDirectDamage { get; init; }
	public float PulseTitanDamage { get; init; }
	public float PulseSonarRadius { get; init; }
	public float PulseSonarDuration { get; init; }
	public float PulseIntervalSeconds { get; init; }
	public float PulseCooldownSeconds { get; init; }

	// Verified retail Miles sound event keys.
	public string SoundStim1P { get; init; }
	public string SoundStim3P { get; init; }
	public string SoundPulseThrow1P { get; init; }
	public string SoundPulseThrow3P { get; init; }

	public static Titanfall2PilotTacticalSettings RetailDefault => new()
	{
		StimDuration = 3.0f,
		StimSpeedScale = 1.55f,
		StimWallrunSpeedScale = 1.32f,
		StimHealPerSecond = 35f,
		StimFovScale = 1.14f,
		StimCooldownSeconds = 25f,

		GrappleMaxRange = 1100f,
		GrapplePullAccel = 1280f,
		GrappleMaxPullSpeed = 920f,
		GrappleDetachLengthMin = 33f,
		GrappleDetachLengthMax = 50f,
		GrappleDetachAwaySpeed = 500f,
		GrappleGravityFracMin = 0.25f,
		GrappleGravityFracMax = 0.70f,
		GrappleDetachVerticalBoost = 200f,
		GrappleDetachVerticalMaxSpeed = 200f,
		GrappleImpactVerticalBoost = 300f,
		GrappleImpactVerticalMaxSpeed = 300f,
		GrappleAirSpeedMax = 420f,
		GrappleAirAccel = 650f,
		GrappleMaxActiveSeconds = 4.5f,
		GrappleChargeRechargeSeconds = 12f,
		GrappleLowSpeedThreshold = 250f,
		GrappleLowSpeedTime = 1.5f,
		GrappleLowSpeedGroundTime = 0.7f,

		PhaseDuration = 2.0f,
		PhaseSpeedScale = 1f,
		PhaseFovScale = 1.08f,
		PhaseCooldownSeconds = 15f,

		PulseThrowSpeed = 3500f,
		PulseGravityScale = 2f,
		PulseDirectDamage = 100f,
		PulseTitanDamage = 100f,
		PulseSonarRadius = 1250f,
		PulseSonarDuration = 4.5f,
		PulseIntervalSeconds = 1.3333f,
		PulseCooldownSeconds = 25f,

		SoundStim1P = "pilot_stimpack_activate_1P",
		SoundStim3P = "pilot_stimpack_activate_3P",
		SoundPulseThrow1P = "Pilot_PulseBlade_Throw_1P",
		SoundPulseThrow3P = "Pilot_PulseBlade_Throw_3P"
	};

	internal static Titanfall2PilotTacticalSettings Read( Func<string, string> read )
	{
		var d = RetailDefault;
		if ( read is null ) return d;

		var baseText = read( PilotBaseScript );
		if ( !string.IsNullOrWhiteSpace( baseText ) )
		{
			var setValues = ParseFlatKeyValues( baseText );
			float SetNum( string key, float fallback, float min, float max ) =>
				setValues.TryGetValue( key, out var raw )
				&& float.TryParse( raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed )
				&& float.IsFinite( parsed )
					? Math.Clamp( parsed, min, max )
					: fallback;

			d = d with
			{
				GrappleDetachLengthMin = SetNum( "grapple_detachLengthMin", d.GrappleDetachLengthMin, 16f, 120f ),
				GrappleDetachLengthMax = SetNum( "grapple_detachLengthMax", d.GrappleDetachLengthMax, 24f, 160f ),
				GrappleDetachAwaySpeed = SetNum( "grapple_detachAwaySpeed", d.GrappleDetachAwaySpeed, 150f, 1200f ),
				GrappleGravityFracMin = SetNum( "grapple_gravityFracMin", d.GrappleGravityFracMin, 0.05f, 1f ),
				GrappleGravityFracMax = SetNum( "grapple_gravityFracMax", d.GrappleGravityFracMax, 0.1f, 1f ),
				GrappleDetachVerticalBoost = SetNum( "grapple_detachVerticalBoost", d.GrappleDetachVerticalBoost, 50f, 500f ),
				GrappleDetachVerticalMaxSpeed = SetNum( "grapple_detachVerticalMaxSpeed", d.GrappleDetachVerticalMaxSpeed, 50f, 500f ),
				GrappleImpactVerticalBoost = SetNum( "grapple_impactVerticalBoost", d.GrappleImpactVerticalBoost, 50f, 600f ),
				GrappleImpactVerticalMaxSpeed = SetNum( "grapple_impactVerticalMaxSpeed", d.GrappleImpactVerticalMaxSpeed, 50f, 600f ),
				GrappleAirSpeedMax = SetNum( "grapple_airSpeedMax", d.GrappleAirSpeedMax, 200f, 900f ),
				GrappleAirAccel = SetNum( "grapple_airAccel", d.GrappleAirAccel, 200f, 2000f ),
				GrappleLowSpeedThreshold = SetNum( "grapple_detachLowSpeedThreshold", d.GrappleLowSpeedThreshold, 50f, 500f ),
				GrappleLowSpeedTime = SetNum( "grapple_detachLowSpeedTime", d.GrappleLowSpeedTime, 0.4f, 4f ),
				GrappleLowSpeedGroundTime = SetNum( "grapple_detachLowSpeedGroundTime", d.GrappleLowSpeedGroundTime, 0.2f, 3f )
			};
		}

		var stimText = read( StimScript );
		if ( !string.IsNullOrWhiteSpace( stimText ) )
		{
			var v = ParseWeaponKeyValues( stimText );
			var s1 = v.GetValueOrDefault( "fire_sound_1_player_1p" );
			var s3 = v.GetValueOrDefault( "fire_sound_1_player_3p" );
			if ( !string.IsNullOrWhiteSpace( s1 ) ) d = d with { SoundStim1P = s1 };
			if ( !string.IsNullOrWhiteSpace( s3 ) ) d = d with { SoundStim3P = s3 };
			d = d with
			{
				StimDuration = Number( v, "fire_duration", d.StimDuration, 0.1f, 30f ),
				StimCooldownSeconds = RechargeSeconds( v, d.StimCooldownSeconds )
			};
		}

		var grappleText = read( GrappleScript );
		if ( !string.IsNullOrWhiteSpace( grappleText ) )
			d = d with { GrappleMaxRange = Number( ParseWeaponKeyValues( grappleText ), "grapple_maxLength", d.GrappleMaxRange, 32f, 12000f ) };

		var phaseText = read( PhaseScript );
		if ( !string.IsNullOrWhiteSpace( phaseText ) )
		{
			var v = ParseWeaponKeyValues( phaseText );
			if ( TryNum( v, "fire_duration", out var dur ) && dur is >= 0.8f and <= 5f )
				d = d with { PhaseDuration = dur };
			if ( TryNum( v, "ammo_clip_size", out var clip ) && TryNum( v, "regen_ammo_refill_rate", out var rate ) && rate > 0.1f )
				d = d with { PhaseCooldownSeconds = Math.Clamp( clip / rate, 4f, 40f ) };
		}

		var pulseText = read( PulseScript );
		if ( !string.IsNullOrWhiteSpace( pulseText ) )
		{
			var v = ParseWeaponKeyValues( pulseText );
			var s1 = v.GetValueOrDefault( "sound_throw_1p" );
			var s3 = v.GetValueOrDefault( "sound_throw_3p" );
			if ( !string.IsNullOrWhiteSpace( s1 ) ) d = d with { SoundPulseThrow1P = s1 };
			if ( !string.IsNullOrWhiteSpace( s3 ) ) d = d with { SoundPulseThrow3P = s3 };
			d = d with
			{
				PulseThrowSpeed = Number( v, "projectile_launch_speed", d.PulseThrowSpeed, 1f, 12000f ),
				PulseGravityScale = Number( v, "projectile_gravity_scale", d.PulseGravityScale, 0f, 10f ),
				PulseDirectDamage = Number( v, "damage_near_value", d.PulseDirectDamage, 0f, 10000f ),
				PulseTitanDamage = Number( v, "damage_near_value_titanarmor", d.PulseTitanDamage, 0f, 10000f ),
				PulseSonarDuration = Number( v, "grenade_ignition_time", d.PulseSonarDuration, 0.1f, 60f ),
				PulseCooldownSeconds = RechargeSeconds( v, d.PulseCooldownSeconds )
			};
		}

		return d;
	}

	static float Number( Dictionary<string, string> values, string key, float fallback, float min, float max ) =>
		TryNum( values, key, out var number ) ? Math.Clamp( number, min, max ) : fallback;

	static float RechargeSeconds( Dictionary<string, string> values, float fallback ) =>
		TryNum( values, "ammo_min_to_fire", out var cost ) && TryNum( values, "regen_ammo_refill_rate", out var rate ) && cost > 0 && rate > 0
			? Math.Clamp( cost / rate, 0.1f, 300f ) : fallback;

	static bool TryNum( Dictionary<string, string> values, string key, out float number )
	{
		number = 0f;
		return values.TryGetValue( key, out var raw )
			&& float.TryParse( raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number )
			&& float.IsFinite( number );
	}

	static Dictionary<string, string> ParseFlatKeyValues( string text )
	{
		var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var raw in text.Split( '\n' ) )
		{
			var line = StripComment( raw ).Trim();
			if ( line.Length == 0 || line == "{" || line == "}" ) continue;
			if ( TrySplitPair( line, out var key, out var value ) )
				result[key] = value;
		}
		return result;
	}

	static Dictionary<string, string> ParseWeaponKeyValues( string text )
	{
		var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var depth = 0;
		var mpDepth = -1;
		foreach ( var raw in text.Split( '\n' ) )
		{
			var line = StripComment( raw ).Trim();
			if ( line.Length == 0 ) continue;
			if ( line == "{" ) { depth++; continue; }
			if ( line == "}" )
			{
				if ( depth == mpDepth ) mpDepth = -1;
				depth = Math.Max( 0, depth - 1 );
				continue;
			}
			if ( line.Equals( "MP_BASE", StringComparison.OrdinalIgnoreCase )
				|| line.Equals( "\"MP_BASE\"", StringComparison.OrdinalIgnoreCase ) )
			{
				mpDepth = depth + 1;
				continue;
			}
			if ( (depth == 1 || depth == mpDepth) && TrySplitPair( line, out var key, out var value )
				&& !value.Equals( "<KEEP_DEFAULT>", StringComparison.OrdinalIgnoreCase ) )
			{
				result[key] = value;
			}
		}
		return result;
	}

	static string StripComment( string line )
	{
		var quoted = false;
		for ( var i = 0; i < line.Length; i++ )
		{
			if ( line[i] == '"' ) quoted = !quoted;
			else if ( !quoted && line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/' )
				return line[..i];
		}
		return line;
	}

	static bool TrySplitPair( string line, out string key, out string value )
	{
		key = null;
		value = null;
		var idx = 0;
		key = ReadToken( line, ref idx );
		if ( string.IsNullOrEmpty( key ) ) return false;
		value = ReadToken( line, ref idx );
		return !string.IsNullOrEmpty( value );
	}

	static string ReadToken( string line, ref int idx )
	{
		while ( idx < line.Length && char.IsWhiteSpace( line[idx] ) ) idx++;
		if ( idx >= line.Length ) return null;
		if ( line[idx] == '"' )
		{
			idx++;
			var start = idx;
			while ( idx < line.Length && line[idx] != '"' ) idx++;
			var token = line[start..idx];
			if ( idx < line.Length && line[idx] == '"' ) idx++;
			return token;
		}
		var rawStart = idx;
		while ( idx < line.Length && !char.IsWhiteSpace( line[idx] ) ) idx++;
		return line[rawStart..idx];
	}
}

/// <summary>
/// Pure deterministic physics, cooldown, and presentation rules for Pilot Tactical abilities.
/// Zero engine runtime dependencies so all math is directly unit-testable.
/// </summary>
public static class Titanfall2PilotTacticalRules
{
	public const float ChargeUnitsPerUse = 100f;
	public const float StimSpeedTailSeconds = 0.5f;
	public const float StimSpeedFadeSeconds = 0.25f;

	/// <summary>Retail status timing applied to the s&box movement multiplier, without a hard speed snap at expiry.</summary>
	public static float ComputeStimSpeedScale( float remaining, float multiplier )
	{
		if ( !float.IsFinite( remaining ) || !float.IsFinite( multiplier ) ) return 1f;
		return 1f + (MathF.Max( 1f, multiplier ) - 1f) * Math.Clamp( remaining / StimSpeedFadeSeconds, 0f, 1f );
	}

	public static bool CanEquip( bool hasEquipped, Titanfall2PilotTacticalKind current, Titanfall2PilotTacticalKind incoming ) =>
		incoming is >= Titanfall2PilotTacticalKind.Stim and <= Titanfall2PilotTacticalKind.PulseBlade
		&& (!hasEquipped || current != incoming);

	public static Titanfall2PilotTacticalKind NextKind( Titanfall2PilotTacticalKind current ) => current switch
	{
		Titanfall2PilotTacticalKind.Stim => Titanfall2PilotTacticalKind.Grapple,
		Titanfall2PilotTacticalKind.Grapple => Titanfall2PilotTacticalKind.PhaseShift,
		Titanfall2PilotTacticalKind.PhaseShift => Titanfall2PilotTacticalKind.PulseBlade,
		_ => Titanfall2PilotTacticalKind.Stim
	};

	public static bool TryParseKind( string raw, out Titanfall2PilotTacticalKind kind )
	{
		kind = Titanfall2PilotTacticalKind.Stim;
		if ( string.IsNullOrWhiteSpace( raw ) ) return false;
		var token = raw.Trim().ToLowerInvariant();
		switch ( token )
		{
			case "stim":
			case "heal":
			case "0":
			case "兴奋剂":
				kind = Titanfall2PilotTacticalKind.Stim;
				return true;
			case "grapple":
			case "hook":
			case "1":
			case "钩爪":
				kind = Titanfall2PilotTacticalKind.Grapple;
				return true;
			case "phase":
			case "phaseshift":
			case "shifter":
			case "2":
			case "相位":
			case "相位转移":
				kind = Titanfall2PilotTacticalKind.PhaseShift;
				return true;
			case "pulse":
			case "pulseblade":
			case "sonar":
			case "blade":
			case "3":
			case "脉冲":
			case "脉冲飞刀":
				kind = Titanfall2PilotTacticalKind.PulseBlade;
				return true;
			default:
				return false;
		}
	}

	public static string ShortLabel( Titanfall2PilotTacticalKind kind ) => kind switch
	{
		Titanfall2PilotTacticalKind.Stim => "STIM 兴奋剂",
		Titanfall2PilotTacticalKind.Grapple => "GRAPPLE 钩爪",
		Titanfall2PilotTacticalKind.PhaseShift => "PHASE 相位",
		Titanfall2PilotTacticalKind.PulseBlade => "PULSE 脉冲刀",
		_ => "TACTICAL"
	};

	public static int MaxCharges( Titanfall2PilotTacticalKind kind ) =>
		kind == Titanfall2PilotTacticalKind.Grapple ? 2 : 1;

	public static float MaxChargeUnits( Titanfall2PilotTacticalKind kind ) =>
		MaxCharges( kind ) * ChargeUnitsPerUse;

	public static float RechargeUnitsPerSecond( Titanfall2PilotTacticalKind kind, in Titanfall2PilotTacticalSettings settings )
	{
		var seconds = kind switch
		{
			Titanfall2PilotTacticalKind.Stim => settings.StimCooldownSeconds,
			Titanfall2PilotTacticalKind.Grapple => settings.GrappleChargeRechargeSeconds,
			Titanfall2PilotTacticalKind.PhaseShift => settings.PhaseCooldownSeconds,
			Titanfall2PilotTacticalKind.PulseBlade => settings.PulseCooldownSeconds,
			_ => 15f
		};
		return ChargeUnitsPerUse / MathF.Max( 1f, seconds );
	}

	/// <summary>
	/// s&box charge-meter adapter for early grapple detachment:
	/// short taps keep most of the charge; full swings consume the whole 100 units.
	/// </summary>
	public static float ComputeGrappleEarlyDetachRefund( float activeSeconds, float maxActiveSeconds )
	{
		if ( !float.IsFinite( activeSeconds ) || activeSeconds < 0f ) return 0f;
		var fullDrainTime = MathF.Max( 0.8f, maxActiveSeconds * 0.45f );
		var usedFrac = Math.Clamp( activeSeconds / fullDrainTime, 0.25f, 1f );
		return (1f - usedFrac) * ChargeUnitsPerUse;
	}

	/// <summary>
	/// Integrates Grapple swing and pull in the s&box movement motor:
	/// radial reel-in toward the anchor + directional air-strafe slingshot + reduced gravity.
	/// </summary>
	public static Vector3 ComputeGrappleVelocity(
		Vector3 position,
		Vector3 velocity,
		Vector3 anchor,
		Vector3 wishDir,
		Vector3 eyeForward,
		float elapsed,
		in Titanfall2PilotTacticalSettings settings,
		float gravity,
		float dt )
	{
		var toAnchor = anchor - position;
		var dist = toAnchor.Length;
		if ( dist < 1f ) return velocity;

		var dirToAnchor = toAnchor / dist;

		// Radial pull toward anchor, ramping gently during the first 0.2s for smooth slingshot arcs.
		var ramp = Math.Clamp( elapsed / 0.22f, 0.45f, 1f );
		var pullAccel = settings.GrapplePullAccel * ramp;
		var next = velocity + dirToAnchor * (pullAccel * dt);

		// Add directional swing thrust perpendicular to the rope when looking or strafing sideways (TF2 slingshot).
		var steer = wishDir.LengthSquared > 0.01f ? wishDir.Normal : eyeForward.WithZ( 0 ).Normal;
		if ( steer.LengthSquared > 0.01f )
		{
			var tangential = steer - dirToAnchor * Vector3.Dot( steer, dirToAnchor );
			if ( tangential.LengthSquared > 0.01f )
			{
				tangential = tangential.Normal;
				var alongTangential = Vector3.Dot( next, tangential );
				var add = Math.Clamp( settings.GrappleAirSpeedMax - alongTangential, 0f, settings.GrappleAirAccel * dt );
				next += tangential * add;
			}
		}

		// Prevent outward rope stretch once moving away past a mild slack tolerance.
		var radialSpeed = Vector3.Dot( next, dirToAnchor );
		if ( radialSpeed < -180f )
			next -= dirToAnchor * (radialSpeed + 180f);

		// Reduced gravity while suspended on the grapple cable.
		var gravFrac = MathX.Lerp( settings.GrappleGravityFracMin, settings.GrappleGravityFracMax, Math.Clamp( elapsed / 2.0f, 0f, 1f ) );
		next.z -= gravity * gravFrac * dt;

		// Soft speed cap preserving extreme slingshot momentum up to MaxPullSpeed.
		var speed = next.Length;
		var cap = MathF.Max( settings.GrappleMaxPullSpeed, velocity.Length );
		if ( speed > cap )
			next = next * (cap / speed);

		return next;
	}

	/// <summary>Evaluates whether an active grapple cable should automatically detach.</summary>
	public static bool ShouldDetachGrapple(
		Vector3 position,
		Vector3 velocity,
		Vector3 anchor,
		Vector3 eyeForward,
		float elapsed,
		float lowSpeedDuration,
		bool grounded,
		in Titanfall2PilotTacticalSettings settings )
	{
		if ( elapsed >= settings.GrappleMaxActiveSeconds ) return true;

		var toAnchor = anchor - position;
		var dist = toAnchor.Length;
		if ( dist <= settings.GrappleDetachLengthMin ) return true;

		if ( elapsed >= 0.35f )
		{
			var dirToAnchor = toAnchor / MathF.Max( 1f, dist );
			var lookDot = eyeForward.LengthSquared > 0.01f ? Vector3.Dot( eyeForward.Normal, dirToAnchor ) : 1f;
			var awaySpeed = -Vector3.Dot( velocity, dirToAnchor );
			if ( lookDot < -0.35f && awaySpeed > settings.GrappleDetachAwaySpeed * 0.65f )
				return true;
			if ( dist <= settings.GrappleDetachLengthMax && lookDot < 0f )
				return true;
		}

		var lowLimit = grounded ? settings.GrappleLowSpeedGroundTime : settings.GrappleLowSpeedTime;
		if ( elapsed >= 0.45f && lowSpeedDuration >= lowLimit )
			return true;

		return false;
	}

	/// <summary>Applies retail grapple detach vertical pop while preserving slingshot horizontal speed.</summary>
	public static Vector3 ComputeGrappleDetachVelocity( Vector3 velocity, bool impactJump, in Titanfall2PilotTacticalSettings settings )
	{
		var boost = impactJump ? settings.GrappleImpactVerticalBoost : settings.GrappleDetachVerticalBoost;
		var cap = impactJump ? settings.GrappleImpactVerticalMaxSpeed : settings.GrappleDetachVerticalMaxSpeed;
		var nextZ = velocity.z < cap ? MathF.Min( cap, velocity.z + boost ) : velocity.z;
		return velocity.WithZ( nextZ );
	}

	/// <summary>Computes tactical FOV multiplier suppressed during ADS so sights stay crisp.</summary>
	public static float ComputeTacticalFovScale(
		bool stimActive,
		bool phaseActive,
		bool grappleActive,
		float adsBlend,
		in Titanfall2PilotTacticalSettings settings )
	{
		var scale = 1f;
		if ( stimActive ) scale = MathF.Max( scale, settings.StimFovScale );
		if ( phaseActive ) scale = MathF.Max( scale, settings.PhaseFovScale );
		if ( grappleActive ) scale = MathF.Max( scale, 1.06f );
		var suppress = 1f - Math.Clamp( adsBlend, 0f, 1f ) * 0.85f;
		return 1f + (scale - 1f) * suppress;
	}
}
