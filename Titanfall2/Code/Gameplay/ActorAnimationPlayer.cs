using Sandbox.Animation;

/// <summary>
/// MDL53 role sequences with original aim grids, authored masks and phase-preserving
/// two-slot selectors. Source 1 acttables and IK descriptors are not interchangeable.
/// </summary>
internal sealed class Titanfall2ActorAnimationPlayer : IDisposable
{
	readonly SkinnedModelRenderer _renderer;
	readonly Dictionary<string, Clip> _clips = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, Titanfall2ActorAnimationData.Clip> _metadata = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, Titanfall2ActorAnimationData.AimLayer> _aims = new( StringComparer.OrdinalIgnoreCase );
	Vector2 _aim;
	double _lastTick = double.NaN;
	readonly Graph _graph;
	readonly Channel _body = new( "body" ), _upper = new( "upper" ), _reload = new( "reload" ), _flinch = new( "flinch" );
	bool _reloadRequested;
	sealed class Channel( string name )
	{
		public readonly string Name = name;
		public string Clip;
		public int Slot = 1;
		public float Weight;
		public float[] Rates = [1, 1];
		public double[] Cycles = [0, 0];
		public string[] Roles = new string[2];
		public bool Active, Loop;
	}
	public string Current => _body.Clip;
	public float Duration( string role ) => _clips.GetValueOrDefault( role ?? "" )?.Duration ?? 0;
	public bool Has( string role ) => role is not null && _clips.ContainsKey( role );
	public bool Finished => _body.Cycles[_body.Slot] >= 1;
	public float Cycle => NormalizedCycle( _body.Cycles[_body.Slot], _body.Loop );
	internal int AimGridCount => _aims.Count;
	internal Vector2 AimAngles => _aim;
	public bool HasUpperMask { get; }
	internal float FlinchWeight => _flinch.Weight;
	internal float FlinchCycle => NormalizedCycle( _flinch.Cycles[_flinch.Slot], false );
	internal float ReloadWeight => _reload.Weight;
	internal float FireWeight => _upper.Weight;

