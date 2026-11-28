using Sandbox.UI;
using Sandbox.UI.Construct;

/// <summary>One retail tactical icon beside the host health readout, plus transient combat feedback.</summary>
[Hide]
public sealed class Titanfall2PilotCombatHud : Titanfall2MountPanelComponent
{
	const int MaxSonarMarkers = 12;
	[Property] public Titanfall2PilotParkour Parkour { get; set; }
	[Property] public Titanfall2PilotCombat Combat { get; set; }
	[Property] public Titanfall2PilotTactical Tactical { get; set; }

	Panel _root;
	Image _tacticalIcon;
	Label _fallback;
	Label _tacticalState;
	Label _riderState;
	Panel _tacticalFill;
	Panel _hitmarker;
	readonly List<Panel> _sonarMarkers = [];
	readonly List<GameObject> _sonarTargets = [];
	string _requestedArt;
	int _artGeneration;
	int _lastHitSerial;
	TimeUntil _hitFlash;
	TimeUntil _layoutRefresh;
	Component _vitalsOwner;
	PropertyDescription _healthProperty;
	PropertyDescription _armourProperty;
	bool _vitalsResolved;

	const string PilotHudStyles = """
titanfall2pilotcombathud {
	position: absolute;
	width: 100%;
	height: 100%;
	pointer-events: none;
	font-family: Roboto;
	color: #e4f6ff;
	text-shadow: 0 1px 3px #000d;

	.pilot-tactical {
		position: absolute;
		left: 200px;
		bottom: 38px;
		width: 58px;
		height: 58px;
		background-color: #09121b99;
		border: 1px solid #d8edff55;
		border-radius: 3px;
	}
	.pilot-tactical.hidden { display: none; }
	.pilot-tactical.cooling { opacity: 0.6; }
	.pilot-tactical.active { border-color: #f7c773; }
	.rider-state { position: absolute; bottom: 110px; left: 38px; font-size: 15px; color: #a8ffd0; }
	.tactical-icon {
		position: absolute;
		left: 8px;
		top: 6px;
		width: 40px;
		height: 40px;
		background-size: contain;
		background-repeat: no-repeat;
		background-position: center;
	}
	.icon-fallback {
		position: absolute;
		width: 100%;
		top: 19px;
		text-align: center;
		font-size: 11px;
		font-weight: 700;
	}
	.slot-key { position: absolute; left: 3px; bottom: 5px; font-size: 11px; font-weight: 700; }
	.slot-state { position: absolute; right: 3px; bottom: 5px; font-size: 10px; font-weight: 600; }
	.slot-bar {
		position: absolute;
		left: 0;
		bottom: 0;
		width: 100%;
		height: 2px;
		background-color: #152836aa;
	}
	.slot-fill { height: 100%; width: 100%; background-color: #d8edff; }
	.pilot-tactical.active .slot-fill { background-color: #f7c773; }
	.sonar-marker {
		position: absolute;
		width: 16px;
		height: 16px;
		margin-left: -8px;
		margin-top: -8px;
		border: 2px solid #ff7a2e;
		background-color: #ff601433;
		transform: rotate(45deg);
		display: none;
	}
	.sonar-marker.visible { display: flex; }
	.melee-hitmarker {
		position: absolute;
		left: 50%;
		top: 50%;
		width: 22px;
		height: 22px;
		margin-left: -11px;
		margin-top: -11px;
		opacity: 0;
	}
	.melee-hitmarker.active { opacity: 0.95; }
	.hm-arm { position: absolute; width: 8px; height: 2px; background-color: #fff; }
	.melee-hitmarker.kill .hm-arm { background-color: #ff5548; }
	.hm-tl { left: 1px; top: 4px; transform: rotate(45deg); }
	.hm-tr { right: 1px; top: 4px; transform: rotate(-45deg); }
	.hm-bl { left: 1px; bottom: 4px; transform: rotate(-45deg); }
	.hm-br { right: 1px; bottom: 4px; transform: rotate(45deg); }
}
""";

	protected override void OnEnabled()
	{
		base.OnEnabled();
		if ( !Panel.IsValid() ) return;
		Panel.DeleteChildren( true );
		_sonarMarkers.Clear();
		_requestedArt = null;
		_artGeneration++;
		_layoutRefresh = 0;
		Panel.StyleSheet.AddInline( PilotHudStyles, "titanfall2:pilot-combat" );
		for ( var i = 0; i < MaxSonarMarkers; i++ )
			_sonarMarkers.Add( Panel.Add.Panel( "sonar-marker" ) );
		_hitmarker = Panel.Add.Panel( "melee-hitmarker" );
		_hitmarker.Add.Panel( "hm-arm hm-tl" );
		_hitmarker.Add.Panel( "hm-arm hm-tr" );
		_hitmarker.Add.Panel( "hm-arm hm-bl" );
		_hitmarker.Add.Panel( "hm-arm hm-br" );
		_root = Panel.Add.Panel( "pilot-tactical hidden" );
		_tacticalIcon = _root.AddChild<Image>( "tactical-icon" );
		_fallback = _root.Add.Label( "", "icon-fallback" );
		_root.Add.Label( "T", "slot-key" );
		_tacticalState = _root.Add.Label( "", "slot-state" );
		_tacticalFill = _root.Add.Panel( "slot-bar" ).Add.Panel( "slot-fill" );
		_riderState = Panel.Add.Label( "", "rider-state" );
	}

