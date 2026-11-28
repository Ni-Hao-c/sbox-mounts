using Sandbox.UI;
using Sandbox.UI.Construct;

/// <summary>A native replacement for a retail ammo-counter RUI, rendered on its authored 3D surface.</summary>
[Hide]
public sealed class Titanfall2WeaponAmmoPanel : Titanfall2MountPanelComponent
{
	[Property] public Titanfall2Weapon Weapon { get; set; }
	[Property] public float CounterFontSize { get; set; }
	[Property] public Vector2 CanvasSize { get; set; }
	[Property] public Vector2 CanvasOffset { get; set; }
	[Property] public Vector2 TextAnchor { get; set; }
	[Property] public Vector2 TextPivot { get; set; }
	[Property] public int NativeFontId { get; set; }
	internal Titanfall2WeaponUiLayout DisplayLayout { get; set; }
	internal bool FadeWithZoom { get; set; }
	internal (Titanfall2WeaponUiImageSpec Spec, Texture Texture)[] Artwork { get; private set; } = [];
	internal bool HasOpticArtwork => Artwork.Length > 0 && Artwork.All( image => image.Texture is { IsValid: true, IsError: false } );
	internal bool ArtworkPending { get; private set; }
	internal string ArtworkError { get; private set; }
	int _artworkGeneration;
	internal async Task LoadArtworkAsync( string mountIdent )
	{
		var generation = ++_artworkGeneration;
		Artwork = [];
		ArtworkPending = true;
		ArtworkError = null;
		var images = DisplayLayout?.Images ?? [];
		try
		{
			// The engine queues mounted resource creation on its main thread and
			// coalesces identical loads. Never decode the atlas from OnDraw.
			var textures = await Task.WhenAll( images.Select( image => image.TexturePath == "white"
				? Task.FromResult( Texture.White ) : Texture.LoadAsync( $"mount://{mountIdent}/rui_full/{image.TexturePath}.vtex", false ) ) );
			if ( !this.IsValid() || generation != _artworkGeneration ) return;
			Artwork = images.Select( (image, index) => (image, textures[index]) ).ToArray();
			if ( Artwork.Any( image => image.Texture is not { IsValid: true, IsError: false } ) )
				ArtworkError = "Optic image resource missing: " + string.Join( ", ", Artwork.Where( image => image.Texture is not { IsValid: true, IsError: false } ).Select( image => image.Spec.TexturePath ) );
		}
		catch ( Exception error )
		{
			if ( generation == _artworkGeneration ) ArtworkError = error.Message;
			Titanfall2Log.Warning( $"Weapon optic artwork unavailable for '{DisplayLayout?.Name}': {error.Message}" );
		}
		finally { if ( generation == _artworkGeneration ) ArtworkPending = false; }
	}
	Titanfall2WeaponAmmoTextPanel _count;
	Panel _canvas;
	int _lastAmmo = -1;
	int _lastCapacity = -1;
	float _lastFontSize = -1;
	float _lastCharge = -1;
	bool _lastVenting;
	float _canvasScale;
	internal string ActualText => _count.IsValid() ? _count.DrawnText : null;
	internal Rect ActualTextFrame => _count.IsValid() ? _count.TextFrame : default;
	internal Rect ActualInkBounds => _count.IsValid() ? _count.InkBounds : default;
	internal Vector2 PhysicalCanvasSize => _canvas.IsValid() ? _canvas.Box.Rect.Size : default;
	internal Vector2 PhysicalCanvasOffset => _canvas.IsValid() && Panel.IsValid() ? _canvas.Box.Rect.Position - Panel.Box.Rect.Position : default;
	internal object GetPanelDiagnostics() => new
	{
		Root = Panel?.Box.Rect, Canvas = _canvas?.Box.Rect, Content = _count?.Box.Rect,
		CanvasOffset, CanvasSize, TextAnchor, ActualText, ActualInkBounds,
		Opacity = _canvas?.ComputedStyle?.Opacity
	};

	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( !Panel.IsValid() ) return;
		// These dimensions are correctness requirements, even when an installation
		// has omitted this component's optional stylesheet.
		Panel.Style.Width = Length.Percent( 100 );
		Panel.Style.Height = Length.Percent( 100 );
		Panel.Style.Overflow = OverflowMode.Hidden;
		_canvas = Panel.Add.Panel( "canvas" );
		// A 128-pixel UV window must not flex-shrink the 256-pixel source canvas.
		// Otherwise the right-hand ammo text is laid out outside its drawable box.
		_canvas.Style.Position = PositionMode.Absolute;
		_canvas.Style.FlexShrink = 0;
		_count = _canvas.AddChild<Titanfall2WeaponAmmoTextPanel>( "count" );
		_count.Owner = this;
		_count.Style.Position = PositionMode.Absolute;
		_count.Style.Left = 0;
		_count.Style.Top = 0;
		_count.Style.Width = Length.Percent( 100 );
		_count.Style.Height = Length.Percent( 100 );
		_lastAmmo = _lastCapacity = -1;
		_lastFontSize = -1;
		_lastCharge = -1;
		_canvasScale = -1;
		RefreshCounter();
		// Sandboxed component async calls are tied to their enabled lifetime.
		// BuildSurfaces configures us while disabled, so starting there cancels
		// even an otherwise-ready texture request before it can publish artwork.
		_ = LoadArtworkAsync( Weapon?.MountIdent ?? "titanfall2" );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		RefreshCounter();
	}

	void RefreshCounter()
	{
		if ( !Panel.IsValid() || !_count.IsValid() || !Weapon.IsValid() ) return;
		if ( _canvasScale != Panel.ScaleToScreen )
		{
			_canvasScale = Panel.ScaleToScreen;
			// WorldPanel defaults to a 2x CSS scale, but MDL UV windows and Painter
			// commands are physical panel pixels. Convert only the CSS lengths.
			var inverseScale = Panel.ScaleFromScreen;
			_canvas.Style.Width = Length.Pixels( CanvasSize.x * inverseScale );
			_canvas.Style.Height = Length.Pixels( CanvasSize.y * inverseScale );
			_canvas.Style.Left = Length.Pixels( CanvasOffset.x * inverseScale );
			_canvas.Style.Top = Length.Pixels( CanvasOffset.y * inverseScale );
		}
		_canvas.Style.Opacity = FadeWithZoom ? Math.Clamp( Weapon.HudZoom, 0f, 1f ) : 1f;
		var ammo = Weapon.DisplayAmmo;
		var capacity = Math.Max( 1, Weapon.DisplayAmmoCapacity );
		var charge = Weapon.ChargeProfile.Valid ? Weapon.ChargeFraction : 0f;
		var venting = Weapon.HeatVenting;
		if ( ammo == _lastAmmo && capacity == _lastCapacity && _lastFontSize == CounterFontSize
			&& MathF.Abs( charge - _lastCharge ) < 0.005f && venting == _lastVenting ) return;
		_lastAmmo = ammo;
		_lastCapacity = capacity;
		_lastFontSize = CounterFontSize;
		_lastCharge = charge;
		_lastVenting = venting;
		_count.Text = AmmoText( ammo );
		var ratio = ammo / (float)capacity;
		Panel.SetClass( "medium", ratio > 0.33f && ratio < 0.6f );
		Panel.SetClass( "low", ratio <= 0.33f );
	}

	// Compiled retail r101_ammo_counter/car_smg_ammo_counter use "%i".
	internal static string AmmoText( int rounds ) => Math.Max( 0, rounds ).ToString( System.Globalization.CultureInfo.InvariantCulture );
}

