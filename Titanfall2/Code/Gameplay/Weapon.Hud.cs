using Sandbox.UI;

public sealed partial class Titanfall2Weapon
{
	static readonly Color CrosshairShadow = Color.Black.WithAlpha( 0.78f );
	static readonly Color CrosshairLowAmmo = new( 1f, 0.3f, 0.2f );
	static readonly Color CrosshairCyan = new( 135 / 255f, 235 / 255f, 1f );
	static readonly Color CrosshairAmber = new( 1f, 165 / 255f, 56 / 255f );
	static readonly Color CrosshairRed = new( 1f, 64 / 255f, 56 / 255f );
	static readonly Color CrosshairGreen = new( 96 / 255f, 1f, 132 / 255f );

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

		var opacity = Math.Clamp( 1 - _adsBlend / 0.8f, 0, 1 );
		var dotOpacity = Math.Clamp( (_adsBlend - 0.15f) / 0.65f, 0, 1 );
		var camera = Scene.Camera;
		var size = camera.IsValid() ? camera.ScreenRect.Size : new Vector2( 1920, 1080 );
		var scale = Math.Clamp( size.y / 1080f, 0.5f, 2.5f );
		var cone = MathF.Max( MathF.Abs( CurrentSpread.x ), MathF.Abs( CurrentSpread.y ) );
		var fov = float.Lerp( camera.IsValid() ? camera.FieldOfView : 90f, AdsFov, _adsBlend );
		var radius = CrosshairRadius( cone, fov, size.x, scale );
		var color = HasRoundAmmo() ? Color.White : CrosshairLowAmmo;

		using var state = painter.Scope();
		painter.Stroke = Stroke.None;

		if ( opacity > 0 )
		{
			painter.Opacity = opacity;
			DrawHipCrosshair( painter, center, radius, scale, color, Presentation.CrosshairStyle, fov, size );
		}

		if ( UsesRetailSmartAmmo )
		{
			painter.Opacity = 1f;
			DrawSmartPistolOverlay( painter, center, scale, fov, size, camera );
		}

		if ( dotOpacity > 0 )
		{
			painter.Opacity = dotOpacity;
			DrawAdsOpticReticle( painter, center, scale, color, Presentation.OpticReticle );
		}

