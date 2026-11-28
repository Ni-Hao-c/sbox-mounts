public sealed partial class Titanfall2Weapon
{
	[Property] public Titanfall2ViewMotionProfile ViewMotion { get; set; }
}

/// <summary>Retail sway/bob state from client.dll 0x1805939C0, without engine object dependencies.</summary>
internal sealed class Titanfall2ViewMotionState
{
	readonly Vector3[] _angularVelocities = new Vector3[5];
	int _sample;
	Angles _previousAngles;
	bool _ready;
	double _lastTime = double.NaN;
	float _bobPhase, _verticalWave, _horizontalWave, _verticalBob, _horizontalBob;
	internal Vector3 SwayTranslation { get; private set; }
	internal Vector3 SwayRotation { get; private set; }
	internal Vector3 BobTranslation { get; private set; }
	internal Vector3 BobRotation { get; private set; }
	internal float AttachmentBlend { get; private set; }
	internal float BobPhase => _bobPhase;
	internal Vector3 Translation => SwayTranslation + BobTranslation;
	internal Vector3 Rotation => SwayRotation + BobRotation;

	internal void Reset()
	{
		Array.Clear( _angularVelocities ); _sample = 0; _ready = false; _lastTime = double.NaN;
		_bobPhase = _verticalWave = _horizontalWave = _verticalBob = _horizontalBob = 0;
		SwayTranslation = SwayRotation = BobTranslation = BobRotation = default; AttachmentBlend = 0;
	}

	internal void Step( in Titanfall2ViewMotionProfile profile, double time, float delta, float tickInterval,
		Angles viewAngles, Vector3 worldVelocity, float zoom, bool grounded, bool sliding )
	{
		if ( !profile.Enabled ) { Reset(); return; }
		if ( !double.IsFinite( time ) || !float.IsFinite( delta ) || delta <= 0 || !worldVelocity.IsFinite
			|| !float.IsFinite( viewAngles.pitch ) || !float.IsFinite( viewAngles.yaw ) || !float.IsFinite( viewAngles.roll ) ) return;
		if ( time == _lastTime ) return; // Camera composition may run more than once in one frame.
		if ( !_ready || time < _lastTime )
		{
			Reset(); _previousAngles = viewAngles; _ready = true;
		}
		_lastTime = time;
		zoom = float.IsFinite( zoom ) ? Math.Clamp( zoom, 0, 1 ) : 0;
		var ads = profile.ZoomedEnabled && zoom > 0;
		// Retail switches the authored input block as ADS begins. The angular
		// envelope closes over the first 5% of ADS; the pivot has its own timer.
		var settings = ads ? profile.Ads : profile.Hip;
		var targetAttachment = ads ? 1f : 0f;
		AttachmentBlend = settings.AttachmentBlendTime > 0
			? MoveTowards( AttachmentBlend, targetAttachment, delta / settings.AttachmentBlendTime ) : targetAttachment;
		var localVelocity = viewAngles.ToRotation().Inverse * worldVelocity;
		StepBob( settings.Bob, localVelocity.WithZ( 0 ).Length, delta, grounded && !sliding );
		var translation = Vector3.Zero; var rotation = Vector3.Zero;
		Add( localVelocity.x * delta, 0, 1 );
		Add( localVelocity.y * delta, 2, 3 );
		Add( localVelocity.z * delta, 4, 5 );
		_sample = (_sample + 1) % _angularVelocities.Length;
		_angularVelocities[_sample] = new Vector3( AngleDelta( viewAngles.pitch, _previousAngles.pitch ),
			AngleDelta( viewAngles.yaw, _previousAngles.yaw ), AngleDelta( viewAngles.roll, _previousAngles.roll ) ) / delta;
		_previousAngles = viewAngles;
		var angularVelocity = Vector3.Zero;
		foreach ( var sample in _angularVelocities ) angularVelocity += sample * 0.2f;
		var tick = float.IsFinite( tickInterval ) && tickInterval > 0 ? tickInterval : delta;
		Add( angularVelocity.y * tick, 6, 7 );
		Add( angularVelocity.x * tick, 9, 8 ); // Source positive pitch looks down.
		translation = Clamp( translation, settings.TranslationMinimum, settings.TranslationMaximum );
		rotation = Clamp( Normalize( rotation ), settings.RotationMinimum, settings.RotationMaximum );
		SwayTranslation = Smooth( SwayTranslation, translation, settings.TranslationGain, delta );
		var angularStep = new Vector3( AngleDelta( rotation.x, SwayRotation.x ), AngleDelta( rotation.y, SwayRotation.y ), AngleDelta( rotation.z, SwayRotation.z ) );
		SwayRotation += angularStep * (1 - MathF.Exp( -settings.RotationGain * delta ));
		if ( profile.ZoomedEnabled )
		{
			var blend = Math.Clamp( zoom * 20, 0, 1 );
			SwayRotation = Clamp( SwayRotation, Vector3.Lerp( profile.Hip.RotationMinimum, profile.Ads.RotationMinimum, blend ),
				Vector3.Lerp( profile.Hip.RotationMaximum, profile.Ads.RotationMaximum, blend ) );
		}

		void Add( float amount, int positive, int negative )
		{
			var index = amount >= 0 ? positive : negative;
			if ( settings.Directions is null || index >= settings.Directions.Length ) return;
			var contribution = settings.Directions[index];
			translation += contribution.Translation * MathF.Abs( amount );
			rotation += contribution.Rotation * MathF.Abs( amount );
		}
	}