/// <summary>Draws retail proportional font glyphs and bar/heat/charge gauges on the compiled RUI canvas.</summary>
internal sealed class Titanfall2WeaponAmmoTextPanel : Panel
{
	static readonly Color CyanNormal = new( 135 / 255f, 235 / 255f, 1f );
	static readonly Color AmberMedium = new( 1f, 152 / 255f, 56 / 255f );
	static readonly Color RedCritical = new( 1f, 72 / 255f, 64 / 255f );
	static readonly Color TrackBackground = new( 0.03f, 0.07f, 0.1f, 0.55f );

	internal Titanfall2WeaponAmmoPanel Owner;
	internal string Text = "0";
	internal string DrawnText { get; private set; }
	internal Rect TextFrame { get; private set; }
	internal Rect InkBounds { get; private set; }

	public override void OnDraw( Painter painter )
	{
		DrawnText = null;
		if ( !Owner.IsValid() || !Owner.Weapon.IsValid() ) return;

		var weapon = Owner.Weapon;
		var ammo = Math.Max( 0, weapon.DisplayAmmo );
		var capacity = Math.Max( 1, weapon.DisplayAmmoCapacity );
		var ratio = Math.Clamp( ammo / (float)capacity, 0f, 1f );
		var layout = Owner.DisplayLayout;
		var mode = layout?.Mode ?? Titanfall2WeaponUiDisplayMode.Numeric;
		var color = SelectAmmoColor( ratio );

		foreach ( var image in Owner.Artwork )
		{
			if ( image.Texture is not { IsValid: true, IsError: false } ) continue;
			var tint = image.Spec.AmmoColorScale > 0
				? (SelectOpticAmmoColor( ratio ) * image.Spec.AmmoColorScale).WithAlpha( image.Spec.Tint.a ) : image.Spec.Tint;
			var transform = painter.Transform;
			var rect = image.Spec.Rect;
			if ( image.Spec.FlipX || image.Spec.FlipY )
			{
				painter.Translate( rect.Center );
				painter.Scale( image.Spec.FlipX ? -1 : 1, image.Spec.FlipY ? -1 : 1 );
				rect = new( -rect.Size * .5f, rect.Size );
			}
			painter.Texture( image.Texture, rect, LinearToPainterColor( tint ) );
			painter.Transform = transform;
		}
		if ( mode == Titanfall2WeaponUiDisplayMode.OpticFrame ) return;

		if ( mode == Titanfall2WeaponUiDisplayMode.HeatScreen )
		{
			DrawHeatScreen( painter, weapon, layout, ratio );
			return;
		}
		if ( mode == Titanfall2WeaponUiDisplayMode.ChargeStack )
		{
			DrawChargeStack( painter, weapon, layout, ratio, color );
			return;
		}

		// Draw bar components first so numeric text remains legible on top.
		if ( layout?.PrimaryBar is { } primary )
		{
			if ( mode == Titanfall2WeaponUiDisplayMode.DualBar && layout.SecondaryBar is { } secondary )
			{
				// Alternator splits alternating barrels across left/right twin bars; other dual bars mirror ammo.
				var isAlternator = string.Equals( layout.Name, "ui/alternator_ammo_counter", StringComparison.OrdinalIgnoreCase );
				var leftAmmo = isAlternator ? (ammo + 1) / 2 : ammo;
				var leftCap = isAlternator ? Math.Max( 1, (capacity + 1) / 2 ) : capacity;
				var rightAmmo = isAlternator ? ammo / 2 : ammo;
				var rightCap = isAlternator ? Math.Max( 1, capacity / 2 ) : capacity;
				DrawBar( painter, primary, leftAmmo, leftCap, color );
				DrawBar( painter, secondary, rightAmmo, rightCap, color );
			}
			else
			{
				DrawBar( painter, primary, ammo, capacity, color );
				if ( layout.SecondaryBar is { } secondaryBar )
					DrawBar( painter, secondaryBar, ammo, capacity, color );
			}
		}

		var hasNumeric = mode is Titanfall2WeaponUiDisplayMode.Numeric or Titanfall2WeaponUiDisplayMode.NumericWithBar
			or Titanfall2WeaponUiDisplayMode.DualNumeric or Titanfall2WeaponUiDisplayMode.OpticReticle;
		if ( !hasNumeric || Owner.CounterFontSize <= 0 )
		{
			DrawnText = Text;
			return;
		}

		if ( Sandbox.Mounting.Directory.Get( weapon.MountIdent ?? "titanfall2" ) is not Titanfall2Mount mount
			|| !mount.TryGetWeaponFont( Owner.NativeFontId, out var font, out _ ) ) return;

		if ( layout?.Name == "ui/hcog_lower" ) color = LinearToPainterColor( (SelectOpticAmmoColor( ratio ) * 8f).WithAlpha( 1 ) );
		else if ( layout?.Name == "ui/holo_scope" ) color = LinearToPainterColor( (SelectOpticAmmoColor( ratio ) * 4f).WithAlpha( 1 ) );
		DrawDigits( painter, font, Text, Owner.TextAnchor, Owner.TextPivot, Owner.CounterFontSize, color, recordMetrics: true );
		if ( mode == Titanfall2WeaponUiDisplayMode.DualNumeric && layout?.SecondaryTextAnchor is { } secondAnchor )
			DrawDigits( painter, font, Text, secondAnchor, Owner.TextPivot, Owner.CounterFontSize, color, recordMetrics: false );
	}

