using Sandbox.Movement;

internal static class Titanfall2TitanBoardingRules
{
	internal static bool CanClaim( bool friendly, bool hasOwner, bool ownsTitan ) => friendly && (!hasOwner || ownsTitan);
	internal static string Subclass( Titanfall2TitanKit.Chassis chassis, string model ) =>
		(model ?? "").Contains( "/buddy/", StringComparison.OrdinalIgnoreCase ) ? "buddy" : chassis switch
		{
			Titanfall2TitanKit.Chassis.Scorch or Titanfall2TitanKit.Chassis.Legion => "ogre",
			Titanfall2TitanKit.Chassis.Northstar or Titanfall2TitanKit.Chassis.Ronin => "stryder",
			_ => "atlas"
		};
	internal static string GroundDirection( Vector3 localOffset ) => MathF.Abs( localOffset.x ) >= MathF.Abs( localOffset.y )
		? localOffset.x >= 0 ? "front" : "behind" : localOffset.y >= 0 ? "left" : "right";
}

public sealed partial class Titanfall2TitanSeat
{
	public enum InteractionStage { None, Embarking, Disembarking }
	[Sync( SyncFlags.FromHost )] public InteractionStage Transition { get; private set; }
	[Sync( SyncFlags.FromHost )] public float TransitionDuration { get; private set; }
	[Sync( SyncFlags.FromHost )] public double TransitionStartedAt { get; private set; }
	public bool IsTransitioning => Transition != InteractionStage.None;
	public bool IsDisembarking => Transition == InteractionStage.Disembarking;
	public string InteractionDiagnostic { get; private set; }
	public string InteractionPilotSequence { get; private set; }
	public string InteractionTitanSequence { get; private set; }
	internal float TransitionElapsed => MathF.Max( 0, (float)(Time.NowDouble - TransitionStartedAt) );
	PlayerController _transitionPilot;
	Titanfall2TitanInteractionPresentation _interactionPresentation;
	Titanfall2TitanBoardingMoveMode _boardingMoveMode;
	Vector3 _boardingOrigin;
	Angles _disembarkLook;

	bool CanClaimCockpit( PlayerController pilot ) => Actor.IsValid() && pilot.IsValid()
		&& Titanfall2TitanBoardingRules.CanClaim( Actor.Friendly, Actor.Leader.IsValid(), Actor.Leader == pilot.GameObject );

	bool WantsRodeo( PlayerController pilot ) => !CanClaimCockpit( pilot ) || IsOccupied
		|| pilot.UseInputControls && Input.Down( pilot.DuckButton );
	bool CanInteract( PlayerController pilot ) => WantsRodeo( pilot )
		? GetComponent<Titanfall2TitanRodeo>()?.CanMount( pilot ) == true : CanEnter( pilot );
	bool TryInteract( PlayerController pilot ) => WantsRodeo( pilot )
		? GetComponent<Titanfall2TitanRodeo>()?.TryMount( pilot ) == true : TryEmbark( pilot );

	Component.IPressable.Tooltip? InteractionTooltip( Component.IPressable.Event e )
	{
		if ( Actor is not { TitanCanAct: true } ) return null;
		var text = IsTransitioning ? IsDisembarking ? "正在离舱" : "正在登乘"
			: e.Source is PlayerController pilot && !HasPilotCombatState( pilot ) ? "需先穿戴铁驭跃升背包"
			: !Actor.Friendly ? "训牛术 · 拔电池 / 携带电池时投雷"
			: IsOccupied || Actor.Leader.IsValid() && e.Source is PlayerController other && Actor.Leader != other.GameObject
				? "搭乘友方泰坦 · 携带电池时补给"
			: "登乘泰坦 · 按住蹲伏键搭乘背部";
		return new Component.IPressable.Tooltip( Actor.DisplayName ?? "泰坦", "airline_seat_recline_normal", text );
	}

