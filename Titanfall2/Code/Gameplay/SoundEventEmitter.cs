/// <summary>Client playback of exact Miles events, preserving simultaneous sound layers.</summary>
[Hide]
public sealed class Titanfall2SoundEventEmitter : Component
{
	sealed class Voice
	{
		internal MilesSoundLayer Layer;
		internal string Source;
		internal string EventName;
		internal Titanfall2Mount Mount;
		internal SoundHandle Handle;
		internal TimeSince Age = 0;
		internal float Delay, VolumeRandom, PitchRandom;
		internal bool FirstPerson, Sustain, Started;
		internal int Remaining;
	}
	readonly List<Voice> _voices = [];
	readonly MilesSoundSelection _selection = new();
	Vector3 _localPosition;
	float _ammoPercent = 100;
	bool _transient;
	internal bool HasSustain => _voices.Any( voice => voice.Sustain );
	internal int VoiceCount => _voices.Count;
	internal Vector3 SourcePosition => GameObject.WorldTransform.PointToWorld( _localPosition );

	internal void SetSource( Vector3 position, float ammoPercent = 100 )
	{
		_localPosition = GameObject.WorldTransform.PointToLocal( position ); _ammoPercent = Math.Clamp( ammoPercent, 0, 100 );
	}
	MilesSoundContext Context( bool firstPerson ) => new( firstPerson || !Scene.Camera.IsValid()
		? 0 : Scene.Camera.WorldPosition.Distance( SourcePosition ), _ammoPercent );

	internal bool Play( string mountIdent, string name, bool firstPerson, Vector3 position, bool sustain = false, float ammoPercent = 100 )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || string.IsNullOrWhiteSpace( name )
			|| Sandbox.Mounting.Directory.Get( mountIdent ?? "titanfall2" ) is not Titanfall2Mount mount ) return false;
		SetSource( position, ammoPercent );
		if ( !mount.TryGetSoundEvent( name, out var definition ) )
		{
			// Some existing NPC voice cues explicitly name a raw source. Accept
			// that exact source only; never run the map soundscape token resolver.
			if ( !mount.TryGetExactSound( name, sustain, out _ ) ) return false;
			definition = new( name, [new( new( name, 100, 0, [] ), new( 0, null ), new( 0, null ),
				new( 0, null ), null, 1, 0, 0, 0, 1, 0 )] );
		}
		var context = Context( firstPerson );
		var accepted = false;
		foreach ( var layer in definition.Layers )
		{
			// Native +4 is a priority/culling hint: zero bypasses culling. It
			// does not mute the low-ammo or other intentionally uncullable layer.
			if ( _voices.Count >= 192 ) continue;
			var source = _selection.Select( layer.Choice );
			if ( string.IsNullOrEmpty( source ) ) continue;
			var voice = new Voice { Layer = layer, Source = source, EventName = definition.Name, Mount = mount, FirstPerson = firstPerson, Sustain = sustain,
				Delay = Math.Clamp( (layer.Delay.Evaluate( context ) + RandomSigned( layer.DelayRandom )) / 1000f, 0, 10 ),
				VolumeRandom = RandomSigned( layer.VolumeRandom ), PitchRandom = RandomSigned( layer.PitchRandom ),
				Remaining = Math.Clamp( layer.Repeats, 1, 8 ) };
			if ( !UpdateVoice( voice ) ) continue;
			_voices.Add( voice );
			accepted = true;
		}
		return accepted;
	}

	static float RandomSigned( float range ) => (Random.Shared.NextSingle() * 2 - 1) * MathF.Abs( range );
	bool UpdateVoice( Voice voice )
	{
		if ( voice.Age < voice.Delay ) return true;
		var context = Context( voice.FirstPerson );
		var layer = voice.Layer;
		var db = layer.Volume.Evaluate( context ) + voice.VolumeRandom
			+ (layer.Distance?.Evaluate( context.Value( layer.Distance.Controller ) ) ?? 0);
		var volume = db <= -94 ? 0 : Math.Clamp( MathF.Pow( 10, Math.Clamp( db, -96, 24 ) / 20f ) * layer.Gain * 0.65f, 0, 16 );
		var pitch = MathF.Pow( 2, Math.Clamp( layer.Pitch.Evaluate( context ) + voice.PitchRandom, -40, 40 ) / 12f );
		if ( !voice.Started )
		{
			if ( !voice.Mount.TryGetExactSound( voice.Source, voice.Sustain, out var file ) ) return false;
			voice.Handle = Sound.PlayFile( file, volume, pitch, 0, 0 );
			voice.Started = true;
			if ( !voice.Handle.IsValid() ) return false;
			voice.Handle.ListenLocal = voice.FirstPerson;
			voice.Handle.SpacialBlend = voice.FirstPerson ? 0 : 1;
			if ( layer.Distance is not null )
			{
				// The event already supplies attenuation. Applying the engine's
				// default falloff a second time destroys its close/mid/distant mix.
				voice.Handle.Distance = 50000;
				voice.Handle.Falloff = new Curve( new Curve.Frame( 0, 1 ), new Curve.Frame( 1, 1 ) );
			}
		}
		if ( !voice.Handle.IsValid() || voice.Handle.Finished )
		{
			if ( !voice.Sustain && --voice.Remaining > 0 ) { voice.Started = false; return UpdateVoice( voice ); }
			return false;
		}
		// ListenLocal uses an origin-space listener. A world-space muzzle here
		// would make a local gun pan/attenuate relative to the map's origin.
		voice.Handle.Position = voice.FirstPerson ? Vector3.Zero : SourcePosition;
		voice.Handle.Volume = volume;
		voice.Handle.Pitch = pitch;
		return true;
	}

	protected override void OnUpdate()
	{
		for ( var i = _voices.Count - 1; i >= 0; i-- )
			if ( !UpdateVoice( _voices[i] ) ) { _voices[i].Handle?.Stop( 0 ); _voices.RemoveAt( i ); }
		if ( _transient && _voices.Count == 0 ) GameObject.Destroy();
	}
	internal void StopSustain()
	{
		for ( var i = _voices.Count - 1; i >= 0; i-- )
			if ( _voices[i].Sustain ) { _voices[i].Handle?.Stop( 0.025f ); _voices.RemoveAt( i ); }
	}
	internal void StopAll()
	{
		foreach ( var voice in _voices ) voice.Handle?.Stop( 0 );
		_voices.Clear();
	}
	protected override void OnDisabled() => StopAll();

	internal static bool OneShot( Scene scene, string mount, string name, Vector3 position, bool firstPerson = false )
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !scene.IsValid() || string.IsNullOrWhiteSpace( name ) ) return false;
		var go = new GameObject( scene, false, "TF2 Sound Event" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.WorldPosition = position;
		var emitter = go.AddComponent<Titanfall2SoundEventEmitter>();
		emitter._transient = true;
		if ( !emitter.Play( mount, name, firstPerson, position ) ) { go.Destroy(); return false; }
		go.Enabled = true;
		return true;
	}
}