	protected override void OnDisabled()
	{
		_artGeneration++;
		base.OnDisabled();
	}

	protected override void OnUpdate()
	{
		if ( !_root.IsValid() ) return;
		var valid = Parkour is { IsValid: true, Equipped: true } && Combat is { IsValid: true };
		var controller = valid ? Parkour.GetComponent<PlayerController>() : null;
		var inPilotMode = controller is { IsValid: true, Mode: Titanfall2PilotMoveMode } && !controller.IsProxy;
		if ( _riderState.IsValid() )
		{
			var rider = controller.IsValid() && !controller.IsProxy ? Titanfall2TitanRodeo.ForPilot( controller ) : null;
			var carrying = controller.IsValid() && !controller.IsProxy && controller.GetComponent<Titanfall2PilotBattery>() is { HasBattery: true };
			_riderState.Text = rider?.State switch
			{
				Titanfall2RodeoState.Mounting => "正在攀上泰坦",
				Titanfall2RodeoState.StealingBattery => "正在拔取电池",
				Titanfall2RodeoState.ThrowingGrenade => "正在向电池舱投雷",
				Titanfall2RodeoState.InsertingBattery => "正在补给友方泰坦",
				Titanfall2RodeoState.FriendlyIdle => "友方搭乘 · 可射击 · 跳跃键离开",
				_ => carrying && inPilotMode ? "携带电池 · 搭乘友方泰坦进行补给" : ""
			};
		}
		if ( !Tactical.IsValid() && valid ) Tactical = Parkour.Tactical ?? Parkour.GetComponent<Titanfall2PilotTactical>();
		var showTactical = valid && inPilotMode && Tactical is { IsValid: true, HasEquippedTactical: true };
		_root.SetClass( "hidden", !showTactical );
		if ( !valid || !inPilotMode )
		{
			_hitmarker?.SetClass( "active", false );
			HideSonarMarkers();
			return;
		}
		if ( showTactical )
		{
			UpdateTacticalArtwork();
			UpdatePlacement();
			var active = Tactical.IsActive;
			var ready = Tactical.ChargesAvailable > 0;
			_root.SetClass( "active", active );
			_root.SetClass( "cooling", !active && !ready );
			_tacticalState.Text = Tactical.IsStimActive ? $"{Tactical.StimRemaining:0.0}"
				: Tactical.IsPhaseActive ? $"{Tactical.PhaseRemaining:0.0}"
				: Tactical.IsGrappling ? ""
				: Tactical.MaxCharges > 1 && ready ? $"{Tactical.ChargesAvailable}"
				: ready ? "" : $"{(int)MathF.Round( Tactical.ChargeFillFraction * 100f )}%";
			_tacticalFill.Style.Width = Length.Percent( Tactical.ChargeFillFraction * 100f );
		}
		if ( Combat.MeleeHitSerial != _lastHitSerial )
		{
			_lastHitSerial = Combat.MeleeHitSerial;
			_hitFlash = Combat.MeleeLastHitWasKill ? 0.24f : 0.16f;
		}
		var showHit = _hitFlash > 0f;
		_hitmarker.SetClass( "active", showHit );
		_hitmarker.SetClass( "kill", showHit && Combat.MeleeLastHitWasKill );
		UpdateSonarMarkers();
	}

	void UpdateTacticalArtwork()
	{
		// hud_icon in retail mp_ability_heal/grapple/shifter and mp_weapon_grenade_sonar.txt.
		var suffix = Tactical.SelectedKind switch
		{
			Titanfall2PilotTacticalKind.Grapple => "grapple",
			Titanfall2PilotTacticalKind.PhaseShift => "phase_shift",
			Titanfall2PilotTacticalKind.PulseBlade => "pulse_blade",
			_ => "stim"
		};
		var uri = $"mount://{Tactical.MountIdent}/rui/pilot_loadout/tactical/pilot_tactical_{suffix}.vtex";
		if ( _requestedArt == uri ) return;
		_requestedArt = uri;
		_fallback.Text = Tactical.SelectedKind switch
		{
			Titanfall2PilotTacticalKind.Grapple => "HOOK",
			Titanfall2PilotTacticalKind.PhaseShift => "PHASE",
			Titanfall2PilotTacticalKind.PulseBlade => "PULSE",
			_ => "STIM"
		};
		LoadTacticalArtwork( uri, ++_artGeneration );
	}

