using Sandbox.UI;

public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2WeaponUiDisplay[] UiDisplays { get; set; } = [];
	Titanfall2WeaponAmmoDisplay _ammoDisplay;
	internal bool HasLocalFirstPersonHud => Active && !Application.IsDedicatedServer && !Application.IsHeadless
		&& !IsProxy && IsHeld && IsActive && Owner.IsValid() && Owner is { IsProxy: false, ThirdPerson: false, Active: true }
		&& Inventory.IsValid() && ViewModel.IsValid() && ViewModel.Enabled;
	internal float HudZoom => _adsBlend;
	internal int CrosshairDrawSerial { get; private set; }
	internal float CrosshairArmOpacity { get; private set; }
	internal float CrosshairAdsDotOpacity { get; private set; }
	internal int AmmoScreenCount => _ammoDisplay.IsValid() ? _ammoDisplay.ScreenCount : 0;
	internal Titanfall2WeaponAmmoObservation[] GetAmmoScreenObservations() => _ammoDisplay.IsValid() ? _ammoDisplay.ObserveScreens() : [];

	void UpdateWeaponHud()
	{
		if ( !HasLocalFirstPersonHud )
		{
			if ( _ammoDisplay.IsValid() ) _ammoDisplay.SetVisible( false );
			return;
		}
		if ( !_ammoDisplay.IsValid() || _ammoDisplay.GameObject != ViewModel )
		{
			DestroyWeaponHud();
			_ammoDisplay = ViewModel.GetOrAddComponent<Titanfall2WeaponAmmoDisplay>();
			_ammoDisplay.Weapon = this;
		}
		_ammoDisplay.SetVisible( ScopeOpacity < 1 );
	}

	void DestroyWeaponHud()
	{
		if ( _ammoDisplay.IsValid() ) _ammoDisplay.Destroy();
		_ammoDisplay = null;
	}

	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		CrosshairArmOpacity = CrosshairAdsDotOpacity = 0;
		if ( !HasLocalFirstPersonHud || IsSprinting || IsReloading || ScopeOpacity > 0 ) return;
		// Full-screen retail scopes provide their own sight. Other ADS views retain
		// a small aim point until the corresponding compiled optic RUI is supported.
		var opacity = Math.Clamp( 1 - _adsBlend / 0.8f, 0, 1 );
		var dotOpacity = Math.Clamp( (_adsBlend - 0.15f) / 0.65f, 0, 1 );
		var camera = Scene.Camera;
		var size = camera.IsValid() ? camera.ScreenRect.Size : new Vector2( 1920, 1080 );
		var scale = Math.Clamp( size.y / 1080f, 0.5f, 2.5f );
		var cone = MathF.Max( MathF.Abs( CurrentSpread.x ), MathF.Abs( CurrentSpread.y ) );
		var fov = float.Lerp( camera.IsValid() ? camera.FieldOfView : 90f, AdsFov, _adsBlend );
		var radius = CrosshairRadius( cone, fov, size.x, scale );
		var arm = 6 * scale;
		var thickness = 1.4f * scale;
		using var state = painter.Scope();
		painter.Stroke = Stroke.None;
		painter.Opacity = opacity;
		var color = HasRoundAmmo() ? Color.White : new Color( 1, 0.3f, 0.2f );
		if ( opacity > 0 )
		{
			DrawCrosshairBar( painter, new Rect( center.x - radius - arm, center.y - thickness * 0.5f, arm, thickness ), color, scale );
			DrawCrosshairBar( painter, new Rect( center.x + radius, center.y - thickness * 0.5f, arm, thickness ), color, scale );
			DrawCrosshairBar( painter, new Rect( center.x - thickness * 0.5f, center.y - radius - arm, thickness, arm ), color, scale );
			DrawCrosshairBar( painter, new Rect( center.x - thickness * 0.5f, center.y + radius, thickness, arm ), color, scale );
		}
		if ( dotOpacity > 0 )
		{
			painter.Opacity = dotOpacity;
			painter.Fill = Color.Black.WithAlpha( 0.8f );
			painter.Circle( center, 2.25f * scale );
			painter.Fill = color;
			painter.Circle( center, 1.25f * scale );
		}
		CrosshairArmOpacity = opacity;
		CrosshairAdsDotOpacity = dotOpacity;
		CrosshairDrawSerial++;
	}

	static void DrawCrosshairBar( Painter painter, Rect rect, Color color, float scale )
	{
		painter.Fill = Color.Black.WithAlpha( 0.8f );
		painter.Rect( new Rect( rect.Left - scale, rect.Top - scale, rect.Width + 2 * scale, rect.Height + 2 * scale ) );
		painter.Fill = color;
		painter.Rect( rect );
	}

	internal static float CrosshairRadius( float coneDegrees, float fov, float width, float scale )
	{
		if ( !float.IsFinite( coneDegrees ) || !float.IsFinite( fov ) || !float.IsFinite( width ) || !float.IsFinite( scale ) ) return 4;
		var pixels = MathF.Tan( Math.Clamp( coneDegrees, 0, 45 ) * MathF.PI / 180 )
			/ MathF.Tan( Math.Clamp( fov, 15, 150 ) * MathF.PI / 360 ) * MathF.Max( 1, width ) * 0.5f;
		return Math.Clamp( pixels, 4 * scale, 35 * scale );
	}
}
