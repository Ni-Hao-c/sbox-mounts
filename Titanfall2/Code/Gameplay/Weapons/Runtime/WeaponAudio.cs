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
	internal bool TryGetSoundEvent( string name, out MilesSoundEvent definition )
	{
		definition = null;
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		if ( _milesArchive?.Events is { } events && events.TryGet( name, out definition, out _ ) ) return true;
		// Retail impact tables use these legacy dotted spellings; the installed Miles
		// bank contains these exact underscore events. Keep this verified alias
		// set bounded instead of applying punctuation/fuzzy matching to all sounds.
		if ( Titanfall2ImpactAudioAliasCatalog.TryGetAlias( name, out var alias )
			&& _milesArchive?.Events is { } aliasEvents && aliasEvents.TryGet( alias, out definition, out _ ) ) return true;
		if ( _gameplayCache.RecordMissingWeaponEvent( name ) && Titanfall2Log.VerboseLogging )
			Titanfall2Log.Warning( $"TF2 exact sound event '{name}' is unavailable; no fuzzy substitution was made." );
		return false;
	}

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
