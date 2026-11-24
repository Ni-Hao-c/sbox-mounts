using System.Globalization;

/// <summary>The supported subset of Respawn WeaponData, resolved for PC multiplayer.</summary>
internal sealed partial record Titanfall2WeaponData( string WorldModel, string ViewModel, bool Automatic, int Magazine,
	float ShotsPerSecond, float ReloadSeconds, float Damage, float Range, float Spread, string[] FireSounds, string[] FireSounds1P, float AdsFov, string HoldType, int Pellets, int BurstCount, float BurstDelay, float TitanDamage, Titanfall2ProjectileSettings Projectile )
{
	internal const string Folder = "scripts/weapons/";
	internal string ScriptName { get; init; }
	internal Titanfall2WeaponPresentation Presentation { get; init; }

	internal static Titanfall2WeaponData Read( string path, Func<string, string> read, bool titan = false, bool projectile = false, bool integrated = false, float fallbackSpeed = 0, Color? tint = null, bool ordnance = false, bool antiTitan = false, Titanfall2ProjectileBehavior behavior = Titanfall2ProjectileBehavior.Standard )
	{
		var root = ReadFile( path, read, new( StringComparer.OrdinalIgnoreCase ) );
		var values = new Dictionary<string, string>( root.Values, StringComparer.OrdinalIgnoreCase );
		if ( root.Blocks.TryGetValue( "MP_BASE", out var multiplayer ) )
			foreach ( var (key, value) in multiplayer.Values )
				if ( !value.Equals( "<KEEP_DEFAULT>", StringComparison.OrdinalIgnoreCase ) ) values[key] = value;

		string Get( string key ) => values.GetValueOrDefault( key, "" );
		float Number( string key, float fallback = 0 ) => float.TryParse( Get( key ), NumberStyles.Float, CultureInfo.InvariantCulture, out var value )
			&& float.IsFinite( value ) ? value : fallback;
		var world = ModelPath( Get( "playermodel" ) );
		var view = ModelPath( Get( "viewmodel" ) );
		var mode = Get( "fire_mode" ).ToLowerInvariant();
		// Retail hitscan Titan weapons and some launchers omit fire_mode; ordnance uses offhand.
		if ( (titan || projectile || antiTitan) && mode.Length == 0 ) mode = "auto";
		if ( ordnance && mode.StartsWith( "offhand", StringComparison.Ordinal ) ) mode = "semi-auto";
		var magazine = Number( "ammo_clip_size" );
		if ( integrated && magazine <= 0 ) magazine = 12;
		if ( antiTitan && magazine <= 0 ) magazine = 20;
		if ( ordnance ) magazine = Math.Clamp( magazine <= 0 ? 4 : magazine, 1, 8 );
		var rate = Number( "fire_rate", ordnance ? 1.5f : 0 );
		var reload = Number( "reload_time", Number( "reloadempty_time", ordnance ? 0.9f : 0 ) );
		var damage = Number( "damage_near_value" );
		if ( integrated ) damage = MathF.Max( 1, Number( "npc_damage_near_value", damage ) );
		if ( ordnance && damage <= 0 ) damage = MathF.Max( 10, Number( "explosion_damage", 60 ) );
		var shotgun = Get( "damage_flags" ).Contains( "DF_SHOTGUN", StringComparison.OrdinalIgnoreCase );
		var pellets = Number( "projectiles_per_shot", shotgun ? 8 : 1 );
		var burst = Number( "burst_fire_count", 1 );
		var speed = Number( "projectile_launch_speed", Number( "bolt_speed", fallbackSpeed ) );
		var pattern = Get( "OnWeaponPrimaryAttack" ) switch
		{
			"OnWeaponPrimaryAttack_weapon_doubletake" => Titanfall2BoltPattern.DoubleTake,
			"OnWeaponPrimaryAttack_weapon_mastiff" => Titanfall2BoltPattern.Mastiff,
			_ => Titanfall2BoltPattern.Default
		};
		var firing = new Titanfall2WeaponFiring { Pattern = pattern,
			AmmoPerShot = (int)Math.Clamp( Number( "ammo_per_shot", 1 ), 1, 250 ),
			AmmoMinToFire = (int)Math.Clamp( Number( "ammo_min_to_fire", 1 ), 1, 250 ),
			BoltSpreadHip = pattern == Titanfall2BoltPattern.Mastiff ? 0.05f : Math.Clamp( Number( "bolt_spread_max" ), 0, 1 ),
			BoltSpreadAds = pattern == Titanfall2BoltPattern.Mastiff ? 0.025f : Math.Clamp( Number( "bolt_spread_min" ), 0, 1 ) };
		if ( pattern == Titanfall2BoltPattern.Mastiff ) { pellets = 8; speed = 3000; }
		if ( (!integrated && world is null) || mode is not ("auto" or "semi-auto")
			|| pellets is < 1 or > 12 || pellets != MathF.Truncate( pellets )
			|| burst is < 1 or > 8 || burst != MathF.Truncate( burst ) || (pellets > 1 && burst > 1)
			|| (!projectile && !Get( "damage_type" ).Equals( "bullet", StringComparison.OrdinalIgnoreCase ))
			|| (projectile ? speed is < 100 or > 20000 : speed > 0)
			|| magazine is < 1 or > 250 || magazine != MathF.Truncate( magazine )
			|| rate is < 0.1f or > 60f || reload is <= 0 or > 30f || damage is <= 0 or > 1000 )
			return null;

		var hold = ordnance ? "grenade"
			: Get( "weaponSubClass" ).ToLowerInvariant() is "pistol" or "autolock" ? "pistol" : "rifle";
		// Falloff thresholds describe damage, not the end of a hitscan trace.
		// Keep a bounded engine trace distance; projectile lifetime retains its existing policy.
		var range = projectile ? Math.Clamp( Number( "damage_very_far_distance", Number( "damage_far_distance", 4096 ) ), shotgun ? 256 : 1024, 16384 ) : 16384;
		var flight = new Titanfall2ProjectileSettings();
		if ( projectile )
		{
			var impactRadius = Math.Clamp( Number( "explosionradius" ), 0, 512 );
			var projectileModel = ModelPath( Get( "projectilemodel" ) );
			flight = new()
			{
				Speed = speed,
				GravityScale = pattern == Titanfall2BoltPattern.Mastiff ? 0.09f : Math.Clamp( Number( "projectile_gravity_scale", Number( "bolt_gravity_amount", ordnance ? 1.1f : 0 ) ), 0, 4 ),
				Lifetime = Math.Clamp( Number( "projectile_lifetime", MathF.Max( ordnance ? 3.5f : 1.25f, range / speed ) ), 0.1f, 8 ),
				Radius = Math.Clamp( Number( "projectile_radius", impactRadius > 0 ? 3 : 1.5f ), 0.5f, 10 ),
				SplashRadius = impactRadius,
				SplashDamage = Math.Clamp( Number( "explosion_damage", integrated && impactRadius > 0 ? damage * 0.5f : 0 ), 0, 1000 ),
				SplashTitanDamage = Math.Clamp( Number( "explosion_damage_heavy_armor", Number( "explosion_damage" ) ), 0, 10000 ),
				ImpactTable = Get( "impact_effect_table" ) is { Length: > 0 } table ? table : "default",
				TrailEffect = Get( "projectile_trail_effect_0" ),
				TrailAttachment = Get( "projectile_trail_attachment" ),
				// This active client callback adds an ABSORIGIN_FOLLOW effect in
				// addition to projectile_trail_effect_0; it does not replace it.
				ScriptTrailEffect = Get( "OnWeaponPrimaryAttack" ) == "OnWeaponPrimaryAttack_weapon_sniper" ? "Rocket_Smoke_SMR_Glow" : null,
				FuseSeconds = behavior == Titanfall2ProjectileBehavior.FragGrenade ? Math.Clamp( Number( "grenade_fuse_time", 3.25f ), 0.1f, 12 ) : 0,
				HazardSeconds = behavior == Titanfall2ProjectileBehavior.GravityPull ? 2.9f
					: Math.Clamp( Number( "grenade_ignition_time", behavior == Titanfall2ProjectileBehavior.ElectricSmoke ? 5 : 3.5f ), 0.1f, 12 ),
				HazardEffect = behavior switch { Titanfall2ProjectileBehavior.ThermiteBurn => "P_grenade_thermite", Titanfall2ProjectileBehavior.GravityPull => "P_wpn_grenade_gravity",
					Titanfall2ProjectileBehavior.ElectricSmoke => "P_wpn_smk_electric_pilot", _ => null },
				DamageOwner = Get( "explosion_damages_owner" ) == "1" || ordnance,
				ModelPath = projectileModel == "models/dev/empty_model.mdl" ? null : projectileModel,
				Tint = tint ?? Color.White,
				Behavior = behavior
			};
		}
		string[] FireSounds( string perspective ) => new[] { Get( "burst_or_looping_fire_sound_start_" + perspective ), Get( "fire_sound_1_player_" + perspective ), Get( "fire_sound_2_player_" + perspective ), Get( "burst_or_looping_fire_sound_middle_" + perspective ) }
			.Where( sound => !string.IsNullOrWhiteSpace( sound ) && !sound.Contains( "shell", StringComparison.OrdinalIgnoreCase )
				&& !sound.Contains( "casing", StringComparison.OrdinalIgnoreCase ) ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
		var fireSounds3p = FireSounds( "3p" );
		var fireSounds1p = FireSounds( "1p" );
		var presentation = new Titanfall2WeaponPresentation
		{
			MuzzleView = Get( "fx_muzzle_flash_view" ), MuzzleWorld = Get( "fx_muzzle_flash_world" ), MuzzleAttachment = Get( "fx_muzzle_flash_attach" ),
			MuzzleAttachmentScoped = Get( "fx_muzzle_flash_attach_scoped" ),
			ScopeReticle = Get( "bodygroup_ads_scope_name" ) == "ads_scopes" && Number( "bodygroup_ads_scope_set" ) == 3
				&& Enumerable.Range( 1, 16 ).Any( i => Get( $"bodygroup{i}_name" ) == "scope_dcom" && Number( $"bodygroup{i}_set" ) == 1 )
				? "models/weapons/attachments/dcom_rifle_scope_crosshair" : null,
			ScopeBodyGroup = Get( "bodygroup_ads_scope_name" ), ScopeBodyChoice = (int)Number( "bodygroup_ads_scope_set" ),
			ScopeBlendStart = Number( "zoom_scope_frac_start", 0.2f ), ScopeBlendEnd = Number( "zoom_scope_frac_end", 0.7f ),
			ShellView = Get( "fx_shell_eject_view" ), ShellWorld = Get( "fx_shell_eject_world" ), ShellAttachment = Get( "fx_shell_eject_attach" ),
			ShellAttachmentScoped = Get( "fx_shell_eject_attach_scoped" ), Shell2View = Get( "fx_shell_eject2_view" ),
			Shell2World = Get( "fx_shell_eject2_world" ), Shell2Attachment = Get( "fx_shell_eject2_attach" ),
			TracerView = Get( "tracer_effect_first_person" ), TracerWorld = Get( "tracer_effect" ), ImpactTable = Get( "impact_effect_table" ),
			MuzzleLayers = Get( "OnClientAnimEvent" ) is "OnClientAnimEvent_weapon_sniper" or "OnClientAnimEvent_weapon_dmr"
				? new[] { "muzzle_flash_L", "muzzle_flash_R" }.Select( attachment => new Titanfall2WeaponMuzzleLayer(
					"wpn_mflash_snp_hmn_smoke_side_FP", "wpn_mflash_snp_hmn_smoke_side", attachment, true,
					Get( "OnClientAnimEvent" ) == "OnClientAnimEvent_weapon_dmr" ) ).ToArray() : [],
			ShellSound = Get( "OnClientAnimEvent" ) == "OnClientAnimEvent_weapon_dmr" ? "large_shell_drop" : null,
			ShellSoundDelay = Get( "OnClientAnimEvent" ) == "OnClientAnimEvent_weapon_dmr" ? 0.6f : 0f,
			BodyGroups = Enumerable.Range( 1, 16 ).Where( i => Get( $"bodygroup{i}_name" ).Length > 0 )
				.Select( i => new Titanfall2WeaponBodyGroup( Get( $"bodygroup{i}_name" ), (int)Math.Clamp( Number( $"bodygroup{i}_set" ), 0, 32 ) ) ).ToArray()
		};
		var adsFov = Math.Clamp( Number( "zoom_fov", 55 ), 15, 75 );
		// Offhand throw callbacks use their own launch direction, not a gun aim cone.
		var spread = ordnance ? default : ReadSpreadProfile( Number );
		return new( world, view, mode == "auto", (int)magazine, rate, reload, projectile ? damage : damage / pellets, range,
			spread.Stand.Hip, fireSounds3p, fireSounds1p, adsFov, hold, (int)pellets, (int)burst, Math.Clamp( Number( "burst_fire_delay", 0.25f ), 0, 5 ),
			Math.Clamp( Number( "damage_near_value_titanarmor", damage ), 1, 4000 ) / (projectile ? 1 : pellets), flight )
			{ ScriptName = Path.GetFileNameWithoutExtension( path ), Presentation = presentation, Audio = ReadAudio( Get, (int)burst ), Firing = firing, SpreadProfile = spread,
				DamageProfile = ReadDamageProfile( Number, damage, Math.Clamp( Number( "damage_near_value_titanarmor", damage ), 1, 4000 ) ) };
	}

	static string ModelPath( string text ) => Titanfall2ContentSnapshot.TryNormalize( text, out var path )
		&& path.StartsWith( "models/", StringComparison.Ordinal ) && Path.GetExtension( path ) is ".mdl" or ".rmdl" ? path : null;

	sealed class Block
	{
		internal Dictionary<string, string> Values { get; } = new( StringComparer.OrdinalIgnoreCase );
		internal Dictionary<string, Block> Blocks { get; } = new( StringComparer.OrdinalIgnoreCase );

		internal void Merge( Block other )
		{
			foreach ( var (key, value) in other.Values ) Values[key] = value;
			foreach ( var (key, value) in other.Blocks )
			{
				if ( !Blocks.TryGetValue( key, out var target ) ) Blocks[key] = target = new();
				target.Merge( value );
			}
		}
	}

	static Block ReadFile( string path, Func<string, string> read, HashSet<string> active )
	{
		if ( active.Count >= 8 || !active.Add( path ) ) throw new InvalidDataException( $"Invalid weapon #base chain: {path}" );
		try
		{
			var text = read( path ) ?? throw new InvalidDataException( $"Missing weapon script: {path}" );
			if ( text.Length > 512 * 1024 ) throw new InvalidDataException( $"Weapon script exceeds the size limit: {path}" );
			var reader = new Reader( text.Trim( '\uFEFF', '\0' ) );
			var result = new Block();
			while ( reader.Peek.Text.Equals( "#base", StringComparison.OrdinalIgnoreCase ) )
			{
				reader.Next();
				var include = reader.Next();
				var enabled = reader.Condition();
				if ( include.Kind != Kind.Word || !Titanfall2ContentSnapshot.TryNormalize( Folder + include.Text, out var resolved )
					|| include.Text.Contains( ':' ) || include.Text.StartsWith( '/' ) || include.Text.StartsWith( '\\' )
					|| !resolved.StartsWith( Folder, StringComparison.Ordinal ) || !resolved.EndsWith( ".txt", StringComparison.Ordinal ) )
					throw new InvalidDataException( $"Invalid weapon #base path: {include.Text}" );
				if ( enabled ) result.Merge( ReadFile( resolved, read, active ) );
			}
			if ( !reader.Next().Text.Equals( "WeaponData", StringComparison.OrdinalIgnoreCase ) || reader.Next().Kind != Kind.Open )
				throw new InvalidDataException( $"Expected WeaponData block: {path}" );
			result.Merge( reader.ReadBlock( 0 ) );
			if ( reader.Peek.Kind != Kind.End ) throw new InvalidDataException( $"Unexpected data after WeaponData: {path}" );
			return result;
		}
		finally { active.Remove( path ); }
	}

	enum Kind { End, Word, Open, Close, Condition }
	readonly record struct Token( Kind Kind, string Text );

	sealed class Reader( string text )
	{
		int _position;
		Token? _pending;
		internal Token Peek => _pending ??= Lex();

		internal Token Next()
		{
			var result = Peek;
			_pending = null;
			return result;
		}

		internal Block ReadBlock( int depth )
		{
			if ( depth >= 16 ) throw new InvalidDataException( "WeaponData nesting limit exceeded." );
			var result = new Block();
			while ( Peek.Kind != Kind.Close )
			{
				var key = Next();
				var value = Next();
				if ( key.Kind != Kind.Word || value.Kind is not (Kind.Word or Kind.Open) ) throw new InvalidDataException( "Invalid WeaponData entry." );
				var child = value.Kind == Kind.Open ? ReadBlock( depth + 1 ) : null;
				if ( !Condition() ) continue;
				if ( child is null ) result.Values[key.Text] = value.Text;
				else if ( result.Blocks.TryGetValue( key.Text, out var existing ) ) existing.Merge( child );
				else result.Blocks[key.Text] = child;
			}
			Next();
			return result;
		}

		internal bool Condition()
		{
			var enabled = true;
			while ( Peek.Kind == Kind.Condition )
			{
				var expression = Next().Text;
				enabled &= expression.Split( "||", StringSplitOptions.TrimEntries ).Any( term => term.Split( "&&", StringSplitOptions.TrimEntries ).All( atom =>
				{
					var negate = atom.StartsWith( '!' );
					var name = (negate ? atom[1..].Trim() : atom).TrimStart( '$' ).ToLowerInvariant();
					if ( name.Length == 0 || name.Any( c => !char.IsAsciiLetterOrDigit( c ) && c != '_' ) )
						throw new InvalidDataException( $"Unsupported WeaponData condition: {expression}" );
					return negate != (name is "pc" or "windows" or "win32" or "mp");
				} ) );
			}
			return enabled;
		}

		Token Lex()
		{
			while ( _position < text.Length )
			{
				if ( char.IsWhiteSpace( text[_position] ) ) { _position++; continue; }
				if ( text[_position] != '/' || _position + 1 >= text.Length ) break;
				if ( text[_position + 1] == '/' )
				{
					while ( _position < text.Length && text[_position] is not ('\r' or '\n') ) _position++;
				}
				else if ( text[_position + 1] == '*' )
				{
					var end = text.IndexOf( "*/", _position + 2, StringComparison.Ordinal );
					if ( end < 0 ) throw new InvalidDataException( "Unclosed WeaponData comment." );
					_position = end + 2;
				}
				else break;
			}
			if ( _position >= text.Length ) return new( Kind.End, "" );
			var c = text[_position++];
			if ( c == '{' ) return new( Kind.Open, "{" );
			if ( c == '}' ) return new( Kind.Close, "}" );
			if ( c is '"' or '[' )
			{
				var end = text.IndexOf( c == '[' ? ']' : '"', _position );
				if ( end < 0 ) throw new InvalidDataException( "Unclosed WeaponData token." );
				var value = text[_position..end];
				_position = end + 1;
				return new( c == '[' ? Kind.Condition : Kind.Word, value );
			}
			var start = _position - 1;
			while ( _position < text.Length && !char.IsWhiteSpace( text[_position] ) && text[_position] is not ('{' or '}' or '[' or '"') ) _position++;
			return new( Kind.Word, text[start.._position] );
		}
	}
}
