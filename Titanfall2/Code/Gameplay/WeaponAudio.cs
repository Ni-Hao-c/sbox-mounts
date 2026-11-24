/// <summary>Raw retail source families, separated by perspective and firing stage.</summary>
public record struct Titanfall2WeaponAudio
{
	public string[] Start1P { get; set; }
	public string[] Loop1P { get; set; }
	public string[] Tail1P { get; set; }
	public string[] Start3P { get; set; }
	public string[] Loop3P { get; set; }
	public string[] Tail3P { get; set; }
	public int BurstShots { get; set; }
}

static class Titanfall2WeaponSoundSelection
{
	internal static string Family( string script ) => script?.Replace( "mp_weapon_", "" ).Replace( "mp_titanweapon_", "" ) switch
	{
		"car" => "cbr101", "rspn101" or "rspn101_og" => "r101", "g2" => "g2a4", "semipistol" => "2011pistol",
		"alternator_smg" => "alternator", "hemlok_smg" => "volt", "esaw" => "devotion", "autopistol" => "re45",
		"shotgun" => "shotgun", "sniper" => "krabersniper", "pulse_lmg" => "coldwar", "shotgun_pistol" => "pistolshotgun",
		"smart_pistol" => "smartpistol", "rocket_launcher" => "archer", "defender" => "chargerifle", "mgl" => "mglv2",
		"smr" => "arl", "wingman_n" => "wingman", "arc_launcher" => "thunderbolt",
		"xo16_shorty" or "xo16_vanguard" => "xo16", "predator_cannon" => "predator", "sticky_40mm" => "40mm",
		"leadwall" => "triplethreat", "meteor" => "titanmortar", "particle_accelerator" => "arccannon",
		var other => other
	};

