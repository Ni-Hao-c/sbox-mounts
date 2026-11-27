/// <summary>Exact event names from the resolved retail WeaponData.</summary>
public record struct Titanfall2WeaponSoundEvents
{
	public string Fire1 { get; set; }
	public string Fire2 { get; set; }
	public string Start { get; set; }
	public string Loop { get; set; }
	public string End { get; set; }
}

public record struct Titanfall2WeaponAudio
{
	public Titanfall2WeaponSoundEvents Player1P { get; set; }
	public Titanfall2WeaponSoundEvents Player3P { get; set; }
	public Titanfall2WeaponSoundEvents Npc { get; set; }
	public bool Looping { get; set; }
	public int BurstShots { get; set; }
	// Diagnostics only. Playback uses exact events, including deliberately shared samples.
	public string[] Start1P { get; set; }
	public string[] Loop1P { get; set; }
	public string[] Tail1P { get; set; }
	public string[] Start3P { get; set; }
	public string[] Loop3P { get; set; }
	public string[] Tail3P { get; set; }
}

internal sealed partial record Titanfall2WeaponData
{
	internal Titanfall2WeaponAudio Audio { get; init; }
	static Titanfall2WeaponAudio ReadAudio( Func<string, string> get, int burstCount )
	{
		Titanfall2WeaponSoundEvents Perspective( string perspective, string shotPerspective ) => new()
		{
			Fire1 = get( "fire_sound_1_" + shotPerspective ), Fire2 = get( "fire_sound_2_" + shotPerspective ),
			Start = get( "burst_or_looping_fire_sound_start_" + perspective ),
			Loop = get( "burst_or_looping_fire_sound_middle_" + perspective ),
			End = get( "burst_or_looping_fire_sound_end_" + perspective )
		};
		return new() { Player1P = Perspective( "1p", "player_1p" ), Player3P = Perspective( "3p", "player_3p" ),
			Npc = Perspective( "npc", "npc" ), Looping = get( "looping_sounds" ) == "1", BurstShots = burstCount };
	}
}

public partial class Titanfall2Mount
{
	readonly Dictionary<string, Titanfall2WeaponAudio> _weaponAudio = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> _missingWeaponEvents = new( StringComparer.OrdinalIgnoreCase );
	internal Titanfall2WeaponAudio GetWeaponAudio( Titanfall2WeaponData data )
	{
		if ( _weaponAudio.TryGetValue( data.ScriptName, out var audio ) ) return audio;
		audio = data.Audio;
		string[] Sources( string name ) => TryGetSoundEvent( name, out var definition )
			? definition.Layers.SelectMany( layer => layer.Choice.Sources ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray() : [];
		audio.Start1P = Sources( string.IsNullOrEmpty( audio.Player1P.Start ) ? audio.Player1P.Fire1 : audio.Player1P.Start );
		audio.Start3P = Sources( string.IsNullOrEmpty( audio.Player3P.Start ) ? audio.Player3P.Fire1 : audio.Player3P.Start );
		audio.Loop1P = Sources( audio.Player1P.Loop ); audio.Loop3P = Sources( audio.Player3P.Loop );
		audio.Tail1P = Sources( audio.Player1P.End ); audio.Tail3P = Sources( audio.Player3P.End );
		return _weaponAudio[data.ScriptName] = audio;
	}

	internal bool TryGetSoundEvent( string name, out MilesSoundEvent definition )
	{
		definition = null;
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		if ( _milesArchive?.Events is { } events && events.TryGet( name, out definition, out _ ) ) return true;
		// Retail impact tables use these legacy dotted spellings; the installed Miles
		// bank contains these exact underscore events. Keep this verified alias
		// set bounded instead of applying punctuation/fuzzy matching to all sounds.
		if ( RetailImpactEventAliases.TryGetValue( name, out var alias )
			&& _milesArchive?.Events is { } aliasEvents && aliasEvents.TryGet( alias, out definition, out _ ) ) return true;
		if ( _missingWeaponEvents.Add( name ) && Titanfall2Log.VerboseLogging )
			Titanfall2Log.Warning( $"TF2 exact sound event '{name}' is unavailable; no fuzzy substitution was made." );
		return false;
	}

	static readonly Dictionary<string, string> RetailImpactEventAliases = new( StringComparer.OrdinalIgnoreCase )
	{
		["Default.Energy.BulletImpact_3P_vs_3P"] = "default_energy_bulletimpact_3p_vs_3p",
		["Titan.Energy.BulletImpact_3P_vs_3P"] = "titan_energy_bulletimpact_3p_vs_3p",
		["Android.Energy.BulletImpact_3P_vs_3P"] = "android_energy_bulletimpact_3p_vs_3p",
		["TitanBubbleShield.Energy.BulletImpact_3P_vs_3P"] = "titanbubbleshield_energy_bulletimpact_3p_vs_3p",
		["TitanShield.Energy.BulletImpact_3P_vs_3P"] = "titanshield_energy_bulletimpact_3p_vs_3p",
		["Concrete.Heavy.BulletImpact_3P_vs_3P"] = "concrete_heavy_bulletimpact_3p_vs_3p",
		["Dirt.Heavy.BulletImpact_3P_vs_3P"] = "dirt_heavy_bulletimpact_3p_vs_3p",
		["Titan.AmpedXO16.BulletImpact_3P_vs_3P"] = "titan_ampedxo16_bulletimpact_3p_vs_3p",
		["Flesh.LSTAR.BulletImpact_3P_vs_3P"] = "flesh_lstar_bulletimpact_3p_vs_3p",
		["MetalGrate.Heavy.BulletImpact_3P_vs_3P"] = "metalgrate_heavy_bulletimpact_3p_vs_3p",
		["Grass.Heavy.BulletImpact_3P_vs_3P"] = "grass_heavy_bulletimpact_3p_vs_3p",
		["Mud.Heavy.BulletImpact_3P_vs_3P"] = "mud_heavy_bulletimpact_3p_vs_3p",
		["Default.Heavy.BulletImpact_3P_vs_3P"] = "default_heavy_bulletimpact_3p_vs_3p",
		["SolidMetal.Heavy.BulletImpact_3P_vs_3P"] = "solidmetal_heavy_bulletimpact_3p_vs_3p",
		["Sand.Heavy.BulletImpact_3P_vs_3P"] = "sand_heavy_bulletimpact_3p_vs_3p"
	};

	internal bool TryGetExactSound( string name, bool loop, out SoundFile sound )
	{
		sound = null;
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		var key = NormalizeSoundKey( name );
		return (_milesSoundAssets.ContainsKey( key ) || _soundFileSources.ContainsKey( key + ".wav" ))
			&& TryGetSoundFile( key, loop, out sound, out _, exact: true );
	}
}

/// <summary>Per-shot layers plus a single start/loop/end sequence per trigger or burst.</summary>
[Hide]
public sealed class Titanfall2WeaponSoundEmitter : Component
{
	Titanfall2SoundEventEmitter _events;
	TimeSince _sinceShot = 100;
	float _timeout;
	string _mount;
	Titanfall2WeaponSoundEvents _sounds;
	bool _firstPerson, _npc, _active;
	Vector3 _position;
	int _burstRemaining;
	float _ammoPercent = 100;

