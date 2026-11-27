/// <summary>
/// Intercepts Sandbox actions in Stage.StartUpdate—before PlayerFlashlight, PlayerInventory,
/// Player.Noclip, and ContextMenu execute—for both On-Foot Pilot Combat State and Piloted Titan Cockpit Mode.
/// Always preserves SpawnMenu (Q) and automatically yields when UI menus or Sandbox builder tools are active.
/// </summary>
public sealed class Titanfall2PilotInputInterceptor : GameObjectSystem<Titanfall2PilotInputInterceptor>
{
	readonly HashSet<Titanfall2TitanSeat> _seats = [];
	readonly HashSet<Titanfall2PilotCombat> _combats = [];

	public Titanfall2PilotInputInterceptor( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, -100, InterceptPilotCombatInput, nameof( InterceptPilotCombatInput ) );
	}

	internal void RegisterSeat( Titanfall2TitanSeat seat ) { if ( seat is not null ) _seats.Add( seat ); }
	internal void UnregisterSeat( Titanfall2TitanSeat seat ) { if ( seat is not null ) _seats.Remove( seat ); }
	internal void RegisterCombat( Titanfall2PilotCombat combat ) { if ( combat is not null ) _combats.Add( combat ); }
	internal void UnregisterCombat( Titanfall2PilotCombat combat ) { if ( combat is not null ) _combats.Remove( combat ); }

	void InterceptPilotCombatInput()
	{
		if ( !Scene.IsValid() || Application.IsDedicatedServer || (_seats.Count == 0 && _combats.Count == 0) ) return;

		// 1. Titan Cockpit Mode takes precedence when the local Pilot is seated inside a Titan.
		Titanfall2TitanSeat activeSeat = null;
		foreach ( var seat in _seats )
		{
			if ( !seat.IsValid() || !seat.Active || !seat.IsOccupied ) continue;
			var occupant = seat.GetOccupant();
			if ( !occupant.IsValid() || occupant.IsProxy || occupant.Mode is not Sandbox.Movement.SitMoveMode ) continue;
			activeSeat = seat;
			break;
		}
		if ( activeSeat is not null )
		{
			InterceptTitanCockpitInput( activeSeat );
			return;
		}

		// 2. On-Foot Pilot Combat State (Jump Kit equipped).
		Titanfall2PilotCombat localCombat = null;
		foreach ( var combat in _combats )
		{
			if ( !combat.IsValid() || !combat.Active || combat.IsProxy ) continue;
			if ( combat.Parkour is not { IsValid: true, Equipped: true } ) continue;
			localCombat = combat;
			break;
		}
		if ( localCombat is null ) return;

		var yielded = localCombat.EvaluateYieldState();
		if ( yielded )
		{
			localCombat.OnInputYielded();
			return;
		}

		if ( Application.IsHeadless ) return;

		var tactical = localCombat.GameObject.GetComponent<Titanfall2PilotTactical>();
		var inPhase = tactical is { IsValid: true, Active: true, IsPhaseActive: true };

		var meleePressed = Input.Pressed( "Flashlight" );
		var ordnancePressed = Input.Pressed( "drop" );
		var ordnanceDown = Input.Down( "drop" );
		var ordnanceReleased = Input.Released( "drop" );
		var tacticalPressed = Input.Pressed( "toolmenu" );
		var tacticalCyclePressed = Input.Pressed( "spray" );

		// Clear intercepted actions for downstream Sandbox components (PlayerFlashlight / PlayerInventory.Drop / ToolMenu / Spray)
		// while leaving SpawnMenu (Q) and ContextMenu (C) untouched.
		ClearAction( "Flashlight" );
		ClearAction( "drop" );
		ClearAction( "toolmenu" );
		ClearAction( "spray" );

		if ( inPhase )
		{
			// Retail Phase Dimension rule: no firing, ADS, melee, or ordnance throws while phased (reload & parkour stay allowed).
			ClearAction( "attack1" );
			ClearAction( "attack2" );
			localCombat.OnInputYielded();
		}
		else
		{
			localCombat.ProcessFrameInput( meleePressed, ordnancePressed, ordnanceDown, ordnanceReleased );
		}

		if ( tactical is { IsValid: true, Active: true } )
		{
			if ( !inPhase && tacticalCyclePressed ) tactical.CycleTactical();
			if ( !inPhase && tacticalPressed ) tactical.TriggerTactical();
		}
	}

	static void InterceptTitanCockpitInput( Titanfall2TitanSeat seat )
	{
		var uiCapturing = !Application.IsHeadless
			&& (Mouse.Active || Sandbox.UI.InputFocus.Current is Sandbox.UI.TextEntry);
		if ( uiCapturing )
		{
			seat.OnCockpitInputYielded();
			return;
		}

		if ( Application.IsHeadless ) return;

		var kit = seat.Actor?.GetComponent<Titanfall2TitanKit>();
		var isIon = kit is { Kit: Titanfall2TitanKit.Chassis.Ion };

		var offensePressed = IsActionPressed( seat.OffenseAction );
		var adsDown = Input.Down( "attack2" );
		// Non-Ion Titans support RMB as Defensive ability in addition to Ctrl/Mouse3; Ion keeps RMB for Split Shot ADS.
		var defensePressed = IsActionPressed( seat.DefenseAction )
			|| IsActionPressed( seat.DefenseAltAction )
			|| (!isIon && Input.Pressed( "attack2" ));
		var defenseDown = IsActionDown( seat.DefenseAction )
			|| IsActionDown( seat.DefenseAltAction )
			|| (!isIon && adsDown);
		var utilityPressed = IsActionPressed( seat.UtilityAction );
		var smokePressed = IsActionPressed( seat.SmokeAction );
		var meleePressed = IsActionPressed( seat.MeleeAction );
		var corePressed = IsActionPressed( seat.CoreAction );

		// Intercept & clear all cockpit ability/core actions while deliberately leaving "spawnmenu" (Q) untouched.
		ClearAction( seat.OffenseAction );
		ClearAction( seat.DefenseAction );
		ClearAction( seat.DefenseAltAction );
		ClearAction( seat.UtilityAction );
		ClearAction( seat.SmokeAction );
		ClearAction( seat.MeleeAction );
		ClearAction( seat.CoreAction );
		ClearAction( "drop" );

		seat.ConsumeCockpitInput( new Titanfall2TitanCockpitInput(
			offensePressed, defensePressed, defenseDown, utilityPressed, smokePressed, meleePressed, corePressed, adsDown ) );
	}

	static bool IsActionPressed( string action ) => !string.IsNullOrWhiteSpace( action ) && Input.Pressed( action );
	static bool IsActionDown( string action ) => !string.IsNullOrWhiteSpace( action ) && Input.Down( action );

	static void ClearAction( string action )
	{
		if ( string.IsNullOrWhiteSpace( action ) ) return;
		Input.Clear( action );
		Input.SetLastAction( action, false );
	}
}

