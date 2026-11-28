/// <summary>
/// Intercepts Sandbox actions in Stage.StartUpdate—before PlayerFlashlight, PlayerInventory,
/// Player.Noclip, and ContextMenu execute—for both On-Foot Pilot Combat State and Piloted Titan Cockpit Mode.
/// Always preserves SpawnMenu (Q) and automatically yields when UI menus or Sandbox builder tools are active.
/// </summary>
public sealed class Titanfall2PilotInputInterceptor : GameObjectSystem<Titanfall2PilotInputInterceptor>
{
	static readonly string[] InteractionActions = ["use", "jump", "die", "noclip", "drop", "flashlight", "spray"];
	readonly HashSet<Titanfall2TitanSeat> _seats = [];
	readonly HashSet<Titanfall2TitanRodeo> _rodeos = [];
	readonly HashSet<Titanfall2PilotCombat> _combats = [];

	public Titanfall2PilotInputInterceptor( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, -100, InterceptPilotCombatInput, nameof( InterceptPilotCombatInput ) );
	}

	internal void RegisterSeat( Titanfall2TitanSeat seat ) { if ( seat is not null ) _seats.Add( seat ); }
	internal void UnregisterSeat( Titanfall2TitanSeat seat ) { if ( seat is not null ) _seats.Remove( seat ); }
	internal void RegisterRodeo( Titanfall2TitanRodeo rodeo ) { if ( rodeo is not null ) _rodeos.Add( rodeo ); }
	internal void UnregisterRodeo( Titanfall2TitanRodeo rodeo ) { if ( rodeo is not null ) _rodeos.Remove( rodeo ); }
	internal void RegisterCombat( Titanfall2PilotCombat combat ) { if ( combat is not null ) _combats.Add( combat ); }
	internal void UnregisterCombat( Titanfall2PilotCombat combat ) { if ( combat is not null ) _combats.Remove( combat ); }

	void InterceptPilotCombatInput()
	{
		if ( !Scene.IsValid() || Application.IsDedicatedServer || (_seats.Count == 0 && _combats.Count == 0 && _rodeos.Count == 0) ) return;

		foreach ( var seat in _seats )
		{
			if ( !seat.IsValid() || !seat.Active || !seat.IsTransitioning || seat.LocalPilot is not { IsProxy: false } ) continue;
			var capturing = Mouse.Active || Sandbox.UI.InputFocus.Current is Sandbox.UI.TextEntry;
			var cancel = !capturing && (Input.Pressed( "use" ) || Input.Pressed( "jump" ));
			ClearInteractionActions( weapons: true );
			seat.ConsumeTransitionInput( cancel );
			return;
		}
		foreach ( var rodeo in _rodeos )
		{
			if ( !rodeo.IsValid() || !rodeo.Active || !rodeo.IsRiding || rodeo.Rider is not { IsProxy: false } ) continue;
			var capturing = Mouse.Active || Sandbox.UI.InputFocus.Current is Sandbox.UI.TextEntry;
			var jump = !capturing && Input.Pressed( "jump" );
			var use = !capturing && Input.Pressed( "use" );
			var move = capturing ? Vector3.Zero : Input.AnalogMove;
			ClearInteractionActions( rodeo.BlockingWeapons );
			rodeo.ConsumeInput( jump, use, move );
			return;
		}

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
		var hasTactical = tactical is { IsValid: true, Active: true, HasEquippedTactical: true };
		var tacticalPressed = hasTactical && Input.Pressed( "spray" );

		// Reserve T only for an equipped tactical; leave Mouse3, Q and C available to Sandbox.
		ClearAction( "Flashlight" );
		ClearAction( "drop" );
		if ( hasTactical ) ClearAction( "spray" );

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

		var offensePressed = IsActionPressed( seat.OffenseAction );
		var adsDown = Input.Down( "attack2" );
		// Defensive ability uses MMB (toolmenu) with Ctrl (duck) fallback; RMB (attack2) is reserved for primary ADS on all Titans.
		var defensePressed = IsActionPressed( seat.DefenseAction )
			|| IsActionPressed( seat.DefenseAltAction );
		var defenseDown = IsActionDown( seat.DefenseAction )
			|| IsActionDown( seat.DefenseAltAction );
		var utilityPressed = IsActionPressed( seat.UtilityAction );
		var smokePressed = IsActionPressed( seat.SmokeAction );
		var meleePressed = IsActionPressed( seat.MeleeAction );
		var corePressed = IsActionPressed( seat.CoreAction );
		var ejectPressed = IsActionPressed( seat.EjectAction );
		var dashPressed = IsActionPressed( seat.DashAction );

		// Intercept & clear all cockpit ability/core actions while deliberately leaving "spawnmenu" (Q) untouched.
		ClearAction( seat.OffenseAction );
		ClearAction( seat.DefenseAction );
		ClearAction( seat.DefenseAltAction );
		ClearAction( seat.UtilityAction );
		ClearAction( seat.SmokeAction );
		ClearAction( seat.MeleeAction );
		ClearAction( seat.CoreAction );
		// Capture before clearing so Sandbox cannot kill the pilot or jump out of the seat.
		ClearAction( seat.EjectAction );
		ClearAction( seat.DashAction );
		ClearAction( "drop" );

		seat.ConsumeCockpitInput( new Titanfall2TitanCockpitInput(
			offensePressed, defensePressed, defenseDown, utilityPressed, smokePressed, meleePressed, corePressed, adsDown,
			ejectPressed, dashPressed ) );
	}

	static bool IsActionPressed( string action ) => !string.IsNullOrWhiteSpace( action ) && Input.Pressed( action );
	static void ClearInteractionActions( bool weapons )
	{
		foreach ( var action in InteractionActions ) ClearAction( action );
		if ( !weapons ) return;
		ClearAction( "attack1" ); ClearAction( "attack2" ); ClearAction( "reload" );
	}
	static bool IsActionDown( string action ) => !string.IsNullOrWhiteSpace( action ) && Input.Down( action );

	static void ClearAction( string action )
	{
		if ( string.IsNullOrWhiteSpace( action ) ) return;
		Input.Clear( action );
		Input.SetLastAction( action, false );
	}
}

