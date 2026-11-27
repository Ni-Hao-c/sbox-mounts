using System.Globalization;

/// <summary>
/// Retail multiplayer Pilot movement parameters parsed from scripts/players/mp/pilot_mp.set
/// (#base pilot_base.set) combined with verified client.dll ConVar defaults.
/// </summary>
public readonly record struct Titanfall2PilotSettings
{
	internal const string BaseScript = "scripts/players/mp/pilot_base.set";
	internal const string MultiplayerScript = "scripts/players/mp/pilot_mp.set";
	internal const string MediumScript = "scripts/players/mp/pilot_medium.set";

	// Retail sv_gravity = 750, pilot_mp gravityscale = 0.80 -> 600 u/s^2.
	public float WorldGravity { get; init; }
	public float GravityScale { get; init; }
	public float Gravity => WorldGravity * GravityScale;

	// Stand / Sprint / Crouch speeds & hull geometry (pilot_mp.set / pilot_base.set).
	public float WalkSpeed { get; init; }
	public float SprintSpeed { get; init; }
	public float CrouchSpeed { get; init; }
	public float StandHeight { get; init; }
	public float StandViewHeight { get; init; }
	public float CrouchHeight { get; init; }
	public float CrouchViewHeight { get; init; }
	public float StepHeight { get; init; }

	// Jump, coyote grace, air strafing & double jump.
	public float JumpHeight { get; init; }
	public float JumpGracePeriod { get; init; }
	public float AirSpeed { get; init; }
	public float AirAcceleration { get; init; }
	public float DoubleJumpHeight { get; init; }
	public float DoubleJumpHorzSpeed { get; init; }

	// Wallrun & wallkick (pilot_mp.set + client.dll wallrun_* ConVars).
	public float WallrunTimeLimit { get; init; }
	public float WallrunMaxSpeedHorizontal { get; init; }
	public float WallrunAccelerateHorizontal { get; init; }
	public float WallrunMaxSpeedVertical { get; init; }
	public float WallrunAccelerateVertical { get; init; }
	public float WallrunUpWallBoost { get; init; }
	public float WallrunGravityRampUpTime { get; init; }
	public float WallrunSlipStartTime { get; init; }
	public float WallrunSlipDuration { get; init; }
	public float WallrunSlipVelocity { get; init; }
	public float WallrunNoInputSlipFrac { get; init; }
	public float WallrunUpwardAutoPush { get; init; }
	public float WallrunAllowedWallDist { get; init; }
	public float WallrunAngleChangeMinCos { get; init; }
	public float WallrunFallAwaySpeed { get; init; }
	public float WallrunSameWallDist { get; init; }
	public float WallrunSameWallDot { get; init; }
	public float WallrunSameWallHeight { get; init; }
	public float WallrunPushAwayFallOffTime { get; init; }
	public float WallrunJumpOutwardSpeed { get; init; }
	public float WallrunJumpUpSpeed { get; init; }
	public float WallrunJumpInputDirSpeed { get; init; }
	public float WallrunRepelTime { get; init; }
	public float WallrunMaxViewTilt { get; init; }
	public float WallrunViewTiltSpeed { get; init; }

	// Slide & slide-hop (pilot_base.set / pilot_mp.set + client.dll slide* defaults).
	public float SlideRequiredStartSpeed { get; init; }
	public float SlideMaxAngleDot { get; init; }
	public float SlideBoostCooldown { get; init; }
	public float SlideSpeedBoost { get; init; }
	public float SlideSpeedBoostCap { get; init; }
	public float SlideAccel { get; init; }
	public float SlideDecel { get; init; }
	public float SlideWantToStopDecel { get; init; }
	public float SlideVelocityDecay { get; init; }
	public float SlideStopSpeed { get; init; }
	public float SlideMaxStopSpeed { get; init; }
	public float SlideJumpHeight { get; init; }
	public float SlideFovScale { get; init; }
	public float SlideFovLerpInTime { get; init; }
	public float SlideFovLerpOutTime { get; init; }
	public float SlideViewTiltSide { get; init; }
	public float SlideViewTiltPlayerSpeed { get; init; }
	public float SlideViewTiltIncreaseSpeed { get; init; }
	public float SlideViewTiltDecreaseSpeed { get; init; }

	// Mantle / traversal (client.dll traversal_* defaults).
	public float TraversalCooldown { get; init; }
	public float TraversalViewLerpInDuration { get; init; }

	// Retail Miles sound event keys from pilot_base.set / pilot_medium.set.
	public string SoundJumpJetStart1P { get; init; }
	public string SoundJumpJetStart3P { get; init; }
	public string SoundWallrunStart1P { get; init; }
	public string SoundWallrunStart3P { get; init; }
	public string SoundWallrunEnd1P { get; init; }
	public string SoundWallrunEnd3P { get; init; }
	public string SoundWallrunImpact { get; init; }
	public string SoundWallrunFootstep { get; init; }
	public string SoundSlideBoost1P { get; init; }
	public string SoundSlideBoost3P { get; init; }
	public string SoundSlideStart1P { get; init; }
	public string SoundSlideStop1P { get; init; }
	public string SoundGroundImpact { get; init; }
	public string SoundLandDefault { get; init; }
	public string SoundMantleLow { get; init; }
	public string SoundMantleMedium { get; init; }
	public string SoundMantle3P { get; init; }

	/// <summary>Verified retail multiplayer defaults (pilot_mp.set + pilot_base.set + client.dll).</summary>
	public static Titanfall2PilotSettings RetailDefault => new()
	{
		WorldGravity = 750f,
		GravityScale = 0.80f,
		WalkSpeed = 173.5f,
		SprintSpeed = 260f,
		CrouchSpeed = 80f,
		StandHeight = 72f,
		StandViewHeight = 60f,
		CrouchHeight = 47f,
		CrouchViewHeight = 32f,
		StepHeight = 18f,
		JumpHeight = 60f,
		JumpGracePeriod = 0.2f,
		AirSpeed = 60f,
		AirAcceleration = 500f,
		DoubleJumpHeight = 60f,
		DoubleJumpHorzSpeed = 180f,
		WallrunTimeLimit = 1.75f,
		WallrunMaxSpeedHorizontal = 420f,
		WallrunAccelerateHorizontal = 1500f,
		WallrunMaxSpeedVertical = 225f,
		WallrunAccelerateVertical = 360f,
		WallrunUpWallBoost = 250f,
		WallrunGravityRampUpTime = 1.0f,
		WallrunSlipStartTime = 1.5f,
		WallrunSlipDuration = 1.0f,
		WallrunSlipVelocity = 70f,
		WallrunNoInputSlipFrac = 0.7f,
		WallrunUpwardAutoPush = 0.65f,
		WallrunAllowedWallDist = 13f,
		WallrunAngleChangeMinCos = 0.8f,
		WallrunFallAwaySpeed = 70f,
		WallrunSameWallDist = 100f,
		WallrunSameWallDot = 0.9f,
		WallrunSameWallHeight = 0f,
		WallrunPushAwayFallOffTime = 0.05f,
		WallrunJumpOutwardSpeed = 205f,
		WallrunJumpUpSpeed = 230f,
		WallrunJumpInputDirSpeed = 80f,
		WallrunRepelTime = 0.22f,
		WallrunMaxViewTilt = 15f,
		WallrunViewTiltSpeed = 6.0f,
		SlideRequiredStartSpeed = 200f,
		SlideMaxAngleDot = 0.6f,
		SlideBoostCooldown = 2.0f,
		SlideSpeedBoost = 150f,
		SlideSpeedBoostCap = 400f,
		SlideAccel = 50f,
		SlideDecel = 50f,
		SlideWantToStopDecel = 400f,
		SlideVelocityDecay = 0.7f,
		SlideStopSpeed = 125f,
		SlideMaxStopSpeed = 350f,
		SlideJumpHeight = 50f,
		SlideFovScale = 1.1f,
		SlideFovLerpInTime = 0.25f,
		SlideFovLerpOutTime = 0.25f,
		SlideViewTiltSide = 15f,
		SlideViewTiltPlayerSpeed = 400f,
		SlideViewTiltIncreaseSpeed = 5f,
		SlideViewTiltDecreaseSpeed = 2.5f,
		TraversalCooldown = 0.5f,
		TraversalViewLerpInDuration = 0.15f,
		SoundJumpJetStart1P = "Jumpjet_Jump_Start_1P",
		SoundJumpJetStart3P = "Jumpjet_Jump_Start_3P",
		SoundWallrunStart1P = "Jumpjet_Wallrun_Start_1P",
		SoundWallrunStart3P = "Jumpjet_Wallrun_Start_3P",
		SoundWallrunEnd1P = "Jumpjet_Wallrun_End_1P",
		SoundWallrunEnd3P = "Jumpjet_Wallrun_End_3P",
		SoundWallrunImpact = "wallrun_impact",
		SoundWallrunFootstep = "human_wallrun_default",
		SoundSlideBoost1P = "jumpjet_slide_start_1p",
		SoundSlideBoost3P = "jumpjet_slide_start_3p",
		SoundSlideStart1P = "pilotslide_start_default_1p",
		SoundSlideStop1P = "pilotslide_stop_default_1p",
		SoundGroundImpact = "Pilot.GroundImpact",
		SoundLandDefault = "human_land_default",
		SoundMantleLow = "mantle_lowmantle",
		SoundMantleMedium = "mantle_mediummantle",
		SoundMantle3P = "mantle_3p"
	};

	/// <summary>
	/// Reads scripts/players/mp/pilot_mp.set (following #base inheritance to pilot_base.set)
	/// and merges parsed keys over <see cref="RetailDefault"/>.
	/// </summary>
	internal static Titanfall2PilotSettings Read( Func<string, string> readText, string entryScript = MultiplayerScript )
	{
		var settings = RetailDefault;
		if ( readText is null ) return settings;

		var global = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var stand = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var crouch = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		LoadMerged( entryScript, readText, visited, global, stand, crouch );
		if ( global.Count == 0 && stand.Count == 0 && crouch.Count == 0 ) return settings;

		float GlobalFloat( string key, float fallback, float min, float max ) =>
			TryParseFloat( global, key, out var v ) ? Math.Clamp( v, min, max ) : fallback;
		float StandFloat( string key, float fallback, float min, float max ) =>
			TryParseFloat( stand, key, out var v ) ? Math.Clamp( v, min, max ) : fallback;
		float CrouchFloat( string key, float fallback, float min, float max ) =>
			TryParseFloat( crouch, key, out var v ) ? Math.Clamp( v, min, max ) : fallback;
		string GlobalString( string key, string fallback ) =>
			global.TryGetValue( key, out var v ) && !string.IsNullOrWhiteSpace( v ) ? v : fallback;

		return settings with
		{
			GravityScale = GlobalFloat( "gravityscale", settings.GravityScale, 0.2f, 2f ),
			WalkSpeed = StandFloat( "speed", settings.WalkSpeed, 60f, 600f ),
			SprintSpeed = StandFloat( "sprintspeed", settings.SprintSpeed, 100f, 800f ),
			CrouchSpeed = CrouchFloat( "speed", settings.CrouchSpeed, 30f, 300f ),
			StandHeight = TryParseVectorZ( stand, "hull_max", out var sh ) ? Math.Clamp( sh, 40f, 120f ) : settings.StandHeight,
			StandViewHeight = TryParseVectorZ( stand, "viewheight", out var svh ) ? Math.Clamp( svh, 30f, 100f ) : settings.StandViewHeight,
			CrouchHeight = TryParseVectorZ( crouch, "hull_max", out var ch ) ? Math.Clamp( ch, 24f, 70f ) : settings.CrouchHeight,
			CrouchViewHeight = TryParseVectorZ( crouch, "viewheight", out var cvh ) ? Math.Clamp( cvh, 16f, 60f ) : settings.CrouchViewHeight,
			StepHeight = GlobalFloat( "stepheight", settings.StepHeight, 4f, 36f ),
			JumpHeight = GlobalFloat( "jumpheight", settings.JumpHeight, 20f, 180f ),
			DoubleJumpHeight = GlobalFloat( "superjumpMaxHeight", settings.DoubleJumpHeight, 20f, 180f ),
			DoubleJumpHorzSpeed = GlobalFloat( "superjumpHorzSpeed", settings.DoubleJumpHorzSpeed, 0f, 600f ),
			WallrunTimeLimit = GlobalFloat( "wallrun_timeLimit", settings.WallrunTimeLimit, 0.5f, 8f ),
			WallrunMaxSpeedHorizontal = GlobalFloat( "wallrunMaxSpeedHorizontal", settings.WallrunMaxSpeedHorizontal, 200f, 1000f ),
			WallrunAccelerateHorizontal = GlobalFloat( "wallrunAccelerateHorizontal", settings.WallrunAccelerateHorizontal, 200f, 5000f ),
			WallrunMaxSpeedVertical = GlobalFloat( "wallrunMaxSpeedVertical", settings.WallrunMaxSpeedVertical, 50f, 600f ),
			WallrunAccelerateVertical = GlobalFloat( "wallrunAccelerateVertical", settings.WallrunAccelerateVertical, 50f, 2000f ),
			WallrunJumpOutwardSpeed = GlobalFloat( "wallrunJumpOutwardSpeed", settings.WallrunJumpOutwardSpeed, 50f, 600f ),
			WallrunJumpUpSpeed = GlobalFloat( "wallrunJumpUpSpeed", settings.WallrunJumpUpSpeed, 80f, 600f ),
			WallrunJumpInputDirSpeed = GlobalFloat( "wallrunJumpInputDirSpeed", settings.WallrunJumpInputDirSpeed, 0f, 300f ),
			SlideDecel = GlobalFloat( "slidedecel", settings.SlideDecel, 5f, 600f ),
			SlideVelocityDecay = GlobalFloat( "slidevelocitydecay", settings.SlideVelocityDecay, 0.1f, 0.99f ),
			SlideFovScale = GlobalFloat( "slideFOVScale", settings.SlideFovScale, 1f, 1.35f ),
			SlideFovLerpInTime = GlobalFloat( "slideFOVLerpInTime", settings.SlideFovLerpInTime, 0.05f, 1f ),
			SlideFovLerpOutTime = GlobalFloat( "slideFOVLerpOutTime", settings.SlideFovLerpOutTime, 0.05f, 1f ),
			SoundJumpJetStart1P = GlobalString( "sound_jumpjet_jump_start_1p", settings.SoundJumpJetStart1P ),
			SoundJumpJetStart3P = GlobalString( "sound_jumpjet_jump_start_3p", settings.SoundJumpJetStart3P ),
			SoundWallrunStart1P = GlobalString( "sound_jumpjet_wallrun_start_1p", settings.SoundWallrunStart1P ),
			SoundWallrunStart3P = GlobalString( "sound_jumpjet_wallrun_start_3p", settings.SoundWallrunStart3P ),
			SoundWallrunEnd1P = GlobalString( "sound_jumpjet_wallrun_finish_1p", settings.SoundWallrunEnd1P ),
			SoundWallrunEnd3P = GlobalString( "sound_jumpjet_wallrun_finish_3p", settings.SoundWallrunEnd3P ),
			SoundWallrunImpact = GlobalString( "sound_wallrunImpact", settings.SoundWallrunImpact ),
			SoundGroundImpact = GlobalString( "sound_groundImpact", settings.SoundGroundImpact )
		};
	}

	static void LoadMerged( string path, Func<string, string> readText, HashSet<string> visited,
		Dictionary<string, string> global, Dictionary<string, string> stand, Dictionary<string, string> crouch )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !visited.Add( path ) || visited.Count > 8 ) return;
		var text = readText( path );
		if ( string.IsNullOrWhiteSpace( text ) ) return;

		var folder = path.Contains( '/' ) ? path[..(path.LastIndexOf( '/' ) + 1)] : string.Empty;
		var tokens = Tokenize( text );
		var index = 0;

		// Process #base directives before applying this file's overrides.
		while ( index + 1 < tokens.Count && tokens[index].Equals( "#base", StringComparison.OrdinalIgnoreCase ) )
		{
			var baseFile = tokens[index + 1].Replace( '\\', '/' );
			var basePath = baseFile.Contains( '/' ) ? baseFile : folder + baseFile;
			LoadMerged( basePath, readText, visited, global, stand, crouch );
			index += 2;
		}

		// Expect root block: "<setname>" { ... }
		if ( index + 1 >= tokens.Count ) return;
		index++; // skip set name
		if ( tokens[index] != "{" ) return;
		index++;

		while ( index < tokens.Count && tokens[index] != "}" )
		{
			var key = tokens[index++];
			if ( index >= tokens.Count ) break;
			if ( tokens[index] == "{" )
			{
				var target = key.Equals( "global", StringComparison.OrdinalIgnoreCase ) ? global
					: key.Equals( "stand", StringComparison.OrdinalIgnoreCase ) ? stand
					: key.Equals( "crouch", StringComparison.OrdinalIgnoreCase ) ? crouch
					: null;
				ReadFlatBlock( tokens, ref index, target );
			}
			else
			{
				index++; // skip top-level scalar value
			}
		}
	}

	static void ReadFlatBlock( List<string> tokens, ref int index, Dictionary<string, string> target )
	{
		if ( index >= tokens.Count || tokens[index] != "{" ) return;
		index++;
		while ( index < tokens.Count && tokens[index] != "}" )
		{
			var key = tokens[index++];
			if ( index >= tokens.Count ) break;
			if ( tokens[index] == "{" )
			{
				SkipBlock( tokens, ref index );
				continue;
			}
			var value = tokens[index++];
			if ( target is not null ) target[key] = value;
		}
		if ( index < tokens.Count && tokens[index] == "}" ) index++;
	}

	static void SkipBlock( List<string> tokens, ref int index )
	{
		if ( index >= tokens.Count || tokens[index] != "{" ) return;
		var depth = 0;
		while ( index < tokens.Count )
		{
			var tok = tokens[index++];
			if ( tok == "{" ) depth++;
			else if ( tok == "}" && --depth <= 0 ) break;
		}
	}

	static List<string> Tokenize( string text )
	{
		var result = new List<string>( 256 );
		var i = 0;
		while ( i < text.Length )
		{
			var c = text[i];
			if ( char.IsWhiteSpace( c ) ) { i++; continue; }
			if ( c == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				i += 2;
				while ( i < text.Length && text[i] != '\n' ) i++;
				continue;
			}
			if ( c is '{' or '}' ) { result.Add( c.ToString() ); i++; continue; }
			if ( c == '"' )
			{
				var start = ++i;
				while ( i < text.Length && text[i] != '"' ) i++;
				result.Add( text[start..i] );
				if ( i < text.Length ) i++;
				continue;
			}
			var tokenStart = i;
			while ( i < text.Length && !char.IsWhiteSpace( text[i] ) && text[i] is not '{' and not '}' and not '"'
				&& !(text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/') )
				i++;
			result.Add( text[tokenStart..i] );
		}
		return result;
	}

	static bool TryParseFloat( Dictionary<string, string> table, string key, out float value )
	{
		value = 0f;
		return table.TryGetValue( key, out var raw )
			&& float.TryParse( raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value )
			&& float.IsFinite( value );
	}

	static bool TryParseVectorZ( Dictionary<string, string> table, string key, out float z )
	{
		z = 0f;
		if ( !table.TryGetValue( key, out var raw ) ) return false;
		var parts = raw.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		return parts.Length == 3
			&& float.TryParse( parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z )
			&& float.IsFinite( z );
	}
}