	void DrawHeatScreen( Painter painter, Titanfall2Weapon weapon, Titanfall2WeaponUiLayout layout, float coolRatio )
	{
		var heat = 1f - coolRatio;
		var overheating = weapon.HeatVenting || heat >= 0.99f;
		var color = overheating ? RedCritical : heat >= 0.7f ? RedCritical : heat >= 0.4f ? AmberMedium : CyanNormal;
		if ( layout?.PrimaryBar is { } mainRect )
		{
			DrawContinuousBar( painter, mainRect.Rect, Math.Clamp( heat, 0.04f, 1f ), false, color, withFrame: true );
		}
		if ( layout?.SecondaryBar is { } stripRect )
		{
			DrawContinuousBar( painter, stripRect.Rect, Math.Clamp( heat, 0.04f, 1f ), false, color, withFrame: false );
		}

		var heatPercent = Math.Clamp( (int)MathF.Round( heat * 99f ), 0, 99 );
		var label = AmmoText( heatPercent );
		if ( Owner.NativeFontId > 0 && Owner.CounterFontSize > 0
			&& Sandbox.Mounting.Directory.Get( weapon.MountIdent ?? "titanfall2" ) is Titanfall2Mount mount
			&& mount.TryGetWeaponFont( Owner.NativeFontId, out var font, out _ ) )
		{
			DrawDigits( painter, font, label, Owner.TextAnchor, Owner.TextPivot, Owner.CounterFontSize, color, recordMetrics: true );
		}
		else
		{
			DrawnText = label;
		}
	}