	async void LoadTacticalArtwork( string uri, int generation )
	{
		_tacticalIcon.Style.Display = DisplayMode.None;
		_fallback.Style.Display = DisplayMode.Flex;
		if ( Sandbox.Mounting.Directory.Get( Tactical.MountIdent )?.GetByPath( uri ) is null )
		{
			Titanfall2Log.Warning( $"Pilot tactical HUD artwork is not mounted: '{uri}'." );
			return;
		}
		try
		{
			var texture = await Texture.LoadAsync( uri );
			if ( generation != _artGeneration || !_tacticalIcon.IsValid() || !_fallback.IsValid() ) return;
			if ( !texture.IsValid() || texture == Texture.Invalid || texture == Texture.White || texture == Texture.Transparent ) return;
			_tacticalIcon.Texture = texture;
			_tacticalIcon.Style.Display = DisplayMode.Flex;
			_fallback.Style.Display = DisplayMode.None;
		}
		catch ( Exception e )
		{
			Titanfall2Log.Warning( $"Cannot load Pilot tactical artwork '{uri}': {e.Message}" );
		}
	}

	void UpdatePlacement()
	{
		if ( _layoutRefresh > 0f ) return;
		_layoutRefresh = 0.25f;
		if ( !_vitalsResolved )
		{
			_vitalsResolved = true;
			foreach ( var component in Parkour.GameObject.Components.GetAll() )
			{
				var description = Game.TypeLibrary?.GetType( component.GetType() );
				var health = description?.GetProperty( "Health" );
				var armour = description?.GetProperty( "Armour" );
				if ( health is not { CanRead: true } || armour is not { CanRead: true } ) continue;
				_vitalsOwner = component;
				_healthProperty = health;
				_armourProperty = armour;
				break;
			}
		}

		// Sandbox's public HUD preferences and VitalsSection dimensions, without a game-assembly
		// dependency. Other host games receive the same bottom-left fallback placement.
		var hudScale = Math.Clamp( ReadHudNumber( "sandbox.hud_scale", 1f ), 0.5f, 3f );
		var margin = Screen.Width * Math.Clamp( ReadHudNumber( "sandbox.hud_margin", 2f ), 0f, 20f ) / 100f;
		var scale = MathF.Max( Panel.ScaleToScreen, 0.01f );
		var healthValue = ReadVital( _healthProperty, 100f );
		var armourValue = ReadVital( _armourProperty, 0f );
		var healthWidth = StatWidth( healthValue, hudScale );
		var armourWidth = armourValue >= 1f ? StatWidth( armourValue, hudScale ) + 10f * hudScale : 0f;
		_root.Style.Left = Length.Pixels( margin / scale + healthWidth + armourWidth + 12f * hudScale );
		_root.Style.Bottom = Length.Pixels( margin / scale );
	}

	float ReadVital( PropertyDescription property, float fallback )
	{
		if ( !_vitalsOwner.IsValid() || property is null ) return fallback;
		return property.GetValue( _vitalsOwner ) switch { float value => value, int value => value, _ => fallback };
	}

	static float StatWidth( float value, float scale ) =>
		(32f * Math.Max( 3, ((int)MathF.Max( 0f, value )).ToString().Length ) + 52.5f) * scale;

	static float ReadHudNumber( string name, float fallback ) =>
		float.TryParse( ConsoleSystem.GetValue( name, "" ), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value ) ? value : fallback;

	void UpdateSonarMarkers()
	{
		var cam = Scene?.Camera;
		var registry = Scene?.GetSystem<Titanfall2SonarRegistry>();
		if ( !cam.IsValid() || registry is null )
		{
			HideSonarMarkers();
			return;
		}

		registry.FillActiveTargets( _sonarTargets );
		var used = 0;
		for ( var i = 0; i < _sonarTargets.Count && used < _sonarMarkers.Count; i++ )
		{
			var target = _sonarTargets[i];
			if ( !target.IsValid() ) continue;
			var worldCenter = target.GetBounds().Center;
			var screen = cam.PointToScreenNormal( worldCenter, out var isBehind );
			if ( isBehind || screen.x < 0.02f || screen.x > 0.98f || screen.y < 0.02f || screen.y > 0.98f ) continue;

			var marker = _sonarMarkers[used++];
			marker.SetClass( "visible", true );
			marker.Style.Left = Length.Percent( screen.x * 100f );
			marker.Style.Top = Length.Percent( screen.y * 100f );
		}

		for ( var i = used; i < _sonarMarkers.Count; i++ )
			_sonarMarkers[i].SetClass( "visible", false );
	}

	void HideSonarMarkers()
	{
		foreach ( var m in _sonarMarkers )
			m?.SetClass( "visible", false );
	}

}
