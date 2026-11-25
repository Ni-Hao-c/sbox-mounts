namespace GoldSrc.HalfLife;

public abstract partial class AutomatedTurret
{
	readonly MountedGun _gun = new();
	SpriteRenderer _eye;
	SoundHandle _hum;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !_loaded || Health <= 0 ) return;
		_gun.Pose( Renderer, Aim );
		if ( !HasRotaryBarrel ) return;
		if ( !_eye.IsValid() && Renderer.GetAttachmentObject( "attachment2" ) is { } eye
			&& Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount && mount.GetIndexedSprite( "flare3" ) is { } sprite )
		{
			var glow = new GameObject( eye, false, "Turret eye" );
			glow.NetworkMode = NetworkMode.Never;
			_eye = glow.AddComponent<SpriteRenderer>();
			_eye.Sprite = sprite;
			_eye.Size = 10;
			_eye.Additive = true;
			glow.Enabled = true;
		}
		if ( _eye.IsValid() ) _eye.Color = Color.Red.WithAlpha( EyeBrightness / 255f );
		if ( SpinRate >= 1 && !_hum.IsValid() && Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount hl
			&& hl.NpcAudio.Find( "turret/tu_active2.wav" ) is { } sample && SoundFile.Load( sample ) is { } sound )
		{
			_hum = GoldSrcSoundEvents.Play( sound, WorldPosition, volume: 0.5f );
		}
		if ( SpinRate < 1 && _hum.IsValid() ) _hum.Stop();
	}

	bool SpinUp( Vector3 origin )
	{
		if ( !HasRotaryBarrel || SpinRate >= 1 )
		{
			SpinRate = 1;
			return true;
		}
		if ( SpinRate <= 0 )
		{
			SpinRate = 0.1f;
			_ready = 1;
			EmitSound( "turret/tu_spinup.wav", origin, 0.5f );
		}
		else if ( _ready <= 0 ) SpinRate = System.MathF.Min( 1, SpinRate + 0.075f );
		return false;
	}

	void SpinDown( Vector3 origin )
	{
		if ( SpinRate >= 1 ) EmitSound( "turret/tu_spindown.wav", origin, 0.5f );
		SpinRate = System.MathF.Max( 0, SpinRate - 0.02f );
	}

	protected override void OnDisabled()
	{
		_hum?.Stop();
		base.OnDisabled();
	}
}