	internal void Shot( string mount, Titanfall2WeaponAudio audio, bool firstPerson, Vector3 position, float delay,
		bool npc = false, float ammoPercent = 100 )
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless ) return;
		var sounds = npc ? audio.Npc : firstPerson ? audio.Player1P : audio.Player3P;
		var fresh = !_active || _sinceShot > _timeout || firstPerson != _firstPerson || npc != _npc || sounds != _sounds;
		if ( fresh && _active ) StopFiring();
		_events = GameObject.GetOrAddComponent<Titanfall2SoundEventEmitter>();
		_mount = mount; _firstPerson = firstPerson; _npc = npc; _position = position; _sounds = sounds; _ammoPercent = ammoPercent;
		_timeout = MathF.Max( 0.08f, delay * 1.45f ); _sinceShot = 0; _active = true;
		_events.SetSource( position, ammoPercent );
		// These are simultaneous authored events, not alternative candidates.
		Play( sounds.Fire1 ); Play( sounds.Fire2 );
		if ( _burstRemaining > 0 && !fresh ) { _burstRemaining--; return; }
		if ( fresh || (!audio.Looping && audio.BurstShots > 1) )
		{
			Play( sounds.Start );
			_burstRemaining = Math.Max( 0, audio.BurstShots - 1 );
			// Empty start (CAR third person) starts the loop on the first bullet.
			if ( audio.Looping && string.IsNullOrEmpty( sounds.Start ) ) Play( sounds.Loop, true );
		}
		else if ( audio.Looping && !_events.HasSustain ) Play( sounds.Loop, true );
	}

	void Play( string name, bool sustain = false ) => _events.Play( _mount, name, _firstPerson, _position, sustain, _ammoPercent );
	protected override void OnUpdate()
	{
		if ( _active && _sinceShot > _timeout ) StopFiring();
	}
	internal void StopFiring()
	{
		if ( !_active ) return;
		if ( _events.IsValid() ) { _position = _events.SourcePosition; _events.StopSustain(); Play( _sounds.End ); }
		_active = false; _burstRemaining = 0;
	}
	protected override void OnDisabled()
	{
		if ( _events.IsValid() ) _events.StopAll();
		_active = false; _burstRemaining = 0;
	}
}