	// Do not use the general ambient/event fuzzy resolver for a gunshot. A candidate must
	// belong to the exact weapon family and perspective before its acoustic role is ranked.
	internal static string[] Select( IEnumerable<string> bank, string family, string perspective, string stage )
	{
		if ( string.IsNullOrEmpty( family ) ) return [];
		if ( family == "titansniperrifle" ) family = "titansniper";
		if ( perspective == "3p" ) family = family switch { "krabersniper" => "sniper", "triplethreat" => "leadwall", _ => family };
		// The Sidewinder bank predates the wpn_* naming scheme. These are composite
		// attack + mechanical + tail sources, not a sustained loop or a separate tail.
		if ( family == "arl" )
		{
			var attack = perspective == "1p" ? "sound/arl_1p_atkmechwarbletailcomp_" : "sound/arl_3p_close_atktail_";
			return stage == "start" ? bank.Where( name => name.StartsWith( attack, StringComparison.OrdinalIgnoreCase ) ).ToArray() : [];
		}
		if ( family == "thunderbolt" )
		{
			var attack = perspective == "1p" ? "sound/wpn_thunderbolt_1p_energy_lr_" : "sound/wpn_thunderbolt_3p_blast_mid_";
			return stage == "start" ? bank.Where( name => name.StartsWith( attack, StringComparison.OrdinalIgnoreCase ) ).ToArray() : [];
		}
		var prefix = $"sound/wpn_{family}_{perspective}_";
		var candidates = bank.Where( name => name.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
			.Select( name => (Name: name, Score: Score( name[prefix.Length..].ToLowerInvariant(), stage )) )
			.Where( pair => pair.Score > 0 ).OrderByDescending( pair => pair.Score ).ThenBy( pair => pair.Name, StringComparer.OrdinalIgnoreCase ).ToArray();
		if ( candidates.Length == 0 ) return [];
		var best = candidates[0];
		var take = best.Name[..best.Name.LastIndexOf( '_' )];
		return candidates.Where( pair => pair.Score == best.Score && pair.Name.StartsWith( take + "_", StringComparison.OrdinalIgnoreCase ) )
			.Select( pair => pair.Name ).Take( 12 ).ToArray();
	}

	internal static string[] Animation( IEnumerable<string> bank, string logicalName )
	{
		var words = logicalName?.ToLowerInvariant().Split( ['_', '.', ' '], StringSplitOptions.RemoveEmptyEntries );
		if ( words is not { Length: >= 3 } || words[0] is not ("weapon" or "wpn") ) return [];
		var identity = words[1] switch { "eva8" => "shotgun", "kraber" => "krabersniper", "g2" => "g2a4", _ => words[1] };
		var prefix = "sound/wpn_" + identity + "_";
		var tokens = words.Skip( 2 ).Where( token => token is not ("1p" or "3p") )
			.Select( token => token == "maghandrest" ? "handrest" : token ).ToArray();
		return bank.Where( name => name.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
			.Where( name => {
				var parts = name[prefix.Length..].ToLowerInvariant().Split( '_' );
				return !parts.Contains( "3p" ) && tokens.All( token => parts.Contains( token ) );
			} ).OrderBy( name => name.Length ).ThenBy( name => name, StringComparer.OrdinalIgnoreCase ).Take( 12 ).ToArray();
	}

	static int Score( string name, string stage )
	{
		if ( name.Contains( "suppress" ) || name.Contains( "lowammo" ) || name.Contains( "reload" ) || name.Contains( "dryfire" )
			|| name.Contains( "shell" ) || name.Contains( "casing" ) || name.Contains( "triggerrelease" ) || name.Contains( "pullout" )
			|| name.Contains( "ricochet" ) || name.Contains( "rico_" ) || name.Contains( "charge" ) ) return 0;
		var looping = name.Contains( "loop" ) || name.Contains( "fullauto" );
		var tail = name.Contains( "tail" ) || name.Contains( "lastshot" ) || name.Contains( "winddown" ) || name.Contains( "burstend" );
		if ( stage == "loop" && (!looping || tail || name.Contains( "startup" ) || name.Contains( "trigger" )) ) return 0;
		if ( stage == "tail" && !tail ) return 0;
		if ( stage == "start" && (looping || tail) && !name.Contains( "startup" ) ) return 0;
		if ( !name.Contains( "wpnfire" ) && !name.Contains( "fire_" ) && !name.Contains( "core" ) && !name.Contains( "shot" )
			&& !name.Contains( "fullauto" ) ) return 0;
		if ( name.Contains( "mech" ) || name.Contains( "env" ) || name.Contains( "lfe" ) || name.Contains( "bass" ) || name.Contains( "trigger" ) ) return 0;
		return 100 + (name.Contains( "firstshot" ) ? 40 : 0) + (name.Contains( "core" ) ? 30 : 0)
			+ (name.Contains( "shot" ) ? 20 : 0) + (name.Contains( "close" ) ? 15 : 0)
			+ (name.Contains( "6ch" ) ? 2 : 0)
			- (name.Contains( "dist" ) ? 40 : 0) - (name.Contains( "mid" ) || name.Contains( "med_" ) ? 20 : 0)
			- (name.Contains( "alley" ) || name.Contains( "interior" ) ? 5 : 0);
	}
}

public partial class Titanfall2Mount
{
	readonly Dictionary<string, Titanfall2WeaponAudio> _weaponAudio = new( StringComparer.OrdinalIgnoreCase );
	internal Titanfall2WeaponAudio GetWeaponAudio( Titanfall2WeaponData data )
	{
		if ( _weaponAudio.TryGetValue( data.ScriptName, out var audio ) ) return audio;
		var family = Titanfall2WeaponSoundSelection.Family( data.ScriptName );
		// The Titan plasma railgun shares the script basename with the pilot Kraber.
		if ( data.ScriptName == "mp_titanweapon_sniper" ) family = "titansniperrifle";
		string[] Select( string view, string stage ) => Titanfall2WeaponSoundSelection.Select( _milesSoundAssets.Keys, family, view, stage );
		audio = new() { Start1P = Select( "1p", "start" ), Loop1P = data.Automatic ? Select( "1p", "loop" ) : [], Tail1P = Select( "1p", "tail" ),
			Start3P = Select( "3p", "start" ), Loop3P = data.Automatic ? Select( "3p", "loop" ) : [], Tail3P = Select( "3p", "tail" ), BurstShots = family is "hemlok" or "coldwar" ? data.BurstCount : 1 };
		if ( audio.Start1P.Length == 0 && audio.Loop1P.Length == 0 )
		{
			// A same-weapon third-person recording is preferable to another gun's sample.
			audio.Start1P = audio.Start3P; audio.Loop1P = audio.Loop3P; audio.Tail1P = audio.Tail3P;
		}
		_weaponAudio[data.ScriptName] = audio;
		return audio;
	}

	internal bool TryGetExactSound( string name, bool loop, out SoundFile sound )
	{
		sound = null;
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		var key = NormalizeSoundKey( name );
		return (_milesSoundAssets.ContainsKey( key ) || _soundFileSources.ContainsKey( key + ".wav" ))
			&& TryGetSoundFile( key, loop, out sound, out _ );
	}

	readonly Dictionary<string, string[]> _weaponAnimationSounds = new( StringComparer.OrdinalIgnoreCase );
	internal bool TryGetWeaponAnimationSound( string name, out SoundFile sound )
	{
		if ( TryGetExactSound( name, false, out sound ) ) return true;
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		if ( !_weaponAnimationSounds.TryGetValue( name, out var samples ) )
			_weaponAnimationSounds[name] = samples = Titanfall2WeaponSoundSelection.Animation( _milesSoundAssets.Keys, name );
		return samples.Length > 0 && TryGetExactSound( samples[Random.Shared.Next( samples.Length )], false, out sound );
	}
}

/// <summary>One start / continuous loop / one release tail. Called from the existing shot-effect relay.</summary>
[Hide]
public sealed class Titanfall2WeaponSoundEmitter : Component
{
	SoundHandle _loop;
	TimeSince _sinceShot = 100;
	float _timeout;
	string _mount;
	string[] _tails;
	bool _firstPerson;
	Vector3 _position;
	int _burstRemaining;
	bool _active;

	internal void Shot( string mount, Titanfall2WeaponAudio audio, bool firstPerson, Vector3 position, float delay )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless ) return;
		var start = firstPerson ? audio.Start1P : audio.Start3P;
		var loop = firstPerson ? audio.Loop1P : audio.Loop3P;
		var fresh = !_active || _sinceShot > _timeout || firstPerson != _firstPerson;
		if ( fresh && _active ) StopFiring();
		_mount = mount; _firstPerson = firstPerson; _position = position;
		_tails = firstPerson ? audio.Tail1P : audio.Tail3P;
		_timeout = MathF.Max( 0.08f, delay * 1.45f );
		_sinceShot = 0;
		_active = true;
		if ( _burstRemaining > 0 && !fresh ) { _burstRemaining--; return; }
		if ( fresh && start is { Length: > 0 } )
		{
			Play( start, false );
			_burstRemaining = Math.Max( 0, audio.BurstShots - 1 );
		}
		else if ( loop is { Length: > 0 } )
		{
			if ( !_loop.IsValid() ) _loop = Play( loop, true );
		}
		else Play( start, false );
	}

	SoundHandle Play( string[] candidates, bool loop )
	{
		if ( candidates is not { Length: > 0 } || Sandbox.Mounting.Directory.Get( _mount ?? "titanfall2" ) is not Titanfall2Mount mount ) return null;
		var source = candidates[Random.Shared.Next( candidates.Length )];
		if ( !mount.TryGetExactSound( source, loop, out var sound ) ) return null;
		var handle = Sound.PlayFile( sound, _firstPerson ? 0.85f : 0.65f, 1, 0, 0 );
		if ( handle.IsValid() ) { handle.Position = _position; handle.ListenLocal = _firstPerson; }
		return handle;
	}

	protected override void OnUpdate()
	{
		if ( _loop.IsValid() ) _loop.Position = _position;
		if ( _active && _sinceShot > _timeout ) StopFiring();
	}
	internal void StopFiring()
	{
		if ( !_active ) return;
		_loop?.Stop( 0.025f ); _loop = null;
		Play( _tails, false ); _active = false; _burstRemaining = 0;
	}
	protected override void OnDisabled() { _loop?.Stop( 0 ); _loop = null; _active = false; }
}
