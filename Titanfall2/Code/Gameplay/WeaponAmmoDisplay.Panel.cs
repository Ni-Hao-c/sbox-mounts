using Sandbox.UI;
using Sandbox.UI.Construct;

/// <summary>A native replacement for a retail ammo-counter RUI, rendered on its authored 3D surface.</summary>
[Hide]
public sealed class Titanfall2WeaponAmmoPanel : PanelComponent
{
	[Property] public Titanfall2Weapon Weapon { get; set; }
	[Property] public float CounterFontSize { get; set; }
	[Property] public Vector2 CanvasSize { get; set; }
	[Property] public Vector2 CanvasOffset { get; set; }
	[Property] public Vector2 TextAnchor { get; set; }
	[Property] public Vector2 TextPivot { get; set; }
	[Property] public int NativeFontId { get; set; }
	Titanfall2WeaponAmmoTextPanel _count;
	Panel _canvas;
	int _lastAmmo = -1;
	int _lastCapacity = -1;
	float _lastFontSize = -1;
	internal string ActualText => _count.IsValid() ? _count.DrawnText : null;
	internal Rect ActualTextFrame => _count.IsValid() ? _count.TextFrame : default;
	internal Rect ActualInkBounds => _count.IsValid() ? _count.InkBounds : default;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( !Panel.IsValid() ) return;
		_canvas = Panel.Add.Panel( "canvas" );
		_canvas.Style.Width = Length.Pixels( CanvasSize.x );
		_canvas.Style.Height = Length.Pixels( CanvasSize.y );
		_canvas.Style.Left = Length.Pixels( CanvasOffset.x );
		_canvas.Style.Top = Length.Pixels( CanvasOffset.y );
		_count = _canvas.AddChild<Titanfall2WeaponAmmoTextPanel>( "count" );
		_count.Owner = this;
		_lastAmmo = _lastCapacity = -1;
		_lastFontSize = -1;
		RefreshCounter();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		RefreshCounter();
	}

	void RefreshCounter()
	{
		if ( !Panel.IsValid() || !_count.IsValid() || !Weapon.IsValid() ) return;
		var ammo = Math.Max( 0, Weapon.Clip1 );
		var capacity = Math.Max( 1, Weapon.ClipMaxSize );
		if ( ammo == _lastAmmo && capacity == _lastCapacity && _lastFontSize == CounterFontSize ) return;
		_lastAmmo = ammo;
		_lastCapacity = capacity;
		_lastFontSize = CounterFontSize;
		_count.Text = AmmoText( ammo );
		var ratio = ammo / (float)capacity;
		Panel.SetClass( "medium", ratio > 0.33f && ratio < 0.6f );
		Panel.SetClass( "low", ratio <= 0.33f );
	}

	// Compiled retail r101_ammo_counter/car_smg_ammo_counter use "%i".
	internal static string AmmoText( int rounds ) => Math.Max( 0, rounds ).ToString( System.Globalization.CultureInfo.InvariantCulture );
}

/// <summary>Draws original proportional font glyphs in the compiled RUI text frame.</summary>
internal sealed class Titanfall2WeaponAmmoTextPanel : Panel
{
	internal Titanfall2WeaponAmmoPanel Owner;
	internal string Text = "0";
	internal string DrawnText { get; private set; }
	internal Rect TextFrame { get; private set; }
	internal Rect InkBounds { get; private set; }

	public override void OnDraw( Painter painter )
	{
		DrawnText = null;
		// These getters only consult published caches. Resource discovery and
		// glyph coverage preparation belong to the mount's single worker.
		if ( !Owner.IsValid() || !Owner.Weapon.IsValid()
			|| Sandbox.Mounting.Directory.Get( Owner.Weapon.MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount
			|| !mount.TryGetWeaponFont( Owner.NativeFontId, out var font, out _ ) ) return;
		var size = Owner.CounterFontSize;
		var width = 0f;
		var previous = '\0';
		foreach ( var character in Text )
		{
			if ( !font.TryGetGlyph( character, size, out var glyph ) ) return;
			if ( previous != '\0' ) width += font.GetKerning( previous, character ) * size;
			width += glyph.Advance * size;
			previous = character;
		}
		var topLeft = Owner.TextAnchor - new Vector2( width, size ) * Owner.TextPivot;
		TextFrame = new Rect( topLeft, new Vector2( width, size ) );
		var baseline = topLeft + new Vector2( 0, font.Ascent * size );
		var ratio = Math.Max( 0, Owner.Weapon.Clip1 ) / (float)Math.Max( 1, Owner.Weapon.ClipMaxSize );
		var color = ratio >= 0.6f ? new Color( 135 / 255f, 235 / 255f, 1 )
			: ratio > 0.33f ? new Color( 1, 128 / 255f, 64 / 255f ) : new Color( 1, 72 / 255f, 64 / 255f );
		previous = '\0';
		var ink = default( Rect );
		foreach ( var character in Text )
		{
			if ( !font.TryGetGlyph( character, size, out var glyph ) ) return;
			if ( previous != '\0' ) baseline.x += font.GetKerning( previous, character ) * size;
			var glyphRect = new Rect( baseline + glyph.Bounds.Position * size, glyph.Bounds.Size * size );
			if ( previous == '\0' ) ink = glyphRect;
			else ink.Add( glyphRect );
			painter.Texture( glyph.Texture, glyphRect, color );
			baseline.x += glyph.Advance * size;
			previous = character;
		}
		DrawnText = Text;
		InkBounds = ink;
	}
}
