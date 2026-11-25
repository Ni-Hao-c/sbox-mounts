using Sandbox.Rendering;
using Sandbox.UI;
using Sandbox.UI.Construct;

public sealed partial class Titanfall2Weapon
{
	GameObject _scopeHud;
	GameObject _scopeView;
	string _scopeBodyGroup;
	int _scopeBodyChoice = -1;
	bool? _scopeViewVisible;
	public float ScopeOpacity => !Active || string.IsNullOrEmpty( Presentation.ScopeReticle ) || IsProxy || !IsHeld || !IsActive || !ViewModel.IsValid()
		? 0 : Math.Clamp( (_adsBlend - Presentation.ScopeBlendStart) / MathF.Max( 0.01f, Presentation.ScopeBlendEnd - Presentation.ScopeBlendStart ), 0, 1 );

	void UpdateScopePresentation()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || !ViewModel.IsValid() || string.IsNullOrEmpty( Presentation.ScopeReticle ) ) return;
		var opacity = ScopeOpacity;
		if ( _scopeView != ViewModel )
		{
			_scopeView = ViewModel;
			_scopeBodyGroup = null;
			_scopeBodyChoice = -1;
			_scopeViewVisible = null;
		}
		if ( !_scopeHud.IsValid() && opacity > 0 )
		{
			_scopeHud = new GameObject( GameObject, false, "TF2 scoped sight" );
			_scopeHud.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			_scopeHud.AddComponent<ScreenPanel>().ZIndex = 110;
			_scopeHud.AddComponent<Titanfall2WeaponScopeHud>().Weapon = this;
			_scopeHud.Enabled = true;
		}
		// Retail switches to bodygroup_ads_scope_set during zoom_scope_frac_*.
		// The native UI supplies its sight image; the ordinary gold lens must not
		// occlude that view. Keep the authored animation running while hidden.
		var bodyChoice = opacity > 0 ? Presentation.ScopeBodyChoice : 0;
		if ( _scopeBodyGroup != Presentation.ScopeBodyGroup || _scopeBodyChoice != bodyChoice )
		{
			if ( ViewModel.GetComponent<Titanfall2PilotViewModel>() is { } rig && !string.IsNullOrEmpty( Presentation.ScopeBodyGroup ) )
				rig.Renderer.SetBodyGroup( Presentation.ScopeBodyGroup, bodyChoice );
			_scopeBodyGroup = Presentation.ScopeBodyGroup;
			_scopeBodyChoice = bodyChoice;
		}
		var visible = opacity < 1;
		if ( _scopeViewVisible != visible )
		{
			foreach ( var renderer in ViewModel.GetComponentsInChildren<SkinnedModelRenderer>( true ) ) renderer.RenderOptions.Overlay = visible;
			_scopeViewVisible = visible;
		}
	}

	void DestroyScopePresentation()
	{
		if ( _scopeHud.IsValid() ) _scopeHud.Destroy();
		_scopeHud = null;
		_scopeView = null;
		_scopeBodyGroup = null;
		_scopeBodyChoice = -1;
		_scopeViewVisible = null;
	}

}

/// <summary>Native scoped sight using the mounted DCOM artwork and retail zoom timing.</summary>
[Hide]
public sealed class Titanfall2WeaponScopeHud : PanelComponent
{
	[Property] public Titanfall2Weapon Weapon { get; set; }
	Image _reticle;
	string _requested;
	string _reticlePath;
	string _reticleMount;
	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( !Panel.IsValid() ) return;
		Panel.StyleSheet.AddInline( """
titanfall2weaponscopehud {
	position: absolute; width: 100%; height: 100%; pointer-events: none;
	justify-content: center; align-items: center;
	.scope-aperture {
		width: 94vh; height: 94vh; border-radius: 50%; flex-shrink: 0;
		border: 3px solid #080a0b; box-shadow: 0 0 0 4096px #000;
		image { width: 100%; height: 100%; background-size: 100% 100%; }
	}
}
""", "titanfall2:weapon-scope" );
		_reticle = Panel.Add.Panel( "scope-aperture" ).Add.Image( classname: "scope-reticle" );
		Panel.Style.Opacity = 0;
	}
	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Panel.IsValid() ) return;
		Panel.Style.Opacity = Weapon.IsValid() ? Weapon.ScopeOpacity : 0;
		if ( !Weapon.IsValid() || ! _reticle.IsValid() ) return;
		var path = Weapon.Presentation.ScopeReticle;
		var mount = Weapon.MountIdent;
		if ( path != _reticlePath || mount != _reticleMount )
		{
			_reticlePath = path;
			_reticleMount = mount;
			LoadReticle( $"mount://{mount}/{path}.vtex" );
		}
	}
	async void LoadReticle( string uri )
	{
		_requested = uri;
		try
		{
			var texture = await Texture.LoadAsync( uri );
			if ( _reticle.IsValid() && _requested == uri && texture.IsValid() && !texture.IsError ) _reticle.Texture = texture;
		}
		catch ( Exception exception ) { Titanfall2Log.Warning( $"Cannot load scoped sight '{uri}': {exception.Message}" ); }
	}
}
