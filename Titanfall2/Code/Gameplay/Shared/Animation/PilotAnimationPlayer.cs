using Sandbox.Animation;

/// <summary>
/// A viewmodel's ADS pose is sampled from the same zoom value as camera/spread.
/// Retail additive idle and fire stay on top of it, including during ADS reversal.
/// Full-body draw/reload/sprint actions retain their original event time domain.
/// </summary>
internal sealed class Titanfall2PilotAnimationPlayer : IDisposable
{
	internal const string AdsPose = "pilot_ads_pose";
	internal const string IdleDelta = "pilot_idle_delta";
	internal const string FireDelta = "pilot_fire_delta";
	internal const string FireAdsDelta = "pilot_fire_ads_delta";
	internal const string SlideFireDelta = "pilot_slide_fire_delta";
	internal const string SlideFireAdsDelta = "pilot_slide_fire_ads_delta";
	readonly SkinnedModelRenderer _renderer;
	readonly Graph _graph;
	readonly Dictionary<string, Clip> _actions = new( StringComparer.OrdinalIgnoreCase );
	static readonly string[] ShotKeys = ["none", "fire:0", "fire:1"];
	static readonly string[] ActionKeys = ["base", "action:0", "action:1"];
	static readonly string[] ShotTimeKeys = ["shot_time0", "shot_time1"];
	static readonly string[] ActionTimeKeys = ["action_time0", "action_time1"];
	static readonly string[] ActionRoleKeys = ["action_role0", "action_role1"];
	static readonly string[] SlideShotKeys = ["slide_shot0", "slide_shot1"];
	readonly Clip _idle, _fire, _fireAds, _slideFire, _slideFireAds;
	readonly bool _hasStancePose, _hasCrouchAds, _hasReversePose;
	readonly string[] _actionRoles = ["none", "none"];
	readonly bool[] _slideShots = new bool[2];
	readonly double[] _actionStarts = new double[2], _shotStarts = new double[2];
	readonly float[] _shotDurations = new float[2];
	readonly float[] _actionDurations = new float[2];
	readonly bool[] _actionLoops = new bool[2];
	readonly double _idleStart = Time.NowDouble;
	int _actionSlot = 1, _shotSlot = 1;
	string _actionKey = "base";
	bool _firing;
	float _stancePose, _slidePose, _adsOutPose;
	double _lastStanceSample = double.NaN;
	bool _adsOut;
	public float Zoom { get; private set; }
	public int FireSerial { get; private set; }
	internal bool HasSlideFire => _slideFire is not null;
	public bool IsReady => _graph.IsValid() && _renderer.IsValid() && _renderer.Animation.IsValid;
	public float FireCycle => Cycle( Time.NowDouble, _shotStarts[_shotSlot], _shotDurations[_shotSlot], false );
	public float ActionCycle => _actionKey == "base" ? Cycle( Time.NowDouble, _idleStart, _idle.Duration, true )
		: Cycle( Time.NowDouble, _actionStarts[_actionSlot], _actionDurations[_actionSlot], _actionLoops[_actionSlot] );
	public float Duration( string role ) => role is Titanfall2PilotViewModel.Fire or Titanfall2PilotViewModel.SlideFire ? (_shotDurations[_shotSlot] > 0
		? _shotDurations[_shotSlot] : float.Lerp( _fire.Duration, _fireAds.Duration, Zoom ))
		: _actions.GetValueOrDefault( role ?? "" )?.Duration ?? _idle.Duration;

