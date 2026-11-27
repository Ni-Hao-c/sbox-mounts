using Sandbox.UI;
using Sandbox.UI.Construct;

/// <summary>
/// Minimal bottom-right HUD widget active while the local player has the Pilot Jump Kit equipped.
/// Displays combat/yield status, [M3] Tactical ([T] cycle), [F] Melee state, [G] Offhand Ordnance charge/cook progress,
/// Stim/Phase screen tints, Pulse Blade sonar markers, and melee hitmarker.
/// </summary>
[Hide]
public sealed class Titanfall2PilotCombatHud : PanelComponent
{
	const int MaxSonarMarkers = 12;

	[Property] public Titanfall2PilotParkour Parkour { get; set; }
	[Property] public Titanfall2PilotCombat Combat { get; set; }
	[Property] public Titanfall2PilotTactical Tactical { get; set; }

	Panel _root;
	Panel _badge;
	Label _badgeText;
	Panel _tacticalCard;
	Label _tacticalName;
	Label _tacticalState;
	Panel _tacticalFill;
	Panel _meleeCard;
	Label _meleeState;
	Panel _meleeFill;
	Panel _ordnanceCard;
	Label _ordnanceName;
	Label _ordnanceCharges;
	Panel _ordnanceFill;
	Panel _cookTrack;
	Panel _cookFill;
	Panel _hitmarker;
	Panel _stimOverlay;
	Panel _phaseOverlay;
	readonly List<Panel> _sonarMarkers = [];
	readonly List<GameObject> _sonarTargets = [];

	int _lastHitSerial;
	TimeUntil _hitFlash;

