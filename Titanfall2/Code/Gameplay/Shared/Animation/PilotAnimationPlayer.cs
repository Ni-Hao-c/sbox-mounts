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
	readonly SkinnedModelRenderer _renderer;
	readonly Graph _graph;
	readonly Dictionary<string, Clip> _actions = new( StringComparer.OrdinalIgnoreCase );
	readonly Clip _idle, _fire;
	readonly double[] _actionStarts = new double[2], _shotStarts = new double[2];
	readonly float[] _actionDurations = new float[2];
	readonly bool[] _actionLoops = new bool[2];
	readonly double _idleStart = Time.NowDouble;
	int _actionSlot = 1, _shotSlot = 1;
	string _actionKey = "base";
	bool _firing;
	public float Zoom { get; private set; }
	public int FireSerial { get; private set; }
	public bool IsReady => _graph.IsValid() && _renderer.IsValid() && _renderer.Animation.IsValid;
	public float FireCycle => Cycle( Time.NowDouble, _shotStarts[_shotSlot], _fire.Duration, false );
	public float ActionCycle => _actionKey == "base" ? Cycle( Time.NowDouble, _idleStart, _idle.Duration, true )
		: Cycle( Time.NowDouble, _actionStarts[_actionSlot], _actionDurations[_actionSlot], _actionLoops[_actionSlot] );
	public float Duration( string role ) => role == Titanfall2PilotViewModel.Fire ? _fire.Duration
		: _actions.GetValueOrDefault( role ?? "" )?.Duration ?? _idle.Duration;

	public Titanfall2PilotAnimationPlayer( SkinnedModelRenderer renderer, Titanfall2PilotViewData data )
	{
		if ( data is not { ContinuousAds: true } || string.IsNullOrWhiteSpace( data.ClipFolder ) )
			throw new InvalidDataException( "Viewmodel has no verified additive ADS data." );
		_renderer = renderer;
		var skeleton = Skeleton.Load( data.Skeleton );
		if ( !skeleton.IsValid() ) throw new InvalidDataException( "Pilot animation skeleton unavailable." );
		var pose = Load( AdsPose );
		_idle = Load( IdleDelta );
		_fire = Load( FireDelta );
		foreach ( var role in data.Clips.Where( IsAction ).Distinct( StringComparer.OrdinalIgnoreCase ) ) _actions[role] = Load( role );
		if ( !_actions.ContainsKey( Titanfall2PilotViewModel.Draw ) || !_actions.ContainsKey( Titanfall2PilotViewModel.Reload )
			|| !_actions.ContainsKey( Titanfall2PilotViewModel.EmptyReload ) ) throw new InvalidDataException( "Pilot action clips unavailable." );

		var builder = Graph.Builder;
		builder.Skeleton = skeleton.Name;
		// All controls precede graph nodes, as required by the native graph builder.
		var zoom = Float( "zoom" );
		var idleTime = Float( "idle_time" );
		var shotTime = new[] { Float( "shot_time0" ), Float( "shot_time1" ) };
		var shotWeight = Float( "shot_weight" );
		var shotKeys = new[] { "none", "fire:0", "fire:1" };
		var shotId = builder.IdParameter( "shot", shotKeys );
		var actionTime = new[] { Float( "action_time0" ), Float( "action_time1" ) };
		var actionChoices = _actions.Keys.SelectMany( role => new[] { (Role: role, Slot: 0), (Role: role, Slot: 1) } ).ToArray();
		var actionKeys = actionChoices.Select( choice => $"{choice.Role}:{choice.Slot}" ).Prepend( "base" ).ToArray();
		var actionId = builder.IdParameter( "action", actionKeys );

		var layers = builder.Layers( builder.TimeControlledClip( pose, zoom, new() { SampleRootMotion = false } ) );
		layers.AddLayer( builder.TimeControlledClip( _idle, idleTime, new() { SampleRootMotion = false } ),
			new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, IgnoreEvents = true } );
		var shots = Selector( shotId, shotKeys, index => index == 0 ? builder.ZeroPose()
			: builder.TimeControlledClip( _fire, shotTime[index - 1], new() { SampleRootMotion = false } ), 0.025f );
		layers.AddLayer( shots, new() { BlendMode = GraphBuilder.LayerBlendMode.Additive, Weight = shotWeight, IgnoreEvents = true } );
		var action = Selector( actionId, actionKeys, index => index == 0 ? layers : ActionPose( actionChoices[index - 1] ), 0.05f );
		_graph = builder.Create( action.Build(), "Titanfall 2 continuous pilot ADS" );
		renderer.Animation.Graph = _graph;
		renderer.UseAnimGraph = true;
		ApplyParameters();
		GraphBuilder.Node ActionPose( (string Role, int Slot) choice )
		{
			var poseNode = builder.TimeControlledClip( _actions[choice.Role], actionTime[choice.Slot], new() { SampleRootMotion = false } );
			if ( choice.Role != Titanfall2PilotViewModel.Cooldown || !_actions.TryGetValue( Titanfall2PilotViewModel.CooldownAds, out var ads ) ) return poseNode;
			var blend = builder.BlendSpace( zoom, looping: false );
			blend.Add( 0, poseNode ); blend.Add( 1, builder.TimeControlledClip( ads, actionTime[choice.Slot], new() { SampleRootMotion = false } ) );
			return blend;
		}

		Clip Load( string role ) => Clip.Load( $"{data.ClipFolder}/{role}.anim" )
			?? throw new InvalidDataException( $"Pilot clip '{role}' unavailable." );
		GraphBuilder.Node Float( string name ) => builder.AddControlParameter( name, GraphBuilder.NodeKind.Float,
			new() { DefaultFloat = 0, Min = 0, Max = 1 } );
		GraphBuilder.StateMachineBuilder Selector( GraphBuilder.Node id, string[] keys, Func<int, GraphBuilder.Node> node, float fade )
		{
			var machine = builder.StateMachine();
			var states = keys.Select( ( _, index ) => machine.AddState( node( index ) ) ).ToArray();
			for ( var to = 0; to < keys.Length; ++to )
				for ( var from = 0; from < keys.Length; ++from )
					if ( from != to ) machine.AddTransition( states[from], states[to], builder.IdCompare( id, keys[to] ),
						new GraphBuilder.TransitionSettings { Duration = fade, ClampDuration = false, CanBeForced = true } );
			return machine;
		}
	}

	static bool IsAction( string role ) => role is Titanfall2PilotViewModel.Draw or Titanfall2PilotViewModel.Reload
		or Titanfall2PilotViewModel.EmptyReload or Titanfall2PilotViewModel.ReloadSegLoop or Titanfall2PilotViewModel.ReloadSegEnd or Titanfall2PilotViewModel.ReloadEmptySegEnd
		or Titanfall2PilotViewModel.Sprint or Titanfall2PilotViewModel.SprintRaise or Titanfall2PilotViewModel.OneHandedIdle
		or Titanfall2PilotViewModel.Charge or Titanfall2PilotViewModel.Cooldown or Titanfall2PilotViewModel.CooldownAds
		|| role.StartsWith( Titanfall2PilotViewModel.EmptyReload + "_late", StringComparison.Ordinal );
	internal static float Cycle( double now, double started, float duration, bool loop )
	{
		if ( !double.IsFinite( now ) || !double.IsFinite( started ) || !float.IsFinite( duration ) || duration <= 0 ) return 0;
		var cycle = Math.Max( 0, (now - started) / duration );
		return (float)(loop ? cycle % 1 : Math.Clamp( cycle, 0, 1 ));
	}
	public void SetZoom( float zoom ) { Zoom = float.IsFinite( zoom ) ? Math.Clamp( zoom, 0, 1 ) : 0; ApplyParameters(); }
	public void Play( string role, float? seconds = null, float elapsed = 0 )
	{
		if ( role == Titanfall2PilotViewModel.Fire )
		{
			_actionKey = "base";
			_shotSlot = 1 - _shotSlot;
			_shotStarts[_shotSlot] = Time.NowDouble;
			_firing = true;
			FireSerial++;
		}
		else if ( _actions.TryGetValue( role, out var clip ) )
		{
			_actionSlot = 1 - _actionSlot;
			_actionKey = $"{role}:{_actionSlot}";
			_actionDurations[_actionSlot] = seconds is > 0.05f && float.IsFinite( seconds.Value ) ? seconds.Value : clip.Duration;
			_actionStarts[_actionSlot] = Time.NowDouble - Math.Clamp( elapsed, 0, MathF.Max( 0, _actionDurations[_actionSlot] ) );
			_actionLoops[_actionSlot] = role is Titanfall2PilotViewModel.Sprint or Titanfall2PilotViewModel.OneHandedIdle or Titanfall2PilotViewModel.Charge;
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
		parameters.TrySet( "idle_time", Cycle( now, _idleStart, _idle.Duration, true ) );
		var remaining = _fire.Duration - (float)(now - _shotStarts[_shotSlot]);
		var weight = _firing ? Math.Clamp( remaining / 0.05f, 0, 1 ) : 0;
		parameters.TrySet( "shot", weight > 0 ? $"fire:{_shotSlot}" : "none" );
		parameters.TrySet( "shot_weight", weight );
		parameters.TrySet( "action", _actionKey );
		for ( var i = 0; i < 2; ++i )
		{
			parameters.TrySet( $"shot_time{i}", Cycle( now, _shotStarts[i], _fire.Duration, false ) );
			parameters.TrySet( $"action_time{i}", Cycle( now, _actionStarts[i], _actionDurations[i], _actionLoops[i] ) );
		}
	}
	public void Dispose()
	{
		if ( _renderer.IsValid() && _renderer.Animation.Graph == _graph ) { _renderer.Animation.Graph = null; _renderer.UseAnimGraph = false; }
		_graph?.Dispose();
	}
}