	void DrawChargeStack( Painter painter, Titanfall2Weapon weapon, Titanfall2WeaponUiLayout layout, float ammoRatio, Color ammoColor )
	{
		var charge = weapon.ChargeProfile.Valid ? Math.Clamp( weapon.ChargeFraction, 0f, 1f ) : ammoRatio;
		if ( layout?.PrimaryBar is { } chargeBar )
		{
			var chargeColor = weapon.ChargeProfile.Valid && charge >= 0.75f ? AmberMedium : ammoColor;
			var lit = charge <= 0f ? 0 : (int)MathF.Ceiling( charge * chargeBar.Segments );
			DrawSegmentedBar( painter, chargeBar.Rect, chargeBar.Segments, 1, chargeBar.Vertical, lit, chargeBar.Segments, chargeColor );
		}
		if ( layout?.SecondaryBar is { } ammoBar )
		{
			var ammo = Math.Max( 0, weapon.DisplayAmmo );
			var capacity = Math.Max( 1, weapon.DisplayAmmoCapacity );
			DrawBar( painter, ammoBar, ammo, capacity, ammoColor );
		}
		DrawnText = Text;
	}

	void DrawDigits( Painter painter, Titanfall2WeaponFont font, string digits, Vector2 anchor, Vector2 pivot, float size, Color color, bool recordMetrics )
	{
		var widthSize = size * (Owner.DisplayLayout?.TextWidthScale ?? 1f);
		var glyphSize = new Vector2( widthSize, size );
		var width = 0f;
		var previous = '\0';
		foreach ( var character in digits )
		{
			if ( !font.TryGetGlyph( character, size, out var glyph ) ) return;
			if ( previous != '\0' ) width += font.GetKerning( previous, character ) * widthSize;
			width += glyph.Advance * widthSize;
			previous = character;
		}
		var topLeft = anchor - new Vector2( width, size ) * pivot;
		var baseline = topLeft + new Vector2( 0, font.Ascent * size );
		previous = '\0';
		var ink = default( Rect );
		foreach ( var character in digits )
		{
			if ( !font.TryGetGlyph( character, size, out var glyph ) ) return;
			if ( previous != '\0' ) baseline.x += font.GetKerning( previous, character ) * widthSize;
			var glyphRect = new Rect( baseline + glyph.Bounds.Position * glyphSize, glyph.Bounds.Size * glyphSize );
			if ( previous == '\0' ) ink = glyphRect;
			else ink.Add( glyphRect );
			painter.Texture( glyph.Texture, glyphRect, color );
			baseline.x += glyph.Advance * widthSize;
			previous = character;
		}
		if ( recordMetrics )
		{
			TextFrame = new Rect( topLeft, new Vector2( width, size ) );
			DrawnText = digits;
			InkBounds = ink;
		}
	}

	static void DrawBar( Painter painter, Titanfall2WeaponUiBarSpec spec, int ammo, int capacity, Color color )
	{
		var ratio = Math.Clamp( ammo / (float)Math.Max( 1, capacity ), 0f, 1f );
		if ( spec.Segments <= 0 )
		{
			DrawContinuousBar( painter, spec.Rect, ratio, spec.Vertical, color, withFrame: true );
			return;
		}
		var cellCount = Math.Clamp( spec.Rows > 1 ? Math.Min( spec.Segments, Math.Max( capacity, spec.Rows ) ) : Math.Min( spec.Segments, capacity ), 1, 80 );
		var litCount = (int)MathF.Ceiling( ratio * cellCount );
		if ( ammo == 0 ) litCount = 0;
		DrawSegmentedBar( painter, spec.Rect, cellCount, Math.Max( 1, spec.Rows ), spec.Vertical, litCount, cellCount, color );
	}

