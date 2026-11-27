using System.Globalization;

/// <summary>Contextual Titanfall 2 Pilot melee strike variant driven by movement state.</summary>
public enum Titanfall2PilotMeleeStrikeKind
{
	None = 0,
	RightHook = 1,
	LeftHook = 2,
	AirKick = 3,
	SlideKick = 4,
	WallKick = 5
}

/// <summary>
/// Retail Pilot melee parameters parsed from scripts/weapons/melee_pilot_emptyhanded.txt
/// paired with verified general.mbnk Miles sound events.
/// </summary>
public readonly record struct Titanfall2PilotMeleeSettings
{
	internal const string RetailScript = "scripts/weapons/melee_pilot_emptyhanded.txt";

	public float Damage { get; init; }
	public float TitanDamage { get; init; }
	public float Range { get; init; }
	public float LungeRange { get; init; }
	public float LungeAngleDegrees { get; init; }
	public float LungeStopDistance { get; init; }
	public float LungeMaxSpeed { get; init; }
	public float LungeDuration { get; init; }
	public float ImpactDelay { get; init; }
	public float AttackDuration { get; init; }
	public float Impulse { get; init; }

	public string SoundRightHook1P { get; init; }
	public string SoundLeftHook1P { get; init; }
	public string SoundAirKick1P { get; init; }
	public string SoundSlideKick1P { get; init; }
	public string SoundWallKick1P { get; init; }
	public string SoundSwing3P { get; init; }
	public string SoundImpactFlesh1P { get; init; }
	public string SoundImpactFlesh3P { get; init; }
	public string SoundImpactAndroid1P { get; init; }
	public string SoundImpactAndroid3P { get; init; }
	public string SoundImpactMetal1P { get; init; }
	public string SoundImpactConcrete1P { get; init; }
	public string SoundImpactDefault1P { get; init; }
	public string SoundImpactDefault3P { get; init; }

	public float LungeMinCos => MathF.Cos( Math.Clamp( LungeAngleDegrees, 5f, 85f ) * (MathF.PI / 180f) );

	public static Titanfall2PilotMeleeSettings RetailDefault => new()
	{
		Damage = 100f,
		TitanDamage = 0f,
		Range = 68f,
		LungeRange = 132f,
		LungeAngleDegrees = 45f,
		LungeStopDistance = 46f,
		LungeMaxSpeed = 620f,
		LungeDuration = 0.16f,
		ImpactDelay = 0.15f,
		AttackDuration = 0.78f,
		Impulse = 480f,
		SoundRightHook1P = "Pilot_Mvmt_Melee_RightHook_1P",
		SoundLeftHook1P = "Pilot_Mvmt_Melee_LeftHook_1P",
		SoundAirKick1P = "Pilot_Mvmt_Melee_AirKick_1P",
		SoundSlideKick1P = "Pilot_Mvmt_Melee_SlideKick_1P",
		SoundWallKick1P = "Pilot_Mvmt_Melee_WallKick_1P",
		SoundSwing3P = "player_melee_kick_3p",
		SoundImpactFlesh1P = "player_melee_impact_flesh_1p_vs_3p",
		SoundImpactFlesh3P = "player_melee_impact_flesh_3p_vs_3p",
		SoundImpactAndroid1P = "pilot_melee_kick_impact_android_1p_vs_3p",
		SoundImpactAndroid3P = "pilot_melee_kick_impact_android_3p_vs_3p",
		SoundImpactMetal1P = "Player_Melee_Impact_Metal_1p_vs_3p",
		SoundImpactConcrete1P = "Player_Melee_Impact_Concrete_1p_vs_3p",
		SoundImpactDefault1P = "Player_Melee_Impact_Default_1p_vs_3p",
		SoundImpactDefault3P = "Player_Melee_Impact_Default_3p_vs_3p"
	};

	internal static Titanfall2PilotMeleeSettings Read( Func<string, string> read )
	{
		var d = RetailDefault;
		if ( read is null ) return d;

		var values = ParseWeaponValues( RetailScript, read, new HashSet<string>( StringComparer.OrdinalIgnoreCase ) );
		if ( values.Count == 0 ) return d;

		float Num( string key, float fallback, float min, float max ) =>
			values.TryGetValue( key, out var raw )
			&& float.TryParse( raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed )
			&& float.IsFinite( parsed )
				? Math.Clamp( parsed, min, max )
				: fallback;

		var damage = Num( "damage_near_value", d.Damage, 25f, 500f );
		var titanDamage = Num( "damage_near_value_titanarmor", d.TitanDamage, 0f, 1000f );
		var range = MathF.Max( Num( "melee_range", d.Range, 40f, 120f ), 60f );
		var lungeRange = MathF.Max( Num( "melee_lunge_target_range", d.LungeRange, range, 200f ), range + 20f );
		var lungeAngle = Num( "melee_lunge_target_angle", d.LungeAngleDegrees, 15f, 75f );
		var attackTime = Num( "melee_attack_animtime", d.AttackDuration, 0.4f, 1.5f );
		var impulse = Num( "melee_impulse", d.Impulse, 150f, 1200f );

		return d with
		{
			Damage = damage,
			TitanDamage = titanDamage,
			Range = range,
			LungeRange = lungeRange,
			LungeAngleDegrees = lungeAngle,
			AttackDuration = attackTime,
			Impulse = impulse
		};
	}

	static Dictionary<string, string> ParseWeaponValues( string path, Func<string, string> read, HashSet<string> visited )
	{
		var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		if ( !visited.Add( path ) ) return result;

		var text = read( path );
		if ( string.IsNullOrWhiteSpace( text ) ) return result;

		var mpBlock = false;
		var braceDepth = 0;
		var mpDepth = -1;
		foreach ( var rawLine in text.Split( '\n' ) )
		{
			var line = StripLineComment( rawLine ).Trim();
			if ( line.Length == 0 ) continue;

			if ( line.StartsWith( "#base", StringComparison.OrdinalIgnoreCase ) )
			{
				var quoted = ReadQuotedToken( line, 5 );
				if ( !string.IsNullOrWhiteSpace( quoted ) )
				{
					var basePath = quoted.Contains( '/' ) ? quoted : Titanfall2WeaponData.Folder + quoted;
					foreach ( var kv in ParseWeaponValues( basePath, read, visited ) )
						result[kv.Key] = kv.Value;
				}
				continue;
			}

			if ( line == "{" ) { braceDepth++; continue; }
			if ( line == "}" )
			{
				if ( braceDepth == mpDepth ) { mpBlock = false; mpDepth = -1; }
				braceDepth = Math.Max( 0, braceDepth - 1 );
				continue;
			}

			if ( line.Equals( "MP_BASE", StringComparison.OrdinalIgnoreCase ) || line.Equals( "\"MP_BASE\"", StringComparison.OrdinalIgnoreCase ) )
			{
				mpBlock = true;
				mpDepth = braceDepth + 1;
				continue;
			}

			if ( braceDepth == 1 || (mpBlock && braceDepth == mpDepth) )
			{
				if ( TrySplitKeyValue( line, out var key, out var value )
					&& !value.Equals( "<KEEP_DEFAULT>", StringComparison.OrdinalIgnoreCase ) )
				{
					result[key] = value;
				}
			}
		}

		return result;
	}

	static string StripLineComment( string line )
	{
		var inQuotes = false;
		for ( var i = 0; i < line.Length; i++ )
		{
			if ( line[i] == '"' ) inQuotes = !inQuotes;
			else if ( !inQuotes && line[i] == '/' && i + 1 < line.Length && line[i + 1] == '/' )
				return line[..i];
		}
		return line;
	}

	static string ReadQuotedToken( string line, int start )
	{
		var open = line.IndexOf( '"', start );
		if ( open < 0 ) return null;
		var close = line.IndexOf( '"', open + 1 );
		return close > open ? line[(open + 1)..close] : null;
	}

	static bool TrySplitKeyValue( string line, out string key, out string value )
	{
		key = value = null;
		var pos = 0;
		key = ReadToken( line, ref pos );
		value = ReadToken( line, ref pos );
		return !string.IsNullOrEmpty( key ) && value is not null;
	}

	static string ReadToken( string line, ref int pos )
	{
		while ( pos < line.Length && char.IsWhiteSpace( line[pos] ) ) pos++;
		if ( pos >= line.Length ) return null;
		if ( line[pos] == '"' )
		{
			var start = ++pos;
			while ( pos < line.Length && line[pos] != '"' ) pos++;
			var token = line[start..pos];
			if ( pos < line.Length ) pos++;
			return token;
		}
		var wordStart = pos;
		while ( pos < line.Length && !char.IsWhiteSpace( line[pos] ) ) pos++;
		return line[wordStart..pos];
	}
}

