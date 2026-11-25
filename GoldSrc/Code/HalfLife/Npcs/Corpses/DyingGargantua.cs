namespace GoldSrc.HalfLife;

[Hide]
public sealed class DyingGargantua : NpcCorpse
{
	bool _burst;

	protected override void OnStart()
	{
		base.OnStart();
		if ( Networking.IsHost ) GargantuaDeathEffect.Begin( WorldTransform );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Age < 1.6f ) return;
		if ( !_burst )
		{
			_burst = true;
			Renderer.Tint = Color.Red;
		}
		Renderer.LocalScale = Vector3.One * (1f + ((float)Age - 1.6f).Clamp( 0, 0.15f ) * 3);
		if ( Networking.IsHost && Age >= 1.75f ) GameObject.Destroy();
	}
}