	void StepBob( Titanfall2ViewBobProfile bob, float speed, float delta, bool grounded )
	{
		var speedFraction = bob.MaximumSpeed == bob.MinimumSpeed ? (speed >= bob.MaximumSpeed ? 1f : 0f)
			: Math.Clamp( (speed - bob.MinimumSpeed) / (bob.MaximumSpeed - bob.MinimumSpeed), 0, 1 );
		var vertical = 0f; var horizontal = 0f;
		if ( grounded && bob.CycleTime > 0 && speedFraction > 0 )
		{
			_bobPhase = (_bobPhase + speedFraction * delta) % (bob.CycleTime * 2);
			vertical = MathF.Sin( (_bobPhase % bob.CycleTime) * MathF.Tau / bob.CycleTime );
			horizontal = MathF.Sin( _bobPhase * MathF.Tau / (bob.CycleTime * 2) );
		}
		else
		{
			var rest = _bobPhase > bob.CycleTime * 1.5f ? bob.CycleTime * 2
				: _bobPhase < bob.CycleTime * 0.5f ? 0 : bob.CycleTime;
			_bobPhase = Smooth( _bobPhase, rest, bob.Gain, delta );
		}
		_verticalWave = Smooth( _verticalWave, vertical, bob.Gain, delta );
		_horizontalWave = Smooth( _horizontalWave, horizontal, bob.Gain, delta );
		_verticalBob = Smooth( _verticalBob, vertical * bob.VerticalDistance, bob.Gain, delta );
		_horizontalBob = Smooth( _horizontalBob, horizontal * bob.HorizontalDistance, bob.Gain, delta );
		BobTranslation = new( 0, _horizontalBob, _verticalBob );
		BobRotation = new( _verticalWave * bob.Rotation.x, _horizontalWave * bob.Rotation.y, _horizontalWave * bob.Rotation.z );
	}

	internal static Transform AroundPivot( Transform pivot, Vector3 translation, Vector3 angles )
	{
		// client.dll 0x180592680: P * R * inverse(P), then view-local translation.
		var rotation = global::Rotation.From( new Angles( angles.x, angles.y, angles.z ) );
		var delta = pivot.ToWorld( new Transform( Vector3.Zero, rotation ) ).ToWorld( pivot.ToLocal( Transform.Zero ) );
		return delta.WithPosition( delta.Position + translation );
	}
	static float MoveTowards( float value, float target, float step ) => value < target ? MathF.Min( target, value + step ) : MathF.Max( target, value - step );
	static float Smooth( float value, float target, float gain, float delta ) => target + (value - target) * MathF.Exp( -gain * delta );
	static Vector3 Smooth( Vector3 value, Vector3 target, float gain, float delta ) => target + (value - target) * MathF.Exp( -gain * delta );
	static Vector3 Clamp( Vector3 value, Vector3 min, Vector3 max ) => new( MathF.Min( MathF.Max( value.x, min.x ), max.x ),
		MathF.Min( MathF.Max( value.y, min.y ), max.y ), MathF.Min( MathF.Max( value.z, min.z ), max.z ) );
	static float AngleDelta( float target, float current ) => NormalizeAngle( target - current );
	static Vector3 Normalize( Vector3 value ) => new( NormalizeAngle( value.x ), NormalizeAngle( value.y ), NormalizeAngle( value.z ) );
	static float NormalizeAngle( float value ) { value %= 360; return value > 180 ? value - 360 : value < -180 ? value + 360 : value; }
}

public sealed partial class Titanfall2PilotViewModel
{
	readonly Titanfall2ViewMotionState _viewMotion = new();
	Titanfall2PilotCameraAnchor _motionHipAnchor, _motionAdsAnchor;
	string _motionHipAttachment, _motionAdsAttachment;

	Transform ApplyWeaponMotion( Transform camera, Transform localCamera, float zoom, in Titanfall2PilotViewPoseState pose )
	{
		if ( !_owner.IsValid() || _owner.ViewMotion is not { Enabled: true } profile || !_owner.Owner.IsValid() ) return camera;
		var player = _owner.Owner;
		// Use input eye angles rather than recoil-composed render angles, avoiding
		// feeding a weapon's own recoil back through its sway filter.
		_viewMotion.Step( profile, Time.NowDouble, Time.Delta, Scene.FixedDelta, player.EyeAngles, player.Velocity, zoom, player.IsOnGround, pose.IsSliding );
		if ( _motionHipAnchor is null || _motionHipAttachment != profile.Hip.RotateAttachment )
		{
			_motionHipAttachment = profile.Hip.RotateAttachment; _motionHipAnchor = new( _motionHipAttachment );
		}
		if ( _motionAdsAnchor is null || _motionAdsAttachment != profile.Ads.RotateAttachment )
		{
			_motionAdsAttachment = profile.Ads.RotateAttachment; _motionAdsAnchor = new( _motionAdsAttachment );
		}
		var hip = _motionHipAnchor.TryRead( Renderer, out var hipAnchor ) ? localCamera.ToLocal( hipAnchor ) : global::Transform.Zero;
		var ads = _motionAdsAnchor.TryRead( Renderer, out var adsAnchor ) ? localCamera.ToLocal( adsAnchor ) : global::Transform.Zero;
		var pivot = new Transform( Vector3.Lerp( hip.Position, ads.Position, _viewMotion.AttachmentBlend ),
			Rotation.Slerp( hip.Rotation, ads.Rotation, _viewMotion.AttachmentBlend ) );
		return camera.ToWorld( Titanfall2ViewMotionState.AroundPivot( pivot, _viewMotion.Translation, _viewMotion.Rotation ) );
	}
}
