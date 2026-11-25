using Sandbox.Animation;

/// <summary>
/// TF2 adaptation of SourceAnimationPlayer's two-slot selectors and masked overlays.
/// Uses MDL53 role clips; Source 1 acttables and IK descriptors are not interchangeable.
/// </summary>
internal sealed class Titanfall2ActorAnimationPlayer : IDisposable
{
	readonly SkinnedModelRenderer _renderer;
	readonly Dictionary<string, Clip> _clips = new( StringComparer.OrdinalIgnoreCase );
	readonly Graph _graph;
	readonly Channel _body = new( "body" ), _upper = new( "upper" ), _reload = new( "reload" );
	bool _reloadRequested;
	sealed class Channel( string name )
	{
		public readonly string Name = name;
		public string Clip;
		public int Slot = 1;
		public float Elapsed, Weight;
		public float[] Rates = [1, 1];
		public bool Active, Loop;
	}
	public string Current => _body.Clip;
	public float Duration( string role ) => _clips.GetValueOrDefault( role ?? "" )?.Duration ?? 0;
	public bool Has( string role ) => role is not null && _clips.ContainsKey( role );
	public bool Finished => _body.Elapsed * _body.Rates[_body.Slot] >= Duration( Current );
	public bool HasUpperMask { get; }

	public Titanfall2ActorAnimationPlayer( SkinnedModelRenderer renderer, Titanfall2ActorAnimationData data )
	{
		_renderer = renderer;
		var skeleton = Skeleton.Load( data.Skeleton );
		if ( !skeleton.IsValid() ) throw new InvalidDataException( "Actor skeleton unavailable" );
		foreach ( var metadata in data.Clips )
			if ( !string.IsNullOrEmpty( metadata.Path ) && Clip.Load( metadata.Path ) is { } clip ) _clips[metadata.Role] = clip;
		if ( !Has( ModelLoader.IdleRole ) ) throw new InvalidDataException( "Actor idle unavailable" );
		var bones = renderer.Model.Bones.AllBones.ToArray();
		HasUpperMask = bones.Any( bone => bone.Name.Contains( "spine", StringComparison.OrdinalIgnoreCase ) || bone.Name.Contains( "chest", StringComparison.OrdinalIgnoreCase ) );
		var builder = Graph.Builder;
		builder.Skeleton = skeleton.Name;
		// AG2 requires every control parameter before the first pose/graph node.
		var choicesByChannel = new Dictionary<Channel, (string Role, int Slot)[]>();
		var keysByChannel = new Dictionary<Channel, string[]>();
		var ids = new Dictionary<Channel, GraphBuilder.Node>();
		var ratesByChannel = new Dictionary<Channel, GraphBuilder.Node[]>();
		var weights = new Dictionary<Channel, GraphBuilder.Node>();
		Parameters( _body, _clips.Keys.OrderBy( x => x == ModelLoader.IdleRole ? 0 : 1 ), false );
		Parameters( _upper, [ModelLoader.FireRole], true );
		Parameters( _reload, [ModelLoader.ReloadRole], true );
		var body = Selector( _body, false );
		var layers = builder.Layers( body.Build() );
		AddOverlay( _upper );
		AddOverlay( _reload );
		_graph = builder.Create( layers, "Titanfall 2 actor layers" );
		renderer.Animation.Graph = _graph;
		renderer.UseAnimGraph = true;
		Play( ModelLoader.IdleRole, loop: true );

		void Parameters( Channel channel, IEnumerable<string> roles, bool overlay )
		{
			var choices = roles.Where( Has ).SelectMany( role => new[] { (Role: role, Slot: 0), (Role: role, Slot: 1) } ).ToArray();
			var keys = choices.Select( x => $"{x.Role}:{x.Slot}" ).ToList();
			if ( overlay ) keys.Insert( 0, "none" );
			choicesByChannel[channel] = choices;
			keysByChannel[channel] = keys.ToArray();
			ids[channel] = builder.IdParameter( channel.Name, keys.ToArray() );
			ratesByChannel[channel] = Enumerable.Range( 0, 2 ).Select( slot => builder.AddControlParameter( $"{channel.Name}_rate{slot}", GraphBuilder.NodeKind.Float,
				new() { DefaultFloat = 1, Min = 0.01f, Max = 8 } ) ).ToArray();
			if ( overlay ) weights[channel] = builder.AddControlParameter( channel.Name + "_weight", GraphBuilder.NodeKind.Float, new() { DefaultFloat = 0, Min = 0, Max = 1 } );
		}
		GraphBuilder.StateMachineBuilder Selector( Channel channel, bool overlay )
		{
			var choices = choicesByChannel[channel];
			var keys = keysByChannel[channel];
			var id = ids[channel];
			var rates = ratesByChannel[channel];
			var machine = builder.StateMachine();
			var states = keys.Select( ( key, index ) =>
			{
				if ( key == "none" ) return machine.AddState( builder.ReferencePose() );
				var choice = choices[index - (overlay ? 1 : 0)];
				var loop = choice.Role == ModelLoader.IdleRole || ModelLoader.IsLocomotionRole( choice.Role );
				return machine.AddState( builder.SpeedScale( builder.Clip( _clips[choice.Role], new() { Looping = loop, SampleRootMotion = false } ), rates[choice.Slot] ) );
			} ).ToArray();
			for ( var to = 0; to < states.Length; to++ )
				for ( var from = 0; from < states.Length; from++ )
					if ( from != to ) machine.AddTransition( states[from], states[to], builder.IdCompare( id, keys[to] ),
						new GraphBuilder.TransitionSettings { Duration = 0.1f, ClampDuration = false, CanBeForced = true } );
			return machine;
		}
		void AddOverlay( Channel channel )
		{
			layers.AddLayer( Selector( channel, true ), new() { BlendMode = GraphBuilder.LayerBlendMode.Overlay,
				Weight = weights[channel], BoneMask = builder.BoneMask( "tf2_upper" ), IgnoreEvents = true } );
		}
	}

