namespace GoldSrc.HalfLife;

public sealed partial class AlienController
{
	[Sync( SyncFlags.FromHost )] Vector2 Charge { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil ChargeTime { get; set; }
	readonly SpriteRenderer[] _hands = new SpriteRenderer[2];

	readonly PointLight[] _lights = new PointLight[2];
	PointLight _headLight;
	TimeSince _headAge;
	bool _opening;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void HeadLight( bool opening )
	{
		if ( !_headLight.IsValid() )
		{
			var parent = Renderer.GetAttachmentObject( "attachment1" );
			if ( !parent.IsValid() ) return;
			var go = new GameObject( parent, false, "Controller head light" );
			go.NetworkMode = NetworkMode.Never;
			_headLight = go.AddComponent<PointLight>();
			_headLight.LightColor = new Color32( 255, 192, 64 );
			_headLight.Shadows = false;
			go.Enabled = true;
		}
		_headAge = 0;
		_opening = opening;
		_headLight.Radius = opening ? 1 : 32;
	}

	void UpdateLights()
	{
		if ( _headLight.IsValid() )
		{
			if ( _headAge >= (_opening ? 2 : 1) ) _headLight.GameObject.Destroy();
			else _headLight.Radius = _opening ? 1 + 32 * _headAge : 32 * (1 - _headAge);
		}
		for ( int i = 0; i < _hands.Length; i++ )
		{
			if ( !_hands[i].IsValid() )
			{
				_hands[i] = CreateHandGlow( Renderer, i, out _lights[i] );
				if ( !_hands[i].IsValid() ) continue;
			}
			var target = (i == 0 ? Charge.x : Charge.y) / 255;
			var alpha = float.Lerp( _hands[i].Color.a, target, (Time.Delta / System.MathF.Max( Time.Delta, ChargeTime )).Clamp( 0, 1 ) );
			_hands[i].Color = Color.White.WithAlpha( alpha );
			_lights[i].Radius = alpha * (255f / 8);
		}
	}

	internal static SpriteRenderer CreateHandGlow( SkinnedModelRenderer renderer, int index, out PointLight light )
	{
		light = null;
		var hand = renderer.GetAttachmentObject( $"attachment{index + 3}" );
		if ( !hand.IsValid() || Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount || mount.GetIndexedSprite( "xspark4" ) is not { } sprite ) return null;
		var go = new GameObject( hand, false, "Controller hand glow" );
		go.NetworkMode = NetworkMode.Never;
		var glow = go.AddComponent<SpriteRenderer>();
		glow.Sprite = sprite;
		glow.Size = 32;
		glow.Additive = true;
		glow.Color = Color.Transparent;
		light = go.AddComponent<PointLight>();
		light.LightColor = new Color32( 255, 192, 64 );
		light.Shadows = false;
		go.Enabled = true;
		return glow;
	}

	internal override NpcCorpse AddCorpseComponent( GameObject go )
	{
		var corpse = go.AddComponent<DeadController>();
		corpse.HandBrightness = new Vector2( _hands[0]?.Color.a ?? 0, _hands[1]?.Color.a ?? 0 );
		return corpse;
	}
}