// Choice modes come from the bank, never from tokens in a source name.
sealed class MilesSoundSelection
{
	sealed class State { internal int Previous = -1; internal HashSet<int> Used = []; }
	readonly Dictionary<MilesSoundChoice, State> _states = [];
	internal string Select( MilesSoundChoice choice )
	{
		if ( choice.Source is not null ) return choice.Source;
		var children = choice.Children;
		if ( children.Length == 0 ) return null;
		if ( !_states.TryGetValue( choice, out var state ) ) _states[choice] = state = new();
		int selected;
		if ( choice.Mode == 2 ) selected = (state.Previous + 1) % children.Length;
		else if ( choice.Mode == 3 )
		{
			if ( state.Used.Count >= children.Length ) state.Used.Clear();
			var available = Enumerable.Range( 0, children.Length ).Where( i => !state.Used.Contains( i ) ).ToArray();
			selected = available[Random.Shared.Next( available.Length )];
			state.Used.Add( selected );
		}
		else
		{
			var total = children.Sum( child => child.Weight );
			var roll = total > 0 ? Random.Shared.Next( total ) : 0;
			selected = 0;
			while ( selected < children.Length - 1 && roll >= children[selected].Weight ) roll -= children[selected++].Weight;
			// Retail mode 1 advances a repeated weighted pick by one (wrapping).
			if ( choice.Mode == 1 && selected == state.Previous ) selected = (selected + 1) % children.Length;
		}
		state.Previous = selected;
		return Select( children[selected] );
	}
}