		CrosshairArmOpacity = opacity;
		CrosshairAdsDotOpacity = dotOpacity;
		CrosshairDrawSerial++;
	}

	void DrawHipCrosshair( Painter painter, Vector2 center, float radius, float scale, Color color,
		Titanfall2CrosshairStyle style, float fov, Vector2 screenSize )
	{
		var arm = 7f * scale;
		var thick = 1.6f * scale;
		switch ( style )
		{
			case Titanfall2CrosshairStyle.Tri:
				// R-201, CAR, R-97, G2A5, R-101, Hemlok: left, right, bottom ticks + center dot.
				DrawCrosshairBar( painter, new Rect( center.x - radius - arm, center.y - thick * 0.5f, arm, thick ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x + radius, center.y - thick * 0.5f, arm, thick ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x - thick * 0.5f, center.y + radius, thick, arm ), color, scale );
				DrawCrosshairDot( painter, center, 1.3f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.Plus:
				// P2016, RE-45, Spitfire, Devotion: classic 4-arm cross + center dot.
				DrawCrosshairBar( painter, new Rect( center.x - radius - arm, center.y - thick * 0.5f, arm, thick ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x + radius, center.y - thick * 0.5f, arm, thick ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x - thick * 0.5f, center.y - radius - arm, thick, arm ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x - thick * 0.5f, center.y + radius, thick, arm ), color, scale );
				DrawCrosshairDot( painter, center, 1.3f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.Alternator:
				// Alternator, Volt, Flatline: wide horizontal left/right T-bars + center dot.
				var wideArm = 11f * scale;
				var capH = 5f * scale;
				DrawCrosshairBar( painter, new Rect( center.x - radius - wideArm, center.y - thick * 0.5f, wideArm, thick ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x + radius, center.y - thick * 0.5f, wideArm, thick ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x - radius - wideArm, center.y - capH * 0.5f, thick, capH ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x + radius + wideArm - thick, center.y - capH * 0.5f, thick, capH ), color, scale );
				DrawCrosshairDot( painter, center, 1.5f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.Sniper:
				// Kraber, Longbow-DMR, Double Take, Archer, Thunderbolt: thin outer sniper ticks + subtle center dot.
				var sniperGap = MathF.Max( radius * 1.25f, 12f * scale );
				var sniperArm = 6f * scale;
				var sniperThin = 1.2f * scale;
				DrawCrosshairBar( painter, new Rect( center.x - sniperGap - sniperArm, center.y - sniperThin * 0.5f, sniperArm, sniperThin ), color.WithAlpha( 0.85f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x + sniperGap, center.y - sniperThin * 0.5f, sniperArm, sniperThin ), color.WithAlpha( 0.85f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x - sniperThin * 0.5f, center.y - sniperGap - sniperArm, sniperThin, sniperArm ), color.WithAlpha( 0.85f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x - sniperThin * 0.5f, center.y + sniperGap, sniperThin, sniperArm ), color.WithAlpha( 0.85f ), scale );
				DrawCrosshairDot( painter, center, 1.1f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.ChargeRifle:
				// Charge Rifle & Cold War: center diamond + dual left/right charge gauge bars.
				var charge = ChargeProfile.Valid ? Math.Clamp( ChargeFraction, 0f, 1f ) : 0f;
				var gaugeX = MathF.Max( radius, 16f * scale );
				var gaugeH = 26f * scale;
				var gaugeW = 3f * scale;
				var gaugeColor = charge >= 0.85f ? CrosshairRed : charge > 0.05f ? CrosshairAmber : CrosshairCyan;
				DrawCrosshairBracket( painter, center + new Vector2( -gaugeX, 0 ), 5f * scale, gaugeH, leftSide: true, thick, color, scale );
				DrawCrosshairBracket( painter, center + new Vector2( gaugeX, 0 ), 5f * scale, gaugeH, leftSide: false, thick, color, scale );
				if ( charge > 0.01f )
				{
					var fillH = gaugeH * charge;
					painter.Fill = gaugeColor;
					painter.Rect( new Rect( center.x - gaugeX + 3f * scale, center.y + gaugeH * 0.5f - fillH, gaugeW, fillH ) );
					painter.Rect( new Rect( center.x + gaugeX - 3f * scale - gaugeW, center.y + gaugeH * 0.5f - fillH, gaugeW, fillH ) );
				}
				DrawCrosshairDiamond( painter, center, 4.5f * scale, charge > 0.05f ? gaugeColor : color, scale );
				DrawCrosshairDot( painter, center, 1.4f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.Shotgun:
				// EVA-8 Auto: wide circular 4-arc spread ring + center dot.
				var ringR = MathF.Max( radius * 1.4f, 18f * scale );
				DrawCrosshairSegmentedRing( painter, center, ringR, 4, 54f, thick * 1.1f, color, scale );
				DrawCrosshairDot( painter, center, 1.6f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.Mastiff:
				// Mastiff: horizontal wide fan brackets matching the 8-bolt pattern (halves width on ADS).
				var halfSpan = MathF.Max( radius * 1.6f, 24f * scale ) * float.Lerp( 1f, 0.5f, _adsBlend );
				var bh = 14f * scale;
				var bw = 6f * scale;
				DrawCrosshairBracket( painter, center + new Vector2( -halfSpan, 0 ), bw, bh, leftSide: true, thick * 1.1f, color, scale );
				DrawCrosshairBracket( painter, center + new Vector2( halfSpan, 0 ), bw, bh, leftSide: false, thick * 1.1f, color, scale );
				DrawCrosshairBar( painter, new Rect( center.x - halfSpan * 0.5f, center.y - thick * 0.5f, 3f * scale, thick ), color.WithAlpha( 0.7f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x + halfSpan * 0.5f - 3f * scale, center.y - thick * 0.5f, 3f * scale, thick ), color.WithAlpha( 0.7f ), scale );
				DrawCrosshairDot( painter, center, 1.7f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.Mozambique:
				// SA-3 Mozambique: 3-point triangle matching bolt offsets (0,0), (-0.2,-0.4), (-0.2,+0.4).
				var triX = MathF.Max( radius * 0.85f, 13f * scale ) * float.Lerp( 1f, 0.5f, _adsBlend );
				var triY = triX * 0.55f;
				var dotR = 2.1f * scale;
				DrawCrosshairDot( painter, center, dotR, color, scale );
				DrawCrosshairDot( painter, center + new Vector2( -triX, triY ), dotR, color, scale );
				DrawCrosshairDot( painter, center + new Vector2( triX, triY ), dotR, color, scale );
				DrawCrosshairBar( painter, new Rect( center.x - triX - 5f * scale, center.y + triY - thick * 0.5f, 3.5f * scale, thick ), color.WithAlpha( 0.8f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x + triX + 1.5f * scale, center.y + triY - thick * 0.5f, 3.5f * scale, thick ), color.WithAlpha( 0.8f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x - thick * 0.5f, center.y - 6f * scale, thick, 3.5f * scale ), color.WithAlpha( 0.8f ), scale );
				break;

			case Titanfall2CrosshairStyle.Wingman:
				// B3 Wingman: compact pistol side brackets + bottom tick + center dot.
				var wg = MathF.Max( radius, 8f * scale );
				DrawCrosshairBracket( painter, center + new Vector2( -wg, 0 ), 3.5f * scale, 10f * scale, leftSide: true, thick, color, scale );
				DrawCrosshairBracket( painter, center + new Vector2( wg, 0 ), 3.5f * scale, 10f * scale, leftSide: false, thick, color, scale );
				DrawCrosshairBar( painter, new Rect( center.x - thick * 0.5f, center.y + wg * 0.85f, thick, 4.5f * scale ), color, scale );
				DrawCrosshairDot( painter, center, 1.5f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.WingmanElite:
				// Wingman Elite: pinpoint accurate center dot + tight diamond corner ticks.
				DrawCrosshairDiamond( painter, center, 7f * scale, color.WithAlpha( 0.75f ), scale );
				DrawCrosshairDot( painter, center, 1.8f * scale, CrosshairCyan, scale );
				break;

			case Titanfall2CrosshairStyle.SmartPistol:
				// Smart Pistol center aim point (acquisition frame & locks draw in DrawSmartPistolOverlay).
				DrawCrosshairDot( painter, center, 1.6f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.Circle:
				// EPG-1: crisp energy circle + horizontal lead ticks + center dot.
				var epR = MathF.Max( radius * 0.8f, 12f * scale );
				DrawCrosshairSegmentedRing( painter, center, epR, 2, 130f, thick, color, scale );
				DrawCrosshairBar( painter, new Rect( center.x - epR - 7f * scale, center.y - thick * 0.5f, 5f * scale, thick ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x + epR + 2f * scale, center.y - thick * 0.5f, 5f * scale, thick ), color, scale );
				DrawCrosshairDot( painter, center, 1.6f * scale, CrosshairCyan, scale );
				break;

			case Titanfall2CrosshairStyle.LStar:
				// L-STAR: side curved brackets + live heat indicator gauge.
				var heat = HeatProfile.Valid ? 1f - Math.Clamp( DisplayAmmo / (float)Math.Max( 1, DisplayAmmoCapacity ), 0f, 1f ) : 0f;
				var heatColor = HeatVenting || heat >= 0.75f ? CrosshairRed : heat >= 0.45f ? CrosshairAmber : CrosshairCyan;
				var lsX = MathF.Max( radius, 14f * scale );
				var lsH = 22f * scale;
				DrawCrosshairBracket( painter, center + new Vector2( -lsX, 0 ), 5f * scale, lsH, leftSide: true, thick, color, scale );
				DrawCrosshairBracket( painter, center + new Vector2( lsX, 0 ), 5f * scale, lsH, leftSide: false, thick, color, scale );
				if ( heat > 0.02f )
				{
					var hBar = lsH * heat;
					painter.Fill = heatColor;
					painter.Rect( new Rect( center.x - lsX - 5f * scale, center.y + lsH * 0.5f - hBar, 2.5f * scale, hBar ) );
					painter.Rect( new Rect( center.x + lsX + 2.5f * scale, center.y + lsH * 0.5f - hBar, 2.5f * scale, hBar ) );
				}
				DrawCrosshairDot( painter, center, 1.5f * scale, heat > 0.45f ? heatColor : color, scale );
				break;

			case Titanfall2CrosshairStyle.Smr:
				// Sidewinder SMR: launcher corner box brackets + center dot.
				var smrHalf = MathF.Max( radius * 1.1f, 14f * scale );
				DrawCrosshairCornerBox( painter, center, smrHalf, smrHalf, 6f * scale, thick, color, scale );
				DrawCrosshairDot( painter, center, 1.6f * scale, color, scale );
				break;

			case Titanfall2CrosshairStyle.GrenadeLauncher:
			case Titanfall2CrosshairStyle.GrenadeLauncherArc:
				// MGL & Softball: side ticks + vertical stadiametric drop-ladder below center.
				var glGap = MathF.Max( radius * 0.7f, 8f * scale );
				DrawCrosshairBar( painter, new Rect( center.x - glGap - 8f * scale, center.y - thick * 0.5f, 8f * scale, thick ), color, scale );
				DrawCrosshairBar( painter, new Rect( center.x + glGap, center.y - thick * 0.5f, 8f * scale, thick ), color, scale );
				DrawCrosshairDot( painter, center, 1.5f * scale, color, scale );
				var rungs = style == Titanfall2CrosshairStyle.GrenadeLauncherArc ? 4 : 3;
				for ( var r = 1; r <= rungs; ++r )
				{
					var rungW = (18f - r * 3f) * scale;
					var rungY = center.y + (r * 11f) * scale;
					DrawCrosshairBar( painter, new Rect( center.x - rungW * 0.5f, rungY, rungW, thick * 0.9f ), color.WithAlpha( 0.85f ), scale );
				}
				break;

			case Titanfall2CrosshairStyle.Ordnance:
				DrawCrosshairSegmentedRing( painter, center, 13f * scale, 4, 48f, thick, color, scale );
				DrawCrosshairDot( painter, center, 1.5f * scale, color, scale );
				break;
		}
	}

	void DrawSmartPistolOverlay( Painter painter, Vector2 center, float scale, float fov, Vector2 screenSize, CameraComponent camera )
	{
		// Acquisition box size derived from SmartAmmo.SearchAngle in screen projection.
		var halfAngle = Math.Clamp( SmartAmmo.SearchAngle * 0.5f, 4f, 35f ) * MathF.PI / 180f;
		var tanFov = MathF.Tan( Math.Clamp( fov, 15f, 140f ) * MathF.PI / 360f );
		var boxHalf = Math.Clamp( MathF.Tan( halfAngle ) / MathF.Max( 0.1f, tanFov ) * screenSize.y * 0.42f, 52f * scale, screenSize.y * 0.34f );
		var boxHalfW = boxHalf * 1.15f;
		var boxHalfH = boxHalf * 0.88f;
		var targets = GetSmartLockDrawTargets();
		var hasFullLock = targets.Any( t => t.Fraction >= 1f );
		var hasAcquiring = targets.Count > 0;
		var frameColor = hasFullLock ? CrosshairRed : hasAcquiring ? CrosshairAmber : Color.White.WithAlpha( 0.72f );
		var cornerLen = 18f * scale;
		var thick = 2.2f * scale;

		DrawCrosshairCornerBox( painter, center, boxHalfW, boxHalfH, cornerLen, thick, frameColor, scale );

		// Draw total ready burst pips under the acquisition box.
		var ready = SmartReadyShots;
		if ( ready > 0 )
		{
			var pipW = 7f * scale;
			var pipH = 3.5f * scale;
			var pipGap = 3f * scale;
			var totalW = ready * pipW + (ready - 1) * pipGap;
			var startX = center.x - totalW * 0.5f;
			var pipY = center.y + boxHalfH + 8f * scale;
			for ( var i = 0; i < ready; ++i )
				DrawCrosshairBar( painter, new Rect( startX + i * (pipW + pipGap), pipY, pipW, pipH ), CrosshairRed, scale );
		}

		if ( !camera.IsValid() || targets.Count == 0 ) return;
		foreach ( var target in targets )
		{
			var screenPos = camera.PointToScreenPixels( target.AimPoint, out var isBehind );
			if ( isBehind || !screenPos.IsFinite ) continue;
			var locked = target.Fraction >= 1f;
			var locks = (int)MathF.Floor( target.Fraction );
			var frac = target.Fraction - locks;
			var tColor = locked ? CrosshairRed : CrosshairCyan;
			var diamondR = locked ? 10f * scale : float.Lerp( 22f * scale, 10f * scale, Math.Clamp( frac, 0f, 1f ) );

			// Subtle acquisition line from center toward target.
			DrawCrosshairLine( painter, center, screenPos, 1f * scale, tColor.WithAlpha( locked ? 0.35f : 0.22f ) );
			DrawCrosshairDiamond( painter, screenPos, diamondR, tColor, scale );
			if ( locked )
			{
				DrawCrosshairDiamond( painter, screenPos, diamondR * 0.55f, tColor, scale );
				// Per-target lock count pips above the target diamond.
				var pips = Math.Clamp( locks, 1, 6 );
				var pw = 5f * scale;
				var pg = 2.5f * scale;
				var px = screenPos.x - (pips * pw + (pips - 1) * pg) * 0.5f;
				for ( var p = 0; p < pips; ++p )
					DrawCrosshairBar( painter, new Rect( px + p * (pw + pg), screenPos.y - diamondR - 7f * scale, pw, 3f * scale ), CrosshairRed, scale );
			}
		}
	}

	static void DrawAdsOpticReticle( Painter painter, Vector2 center, float scale, Color baseColor, Titanfall2OpticReticleStyle optic )
	{
		var thick = 1.4f * scale;
		switch ( optic )
		{
			case Titanfall2OpticReticleStyle.None:
				return;

			case Titanfall2OpticReticleStyle.GreenDot:
				DrawCrosshairDot( painter, center, 1.5f * scale, CrosshairGreen, scale );
				break;

			case Titanfall2OpticReticleStyle.Diamond:
				DrawCrosshairDiamond( painter, center, 6f * scale, CrosshairCyan, scale );
				DrawCrosshairDot( painter, center, 1.2f * scale, CrosshairCyan, scale );
				break;

			case Titanfall2OpticReticleStyle.Hcog:
				// HCOG: crisp red dot + subtle cyan side frame ticks.
				DrawCrosshairBar( painter, new Rect( center.x - 16f * scale, center.y - thick * 0.5f, 5f * scale, thick ), CrosshairCyan.WithAlpha( 0.75f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x + 11f * scale, center.y - thick * 0.5f, 5f * scale, thick ), CrosshairCyan.WithAlpha( 0.75f ), scale );
				DrawCrosshairDot( painter, center, 1.8f * scale, CrosshairRed, scale );
				break;

			case Titanfall2OpticReticleStyle.Holo:
				// Holosight: red 4-arc ring + sharp red center dot.
				DrawCrosshairSegmentedRing( painter, center, 13f * scale, 4, 62f, thick, CrosshairRed.WithAlpha( 0.9f ), scale );
				DrawCrosshairDot( painter, center, 1.6f * scale, CrosshairRed, scale );
				break;

			case Titanfall2OpticReticleStyle.AcgsRedline:
				// ACGS Redline: horizontal redline windage ticks + drop stadia + red center chevron/dot.
				DrawCrosshairBar( painter, new Rect( center.x - 24f * scale, center.y - thick * 0.5f, 16f * scale, thick ), CrosshairRed.WithAlpha( 0.85f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x + 8f * scale, center.y - thick * 0.5f, 16f * scale, thick ), CrosshairRed.WithAlpha( 0.85f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x - thick * 0.5f, center.y + 6f * scale, thick, 16f * scale ), CrosshairRed.WithAlpha( 0.8f ), scale );
				DrawCrosshairDot( painter, center, 1.7f * scale, CrosshairRed, scale );
				break;

			case Titanfall2OpticReticleStyle.ThreatScope:
				// Threat Scope: digital square frame + red center dot.
				DrawCrosshairCornerBox( painter, center, 16f * scale, 16f * scale, 5f * scale, thick, CrosshairCyan.WithAlpha( 0.85f ), scale );
				DrawCrosshairDot( painter, center, 1.7f * scale, CrosshairRed, scale );
				break;

			case Titanfall2OpticReticleStyle.Aog:
				// AOG: precision outer ring + windage crosshairs + center dot.
				DrawCrosshairSegmentedRing( painter, center, 15f * scale, 4, 55f, thick, CrosshairCyan.WithAlpha( 0.8f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x - 22f * scale, center.y - thick * 0.5f, 10f * scale, thick ), CrosshairCyan.WithAlpha( 0.85f ), scale );
				DrawCrosshairBar( painter, new Rect( center.x + 12f * scale, center.y - thick * 0.5f, 10f * scale, thick ), CrosshairCyan.WithAlpha( 0.85f ), scale );
				DrawCrosshairDot( painter, center, 1.5f * scale, CrosshairRed, scale );
				break;

			case Titanfall2OpticReticleStyle.MastiffHorseshoe:
				// Mastiff ADS horseshoe: upper arc + wide side ticks + glowing amber dot.
				DrawCrosshairBar( painter, new Rect( center.x - 14f * scale, center.y - thick * 0.5f, 7f * scale, thick ), CrosshairAmber, scale );
				DrawCrosshairBar( painter, new Rect( center.x + 7f * scale, center.y - thick * 0.5f, 7f * scale, thick ), CrosshairAmber, scale );
				DrawCrosshairBar( painter, new Rect( center.x - 7f * scale, center.y - 9f * scale, 14f * scale, thick ), CrosshairAmber, scale );
				DrawCrosshairDot( painter, center, 1.6f * scale, CrosshairAmber, scale );
				break;

			case Titanfall2OpticReticleStyle.DoubleTakeDoubleV:
				// Double Take ADS double-V: twin horizontal side dots + center dot.
				DrawCrosshairDot( painter, center + new Vector2( -8f * scale, 0 ), 1.3f * scale, CrosshairCyan, scale );
				DrawCrosshairDot( painter, center + new Vector2( 8f * scale, 0 ), 1.3f * scale, CrosshairCyan, scale );
				DrawCrosshairDot( painter, center, 1.5f * scale, CrosshairCyan, scale );
				break;

			default:
				DrawCrosshairDot( painter, center, 1.35f * scale, baseColor, scale );
				break;
		}
	}

	static void DrawCrosshairBar( Painter painter, Rect rect, Color color, float scale )
	{
		painter.Fill = CrosshairShadow;
		painter.Rect( new Rect( rect.Left - scale, rect.Top - scale, rect.Width + 2 * scale, rect.Height + 2 * scale ) );
		painter.Fill = color;
		painter.Rect( rect );
	}

	static void DrawCrosshairDot( Painter painter, Vector2 center, float radius, Color color, float scale )
	{
		painter.Fill = CrosshairShadow;
		painter.Circle( center, radius + scale );
		painter.Fill = color;
		painter.Circle( center, radius );
	}

	static void DrawCrosshairBracket( Painter painter, Vector2 edgeCenter, float capWidth, float height, bool leftSide, float thick, Color color, float scale )
	{
		var x = leftSide ? edgeCenter.x : edgeCenter.x - thick;
		var top = edgeCenter.y - height * 0.5f;
		var capX = leftSide ? edgeCenter.x : edgeCenter.x - capWidth;
		DrawCrosshairBar( painter, new Rect( x, top, thick, height ), color, scale );
		DrawCrosshairBar( painter, new Rect( capX, top, capWidth, thick ), color, scale );
		DrawCrosshairBar( painter, new Rect( capX, top + height - thick, capWidth, thick ), color, scale );
	}

	static void DrawCrosshairCornerBox( Painter painter, Vector2 center, float halfW, float halfH, float cornerLen, float thick, Color color, float scale )
	{
		var left = center.x - halfW;
		var right = center.x + halfW;
		var top = center.y - halfH;
		var bottom = center.y + halfH;
		// Top-left
		DrawCrosshairBar( painter, new Rect( left, top, cornerLen, thick ), color, scale );
		DrawCrosshairBar( painter, new Rect( left, top, thick, cornerLen ), color, scale );
		// Top-right
		DrawCrosshairBar( painter, new Rect( right - cornerLen, top, cornerLen, thick ), color, scale );
		DrawCrosshairBar( painter, new Rect( right - thick, top, thick, cornerLen ), color, scale );
		// Bottom-left
		DrawCrosshairBar( painter, new Rect( left, bottom - thick, cornerLen, thick ), color, scale );
		DrawCrosshairBar( painter, new Rect( left, bottom - cornerLen, thick, cornerLen ), color, scale );
		// Bottom-right
		DrawCrosshairBar( painter, new Rect( right - cornerLen, bottom - thick, cornerLen, thick ), color, scale );
		DrawCrosshairBar( painter, new Rect( right - thick, bottom - cornerLen, thick, cornerLen ), color, scale );
	}

	static void DrawCrosshairDiamond( Painter painter, Vector2 center, float radius, Color color, float scale )
	{
		var top = center + new Vector2( 0, -radius );
		var right = center + new Vector2( radius, 0 );
		var bottom = center + new Vector2( 0, radius );
		var left = center + new Vector2( -radius, 0 );
		var width = 1.6f * scale;
		DrawCrosshairLine( painter, top, right, width, color );
		DrawCrosshairLine( painter, right, bottom, width, color );
		DrawCrosshairLine( painter, bottom, left, width, color );
		DrawCrosshairLine( painter, left, top, width, color );
	}

	static void DrawCrosshairSegmentedRing( Painter painter, Vector2 center, float radius, int arcs, float sweepDegrees, float thick, Color color, float scale )
	{
		var stepAngle = 360f / arcs;
		var startOffset = arcs == 4 ? 45f : 0f;
		var subSteps = 6;
		for ( var a = 0; a < arcs; ++a )
		{
			var baseAngle = startOffset + a * stepAngle - sweepDegrees * 0.5f;
			for ( var s = 0; s < subSteps; ++s )
			{
				var a0 = (baseAngle + sweepDegrees * (s / (float)subSteps)) * MathF.PI / 180f;
				var a1 = (baseAngle + sweepDegrees * ((s + 1) / (float)subSteps)) * MathF.PI / 180f;
				var p0 = center + new Vector2( MathF.Cos( a0 ), MathF.Sin( a0 ) ) * radius;
				var p1 = center + new Vector2( MathF.Cos( a1 ), MathF.Sin( a1 ) ) * radius;
				DrawCrosshairLine( painter, p0, p1, thick, color );
			}
		}
	}

	static void DrawCrosshairLine( Painter painter, Vector2 a, Vector2 b, float thickness, Color color )
	{
		var delta = b - a;
		var len = delta.Length;
		if ( len < 0.25f ) return;
		var steps = Math.Clamp( (int)MathF.Ceiling( len / MathF.Max( 1.5f, thickness * 0.9f ) ), 1, 64 );
		var half = thickness * 0.5f;
		painter.Fill = color;
		for ( var i = 0; i <= steps; ++i )
		{
			var p = Vector2.Lerp( a, b, i / (float)steps );
			painter.Rect( new Rect( p.x - half, p.y - half, thickness, thickness ) );
		}
	}

	internal static float CrosshairRadius( float coneDegrees, float fov, float width, float scale )
	{
		if ( !float.IsFinite( coneDegrees ) || !float.IsFinite( fov ) || !float.IsFinite( width ) || !float.IsFinite( scale ) ) return 4;
		var pixels = MathF.Tan( Math.Clamp( coneDegrees, 0, 45 ) * MathF.PI / 180 )
			/ MathF.Tan( Math.Clamp( fov, 15, 150 ) * MathF.PI / 360 ) * MathF.Max( 1, width ) * 0.5f;
		return Math.Clamp( pixels, 4 * scale, 35 * scale );
	}
}
