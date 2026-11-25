namespace GoldSrc.HalfLife;

[Hide]
public abstract class Aircraft : Npc
{
	internal abstract float SpeedPerPitchStep { get; }
	internal abstract float RotorAttenuation { get; }
	internal abstract string RotorSample { get; }
	internal abstract string DebrisModel { get; }
	SoundHandle _rotor;
	SoundFile _rotorSample;
	Vector3? _lastOffset;
	TimeSince _pitchSample;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( _rotorSample is null && Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount && mount.NpcAudio.Find( RotorSample ) is { } path )
			_rotorSample = SoundFile.Load( path );
		if ( _rotorSample is null ) return;
		if ( !_rotor.IsValid() || !_rotor.IsPlaying )
		{
			_rotor = GoldSrcSoundEvents.Play( _rotorSample, WorldPosition, pitch: 1.1f, attenuation: RotorAttenuation );
			if ( !_rotor.IsValid() ) return;
			_rotor.DopplerScale = 0;
			_lastOffset = null;
		}
		_rotor.Position = WorldPosition;

		var camera = Scene.Camera;
		if ( !camera.IsValid() )
		{
			_lastOffset = null;
			return;
		}

		if ( _lastOffset.HasValue && _pitchSample < 0.1f ) return;
		var offset = WorldPosition - camera.WorldPosition;
		if ( _lastOffset is { } previous )
		{
			var closingSpeed = (previous - offset).Dot( offset.Normal ) / _pitchSample;
			var pitch = System.Math.Clamp( (int)(100 + closingSpeed / SpeedPerPitchStep), 50, 250 );
			_rotor.Pitch = (pitch == 100 ? 101 : pitch) / 100f;
		}
		_lastOffset = offset;
		_pitchSample = 0;
	}

	protected override GameObject CreateCorpse() => AircraftWreck.Spawn( this );

	protected override void OnDisabled()
	{
		_rotor?.Stop();
		base.OnDisabled();
	}
}
