public sealed partial class Titanfall2TitanRodeo : ICameraModifier
{
	Titanfall2TitanInteractionPresentation _presentation;
	string _handPropModel, _handPropAttachment;
	float _handPropShowAt;
	bool _handPropShown;
	int ICameraModifier.CameraOrder => 240;

	void BeginPresentation()
	{
		DisposePresentation();
		Diagnostic = "interaction-animation-unavailable";
		if ( Application.IsDedicatedServer || !Actor.Renderer.IsValid() ) { ReleaseRider(); return; }
		var (third, first) = AnimationNames();
		if ( !Titanfall2TitanInteractionPresentation.TryBegin( Actor.Renderer, Rider.GameObject,
			Titanfall2TitanInteractionPresentation.DefaultPilotModel, third, null, "hijack", out _presentation, out var diagnostic,
			Titanfall2TitanInteractionPresentation.DefaultFirstPersonModel, State == Titanfall2RodeoState.FriendlyIdle ? null : first ) )
		{
			Diagnostic = diagnostic + "; interaction cancelled before any gameplay commit";
			if ( BlockingWeapons ) ReleaseRider();
			return;
		}
		Diagnostic = diagnostic;
		_handPropModel = null;
		_handPropShown = false;
		_handPropShowAt = float.PositiveInfinity;
		if ( State is Titanfall2RodeoState.StealingBattery or Titanfall2RodeoState.InsertingBattery )
		{
			_handPropModel = Titanfall2TitanInteractionPresentation.BatteryHandPropModel;
			_handPropAttachment = "R_HAND";
			if ( State == Titanfall2RodeoState.InsertingBattery ) _handPropShowAt = 0;
			else if ( _presentation.TryGetEventFraction( "rodeo_battery_show", out var show ) ) _handPropShowAt = show * _presentation.Duration;
		}
		else if ( State == Titanfall2RodeoState.ThrowingGrenade )
		{
			_handPropModel = Titanfall2TitanInteractionPresentation.GrenadeHandPropModel;
			_handPropAttachment = "PROPGUN";
			if ( _presentation.TryGetEventFraction( "rodeo_battery_grenade_show", out var show ) ) _handPropShowAt = show * _presentation.Duration;
		}
		if ( State == Titanfall2RodeoState.FriendlyIdle ) return;
		var eventName = State switch
		{
			Titanfall2RodeoState.StealingBattery => "rodeo_battery_rip",
			Titanfall2RodeoState.ThrowingGrenade => "rodeo_battery_grenade_damage",
			_ => null
		};
		var cycle = 1f;
		var hasEvent = eventName is null || _presentation.TryGetEventFraction( eventName, out cycle );
		if ( !hasEvent ) cycle = 1;
		var pointOfNoReturn = _presentation.TryGetEventFraction( "RodeoPointOfNoReturn", out var point ) ? point : float.PositiveInfinity;
		ConfigureAnimationTiming( SessionSerial, State, _presentation.Duration, cycle, pointOfNoReturn );
		if ( !hasEvent )
		{
			UsesAuthoredTiming = false;
			Diagnostic += $"; missing {eventName}; commit fallback=clip completion";
		}
	}

	(string Third, string First) AnimationNames()
	{
		var chassis = Actor.GetComponent<Titanfall2TitanKit>()?.Kit ?? Titanfall2TitanKit.Chassis.None;
		var family = chassis is Titanfall2TitanKit.Chassis.Scorch or Titanfall2TitanKit.Chassis.Legion ? "ogre"
			: chassis is Titanfall2TitanKit.Chassis.Northstar or Titanfall2TitanKit.Chassis.Ronin ? "stryder" : "atlas";
		if ( State == Titanfall2RodeoState.Mounting )
		{
			var local = Actor.WorldTransform.PointToLocal( _mountStart );
			var side = MathF.Abs( local.x ) > MathF.Abs( local.y ) ? local.x > 0 ? "front" : "back" : local.y > 0 ? "left" : "right";
			if ( local.z < Actor.BodyHeight * 0.3f && side is "front" or "back" ) side += "_lower";
			return Titanfall2TitanInteractionAssets.RodeoClips( family, State, side );
		}
		return Titanfall2TitanInteractionAssets.RodeoClips( family, State );
	}

	void TickPresentation()
	{
		_presentation?.Tick( StateElapsed, State == Titanfall2RodeoState.FriendlyIdle );
		if ( _presentation is not null && !_handPropShown && _handPropModel is not null && StateElapsed >= _handPropShowAt )
		{
			_handPropShown = true;
			if ( !_presentation.SetHandProp( _handPropModel, _handPropAttachment, true ) ) Diagnostic += "; " + _presentation.Diagnostic;
		}
	}
	void DisposePresentation() { _presentation?.Dispose(); _presentation = null; }

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( camera != Scene.Camera || !BlockingWeapons || !Actor.IsValid() || LocalRider is not { } pilot || _presentation is null
			|| !_presentation.TryGetCamera( out var authored ) ) return;
		var trace = Titanfall2CollisionQueries.Run( Scene.Trace.Ray( pilot.EyeTransform.Position, authored.Position )
			.IgnoreGameObjectHierarchy( pilot.GameObject ).IgnoreGameObjectHierarchy( Actor.GameObject ).WithoutTags( "trigger", "projectile" ), Titanfall2TraceMask.PlayerSolid );
		if ( trace.Hit || trace.StartedSolid ) return;
		view.Position = authored.Position;
		view.Rotation = authored.Rotation;
	}
}
