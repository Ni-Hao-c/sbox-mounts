using Sandbox.UI;

/// <summary>
/// Lightweight runtime Panel host for mount HUD components that supply inline or code styles.
/// Avoids <see cref="PanelComponent"/>'s unconditional host-filesystem <c>.cs.scss</c> lookup,
/// which logs FileNotFoundException whenever the mount runs inside a host game.
/// </summary>
public abstract class Titanfall2MountPanelComponent : Component
{
	Panel _panel;

	public Panel Panel => _panel;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		EnsurePanelCreated();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( _panel.IsValid() && !_panel.Parent.IsValid() )
			_panel.Parent = FindParentPanel();
	}

	protected override void OnDisabled()
	{
		DestroyPanel();
		base.OnDisabled();
	}

	protected override void OnDestroy()
	{
		DestroyPanel();
		base.OnDestroy();
	}

	void EnsurePanelCreated()
	{
		if ( _panel.IsValid() ) return;
		var panel = new Panel { ElementName = GetType().Name.ToLowerInvariant() };
		panel.Parent = FindParentPanel();
		_panel = panel;
	}

	Panel FindParentPanel()
	{
		const FindMode EnabledInAncestors = FindMode.Enabled | FindMode.InAncestors;
		if ( Components.Get<ScreenPanel>( FindMode.EnabledInSelf ) is { } screen ) return screen.GetPanel();
		if ( Components.Get<Sandbox.WorldPanel>( FindMode.EnabledInSelf ) is { } world ) return world.GetPanel();
		if ( Components.Get<ScreenPanel>( EnabledInAncestors ) is { } ancScreen ) return ancScreen.GetPanel();
		if ( Components.Get<Sandbox.WorldPanel>( EnabledInAncestors ) is { } ancWorld ) return ancWorld.GetPanel();
		return null;
	}

	void DestroyPanel()
	{
		if ( !_panel.IsValid() ) return;
		_panel.Parent = null;
		_panel.Delete( true );
		_panel = null;
	}
}