/// <summary>Self-contained definition for the Pilot's offhand [G] ordnance slot.</summary>
public readonly record struct Titanfall2OffhandOrdnanceConfig
{
	public string Id { get; init; }
	public string DisplayName { get; init; }
	public string ShortLabel { get; init; }
	public Titanfall2OrdnanceProfile Profile { get; init; }
	public Titanfall2ProjectileSettings Projectile { get; init; }
	public Titanfall2DamageProfile DamageProfile { get; init; }
	public float Damage { get; init; }
	public float TitanDamage { get; init; }
	public int ClipMaxSize { get; init; }
	public int AmmoCost { get; init; }
	public float PrimaryDelay { get; init; }

	public bool Valid => Profile.Valid && Projectile.Enabled && ClipMaxSize > 0 && AmmoCost > 0;
	public int MaxCharges => AmmoCost > 0 ? Math.Max( 1, ClipMaxSize / AmmoCost ) : 1;

	/// <summary>Verified retail Frag Grenade fallback when VPK ordnance scripts are unavailable.</summary>
	public static Titanfall2OffhandOrdnanceConfig RetailFragDefault => new()
	{
		Id = "mp_weapon_frag_grenade",
		DisplayName = "Frag Grenade",
		ShortLabel = "FRAG",
		Profile = new()
		{
			Enabled = true,
			HasModelTiming = true,
			RefillRate = 10f,
			RefillDelay = 0.5f,
			FireDuration = 0.45f,
			CooldownSeconds = 0f,
			PulloutSeconds = 0.16f,
			ThrowSeconds = 0.45f,
			ReleaseCycle = 0.25f,
			ReadyCycle = 0.85f,
			MaxCookSeconds = 3.5f,
			FuseSeconds = 3.75f,
			PitchOffset = 8f,
			InheritVelocity = 0.5f,
			DropOnCancel = true,
			PrepSound1P = "Weapon_Frag_PinPull_1P",
			PrepSound3P = "Weapon_Frag_PinPull_3P",
			ThrowSound1P = "Weapon_Frag_Throw_1P",
			ThrowSound3P = "Weapon_Frag_Throw_3P"
		},
		Projectile = new()
		{
			Speed = 1150f,
			GravityScale = 1.1f,
			Radius = 3f,
			Lifetime = 4f,
			SplashDamage = 150f,
			SplashTitanDamage = 400f,
			SplashRadius = 260f,
			DamageOwner = true,
			FuseSeconds = 3.75f,
			ImpactTable = "frag_grenade",
			MountIdent = "titanfall2",
			Tint = new Color( 1f, 0.6f, 0.2f ),
			Behavior = Titanfall2ProjectileBehavior.FragGrenade
		},
		Damage = 150f,
		TitanDamage = 400f,
		ClipMaxSize = 200,
		AmmoCost = 100,
		PrimaryDelay = 0.65f
	};

	internal static Titanfall2OffhandOrdnanceConfig FromWeaponData( string id, string title, Titanfall2WeaponData data, string mountIdent )
	{
		if ( data is null || !data.OrdnanceProfile.Valid || !data.Projectile.Enabled )
			return RetailFragDefault;

		var modelPath = string.IsNullOrWhiteSpace( data.Projectile.ModelPath )
			? null
			: $"mount://{mountIdent ?? "titanfall2"}/{data.Projectile.ModelPath}.vmdl";
		return new()
		{
			Id = id ?? data.ScriptName ?? "mp_weapon_frag_grenade",
			DisplayName = string.IsNullOrWhiteSpace( title ) ? "Ordnance" : title,
			ShortLabel = ShortLabelFor( title, data.Projectile.Behavior ),
			Profile = data.OrdnanceProfile,
			Projectile = data.Projectile with { ModelPath = modelPath, MountIdent = mountIdent ?? "titanfall2" },
			DamageProfile = data.DamageProfile,
			Damage = data.Damage,
			TitanDamage = data.TitanDamage,
			ClipMaxSize = Math.Max( 1, data.Magazine ),
			AmmoCost = Math.Max( 1, data.Firing.Cost ),
			PrimaryDelay = data.ShotsPerSecond > 0.05f ? 1f / data.ShotsPerSecond : 0.65f
		};
	}

	internal static Titanfall2OffhandOrdnanceConfig FromWeapon( Titanfall2Weapon weapon )
	{
		if ( !weapon.IsValid() || !weapon.OrdnanceProfile.Valid || !weapon.Projectile.Enabled )
			return RetailFragDefault;

		var title = string.IsNullOrWhiteSpace( weapon.DisplayName ) ? weapon.GameObject.Name : weapon.DisplayName;
		return new()
		{
			Id = weapon.GameObject.PrefabInstanceSource ?? title ?? "ordnance",
			DisplayName = title,
			ShortLabel = ShortLabelFor( title, weapon.Projectile.Behavior ),
			Profile = weapon.OrdnanceProfile,
			Projectile = weapon.Projectile,
			DamageProfile = weapon.DamageProfile,
			Damage = weapon.Ballistics.Damage,
			TitanDamage = weapon.TitanDamage,
			ClipMaxSize = Math.Max( 1, weapon.ClipMaxSize ),
			AmmoCost = Math.Max( 1, weapon.Firing.Cost ),
			PrimaryDelay = MathF.Max( 0.2f, weapon.PrimaryDelay )
		};
	}

	internal static string ShortLabelFor( string title, Titanfall2ProjectileBehavior behavior ) => behavior switch
	{
		Titanfall2ProjectileBehavior.FragGrenade => "FRAG",
		Titanfall2ProjectileBehavior.ArcGrenade => "ARC",
		Titanfall2ProjectileBehavior.ThermiteBurn => "FIRESTAR",
		Titanfall2ProjectileBehavior.GravityPull => "GRAV STAR",
		Titanfall2ProjectileBehavior.ElectricSmoke => "E-SMOKE",
		Titanfall2ProjectileBehavior.RemoteCharge => "SATCHEL",
		_ => string.IsNullOrWhiteSpace( title ) ? "ORDNANCE" : title.ToUpperInvariant()
	};
}