	bool BeginBoardingAnimation( PlayerController pilot, bool disembarking )
	{
		if ( IsTransitioning || !pilot.IsValid() || pilot.GameObject.IsDestroyed || !Actor.Renderer.IsValid() ) return false;
		if ( disembarking ) _disembarkLook = pilot.EyeTransform.Rotation.Angles();
		var subclass = Titanfall2TitanBoardingRules.Subclass( Actor.GetComponent<Titanfall2TitanKit>()?.Kit ?? default, Actor.ModelPath );
		var direction = Titanfall2TitanBoardingRules.GroundDirection( Actor.WorldRotation.Inverse * (pilot.WorldPosition - Actor.WorldPosition) );
		var pilotSequence = disembarking ? $"pt_dismount_{subclass}_stand" : $"pt_mount_{subclass}_stand_{direction}";
		var titanSequence = disembarking ? "at_dismount_stand" : $"at_mount_stand_{direction}";
		var ident = Actor.MountIdent ?? "titanfall2";
		var model = $"mount://{ident}/models/humans/pilots/pilot_medium_reaper_m.mdl.vmdl";
		var arms = $"mount://{ident}/models/weapons/arms/pov_pilot_medium_reaper_m.mdl.vmdl";
		if ( !Titanfall2TitanInteractionPresentation.TryBegin( Actor.Renderer, pilot.GameObject, model,
			pilotSequence, titanSequence, "hijack", out var presentation, out var diagnostic,
			disembarking ? arms : null, disembarking ? $"ptpov_dismount_{subclass}_stand" : null ) )
		{
			InteractionDiagnostic = diagnostic;
			SetMessage( "登离舱动画资源不可用", 4 );
			Titanfall2Log.Warning( $"Titan interaction refused: {diagnostic}" );
			return false;
		}
		_interactionPresentation = presentation;
		InteractionDiagnostic = diagnostic;
		InteractionPilotSequence = pilotSequence;
		InteractionTitanSequence = titanSequence;
		TransitionDuration = MathF.Max( 0.1f, presentation.PilotDuration );
		TransitionStartedAt = Time.NowDouble;
		Transition = disembarking ? InteractionStage.Disembarking : InteractionStage.Embarking;
		_transitionPilot = pilot;
		_boardingOrigin = pilot.WorldPosition;
		_boardingMoveMode = pilot.GameObject.GetOrAddComponent<Titanfall2TitanBoardingMoveMode>();
		_boardingMoveMode.Seat = this;
		_boardingMoveMode.Enabled = true;
		pilot.GetComponent<Titanfall2PilotCombat>()?.CancelForTitanInteraction();
		if ( disembarking )
		{
			var kit = Actor.GetComponent<Titanfall2TitanKit>();
			kit?.ClearHoldDefenseInput();
			kit?.ClearIonInput();
			kit?.ClearPrimaryAdsInput();
			_dashTime = 0;
			_hasInterceptedInput = false;
			_interceptedInput = default;
		}
		PauseAiNavigation();
		if ( disembarking ) { SeatSerial++; SetMessage( "正在离舱", TransitionDuration + 1 ); }
		return true;
	}

	void TickBoardingAnimation()
	{
		if ( !IsTransitioning ) return;
		_interactionPresentation?.Tick( TransitionElapsed );
		if ( !Networking.IsHost || IsProxy ) return;
		// Respawn or another system may detach the pilot. This session no longer
		// owns their position and must never pull them back to the old Titan.
		if ( IsDisembarking && (!_transitionPilot.IsValid() || !ContainsPilot( _transitionPilot )) )
		{
			AbandonDisembark();
			return;
		}
		if ( !Titanfall2PilotBattery.IsPilotAlive( _transitionPilot ) || !HasPilotCombatState( _transitionPilot )
			|| _boardingMoveMode is not { IsValid: true, Active: true }
			|| Actor is not { Active: true, TitanCanAct: true, IsInTermination: false } )
		{
			if ( IsDisembarking && _transitionPilot.IsValid() )
			{
				if ( !TryDisembark( _transitionPilot, emergency: true ) ) AbandonDisembark();
			}
			else CancelBoarding();
			return;
		}
		// External teleport/respawn must cancel rather than drag an unrelated player back.
		if ( Transition == InteractionStage.Embarking && _transitionPilot.WorldPosition.Distance( _boardingOrigin ) > 64 )
		{
			CancelBoarding();
			return;
		}
		if ( !IsDisembarking || TransitionElapsed < TransitionDuration ) return;
		var pilot = _transitionPilot;
		EndBoardingAnimation();
		if ( !FinishDisembark( pilot, emergency: false, _disembarkLook ) )
			SetMessage( "出口受阻 · 已恢复驾驶", 3 );
	}

