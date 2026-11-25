namespace GoldSrc.HalfLife;

public sealed partial class Vortigaunt
{
	readonly List<BeamEffect> _beams = [];
	GameObject _light;
	SoundHandle _chargeSound;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayChargeSound( float pitch )
	{
		_chargeSound?.Stop();
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			_chargeSound = NpcAudio.Play( mount.NpcAudio.Find( "debris/zap4.wav" ), WorldPosition, pitch: pitch );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowChargeLight()
	{
		if ( _light.IsValid() ) return;
		_light = new GameObject( GameObject, false, "Zap charge" );
		_light.NetworkMode = NetworkMode.Never;
		_light.LocalPosition = Vector3.Forward * 2;
		var light = _light.AddComponent<PointLight>();
		light.LightColor = new Color32( 255, 180, 96 );
		light.Radius = 120;
		light.Shadows = false;
		_light.Enabled = true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowBeam( int side, Vector3 end, bool discharge )
	{
		var hand = Renderer.GetAttachmentObject( side < 0 ? "attachment2" : "attachment1" );
		var beam = new GameObject( hand.IsValid() ? hand : GameObject, false, discharge ? "Zap beam" : "Charge beam" );
		beam.NetworkMode = NetworkMode.Never;
		beam.LocalPosition = hand.IsValid() ? Vector3.Zero : Vector3.Up * 36;
		var effect = beam.AddComponent<BeamEffect>();
		effect.TargetPosition = end;
		effect.BeamLifetime = 3;
		effect.Scale = discharge ? 5 : 3;
		Color color = discharge ? new Color32( 180, 255, 96 ) : new Color32( 96, 128, 16 );
		effect.BeamColor = color;
		effect.Additive = true;
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount && mount.GetIndexedSprite( "lgtning" ) is { } sprite )
		{
			effect.Texture = sprite.Animations[0].Frames[0].Texture;
		}
		beam.Enabled = true;
		_beams.Add( effect );
		if ( !discharge )
		{
			foreach ( var charging in _beams )
				charging.Brightness = System.MathF.Min( _beams.Count * 32 / 255f, 1 );
			Renderer.MaterialGroup = $"Skin {_beams.Count / 2}";
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void RemoveEffects()
	{
		foreach ( var beam in _beams )
			if ( beam.IsValid() ) beam.GameObject.Destroy();
		_beams.Clear();
		if ( Renderer.IsValid() ) Renderer.MaterialGroup = "Skin 0";
		_chargeSound?.Stop();
		if ( _light.IsValid() ) _light.Destroy();
	}
}