/// <summary>Pure deterministic rules for Pilot Combat State input yielding, melee lunge/hit, and offhand timing.</summary>
internal static class Titanfall2PilotCombatRules
{
	/// <summary>Matches Sandbox builder/camera tools so Pilot Combat yields F/G automatically when building.</summary>
	internal static bool IsSandboxUtilityToolTypeName( string typeName )
	{
		if ( string.IsNullOrWhiteSpace( typeName ) ) return false;
		return typeName.Contains( "Physgun", StringComparison.OrdinalIgnoreCase )
			|| typeName.Contains( "Toolgun", StringComparison.OrdinalIgnoreCase )
			|| typeName.Contains( "CameraWeapon", StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>Determines whether Pilot Combat should yield F/G back to Sandbox.</summary>
	internal static bool ShouldYieldCombatInput( bool equipped, bool pilotMoveModeActive, bool useInputControls,
		bool uiCapturingInput, string activeWeaponTypeName )
	{
		if ( !equipped || !pilotMoveModeActive || !useInputControls || uiCapturingInput )
			return true;
		return IsSandboxUtilityToolTypeName( activeWeaponTypeName );
	}

	/// <summary>Selects the contextual Pilot melee strike variant from parkour state.</summary>
	internal static Titanfall2PilotMeleeStrikeKind SelectMeleeStrikeKind( bool isWallrunning, bool isSliding, bool isAirborne, int swingIndex )
	{
		if ( isWallrunning ) return Titanfall2PilotMeleeStrikeKind.WallKick;
		if ( isSliding ) return Titanfall2PilotMeleeStrikeKind.SlideKick;
		if ( isAirborne ) return Titanfall2PilotMeleeStrikeKind.AirKick;
		return (swingIndex & 1) == 0 ? Titanfall2PilotMeleeStrikeKind.RightHook : Titanfall2PilotMeleeStrikeKind.LeftHook;
	}

	/// <summary>
	/// Scores a potential melee lunge target within range and forward cone.
	/// Returns <see cref="float.NegativeInfinity"/> if out of range or outside the cone.
	/// </summary>
	internal static float ScoreMeleeTarget( Vector3 eyePos, Vector3 eyeForward, Vector3 targetPos, float maxRange, float minCos )
	{
		var delta = targetPos - eyePos;
		var dist = delta.Length;
		if ( dist <= 0.001f || dist > maxRange ) return float.NegativeInfinity;

		var dir = delta / dist;
		var fwd = eyeForward.LengthSquared > 0.001f ? eyeForward.Normal : Vector3.Forward;
		var cos = Vector3.Dot( fwd, dir );
		if ( cos < minCos ) return float.NegativeInfinity;

		// Prefer centered targets first, then closer targets.
		var distFrac = 1f - dist / MathF.Max( maxRange, 1f );
		return cos * 2.2f + distFrac;
	}

	/// <summary>Computes crisp horizontal+vertical lunge velocity toward a locked melee target.</summary>
	internal static Vector3 ComputeMeleeLungeVelocity( Vector3 pilotPos, Vector3 targetPos, Vector3 currentVelocity,
		in Titanfall2PilotMeleeSettings settings )
	{
		var toTarget = targetPos - pilotPos;
		var flat = toTarget.WithZ( 0 );
		var flatDist = flat.Length;
		if ( flatDist <= settings.LungeStopDistance )
			return currentVelocity;

		var travelDist = MathF.Max( 0f, flatDist - settings.LungeStopDistance );
		var duration = MathF.Max( 0.08f, settings.LungeDuration );
		var desiredSpeed = Math.Clamp( travelDist / duration, 220f, settings.LungeMaxSpeed );
		var currentSpeed = currentVelocity.WithZ( 0 ).Length;
		var speed = MathF.Max( desiredSpeed, MathF.Min( currentSpeed, settings.LungeMaxSpeed ) );

		var dir = flat / flatDist;
		var dz = Math.Clamp( (toTarget.z + 8f) / duration, -120f, 240f );
		var nextZ = MathF.Max( currentVelocity.z * 0.4f, dz );
		return (dir * speed).WithZ( nextZ );
	}

	/// <summary>Procedural camera punch (pitch, yaw, roll degrees) during a Pilot melee strike.</summary>
	internal static Angles ComputeMeleeViewKick( Titanfall2PilotMeleeStrikeKind kind, float progress, bool hitConfirmed )
	{
		var t = Math.Clamp( progress, 0f, 1f );
		if ( kind == Titanfall2PilotMeleeStrikeKind.None || t <= 0f || t >= 1f )
			return Angles.Zero;

		// Fast snap into impact around t=0.25, smooth decay across recovery.
		var envelope = t < 0.25f
			? MathF.Sin( (t / 0.25f) * (MathF.PI * 0.5f) )
			: MathF.Pow( 1f - (t - 0.25f) / 0.75f, 1.8f );

		var crunch = hitConfirmed && t is >= 0.15f and <= 0.48f
			? MathF.Sin( ((t - 0.15f) / 0.33f) * MathF.PI )
			: 0f;

		return kind switch
		{
			Titanfall2PilotMeleeStrikeKind.LeftHook => new Angles( -3.5f * envelope + 3.2f * crunch, -6.5f * envelope, 5.5f * envelope + 3f * crunch ),
			Titanfall2PilotMeleeStrikeKind.AirKick => new Angles( -6.5f * envelope + 4.5f * crunch, 4.0f * envelope, -8.5f * envelope - 3.5f * crunch ),
			Titanfall2PilotMeleeStrikeKind.SlideKick => new Angles( -5.0f * envelope + 4.0f * crunch, 5.0f * envelope, -10.5f * envelope - 3f * crunch ),
			Titanfall2PilotMeleeStrikeKind.WallKick => new Angles( -6.0f * envelope + 4.5f * crunch, 5.5f * envelope, -9.5f * envelope - 3.5f * crunch ),
			_ => new Angles( -3.5f * envelope + 3.2f * crunch, 6.5f * envelope, -5.5f * envelope - 3f * crunch )
		};
	}

	/// <summary>Procedural first-person weapon tuck/dip factor (0..1) while cooking/throwing offhand ordnance.</summary>
	internal static float ComputeOffhandDipFactor( bool preparing, bool throwing, float throwProgress )
	{
		if ( preparing ) return 1f;
		if ( !throwing ) return 0f;
		var t = Math.Clamp( throwProgress, 0f, 1f );
		if ( t < 0.45f ) return 1f;
		var recovery = (t - 0.45f) / 0.55f;
		return 1f - recovery * recovery * (3f - 2f * recovery);
	}

	/// <summary>Procedural first-person weapon tuck/swing offset & angles during a melee strike.</summary>
	internal static (Vector3 LocalOffset, Angles LocalAngles) ComputeMeleeViewModelPose( Titanfall2PilotMeleeStrikeKind kind, float progress )
	{
		var t = Math.Clamp( progress, 0f, 1f );
		if ( kind == Titanfall2PilotMeleeStrikeKind.None || t <= 0f || t >= 1f )
			return (Vector3.Zero, Angles.Zero);

		var arc = t < 0.3f
			? MathF.Sin( (t / 0.3f) * (MathF.PI * 0.5f) )
			: MathF.Pow( 1f - (t - 0.3f) / 0.7f, 1.6f );

		var side = kind == Titanfall2PilotMeleeStrikeKind.LeftHook ? -1f : 1f;
		var isKick = kind is Titanfall2PilotMeleeStrikeKind.AirKick or Titanfall2PilotMeleeStrikeKind.SlideKick or Titanfall2PilotMeleeStrikeKind.WallKick;
		var offset = isKick
			? new Vector3( -3.5f, 3.2f, -8.5f ) * arc
			: new Vector3( -2.2f, 4.0f * side, -7.2f ) * arc;
		var angles = isKick
			? new Angles( 32f * arc, -14f * arc, -24f * arc )
			: new Angles( 24f * arc, -18f * side * arc, -20f * side * arc );
		return (offset, angles);
	}

	/// <summary>Normalized 0..1 cook progress for HUD display.</summary>
	internal static float ComputeCookFraction( double preparedAt, double now, float maxCookSeconds )
	{
		if ( maxCookSeconds <= 0.01f ) return 0f;
		return Math.Clamp( (float)((now - preparedAt) / maxCookSeconds), 0f, 1f );
	}

	/// <summary>Normalized 0..1 ordnance energy bar fill.</summary>
	internal static float ComputeChargeFillFraction( int units, int maxUnits ) =>
		maxUnits > 0 ? Math.Clamp( (float)units / maxUnits, 0f, 1f ) : 0f;
}
