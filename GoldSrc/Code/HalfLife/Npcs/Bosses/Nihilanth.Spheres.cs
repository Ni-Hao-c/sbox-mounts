namespace GoldSrc.HalfLife;

public sealed partial class Nihilanth
{
	readonly SpriteRenderer[] _spheres = new SpriteRenderer[SphereCount];
	TimeSince _orbitAge;
	GameObject _headGlow;
	readonly float[] _dissolve = new float[SphereCount];
	int _visible;
	bool _initialized;

	void UpdateSpheres()
	{
		if ( !_initialized )
		{
			if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetIndexedSprite( "muzzleflash3" ) is not { } sprite ) return;
			_visible = ActiveSpheres;
			_orbitAge = 0;
			for ( int i = 0; i < _visible; i++ )
			{
				var go = new GameObject( GameObject, false, "Nihilanth sphere" );
				go.NetworkMode = NetworkMode.Never;
				go.LocalPosition = Vector3.Zero;
				_spheres[i] = go.AddComponent<SpriteRenderer>();
				_spheres[i].Sprite = sprite;
				_spheres[i].Size = 64;
				_spheres[i].Color = new Color32( 255, 224, 192 );
				_spheres[i].Additive = true;
				go.Enabled = true;
			}
			_initialized = true;
		}
		while ( _visible > ActiveSpheres )
		{
			int i = --_visible;
			_dissolve[i] = 3;
			if ( _spheres[i].IsValid() )
			{
				var beam = _spheres[i].GameObject.AddComponent<BeamEffect>();
				beam.TargetGameObject = Renderer.GetAttachmentObject( "attachment1" ) ?? GameObject;
				beam.BeamColor = (Color)new Color32( 255, 128, 64 );
				beam.Scale = 8;
				beam.BeamLifetime = 3;
				beam.Additive = true;
			}
		}
		if ( BrainExposed && !_headGlow.IsValid() ) ShowBrain();
		var age = (float)_orbitAge;
		var radius = System.MathF.Min( age * 200, 360 );
		var altitude = System.MathF.Min( age * 120, 240 );
		for ( int i = 0; i < _spheres.Length; i++ )
		{
			var orb = _spheres[i];
			if ( !orb.IsValid() ) continue;
			var angle = i * (2 * System.MathF.PI / SphereCount) + age * (200f / 360);
			var lift = i < _visible ? 0 : (3 - _dissolve[i]) * 200;
			orb.LocalPosition = new Vector3( System.MathF.Cos( angle ) * radius, System.MathF.Sin( angle ) * radius,
				altitude + lift + System.MathF.Sin( angle * 3 ) * radius / 12 );
			if ( i < _visible ) continue;
			_dissolve[i] -= Time.Delta;
			orb.Size = 64 + 32 * (3 - _dissolve[i]);
			if ( _dissolve[i] <= 0 ) orb.GameObject.Destroy();
		}
	}

	void ShowBrain()
	{
		var head = Renderer.GetAttachmentObject( "attachment1" );
		if ( !head.IsValid() || Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetIndexedSprite( "tele1" ) is not { } sprite ) return;
		_headGlow = new GameObject( head, false, "Nihilanth brain" );
		_headGlow.NetworkMode = NetworkMode.Never;
		_headGlow.LocalPosition = Vector3.Zero;
		var glow = _headGlow.AddComponent<SpriteRenderer>();
		glow.Sprite = sprite;
		glow.Size = 128;
		glow.Additive = true;
		var light = _headGlow.AddComponent<PointLight>();
		light.LightColor = new Color32( 255, 192, 64 );
		light.Radius = 256;
		light.Shadows = false;
		_headGlow.Enabled = true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void HandLight( int index )
	{
		var hand = Renderer.GetAttachmentObject( $"attachment{index}" );
		if ( !hand.IsValid() ) return;
		var go = new GameObject( hand, false, "Nihilanth charge" );
		go.NetworkMode = NetworkMode.Never;
		go.LocalPosition = Vector3.Zero;
		var light = go.AddComponent<PointLight>();
		light.Radius = 256;
		light.LightColor = new Color32( 128, 128, 255 );
		light.Shadows = false;
		go.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 1;
		go.Enabled = true;
	}
}