	const string PilotHudStyles = """
titanfall2pilotcombathud {
	position: absolute;
	width: 100%;
	height: 100%;
	pointer-events: none;
	font-family: Roboto;
	color: #e4f6ff;
	text-shadow: 0 1px 3px #000d;

	.tactical-overlay {
		position: absolute;
		left: 0;
		top: 0;
		width: 100%;
		height: 100%;
		opacity: 0;
		transition: opacity 0.14s ease;
	}
	.tactical-overlay.stim {
		border: 14px solid #3bff9a66;
		background-color: #16ff8814;
	}
	.tactical-overlay.phase {
		border: 18px solid #63d9ff88;
		background-color: #12283852;
	}
	.tactical-overlay.active { opacity: 1; }

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

	.pilot-dock {
		position: absolute;
		right: 2.6%;
		bottom: 4.5%;
		flex-direction: column;
		align-items: flex-end;
		gap: 6px;
		transition: opacity 0.12s ease;
	}
	.pilot-dock.hidden { opacity: 0; }
	.pilot-dock.yielded { opacity: 0.45; }

	.mode-badge {
		padding: 2px 8px;
		background-color: #08131ccc;
		border-left: 3px solid #59d5ff;
		border-top: 1px solid #59d5ff44;
		border-right: 1px solid #59d5ff44;
		border-bottom: 1px solid #59d5ff44;
	}
	.pilot-dock.yielded .mode-badge {
		border-left-color: #ffb85c;
		border-color: #ffb85c55;
	}
	.mode-text {
		font-size: 10px;
		font-weight: 600;
		letter-spacing: 1.5px;
		color: #8ee5ff;
	}
	.pilot-dock.yielded .mode-text { color: #ffd089; }

	.slots-row {
		flex-direction: row;
		gap: 8px;
	}

	.slot-card {
		width: 118px;
		padding: 7px 9px 9px 9px;
		background-color: #07111ad4;
		border: 1px solid #6ec8f055;
		flex-direction: column;
		gap: 3px;
	}
	.slot-card.cooling { opacity: 0.68; border-color: #6ec8f02a; }
	.slot-card.active { border-color: #ffb95c; background-color: #16181ce0; }

	.slot-header {
		flex-direction: row;
		justify-content: space-between;
		align-items: center;
	}
	.slot-key {
		padding: 1px 5px;
		background-color: #0d2130;
		border: 1px solid #8bdcff88;
		font-size: 10px;
		font-weight: 700;
		color: #d8f5ff;
	}
	.slot-card.active .slot-key {
		border-color: #ffc56e;
		color: #ffe0a8;
	}
	.slot-count {
		font-size: 12px;
		font-weight: 700;
		color: #c9f1ff;
	}
	.slot-name {
		font-size: 11px;
		font-weight: 600;
		letter-spacing: 0.6px;
		color: #e6f7ff;
		margin-top: 2px;
	}
	.slot-bar {
		width: 100%;
		height: 3px;
		background-color: #152836aa;
		margin-top: 2px;
	}
	.slot-fill {
		height: 100%;
		width: 100%;
		background-color: #65d6ff;
	}
	.slot-card.cooling .slot-fill { background-color: #5d8da3; }
	.slot-card.active .slot-fill { background-color: #ffb95c; }

	.cook-track {
		width: 100%;
		height: 3px;
		background-color: #241910cc;
		margin-top: 2px;
		display: none;
	}
	.cook-track.visible { display: flex; }
	.cook-fill {
		height: 100%;
		width: 0%;
		background-color: #ff9c42;
	}

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
	.hm-arm {
		position: absolute;
		width: 8px;
		height: 2px;
		background-color: #ffffff;
	}
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

		_sonarMarkers.Clear();
		Panel.StyleSheet.AddInline( PilotHudStyles, "titanfall2:pilot-combat" );

		_stimOverlay = Panel.Add.Panel( "tactical-overlay stim" );
		_phaseOverlay = Panel.Add.Panel( "tactical-overlay phase" );

		for ( var i = 0; i < MaxSonarMarkers; i++ )
			_sonarMarkers.Add( Panel.Add.Panel( "sonar-marker" ) );

		_hitmarker = Panel.Add.Panel( "melee-hitmarker" );
		_hitmarker.Add.Panel( "hm-arm hm-tl" );
		_hitmarker.Add.Panel( "hm-arm hm-tr" );
		_hitmarker.Add.Panel( "hm-arm hm-bl" );
		_hitmarker.Add.Panel( "hm-arm hm-br" );

		_root = Panel.Add.Panel( "pilot-dock" );
		_badge = _root.Add.Panel( "mode-badge" );
		_badgeText = _badge.Add.Label( "PILOT COMBAT · [T] 切技能", "mode-text" );

		var slots = _root.Add.Panel( "slots-row" );

		_tacticalCard = slots.Add.Panel( "slot-card" );
		var tacHeader = _tacticalCard.Add.Panel( "slot-header" );
		tacHeader.Add.Label( "M3", "slot-key" );
		_tacticalState = tacHeader.Add.Label( "READY", "slot-count" );
		_tacticalName = _tacticalCard.Add.Label( "STIM 兴奋剂", "slot-name" );
		var tacBar = _tacticalCard.Add.Panel( "slot-bar" );
		_tacticalFill = tacBar.Add.Panel( "slot-fill" );

		_meleeCard = slots.Add.Panel( "slot-card" );
		var meleeHeader = _meleeCard.Add.Panel( "slot-header" );
		meleeHeader.Add.Label( "F", "slot-key" );
		_meleeState = meleeHeader.Add.Label( "READY", "slot-count" );
		_meleeCard.Add.Label( "MELEE 近战", "slot-name" );
		var meleeBar = _meleeCard.Add.Panel( "slot-bar" );
		_meleeFill = meleeBar.Add.Panel( "slot-fill" );

		_ordnanceCard = slots.Add.Panel( "slot-card" );
		var ordHeader = _ordnanceCard.Add.Panel( "slot-header" );
		ordHeader.Add.Label( "G", "slot-key" );
		_ordnanceCharges = ordHeader.Add.Label( "2/2", "slot-count" );
		_ordnanceName = _ordnanceCard.Add.Label( "FRAG", "slot-name" );
		var ordBar = _ordnanceCard.Add.Panel( "slot-bar" );
		_ordnanceFill = ordBar.Add.Panel( "slot-fill" );
		_cookTrack = _ordnanceCard.Add.Panel( "cook-track" );
		_cookFill = _cookTrack.Add.Panel( "cook-fill" );
	}

	protected override void OnUpdate()
	{
		if ( !_root.IsValid() ) return;

		var valid = Parkour is { IsValid: true, Equipped: true } && Combat is { IsValid: true };
		var controller = valid ? Parkour.GetComponent<PlayerController>() : null;
		var inPilotMode = controller is { IsValid: true, Mode: Titanfall2PilotMoveMode };
		_root.SetClass( "hidden", !valid || !inPilotMode );
		if ( !valid || !inPilotMode )
		{
			_hitmarker?.SetClass( "active", false );
			_stimOverlay?.SetClass( "active", false );
			_phaseOverlay?.SetClass( "active", false );
			HideSonarMarkers();
			return;
		}

		Tactical ??= Parkour.Tactical ?? Parkour.GetComponent<Titanfall2PilotTactical>();
		var yielded = Combat.YieldedToSandbox;
		_root.SetClass( "yielded", yielded );
		_badgeText.Text = yielded ? "SANDBOX TOOL" : "PILOT COMBAT · [T] 切技能";

		// Tactical card & screen overlays.
		if ( Tactical.IsValid() )
		{
			var active = Tactical.IsActive;
			var ready = Tactical.ChargesAvailable > 0;
			_tacticalCard.SetClass( "active", active );
			_tacticalCard.SetClass( "cooling", !active && !ready );
			_tacticalName.Text = Tactical.DisplayLabel;
			_tacticalState.Text = Tactical.IsStimActive ? $"{Tactical.StimRemaining:0.0}s"
				: Tactical.IsPhaseActive ? $"{Tactical.PhaseRemaining:0.0}s"
				: Tactical.IsGrappling ? "HOOK"
				: Tactical.MaxCharges > 1 ? $"{Tactical.ChargesAvailable}/{Tactical.MaxCharges}"
				: ready ? "READY"
				: $"{(int)MathF.Round( Tactical.ChargeFillFraction * 100f )}%";
			_tacticalFill.Style.Width = Length.Percent( Tactical.ChargeFillFraction * 100f );

			_stimOverlay?.SetClass( "active", Tactical.IsStimActive );
			_phaseOverlay?.SetClass( "active", Tactical.IsPhaseActive );
		}
		else
		{
			_stimOverlay?.SetClass( "active", false );
			_phaseOverlay?.SetClass( "active", false );
		}

		// Melee card.
		var meleeFrac = Combat.MeleeCooldownFraction;
		_meleeCard.SetClass( "active", Combat.MeleeActive );
		_meleeCard.SetClass( "cooling", !Combat.MeleeActive && meleeFrac < 0.999f );
		_meleeState.Text = Combat.MeleeActive ? StrikeLabel( Combat.MeleeStrikeKind ) : meleeFrac >= 0.999f ? "READY" : $"{(int)MathF.Round( meleeFrac * 100f )}%";
		_meleeFill.Style.Width = Length.Percent( meleeFrac * 100f );

		// Ordnance card.
		var charges = Combat.OrdnanceCharges;
		var maxCharges = Combat.OrdnanceMaxCharges;
		var cooking = Combat.OrdnanceCooking;
		_ordnanceCard.SetClass( "active", cooking || Combat.OrdnanceThrowing );
		_ordnanceCard.SetClass( "cooling", charges <= 0 && !cooking && !Combat.OrdnanceThrowing );
		_ordnanceName.Text = Combat.OrdnanceShortLabel ?? "FRAG";
		_ordnanceCharges.Text = $"{charges}/{maxCharges}";
		_ordnanceFill.Style.Width = Length.Percent( Combat.OrdnanceChargeFill * 100f );

		_cookTrack.SetClass( "visible", cooking && Combat.OrdnanceCookFraction > 0.001f );
		if ( cooking )
			_cookFill.Style.Width = Length.Percent( Combat.OrdnanceCookFraction * 100f );

		// Brief melee hitmarker confirmation.
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

	static string StrikeLabel( Titanfall2PilotMeleeStrikeKind kind ) => kind switch
	{
		Titanfall2PilotMeleeStrikeKind.AirKick => "AIR KICK",
		Titanfall2PilotMeleeStrikeKind.SlideKick => "SLIDE KICK",
		Titanfall2PilotMeleeStrikeKind.WallKick => "WALL KICK",
		Titanfall2PilotMeleeStrikeKind.LeftHook => "PUNCH",
		Titanfall2PilotMeleeStrikeKind.RightHook => "PUNCH",
		_ => "STRIKE"
	};
}
