namespace GoldSrc.HalfLife;

[Hide]
public sealed class GaussOverchargeFlash : PanelComponent
{
	TimeSince _age;

	internal static void Show()
	{
		var go = new GameObject( false, "Gauss overcharge flash" );
		go.NetworkMode = NetworkMode.Never;
		go.Flags |= GameObjectFlags.NotSaved;
		go.AddComponent<ScreenPanel>().ZIndex = 10000;
		go.AddComponent<GaussOverchargeFlash>()._age = 0;
		go.Enabled = true;
	}

	protected override void OnUpdate()
	{
		if ( _age >= 2.5f ) { GameObject.Destroy(); return; }
		if ( Panel is null ) return;
		Panel.Style.Width = Panel.Style.Height = Sandbox.UI.Length.Percent( 100 );
		Panel.Style.BackgroundColor = new Color( 1, 0.5f, 0, 0.5f * System.MathF.Min( 1, (2.5f - _age) / 2 ) );
	}
}