	internal void ConsumeTransitionInput( bool cancelPressed )
	{
		if ( !Networking.IsHost || IsProxy || !cancelPressed || TransitionElapsed < 0.25f ) return;
		if ( Transition == InteractionStage.Embarking ) CancelBoarding();
		else if ( IsDisembarking ) { EndBoardingAnimation(); SeatSerial++; }
	}

	void CancelBoarding()
	{
		EndBoardingAnimation();
		BoardingPilot = null;
		EmbarkRemaining = 0;
		SeatSerial++;
		RestoreInventory();
		RestoreAiNavigation();
		SetMessage( "登乘已取消", 2 );
	}

	void AbandonDisembark()
	{
		EndBoardingAnimation();
		_seatedPilot = null;
		SeatSerial++;
		RestoreInventory();
		RestoreAiNavigation();
		IsDriving = false;
		DriveVelocity = Vector3.Zero;
	}

	void EndBoardingAnimation()
	{
		_interactionPresentation?.Dispose();
		_interactionPresentation = null;
		if ( _boardingMoveMode.IsValid() ) _boardingMoveMode.Seat = null;
		_boardingMoveMode = null;
		_transitionPilot = null;
		if ( Networking.IsHost && !IsProxy ) { Transition = InteractionStage.None; TransitionDuration = 0; }
	}

	int ICameraModifier.CameraOrder => 270;
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( !IsTransitioning || camera != Scene.Camera || !_transitionPilot.IsValid() || _transitionPilot.IsProxy
			|| _interactionPresentation?.TryGetCamera( out var pose ) != true ) return;
		var focus = Actor.WorldPosition + Vector3.Up * (Actor.BodyHeight * 0.6f);
		var trace = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( focus, pose.Position ).Radius( 6 )
			.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( _transitionPilot.GameObject )
			.WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.PlayerSolid );
		view.Position = trace.StartedSolid ? _transitionPilot.EyeTransform.Position : trace.Hit ? trace.EndPosition + trace.Normal * 3 : pose.Position;
		view.Rotation = pose.Rotation;
	}
}

/// <summary>Owns the body lock during paired choreography without becoming a cockpit occupant.</summary>
[Hide]
public sealed class Titanfall2TitanBoardingMoveMode : MoveMode
{
	[Property] public Titanfall2TitanSeat Seat { get; set; }
	bool _captured, _bodyEnabled, _colliderEnabled;
	public override int Score( PlayerController controller ) => Seat is { IsValid: true, IsTransitioning: true } ? 20000 : -100;
	public override Vector3 UpdateMove( Rotation eyes, Vector3 input ) => Vector3.Zero;
	public override Transform CalculateEyeTransform() => Seat is { IsValid: true, IsDisembarking: true }
		? Seat.CalculateEyeTransform( Controller ) : base.CalculateEyeTransform();
	public override void AddVelocity() { }
	public override void OnJump() { }
	public override void OnModeBegin()
	{
		_bodyEnabled = Controller.Body.IsValid() && Controller.Body.Enabled;
		_colliderEnabled = Controller.ColliderObject.IsValid() && Controller.ColliderObject.Enabled;
		_captured = true;
		if ( Controller.Body.IsValid() ) { Controller.Body.Velocity = Vector3.Zero; Controller.Body.Enabled = false; }
		if ( Controller.ColliderObject.IsValid() ) Controller.ColliderObject.Enabled = false;
	}
	public override void OnModeEnd( MoveMode next )
	{
		RestoreBody();
	}
	void RestoreBody()
	{
		if ( !_captured ) return;
		_captured = false;
		if ( !Controller.IsValid() ) return;
		if ( Controller.Body.IsValid() ) Controller.Body.Enabled = _bodyEnabled;
		if ( Controller.ColliderObject.IsValid() ) Controller.ColliderObject.Enabled = _colliderEnabled;
	}
	protected override void OnDisabled() { Seat?.ConsumeTransitionInput( true ); RestoreBody(); }
	protected override void OnDestroy() { RestoreBody(); }
}