	static void DrawContinuousBar( Painter painter, Rect rect, float ratio, bool vertical, Color color, bool withFrame )
	{
		painter.Stroke = Stroke.None;
		painter.Fill = TrackBackground;
		painter.Rect( rect );
		if ( ratio > 0f )
		{
			var pad = MathF.Min( 2f, MathF.Min( rect.Width, rect.Height ) * 0.1f );
			var inner = new Rect( rect.Left + pad, rect.Top + pad, MathF.Max( 1f, rect.Width - pad * 2 ), MathF.Max( 1f, rect.Height - pad * 2 ) );
			var fill = vertical
				? new Rect( inner.Left, inner.Bottom - inner.Height * ratio, inner.Width, inner.Height * ratio )
				: new Rect( inner.Left, inner.Top, inner.Width * ratio, inner.Height );
			painter.Fill = color;
			painter.Rect( fill );
		}
		if ( withFrame )
		{
			var border = MathF.Max( 1f, MathF.Min( rect.Width, rect.Height ) * 0.04f );
			painter.Fill = color.WithAlpha( 0.35f );
			painter.Rect( new Rect( rect.Left, rect.Top, rect.Width, border ) );
			painter.Rect( new Rect( rect.Left, rect.Bottom - border, rect.Width, border ) );
		}
	}

	static void DrawSegmentedBar( Painter painter, Rect rect, int totalCells, int rows, bool vertical, int litCount, int maxCells, Color color )
	{
		painter.Stroke = Stroke.None;
		painter.Fill = TrackBackground;
		painter.Rect( rect );
		var pad = MathF.Min( 3f, MathF.Min( rect.Width, rect.Height ) * 0.06f );
		var inner = new Rect( rect.Left + pad, rect.Top + pad, MathF.Max( 1f, rect.Width - pad * 2 ), MathF.Max( 1f, rect.Height - pad * 2 ) );
		var cols = Math.Max( 1, (int)MathF.Ceiling( totalCells / (float)rows ) );
		var gapX = MathF.Min( 8f, inner.Width / Math.Max( 1, cols ) * 0.22f );
		var gapY = MathF.Min( 6f, inner.Height / Math.Max( 1, rows ) * 0.22f );
		var cellW = MathF.Max( 1f, (inner.Width - gapX * (cols - 1)) / cols );
		var cellH = MathF.Max( 1f, (inner.Height - gapY * (rows - 1)) / rows );
		var dimColor = color.WithAlpha( 0.16f );

		if ( vertical && rows == 1 )
		{
			var stepH = MathF.Max( 1f, (inner.Height - gapY * (totalCells - 1)) / totalCells );
			for ( var i = 0; i < totalCells; ++i )
			{
				var y = inner.Bottom - (i + 1) * stepH - i * gapY;
				painter.Fill = i < litCount ? color : dimColor;
				painter.Rect( new Rect( inner.Left, y, inner.Width, stepH ) );
			}
			return;
		}

		for ( var i = 0; i < totalCells; ++i )
		{
			var r = i / cols;
			var c = i % cols;
			var x = inner.Left + c * (cellW + gapX);
			var y = inner.Top + r * (cellH + gapY);
			painter.Fill = i < litCount ? color : dimColor;
			painter.Rect( new Rect( x, y, cellW, cellH ) );
		}
	}

	static Color SelectAmmoColor( float ratio ) => ratio >= 0.6f ? CyanNormal : ratio > 0.33f ? AmberMedium : RedCritical;
	// Retail RUI exports already supply linear RGB. Painter/UI decodes its input
	// from sRGB, including HDR values: passing retail 20 directly would become
	// about 1174 in the UI shader. Encode once here; preserve intensity and alpha.
	internal static Color LinearToPainterColor( Color linear )
	{
		static float Encode( float value ) => value <= .0031308f ? value * 12.92f
			: 1.055f * MathF.Pow( value, 1f / 2.4f ) - .055f;
		return new( Encode( linear.r ), Encode( linear.g ), Encode( linear.b ), linear.a );
	}
	// Linear RGB constants from ui(11).dll, shared by its ammo-sensitive optics.
	static Color SelectOpticAmmoColor( float ratio ) => ratio >= .6f ? new( .24228145f, .83077067f, 1f )
		: ratio > .33f ? new( 1f, .21586075f, .05126942f ) : new( 1f, .06480303f, .05126942f );
	static string AmmoText( int value ) => Math.Max( 0, value ).ToString( System.Globalization.CultureInfo.InvariantCulture );
}