	public void Play( string role, float rate = 1, bool loop = false, bool restart = false ) => Select( _body, role, rate, loop, restart );
	public void Fire( bool restart ) { if ( HasUpperMask ) Select( _upper, ModelLoader.FireRole, 1, false, restart ); }
	public void Reload( bool active, float seconds )
	{
		if ( active && !_reloadRequested && HasUpperMask ) Select( _reload, ModelLoader.ReloadRole, Duration( ModelLoader.ReloadRole ) / MathF.Max( 0.1f, seconds ), false, true );
		if ( !active ) _reload.Active = false;
		_reloadRequested = active;
		if ( active ) _upper.Active = false;
	}
	public void ClearOverlays() { _upper.Active = false; _reload.Active = false; _reloadRequested = false; }
	void Select( Channel channel, string role, float rate, bool loop, bool restart )
	{
		if ( !Has( role ) ) return;
		if ( channel.Clip != role || restart || !channel.Active )
		{
			channel.Clip = role;
			channel.Slot = 1 - channel.Slot;
			channel.Elapsed = 0;
		}
		channel.Active = true;
		channel.Loop = loop;
		channel.Rates[channel.Slot] = float.IsFinite( rate ) ? Math.Clamp( rate, 0.01f, 8 ) : 1;
	}
	public void Tick()
	{
		if ( !_renderer.IsValid() || !_renderer.Animation.IsValid ) return;
		foreach ( var channel in new[] { _body, _upper, _reload } )
		{
			var overlay = channel != _body;
			var remaining = Duration( channel.Clip ) / channel.Rates[channel.Slot] - channel.Elapsed;
			if ( overlay && remaining <= 0 ) channel.Active = false;
			var target = channel.Active ? channel.Loop ? 1 : Math.Clamp( remaining / 0.1f, 0, 1 ) : 0;
			channel.Weight = Math.Clamp( channel.Weight + Math.Clamp( target - channel.Weight, -Time.Delta * 10, Time.Delta * 10 ), 0, 1 );
			var parameters = _renderer.Animation.Parameters;
			parameters.TrySet( channel.Name, overlay && channel.Weight <= 0 ? "none" : $"{channel.Clip}:{channel.Slot}" );
			parameters.TrySet( channel.Name + "_rate0", channel.Rates[0] );
			parameters.TrySet( channel.Name + "_rate1", channel.Rates[1] );
			if ( overlay ) parameters.TrySet( channel.Name + "_weight", channel.Weight );
			channel.Elapsed += Time.Delta;
		}
	}
	public void Dispose()
	{
		if ( _renderer.IsValid() && _renderer.Animation.Graph == _graph ) { _renderer.Animation.Graph = null; _renderer.UseAnimGraph = false; }
		_graph?.Dispose();
	}
}
