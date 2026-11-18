/// <summary>
/// Scene-compatible placeholder for the retired Miles event player. Authored Titanfall 2 events
/// (branching, layering, delays, loops, controllers) are interpreted by the retail Miles runtime,
/// which is native code; this mount is managed-only, so event playback is no longer available.
/// The component and its properties are kept so existing scenes still deserialize and load.
/// Ordinary <c>sound/&lt;name&gt;.vsnd</c> assets keep working through the managed Bink Audio decoder.
/// Scenes without this runtime are supported: the component reports unavailability once.
/// </summary>
[Library( "titanfall2_sound_event" ), Hide]
public sealed class Titanfall2SoundEventPlayer : Component, Component.DontExecuteOnServer
{
	static readonly Sandbox.Diagnostics.Logger Log = new( "Titanfall2Audio" );
	static bool _unavailableReported;

	[Property] string _initialMountIdent = "titanfall2";
	public string MountIdent { get => _initialMountIdent; set => _initialMountIdent = value; }
	[Property] public string EventName { get; set; }
	[Property] public string Language { get; set; }
	[Property] public bool PlayOnStart { get; set; } = true;
	[Property] public float Volume { get; set; } = 1f;

	/// <summary>Whether this player can start authored events. Always false in the managed build.</summary>
	public bool IsAvailable => false;

	protected override void OnStart()
	{
		if ( PlayOnStart ) Play();
	}

	/// <summary>Kept for source compatibility; authored Miles events are unavailable.</summary>
	public bool Play()
	{
		ReportUnavailable();
		return false;
	}

	public void Stop() { }

	public void SetController( string name, float value ) { }

	public void SetGlobalController( string name, float value ) { }

	static void ReportUnavailable()
	{
		if ( _unavailableReported ) return;
		_unavailableReported = true;
		Log.Warning( "Titanfall 2 authored sound events were removed with the native Miles bridge; "
			+ "the managed build keeps .vsnd samples (including 3D/looping ambience) but cannot run the "
			+ "Miles event interpreter. Replace 'titanfall2_sound_event' with a Sound or Soundscape asset." );
	}

	public override string ToString() => $"Titanfall2SoundEventPlayer ({EventName}) - unavailable in the managed build";
}
