namespace Source1;

[Hide]
public sealed class SourceWaterAudio : Component, Component.DontExecuteOnServer
{
	[Property] public SourceWaterMap Map { get; set; }
	[Property] public SoundEvent Ambient { get; set; }

	SoundHandle _sound;
	bool _underwater;

	protected override void OnUpdate()
	{
		var camera = Scene.Camera;
		var underwater = Map.IsValid() && Map.Active && camera.IsValid()
			&& Map.FindWater( camera.WorldPosition ) >= 0;
		if ( underwater == _underwater ) return;
		_underwater = underwater;
		StopSound();
		if ( !underwater ) return;

		_sound = Sound.Play( Ambient );
		if ( !_sound.IsValid() )
		{
			Log.Warning( "Source underwater ambience could not be played." );
			return;
		}

		_sound.ListenLocal = true;
		_sound.SpacialBlend = 0;
		_sound.DistanceAttenuation = false;
		_sound.OcclusionEnabled = false;
		_sound.ReverbEnabled = false;
		_sound.AirAbsorption = false;
	}

	protected override void OnDisabled()
	{
		StopSound();
		_underwater = false;
	}

	void StopSound()
	{
		_sound?.Stop( 0 );
		_sound = null;
	}
}