	public Titanfall2ActorAnimationPlayer( SkinnedModelRenderer renderer, Titanfall2ActorAnimationData data )
	{
		_renderer = renderer;
		var skeleton = Skeleton.Load( data.Skeleton );
		if ( !skeleton.IsValid() ) throw new InvalidDataException( "Actor skeleton unavailable" );
		foreach ( var metadata in data.Clips )
		{
			// Missing authored gesture masks must not turn a hurt delta into an
			// arbitrary full-body overlay. Its AG2 clip retains the original delta.
			if ( metadata.Role == ModelLoader.FlinchRole && (!metadata.Additive || string.IsNullOrEmpty( metadata.BoneMask )) ) continue;
			if ( string.IsNullOrEmpty( metadata.Path ) || Clip.Load( metadata.Path ) is not { } clip ) continue;
			_clips[metadata.Role] = clip;
			_metadata[metadata.Role] = metadata;
			foreach ( var aim in metadata.AimLayers ?? [] ) _aims[aim.Id] = aim;
		}
		if ( !Has( ModelLoader.IdleRole ) ) throw new InvalidDataException( "Actor idle unavailable" );
		var bones = renderer.Model.Bones.AllBones.ToArray();
		HasUpperMask = bones.Any( bone => bone.Name.Contains( "spine", StringComparison.OrdinalIgnoreCase ) || bone.Name.Contains( "chest", StringComparison.OrdinalIgnoreCase ) );
		var builder = Graph.Builder;
		builder.Skeleton = skeleton.Name;
		// AG2 requires every control parameter before the first pose/graph node.
		var choicesByChannel = new Dictionary<Channel, (string Role, int Slot)[]>();
		var keysByChannel = new Dictionary<Channel, string[]>();
		var ids = new Dictionary<Channel, GraphBuilder.Node>();
		var timesByChannel = new Dictionary<Channel, GraphBuilder.Node[]>();
		var weights = new Dictionary<Channel, GraphBuilder.Node>();
		var aimParameters = new Dictionary<string, GraphBuilder.Node[]>();
		var aimNodes = new Dictionary<string, GraphBuilder.Node>();
		Parameters( _body, _clips.Keys.Where( x => x != ModelLoader.FlinchRole ).OrderBy( x => x == ModelLoader.IdleRole ? 0 : 1 ), false );
		Parameters( _upper, [ModelLoader.FireRole], true );
		Parameters( _reload, [ModelLoader.ReloadRole], true );
		Parameters( _flinch, [ModelLoader.FlinchRole], true );
		foreach ( var aim in _aims.Values )
			aimParameters[aim.Id] = Enumerable.Range( 0, 2 ).Select( axis => builder.AddControlParameter( $"aim_{aim.Id}_{axis}",
				GraphBuilder.NodeKind.Float, new() { DefaultFloat = 0, Min = -360, Max = 360 } ) ).ToArray();
		foreach ( var aim in _aims.Values ) aimNodes[aim.Id] = BuildGrid( aim );
		var body = Selector( _body, false );
		var layers = builder.Layers( body.Build() );
		AddOverlay( _upper );
		AddOverlay( _reload );
		AddOverlay( _flinch );
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
			timesByChannel[channel] = Enumerable.Range( 0, 2 ).Select( slot => builder.AddControlParameter( $"{channel.Name}_cycle{slot}", GraphBuilder.NodeKind.Float,
				new() { DefaultFloat = 0, Min = 0, Max = 1 } ) ).ToArray();
			if ( overlay ) weights[channel] = builder.AddControlParameter( channel.Name + "_weight", GraphBuilder.NodeKind.Float, new() { DefaultFloat = 0, Min = 0, Max = 1 } );
		}
		GraphBuilder.Node BuildGrid( Titanfall2ActorAnimationData.AimLayer aim )
		{
			var sequence = aim.Sequence;
			var cells = aim.Cells.Select( path => Clip.Load( path ) ?? throw new InvalidDataException( $"Aim cell unavailable: {path}" ) ).ToArray();
			var rows = new GraphBuilder.Node[sequence.Height];
			for ( var y = 0; y < sequence.Height; ++y )
			{
				var columns = new GraphBuilder.Node[sequence.Width];
				for ( var x = 0; x < sequence.Width; ++x )
					columns[x] = builder.TimeControlledClip( cells[x + y * sequence.Width], builder.Constant( 0f ), new() { SampleRootMotion = false } );
				if ( columns.Length == 1 ) rows[y] = columns[0];
				else
				{
					var row = builder.BlendSpace( aimParameters[aim.Id][0], looping: false );
					for ( var x = 0; x < columns.Length; ++x ) row.Add( sequence.Keys[0][x], columns[x] );
					rows[y] = row;
				}
			}
			if ( rows.Length == 1 ) return rows[0];
			// Preserve the authored rectangular interpolation: blend across X, then Y.
			// A triangulated 2D space gives different poses between the same corners.
			var grid = builder.BlendSpace( aimParameters[aim.Id][1], looping: false );
			for ( var y = 0; y < rows.Length; ++y ) grid.Add( sequence.Keys[1][y], rows[y] );
			return grid;
		}
		GraphBuilder.Node RolePose( string role, GraphBuilder.Node time )
		{
			GraphBuilder.Node pose = builder.TimeControlledClip( _clips[role], time, new() { SampleRootMotion = false } );
			var metadata = _metadata[role];
			if ( metadata.AimLayers is not { Length: > 0 } ) return pose;
			var sequence = builder.Layers( pose );
			foreach ( var aim in metadata.AimLayers )
				sequence.AddLayer( aimNodes[aim.Id], new() { BlendMode = GraphBuilder.LayerBlendMode.Additive,
					BoneMask = string.IsNullOrEmpty( aim.BoneMask ) ? default : builder.BoneMask( aim.BoneMask ), IgnoreEvents = true } );
			return sequence;
		}
		GraphBuilder.StateMachineBuilder Selector( Channel channel, bool overlay )
		{
			var choices = choicesByChannel[channel];
			var keys = keysByChannel[channel];
			var id = ids[channel];
			var times = timesByChannel[channel];
			var machine = builder.StateMachine();
			var states = keys.Select( ( key, index ) =>
			{
				if ( key == "none" ) return machine.AddState( choices.Length > 0 && _metadata[choices[0].Role].Additive ? builder.ZeroPose() : builder.ReferencePose() );
				var choice = choices[index - (overlay ? 1 : 0)];
				return machine.AddState( RolePose( choice.Role, times[choice.Slot] ) );
			} ).ToArray();
			for ( var to = 0; to < states.Length; to++ )
				for ( var from = 0; from < states.Length; from++ )
					if ( from != to )
					{
						var fadeIn = keys[to] == "none" ? 0 : _metadata[choices[to - (overlay ? 1 : 0)].Role].Sequence?.FadeIn ?? 0.1f;
						var fadeOut = keys[from] == "none" ? 0 : _metadata[choices[from - (overlay ? 1 : 0)].Role].Sequence?.FadeOut ?? 0.1f;
						machine.AddTransition( states[from], states[to], builder.IdCompare( id, keys[to] ),
							new GraphBuilder.TransitionSettings { Duration = MathF.Max( fadeIn, fadeOut ), ClampDuration = false, CanBeForced = true } );
					}
			return machine;
		}
		void AddOverlay( Channel channel )
		{
			var role = channel == _flinch ? ModelLoader.FlinchRole : channel == _reload ? ModelLoader.ReloadRole : ModelLoader.FireRole;
			var metadata = _metadata.GetValueOrDefault( role );
			if ( metadata is null ) return;
			layers.AddLayer( Selector( channel, true ), new() { BlendMode = metadata?.Additive == true ? GraphBuilder.LayerBlendMode.Additive : GraphBuilder.LayerBlendMode.Overlay,
				Weight = weights[channel], BoneMask = builder.BoneMask( metadata?.BoneMask ?? "tf2_upper" ), IgnoreEvents = true } );
		}
	}

	public void SetAim( float yaw, float pitch ) => _aim = new( float.IsFinite( yaw ) ? yaw : 0, float.IsFinite( pitch ) ? pitch : 0 );
	public void SetCycle( float cycle )
	{
		_body.Cycles[_body.Slot] = float.IsFinite( cycle ) ? Math.Max( 0, cycle ) : 0;
		if ( _renderer.IsValid() && _renderer.Animation.IsValid )
			_renderer.Animation.Parameters.TrySet( $"body_cycle{_body.Slot}", NormalizedCycle( _body.Cycles[_body.Slot], _body.Loop ) );
	}
	public void Play( string role, float rate = 1, bool loop = false, bool restart = false ) => Select( _body, role, rate, loop, restart );
	public void Fire( bool restart ) { if ( HasUpperMask ) Select( _upper, ModelLoader.FireRole, 1, false, restart ); }
	public bool Flinch()
	{
		if ( !Has( ModelLoader.FlinchRole ) || _flinch.Active && _flinch.Cycles[_flinch.Slot] < 1 ) return false;
		Select( _flinch, ModelLoader.FlinchRole, 1, false, true );
		return true;
	}
	public void Reload( bool active, float seconds )
	{
		if ( active && !_reloadRequested && HasUpperMask ) Select( _reload, ModelLoader.ReloadRole, Duration( ModelLoader.ReloadRole ) / MathF.Max( 0.1f, seconds ), false, true );
		if ( !active ) _reload.Active = false;
		_reloadRequested = active;
		if ( active ) _upper.Active = false;
	}
	public void ClearOverlays() { _upper.Active = false; _reload.Active = false; _flinch.Active = false; _reloadRequested = false; }
	void Select( Channel channel, string role, float rate, bool loop, bool restart )
	{
		if ( !Has( role ) ) return;
		if ( channel.Clip != role || restart || !channel.Active )
		{
			var phase = !restart && channel == _body && ModelLoader.IsLocomotionRole( channel.Clip ) && ModelLoader.IsLocomotionRole( role )
				? channel.Cycles[channel.Slot] % 1 : 0;
			channel.Clip = role;
			channel.Slot = 1 - channel.Slot;
			channel.Roles[channel.Slot] = role;
			channel.Cycles[channel.Slot] = phase;
		}
		channel.Active = true;
		channel.Loop = loop;
		channel.Rates[channel.Slot] = float.IsFinite( rate ) ? Math.Clamp( rate, 0.01f, 8 ) : 1;
	}
	public void Tick()
	{
		if ( !_renderer.IsValid() || !_renderer.Animation.IsValid ) return;
		var dt = _lastTick == Time.NowDouble ? 0 : MathF.Max( 0, Time.Delta );
		_lastTick = Time.NowDouble;
		var parameters = _renderer.Animation.Parameters;
		foreach ( var aim in _aims.Values )
			for ( var axis = 0; axis < 2; ++axis )
			{
				var descriptor = aim.Sequence.Axes[axis];
				var value = descriptor?.Name == "aim_pitch" ? _aim.y : _aim.x;
				value = descriptor?.ClampValue( value ) ?? 0;
				var keys = aim.Sequence.Keys[axis];
				parameters.TrySet( $"aim_{aim.Id}_{axis}", Math.Clamp( value, keys[0], keys[^1] ) );
			}
		foreach ( var channel in new[] { _body, _upper, _reload, _flinch } )
		{
			var overlay = channel != _body;
			var remaining = (1 - (float)channel.Cycles[channel.Slot]) * Duration( channel.Clip ) / channel.Rates[channel.Slot];
			if ( overlay && remaining <= 0 ) channel.Active = false;
			var metadata = _metadata.GetValueOrDefault( channel.Clip ?? "" );
			var fadeOut = MathF.Max( 0.001f, metadata?.Sequence?.FadeOut ?? 0.1f );
			var target = channel.Active ? channel.Loop ? 1 : Math.Clamp( remaining / fadeOut, 0, 1 ) : 0;
			var fade = target > channel.Weight ? metadata?.Sequence?.FadeIn ?? 0.1f : fadeOut;
			var step = dt / MathF.Max( 0.001f, fade );
			channel.Weight = Math.Clamp( channel.Weight + Math.Clamp( target - channel.Weight, -step, step ), 0, 1 );
			parameters.TrySet( channel.Name, overlay && channel.Weight <= 0 ? "none" : $"{channel.Clip}:{channel.Slot}" );
			if ( overlay ) parameters.TrySet( channel.Name + "_weight", channel.Weight );
			for ( var slot = 0; slot < 2; ++slot )
			{
				var role = channel.Roles[slot];
				var loop = slot == channel.Slot ? channel.Loop : _metadata.GetValueOrDefault( role ?? "" )?.Looping == true;
				parameters.TrySet( $"{channel.Name}_cycle{slot}", NormalizedCycle( channel.Cycles[slot], loop ) );
				if ( Duration( role ) > 0 ) channel.Cycles[slot] += dt * channel.Rates[slot] / Duration( role );
			}
		}
	}
	static float NormalizedCycle( double cycle, bool loop ) => (float)(loop ? cycle % 1 : Math.Clamp( cycle, 0, 1 ));
	public void Dispose()
	{
		if ( _renderer.IsValid() && _renderer.Animation.Graph == _graph ) { _renderer.Animation.Graph = null; _renderer.UseAnimGraph = false; }
		_graph?.Dispose();
	}
}
