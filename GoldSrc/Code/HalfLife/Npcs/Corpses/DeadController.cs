namespace GoldSrc.HalfLife;

[Hide]
public sealed class DeadController : NpcCorpse
{
	[Sync( SyncFlags.FromHost )] internal Vector2 HandBrightness { get; set; }
	readonly SpriteRenderer[] _hands = new SpriteRenderer[2];

	internal override void SetBody()
	{
		if ( System.MathF.Max( HandBrightness.x, HandBrightness.y ) <= FadeAmount ) return;
		Renderer.CreateAttachments = true;
		for ( int i = 0; i < _hands.Length; i++ )
		{
			var brightness = i == 0 ? HandBrightness.x : HandBrightness.y;
			if ( brightness <= FadeAmount ) continue;
			_hands[i] = AlienController.CreateHandGlow( Renderer, i, out var light );
			if ( light.IsValid() ) light.Radius = brightness * (255f / 8);
		}
	}

	// GoldSrc's SUB_FadeOut subtracts seven brightness units every tenth of a second.
	float FadeAmount => System.MathF.Floor( Age * 10 ) * (7f / 255);

	protected override void OnUpdate()
	{
		base.OnUpdate();
		for ( int i = 0; i < _hands.Length; i++ )
		{
			if ( _hands[i] is not { IsValid: true } glow ) continue;
			var alpha = (i == 0 ? HandBrightness.x : HandBrightness.y) - FadeAmount;
			if ( alpha <= 0 )
			{
				glow.GameObject.Destroy();
				_hands[i] = null;
				continue;
			}
			glow.Color = Color.White.WithAlpha( alpha );
			if ( Age >= 0.5f && glow.GetComponent<PointLight>() is { } light ) light.Destroy();
		}
	}
}