	public Titanfall2PilotAnimationPlayer( SkinnedModelRenderer renderer, Titanfall2PilotViewData data )
	{
		if ( data is not { ContinuousAds: true } || string.IsNullOrWhiteSpace( data.ClipFolder ) )
			throw new InvalidDataException( "Viewmodel has no verified additive ADS data." );
		_renderer = renderer;
		var skeleton = Skeleton.Load( data.Skeleton );
		if ( !skeleton.IsValid() ) throw new InvalidDataException( "Pilot animation skeleton unavailable." );
		var pose = Load( AdsPose );
		var crouchIn = Optional( Titanfall2PilotViewModel.CrouchAdsIn );
		var crouchOut = crouchIn is not null ? Optional( Titanfall2PilotViewModel.CrouchAdsOut ) : null;
		var slideIdle = crouchIn is null ? Optional( Titanfall2PilotViewModel.SlideIdle ) : null;
		_hasCrouchAds = crouchIn is not null;
		_hasStancePose = _hasCrouchAds || slideIdle is not null;
		_hasReversePose = crouchOut is not null;
		_slideFire = Optional( SlideFireDelta );
		_slideFireAds = _slideFire is null ? null : Optional( SlideFireAdsDelta );
		_idle = Load( IdleDelta );
		_fire = Load( FireDelta );
		_fireAds = Load( FireAdsDelta );
		foreach ( var role in data.Clips.Where( IsAction ).Distinct( StringComparer.OrdinalIgnoreCase ) ) _actions[role] = Load( role );
		if ( !_actions.ContainsKey( Titanfall2PilotViewModel.Draw ) || !_actions.ContainsKey( Titanfall2PilotViewModel.Reload )
			|| !_actions.ContainsKey( Titanfall2PilotViewModel.EmptyReload ) ) throw new InvalidDataException( "Pilot action clips unavailable." );

		var builder = Graph.Builder;
		builder.Skeleton = skeleton.Name;
		// All controls precede graph nodes, as required by the native graph builder.
		var zoom = Float( "zoom" );
		var stanceWeight = _hasStancePose ? Float( "stance_weight" ) : null;
		if ( _hasStancePose ) { Float( "stance_pose" ); Float( "slide_pose" ); }
		var adsOutWeight = _hasReversePose ? Float( "ads_out_pose" ) : null;
		var reverseZoom = _hasReversePose ? Float( "ads_out_time" ) : null;
		var slideShots = _slideFire is not null ? SlideShotKeys.Select( Float ).ToArray() : null;
		var idleTime = Float( "idle_time" );
		var idleWeight = builder.AddControlParameter( "idle_weight", GraphBuilder.NodeKind.Float,
			new() { DefaultFloat = 1, Min = 0, Max = 1 } );
		var shotTime = ShotTimeKeys.Select( Float ).ToArray();
		var shotWeight = Float( "shot_weight" );
		var shotId = builder.IdParameter( "shot", ShotKeys );
		var actionTime = ActionTimeKeys.Select( Float ).ToArray();
		var actionId = builder.IdParameter( "action", ActionKeys );
		var actionRoles = _actions.Keys.Where( role => role is not (Titanfall2PilotViewModel.CooldownAds or Titanfall2PilotViewModel.RechamberAds) ).ToArray();
		var roleIds = actionRoles.Prepend( "none" ).ToArray();
		var roleSelectors = ActionRoleKeys.Select( key => builder.IdParameter( key, roleIds ) ).ToArray();

		GraphBuilder.Node basePose = Sample( pose, zoom );
		if ( crouchIn is not null )
		{
			var stancePose = Sample( crouchIn, zoom );
			if ( crouchOut is not null ) stancePose = Blend( adsOutWeight, stancePose, Sample( crouchOut, reverseZoom ) );
			basePose = Blend( stanceWeight, basePose, stancePose );
		}
		else if ( slideIdle is not null )
			basePose = Blend( stanceWeight, basePose, Sample( slideIdle, zoom ) );
		var layers = builder.Layers( basePose );
		layers.AddLayer( builder.TimeControlledClip( _idle, idleTime, new() { SampleRootMotion = false } ),
			new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, Weight = idleWeight, IgnoreEvents = true } );
		var shots = Selector( shotId, ShotKeys, index => index == 0 ? builder.ZeroPose() : ShotPose( index - 1 ), 0.025f );
		layers.AddLayer( shots, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, Weight = shotWeight, IgnoreEvents = true } );
		// Action role is chosen on slot entry. Two slots retain the previous clip
		// during the crossfade while keeping the state machine at three states.
		var action = Selector( actionId, ActionKeys, index => index == 0 ? layers : ActionSlot( index - 1 ), 0.05f );
		_graph = builder.Create( action.Build(), "Titanfall 2 continuous pilot ADS" );
		renderer.Animation.Graph = _graph;
		renderer.UseAnimGraph = true;
		ApplyParameters();
		GraphBuilder.Node ShotPose( int slot )
		{
			// attack_seq has a real ads_blend cell, not a scaled hipfire animation.
			// Both samples share a shot phase so zoom changes never restart recoil.
			var hip = Sample( _fire, shotTime[slot] );
			var ads = Sample( _fireAds, shotTime[slot] );
			if ( _slideFire is not null ) hip = Blend( slideShots[slot], hip, Sample( _slideFire, shotTime[slot] ) );
			if ( _slideFireAds is not null ) ads = Blend( slideShots[slot], ads, Sample( _slideFireAds, shotTime[slot] ) );
			return Blend( zoom, hip, ads );
		}
		GraphBuilder.Node ActionSlot( int slot )
		{
			var options = actionRoles.Select( role => (role, ActionPose( role, slot )) ).ToArray();
			return builder.IdBasedSelector( roleSelectors[slot], options, options[0].Item2 );
		}
		GraphBuilder.Node ActionPose( string role, int slot )
		{
			var poseNode = Sample( _actions[role], actionTime[slot] );
			var adsRole = role switch { Titanfall2PilotViewModel.Cooldown => Titanfall2PilotViewModel.CooldownAds,
				Titanfall2PilotViewModel.Rechamber => Titanfall2PilotViewModel.RechamberAds, _ => null };
			if ( adsRole is null || !_actions.TryGetValue( adsRole, out var ads ) ) return poseNode;
			return Blend( zoom, poseNode, Sample( ads, actionTime[slot] ) );
		}

		GraphBuilder.Node Sample( Clip clip, GraphBuilder.Node time ) => builder.TimeControlledClip( clip, time, new() { SampleRootMotion = false } );
		GraphBuilder.Node Blend( GraphBuilder.Node weight, GraphBuilder.Node from, GraphBuilder.Node to )
		{
			var blend = builder.BlendSpace( weight, looping: false );
			blend.Add( 0, from ); blend.Add( 1, to ); return blend;
		}
		Clip Optional( string role ) => data.Clips.Contains( role ) ? Load( role ) : null;
		Clip Load( string role ) => Clip.Load( $"{data.ClipFolder}/{role}.anim" )
			?? throw new InvalidDataException( $"Pilot clip '{role}' unavailable." );
		GraphBuilder.Node Float( string name ) => builder.AddControlParameter( name, GraphBuilder.NodeKind.Float,
			new() { DefaultFloat = 0, Min = 0, Max = 1 } );
		GraphBuilder.StateMachineBuilder Selector( GraphBuilder.Node id, string[] keys, Func<int, GraphBuilder.Node> node, float fade )
		{
			var machine = builder.StateMachine();
			var states = keys.Select( ( _, index ) => machine.AddState( node( index ) ) ).ToArray();
			for ( var to = 0; to < keys.Length; ++to )
			{
				var condition = builder.IdCompare( id, keys[to] );
				var transition = machine.CreateTransition( states[to], new() { Duration = fade, ClampDuration = false, CanBeForced = true } );
				for ( var from = 0; from < keys.Length; ++from )
					if ( from != to ) machine.AddTransition( states[from], states[to], transition, new GraphBuilder.StateMachineBuilder.TransitionRouteSettings { Condition = condition, CanBeForced = true } );
			}
			return machine;
		}
	}

	static bool IsAction( string role ) => role is Titanfall2PilotViewModel.Draw or Titanfall2PilotViewModel.Reload
		or Titanfall2PilotViewModel.EmptyReload or Titanfall2PilotViewModel.ReloadSegLoop or Titanfall2PilotViewModel.ReloadSegEnd or Titanfall2PilotViewModel.ReloadEmptySegEnd
		or Titanfall2PilotViewModel.Sprint or Titanfall2PilotViewModel.SprintRaise or Titanfall2PilotViewModel.OneHandedIdle
		or Titanfall2PilotViewModel.Charge or Titanfall2PilotViewModel.Cooldown or Titanfall2PilotViewModel.CooldownAds
		or Titanfall2PilotViewModel.Rechamber or Titanfall2PilotViewModel.RechamberAds
		|| Titanfall2PilotViewModel.IsOrdnanceAction( role ) || Titanfall2PilotViewModel.IsStanceAction( role )
		|| role.StartsWith( Titanfall2PilotViewModel.EmptyReload + "_late", StringComparison.Ordinal );
	internal static float Cycle( double now, double started, float duration, bool loop )
	{
		if ( !double.IsFinite( now ) || !double.IsFinite( started ) || !float.IsFinite( duration ) || duration <= 0 ) return 0;
		var cycle = Math.Max( 0, (now - started) / duration );
		return (float)(loop ? cycle % 1 : Math.Clamp( cycle, 0, 1 ));
	}
	public void SetZoom( float zoom )
	{
		zoom = float.IsFinite( zoom ) ? Math.Clamp( zoom, 0, 1 ) : 0;
		if ( zoom == Zoom ) return;
		if ( MathF.Abs( zoom - Zoom ) > 0.0001f )
		{
			// A fresh crouch ADS-in from hipfire (or ADS-out from full zoom) starts on
			// its own authored curve; only mid-transition reversals crossfade the two.
			if ( Zoom <= 0.001f && zoom > Zoom ) _adsOutPose = 0f;
			else if ( Zoom >= 0.999f && zoom < Zoom ) _adsOutPose = 1f;
			_adsOut = zoom < Zoom;
		}
		Zoom = zoom;
		ApplyParameters();
	}
	public void SetStancePose( bool crouching, bool sliding, bool aiming )
	{
		if ( !_hasStancePose || Time.NowDouble == _lastStanceSample ) return;
		var firstSample = !double.IsFinite( _lastStanceSample ) || Time.NowDouble < _lastStanceSample;
		_lastStanceSample = Time.NowDouble;
		// Reach the clip exactly and stop touching it at the steady endpoint.
		var step = firstSample ? 1 : Math.Clamp( Time.Delta * 14f, 0, 1 );
		var crouch = MoveTowards( _stancePose, crouching ? 1 : 0, step );
		var slide = MoveTowards( _slidePose, sliding ? 1 : 0, step );
		// During a reversal, crossfade both authored paths at the same zoom.
		var outPose = _hasReversePose ? MoveTowards( _adsOutPose, _adsOut || !aiming ? 1 : 0, step ) : 0f;
		if ( crouch == _stancePose && slide == _slidePose && outPose == _adsOutPose ) return;
		(_stancePose, _slidePose, _adsOutPose) = (crouch, slide, outPose);
		ApplyParameters();
	}
	static float MoveTowards( float from, float to, float amount ) =>
		from < to ? MathF.Min( to, from + amount ) : MathF.Max( to, from - amount );

	internal static float StanceBlend( float crouch, float slide, float zoom, bool hasCrouchAds )
	{
		// Static crouch has the standing idle cells. Use the authored crouch
		// path only once it is blending toward ADS. A slide starts at its own
		// canted hip endpoint. Both always meet the regular full ADS pose.
		var entering = hasCrouchAds ? Smooth( zoom * 4f ) : 0f;
		return float.Lerp( crouch * entering, 1f, slide ) * Smooth( (1 - zoom) * 8f );
		static float Smooth( float x )
		{
			var t = Math.Clamp( x, 0, 1 );
			return t * t * (3 - 2 * t);
		}
	}

	public void Play( string role, float? seconds = null, float elapsed = 0 )
	{
		if ( role is Titanfall2PilotViewModel.Fire or Titanfall2PilotViewModel.SlideFire )
		{
			_actionKey = "base";
			_shotSlot = 1 - _shotSlot;
			_slideShots[_shotSlot] = role == Titanfall2PilotViewModel.SlideFire && _slideFire is not null;
			_shotStarts[_shotSlot] = Time.NowDouble;
			// Freeze the shot's duration at launch: changing zoom during it changes
			// the pose blend without jumping its normalized phase or event cursor.
			var hipDuration = _slideShots[_shotSlot] ? _slideFire.Duration : _fire.Duration;
			var adsDuration = _slideShots[_shotSlot] && _slideFireAds is not null ? _slideFireAds.Duration : _fireAds.Duration;
			_shotDurations[_shotSlot] = float.Lerp( hipDuration, adsDuration, Zoom );
			_firing = true;
			FireSerial++;
		}
		else if ( _actions.TryGetValue( role, out var clip ) && role is not (Titanfall2PilotViewModel.CooldownAds or Titanfall2PilotViewModel.RechamberAds) )
		{
			_actionSlot = 1 - _actionSlot;
			_actionRoles[_actionSlot] = role;
			_actionKey = ActionKeys[_actionSlot + 1];
			_actionDurations[_actionSlot] = seconds is > 0.05f && float.IsFinite( seconds.Value ) ? seconds.Value : clip.Duration;
			_actionStarts[_actionSlot] = Time.NowDouble - Math.Clamp( elapsed, 0, MathF.Max( 0, _actionDurations[_actionSlot] ) );
			_actionLoops[_actionSlot] = role is Titanfall2PilotViewModel.Sprint or Titanfall2PilotViewModel.OneHandedIdle or Titanfall2PilotViewModel.Charge or Titanfall2PilotViewModel.TossHold;
			_firing = false;
		}
		else
		{
			_actionKey = "base";
			_firing = false;
		}
		ApplyParameters();
	}
	public void ApplyParameters()
	{
		if ( !_renderer.IsValid() || !_renderer.Animation.IsValid ) return;
		var now = Time.NowDouble;
		var parameters = _renderer.Animation.Parameters;
		parameters.TrySet( "zoom", Zoom );
		if ( _hasStancePose )
		{
			parameters.TrySet( "stance_pose", _stancePose );
			parameters.TrySet( "slide_pose", _slidePose );
			parameters.TrySet( "stance_weight", StanceBlend( _stancePose, _slidePose, Zoom, _hasCrouchAds ) );
		}
		if ( _hasReversePose )
		{
			parameters.TrySet( "ads_out_pose", _adsOutPose );
			parameters.TrySet( "ads_out_time", 1f - Zoom );
		}
		parameters.TrySet( "idle_time", Cycle( now, _idleStart, _idle.Duration, true ) );
		// Retail idle_seq_autoplay blends from hipfire sway (anim[0]) to steady ADS (anim[1] = 0 delta).
		parameters.TrySet( "idle_weight", 1f - Zoom );
		var remaining = _shotDurations[_shotSlot] - (float)(now - _shotStarts[_shotSlot]);
		var weight = _firing ? Math.Clamp( remaining / 0.05f, 0, 1 ) : 0;
		parameters.TrySet( "shot", ShotKeys[weight > 0 ? _shotSlot + 1 : 0] );
		parameters.TrySet( "shot_weight", weight );
		parameters.TrySet( "action", _actionKey );
		for ( var i = 0; i < 2; ++i )
		{
			if ( _slideFire is not null ) parameters.TrySet( SlideShotKeys[i], _slideShots[i] ? 1f : 0f );
			parameters.TrySet( ActionRoleKeys[i], _actionRoles[i] );
			parameters.TrySet( ShotTimeKeys[i], Cycle( now, _shotStarts[i], _shotDurations[i], false ) );
			parameters.TrySet( ActionTimeKeys[i], Cycle( now, _actionStarts[i], _actionDurations[i], _actionLoops[i] ) );
		}
	}
	public void Dispose()
	{
		if ( _renderer.IsValid() && _renderer.Animation.Graph == _graph ) { _renderer.Animation.Graph = null; _renderer.UseAnimGraph = false; }
		_graph?.Dispose();
	}
}
