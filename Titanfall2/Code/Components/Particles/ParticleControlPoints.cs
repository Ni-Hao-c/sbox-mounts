using Titanfall2;

/// <summary>Control points shared by a PCF collection and inherited by its children.</summary>
[Library, Hide]
public sealed class Titanfall2ParticleControlPoints : Component
{
	readonly Dictionary<int, Vector3> _world = [];
	readonly Dictionary<int, Vector3> _local = [];
	Titanfall2ParticleControlPoints _parent;

	internal void Configure( Titanfall2ParticleControlPoints parent, IReadOnlyList<ParticleControlPointDefinition> points )
	{
		_parent = parent;
		foreach ( var point in points ) _local[point.Index] = new Vector3( point.Position.x, point.Position.y, point.Position.z );
	}

	/// <summary>Update an endpoint without rebuilding the effect or its materials.</summary>
	public void SetControlPoint( int index, Vector3 worldPosition )
	{
		if ( index is < 0 or > 63 || !IsFinite( worldPosition ) ) return;
		_world[index] = worldPosition;
	}

	public Vector3 GetControlPoint( int index )
	{
		return TryGetControlPoint( index, out var position ) ? position : WorldPosition;
	}

	internal bool TryGetControlPoint( int index, out Vector3 position )
	{
		// Entity endpoints and runtime updates must override a collection's default
		// local CP, including defaults authored on child collections.
		if ( TryGetWorldControlPoint( index, out position ) ) return true;
		if ( _local.TryGetValue( index, out var local ) ) { position = WorldTransform.PointToWorld( local ); return true; }
		if ( _parent.IsValid() ) return _parent.TryGetControlPoint( index, out position );
		position = WorldPosition;
		return index == 0;
	}

	bool TryGetWorldControlPoint( int index, out Vector3 position )
	{
		if ( _world.TryGetValue( index, out position ) ) return true;
		if ( _parent.IsValid() ) return _parent.TryGetWorldControlPoint( index, out position );
		position = default;
		return false;
	}

	internal static bool IsFinite( Vector3 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z );
}

/// <summary>
/// Applies PCF geometry after the engine finishes simulation, before sprite batching.
/// Trail time is in seconds; it must never be passed to an aspect-ratio stretch input.
/// </summary>
[Library, Hide]
public sealed class Titanfall2ParticleGeometry : ParticleController
{
	Titanfall2ParticleDefinition _definition;
	Titanfall2ParticleControlPoints _points;
	ParticleFloat _trailTime;
	float _textureAspect = 1f;
	int _pathCount;
	int _birthCount;
	int _nextParticle;
	float _sizeFadeStart;
	float _sizeFadeEnd;
	bool _sizeFade;
	float _collectionStart;
	readonly Dictionary<Particle, float> _pathFractions = [];
	readonly Dictionary<Particle, (float Radius, float Alpha)> _initialValues = [];
	readonly Dictionary<Particle, (float In, float Out)> _randomFades = [];
	readonly Dictionary<Particle, float[]> _scalarInitialValues = [];
	readonly Dictionary<Particle, OscillationState[]> _oscillations = [];
	readonly Dictionary<Particle, Transform> _lockTransforms = [];
	Transform _currentLockTransform;
	bool _lockTransformReady;
	readonly Dictionary<Particle, Vector3> _randomWeights = [];
	readonly Dictionary<Particle, int> _birthIndices = [];

	readonly record struct OscillationState( float Lifespan, Vector3 Rate, Vector3 Frequency, float Start, float End );

	internal void Configure( Titanfall2ParticleDefinition definition, Titanfall2ParticleControlPoints points,
		ParticleFloat trailTime, float textureAspect = 1f )
	{
		if ( _definition is null ) _collectionStart = Time.Now;
		_definition = definition;
		_points = points;
		_trailTime = trailTime;
		_textureAspect = MathF.Max( 0.001f, textureAspect );
		_birthCount = Math.Max( 1, Titanfall2ParticleFactory.GetBurstCount( definition, points ) );
		_pathCount = Math.Max( 2, definition.Path is { UseParticleCount: true } ? _birthCount : definition.Path?.Count ?? 2 );
	}

	internal void ConfigureSizeFade( float start, float end )
	{
		_sizeFade = float.IsFinite( start ) && float.IsFinite( end ) && start >= 0f && end > start;
		_sizeFadeStart = start;
		_sizeFadeEnd = end;
	}

	// Source SpriteCard compares radius with distance * fade-size; no FOV conversion.
	internal static float EvaluateSizeFade( float radius, float distance, float start, float end ) =>
		radius <= start * distance ? 1f : distance <= 0f || end <= start ? 0f
			: Math.Clamp( (end * distance - radius) / ((end - start) * distance), 0f, 1f );

	protected override void OnParticleCreated( Particle particle )
	{
		if ( _definition is { } movement )
		{
			if ( movement.LocalVelocity ) particle.Velocity = WorldRotation * particle.Velocity;
			if ( movement.LockControlPoint >= 0 )
			{
				_lockTransforms[particle] = LockTransform();
				_lockTransformReady = false;
			}
			if ( movement.RadiusExponent != 1f || movement.AlphaExponent != 1f || movement.TrailExponent != 1f )
				_randomWeights[particle] = new Vector3( MathF.Pow( Random.Shared.Float(), movement.RadiusExponent ),
					MathF.Pow( Random.Shared.Float(), movement.AlphaExponent ), MathF.Pow( Random.Shared.Float(), movement.TrailExponent ) );
		}
		if ( _definition?.DistanceRemaps.Any( remap => remap.ScaleInitialRange ) == true || _definition?.ScalarRemaps.Any( remap => remap.ScaleInitial ) == true )
		{
			var weighted = _randomWeights.TryGetValue( particle, out var initialWeights );
			_initialValues[particle] = (particle.StartScale * (weighted && _definition.RadiusExponent != 1f
				? ParticleEffect.Scale.Evaluate( 0f, initialWeights.x ) : ParticleEffect.Scale.Evaluate( particle, 6211 )),
				weighted && _definition.AlphaExponent != 1f ? ParticleEffect.Alpha.Evaluate( 0f, initialWeights.y ) : ParticleEffect.Alpha.Evaluate( particle, 8525 ));
		}
		if ( _definition is { } fadeDefinition && (fadeDefinition.RandomFadeIn.HasValue || fadeDefinition.RandomFadeOut.HasValue) )
		{
			float Duration( ParticleRandomFadeDefinition? fade ) => fade is { } f ? MathF.Max( 0f,
				MathX.Lerp( f.Minimum, f.Maximum, MathF.Pow( Random.Shared.Float(), MathF.Max( 0.0001f, f.Exponent ) ) ) ) : 0f;
			_randomFades[particle] = (Duration( fadeDefinition.RandomFadeIn ), Duration( fadeDefinition.RandomFadeOut ));
		}
		if ( _definition?.ScalarRemaps.Any( remap => remap.InitialOnly ) == true )
			_scalarInitialValues[particle] = _definition.ScalarRemaps.Select( remap => remap.InitialOnly ? EvaluateScalarRemap( remap, particle ) : float.NaN ).ToArray();
		if ( _definition is { Oscillations.Count: > 0 } oscillating )
		{
			// End-cap-only decay keeps particles past their PCF lifespan, so keep
			// the sampled lifespan for proportional oscillation time.
			var lifespan = MathF.Max( 0.01f, RandomRange( oscillating.LifetimeMinimum, oscillating.LifetimeMaximum ) );
			_oscillations[particle] = oscillating.Oscillations.Select( oscillation => new OscillationState( lifespan,
				RandomVector( oscillation.RateMin, oscillation.RateMax ), RandomVector( oscillation.FrequencyMin, oscillation.FrequencyMax ),
				RandomRange( oscillation.StartMin, oscillation.StartMax ), RandomRange( oscillation.EndMin, oscillation.EndMax ) ) ).ToArray();
		}
		var index = _nextParticle++;
		if ( _definition?.CountRemaps.Count > 0 ) _birthIndices[particle] = index;
		if ( _definition is { } definition && (definition.PositionWarpMinimum != Vector3.One
			|| definition.PositionWarpMaximum != Vector3.One) )
		{
			// Source ground dust flattens a sphere before its final position offset.
			// Preserve that offset: scaling absolute world coordinates moves the
			// entire collection towards the map origin.
			var maximumExtent = Titanfall2ParticleFactory.MaximumEmitterExtent;
			Vector3 Offset( Vector3 value ) => new(
				Math.Clamp( value.x, -maximumExtent, maximumExtent ), Math.Clamp( value.y, -maximumExtent, maximumExtent ),
				Math.Clamp( value.z, -maximumExtent, maximumExtent ) );
			var centre = WorldTransform.PointToWorld( (Offset( definition.PositionMinimum ) + Offset( definition.PositionMaximum )) * 0.5f );
			var local = WorldRotation.Inverse * (particle.Position - centre);
			float Warp( float minimum, float maximum ) => Math.Clamp( Random.Shared.Float( MathF.Min( minimum, maximum ),
				MathF.Max( minimum, maximum ) ), -16f, 16f );
			var min = definition.PositionWarpMinimum; var max = definition.PositionWarpMaximum;
			local *= new Vector3( Warp( min.x, max.x ), Warp( min.y, max.y ), Warp( min.z, max.z ) );
			particle.Position = centre + WorldRotation * local;
			particle.StartPosition = particle.Position;
		}
		if ( _definition is { } offsetDefinition && Titanfall2ParticleFactory.HasShapePositionOffset( offsetDefinition ) )
		{
			// Sphere/ring sampling and Position Modify Offset Random are independent
			// initializers. The emitter supplies the shape plus the offset's centre;
			// restore the random spread instead of collapsing every birth to that centre.
			var extent = Titanfall2ParticleFactory.MaximumEmitterExtent;
			var minimum = offsetDefinition.PositionMinimum.Clamp( -extent, extent );
			var maximum = offsetDefinition.PositionMaximum.Clamp( -extent, extent );
			var centre = (minimum + maximum) * 0.5f;
			var offset = RandomVector( minimum, maximum );
			particle.Position += offsetDefinition.LocalPosition
				? WorldRotation * (offset - centre)
				: offset - WorldRotation * centre;
			particle.StartPosition = particle.Position;
		}
		if ( _definition?.Path is not { } path || !_points.IsValid() ) return;
		var fraction = path.Sequential ? (index % _pathCount) / (float)(_pathCount - 1) : Random.Shared.Float();
		_pathFractions[particle] = fraction;
		particle.Position = Vector3.Lerp( _points.GetControlPoint( path.Start ), _points.GetControlPoint( path.End ), fraction );
		particle.StartPosition = particle.Position;
	}

	protected override void OnParticleDestroyed( Particle particle )
	{
		_pathFractions.Remove( particle );
		_initialValues.Remove( particle );
		_randomFades.Remove( particle );
		_scalarInitialValues.Remove( particle );
		_oscillations.Remove( particle );
		_lockTransforms.Remove( particle );
		_randomWeights.Remove( particle );
		_birthIndices.Remove( particle );
	}

	protected override void OnAfterStep( float delta )
	{
		if ( _definition is null || !ParticleEffect.IsValid() ) return;
		if ( _definition.PlayerControlPoint >= 0 && Scene.Camera.IsValid() )
			_points.SetControlPoint( _definition.PlayerControlPoint, Scene.Camera.WorldPosition );
		var visibility = GetVisibility();
		var hasLocks = UpdateControlPointLock();
		var isTrail = (_definition.Renderer & Titanfall2ParticleRenderer.Trail) != 0;
		foreach ( var particle in ParticleEffect.Particles )
		{
			if ( hasLocks ) ApplyControlPointLock( particle );
			var trailSeconds = isTrail ? MathF.Max( 0f, _randomWeights.TryGetValue( particle, out var trailWeights ) && _definition.TrailExponent != 1f
				? _trailTime.Evaluate( particle.LifeDelta, trailWeights.z ) : _trailTime.Evaluate( particle, 9145 ) ) : 0f;
			if ( _randomWeights.TryGetValue( particle, out var weights ) )
			{
				if ( _definition.RadiusExponent != 1f ) particle.Size = particle.StartScale * ParticleEffect.Scale.Evaluate( particle.LifeDelta, weights.x );
				if ( _definition.AlphaExponent != 1f ) particle.Alpha = ParticleEffect.Alpha.Evaluate( particle.LifeDelta, weights.y );
			}
			for ( var remapIndex = 0; remapIndex < _definition.ScalarRemaps.Count; ++remapIndex )
			{
				var remap = _definition.ScalarRemaps[remapIndex];
				var value = remap.InitialOnly && _scalarInitialValues.TryGetValue( particle, out var values ) ? values[remapIndex] : EvaluateScalarRemap( remap, particle );
				if ( !float.IsFinite( value ) ) continue;
				if ( remap.Field == 10 )
				{
					trailSeconds = MathF.Max( 0f, remap.ScaleInitial ? trailSeconds * value : value );
					continue;
				}
				// Alpha2's initial value is one; normal alpha is multiplied below.
				if ( remap.ScaleInitial && !(remap.InputAttribute >= 0 && remap.Field == 16)
					&& _initialValues.TryGetValue( particle, out var initial ) ) value *= remap.Field == 3 ? initial.Radius : initial.Alpha;
				if ( remap.Field == 3 ) particle.Size = Math.Clamp( value, 0f, Titanfall2ParticleFactory.GetRadiusLimit( _definition ) );
				else if ( remap.Field == 7 ) particle.Alpha = value;
				else if ( remap.Field == 16 ) particle.Alpha *= value;
			}
			if ( _definition.Path is { Maintain: true } path && _points.IsValid()
				&& _pathFractions.TryGetValue( particle, out var fraction ) )
			{
				particle.Position = Vector3.Lerp( _points.GetControlPoint( path.Start ), _points.GetControlPoint( path.End ), fraction );
				particle.Velocity = Vector3.Zero;
			}
			if ( _definition.EndCapLifetime )
			{
				particle.Alpha = Titanfall2ParticleFactory.EvaluateLiveGraphs( _definition, 7, particle.Alpha, particle.Age );
				particle.Alpha = Titanfall2ParticleFactory.EvaluateLiveGraphs( _definition, 16, particle.Alpha, particle.Age );
				var graphRadius = Titanfall2ParticleFactory.EvaluateLiveGraphs( _definition, 3, particle.Size.x, particle.Age );
				particle.Size = Math.Clamp( graphRadius, 0.01f, Titanfall2ParticleFactory.GetRadiusLimit( _definition ) );
			}
			foreach ( var remap in _definition.DistanceRemaps )
			{
				if ( !_points.TryGetControlPoint( remap.ControlPoint, out var controlPoint ) ) continue;
				var distance = particle.Position.Distance( controlPoint );
				var value = Remap( distance, remap.InputMin, remap.InputMax, remap.OutputMin, remap.OutputMax );
				if ( remap.ScaleInitialRange && _initialValues.TryGetValue( particle, out var initial ) )
					value *= remap.Field == 3 ? initial.Radius : initial.Alpha;
				if ( remap.Field == 3 ) particle.Size = Math.Clamp( value, 0f, Titanfall2ParticleFactory.GetRadiusLimit( _definition ) );
				else if ( remap.Field == 7 ) particle.Alpha = value;
				else if ( remap.Field == 16 ) particle.Alpha *= value;
			}
			foreach ( var remap in _definition.ColorRemaps )
			{
				if ( !_points.TryGetControlPoint( remap.ControlPoint, out var input ) ) continue;
				particle.Color = ParticleEffect.Tint.WithAlpha( 1f ) * new Color(
					Remap( input.x, remap.InputMin.x, remap.InputMax.x, remap.OutputMin.x, remap.OutputMax.x ),
					Remap( input.y, remap.InputMin.y, remap.InputMax.y, remap.OutputMin.y, remap.OutputMax.y ),
					Remap( input.z, remap.InputMin.z, remap.InputMax.z, remap.OutputMin.z, remap.OutputMax.z ), particle.Color.a );
			}
			if ( delta > 0f && _oscillations.TryGetValue( particle, out var oscillations ) ) Oscillate( particle, oscillations, delta );
			if ( _randomFades.TryGetValue( particle, out var fade ) )
			{
				var fadeInAge = _definition.RandomFadeIn?.Proportional == true ? particle.LifeDelta : particle.Age;
				var remaining = _definition.RandomFadeOut?.Proportional == true ? 1f - particle.LifeDelta : particle.LifeTimeRemaining - particle.Age;
				if ( fade.In > 0f ) particle.Alpha *= Math.Clamp( fadeInAge / fade.In, 0f, 1f );
				if ( fade.Out > 0f ) particle.Alpha *= Math.Clamp( remaining / fade.Out, 0f, 1f );
			}
			if ( _birthIndices.TryGetValue( particle, out var birthIndex ) )
			{
				foreach ( var remap in _definition.CountRemaps )
				{
					var value = EvaluateCountRemap( remap, birthIndex, _birthCount );
					if ( !float.IsFinite( value ) ) continue;
					if ( remap.Field is 7 or 16 ) particle.Alpha = remap.ScaleInitial ? particle.Alpha * value : value;
					else if ( remap.Field == 3 ) particle.Size = remap.ScaleInitial ? particle.Size * value : new Vector3( value );
				}
			}
			particle.Alpha *= visibility;
			if ( _sizeFade && Scene.Camera.IsValid() )
			{
				var screenRadius = MathF.Abs( particle.Size.x * WorldScale.x );
				var screenFade = EvaluateSizeFade( screenRadius, particle.Position.Distance( Scene.Camera.WorldPosition ), _sizeFadeStart, _sizeFadeEnd );
				// SpriteCard attenuates the vertex tint's RGB and alpha together.
				particle.Color = new Color( particle.Color.r * screenFade, particle.Color.g * screenFade, particle.Color.b * screenFade, particle.Color.a );
				particle.Alpha *= screenFade;
			}
			if ( !isTrail )
			{
				// Source orientation 2 is a world-Z-aligned card, not an upright
				// camera-facing billboard. Preserve roll/yaw in the card's local
				// frame, including rotation curves and the authored yaw flip.
				if ( _definition.OrientationType == 2 )
					particle.Angles = (Rotation.FromPitch( 90f ) * particle.Angles.ToRotation()).Angles();
				continue;
			}
			// Retail C_OP_RenderSpritesTrail (client 0x1806CE590) fades the
			// unbounded length, then clamps it. Fading after the clamp breaks the
			// Volt's fixed 60-unit core into a detached card in front of the muzzle.
			var length = particle.Velocity.Length * trailSeconds * (_definition.TrailIgnoreDeltaTime ? MathF.Max( 0, delta ) : 1f);
			if ( _definition.TrailFadeInTime > 0f ) length *= Math.Clamp( particle.Age / _definition.TrailFadeInTime, 0f, 1f );
			length = Math.Clamp( length, _definition.TrailRenderMinimum, _definition.TrailRenderMaximum );
			var radius = particle.Size.x;
			// Unlike render_animated_sprites, retail sprite trails use the radius
			// attribute as the full ribbon width (vertices are offset by +/- 0.5).
			if ( _definition.ConstrainRadiusToTrail ) radius = MathF.Min( radius, length );
			particle.Size = new Vector3( radius / _textureAspect, length, particle.Size.z );
			particle.Angles = Angles.Zero;
		}
	}

	// Viewmodel placement follows simulation. Locked flashes also need the
	// final camera pose, rather than the previous pose of the animated POV root.
	protected override void OnPreRender() => RefreshControlPointLocks();

	internal void RefreshControlPointLocks()
	{
		if ( !ParticleEffect.IsValid() || !UpdateControlPointLock() ) return;
		foreach ( var particle in ParticleEffect.Particles ) ApplyControlPointLock( particle );
	}

	bool UpdateControlPointLock()
	{
		if ( _lockTransforms.Count == 0 ) return false;
		var current = LockTransform();
		// All existing particles were brought to this transform on the previous
		// pass; new particles record their own current transform at birth. Exact
		// equality preserves even sub-epsilon camera movement across frames.
		if ( _lockTransformReady && current.Equals( _currentLockTransform ) ) return false;
		_currentLockTransform = current;
		_lockTransformReady = true;
		return true;
	}

	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining )]
	void ApplyControlPointLock( Particle particle )
	{
		if ( !_lockTransforms.TryGetValue( particle, out var previous ) ) return;
		var current = _currentLockTransform;
		var factor = _definition.LockDistanceFade > 0f
			? Math.Clamp( 1f - particle.Position.Distance( previous.Position ) / _definition.LockDistanceFade, 0f, 1f ) : 1f;
		// LockTransform is rigid (unit scale). Compose its rotations once rather
		// than doing two point rotations and recomputing the inverse for velocity.
		var rotation = _definition.LockRotation ? current.Rotation * previous.Rotation.Inverse : Rotation.Identity;
		var position = _definition.LockRotation
			? current.Position + rotation * (particle.Position - previous.Position)
			: particle.Position + current.Position - previous.Position;
		particle.Position = factor >= 1f ? position : Vector3.Lerp( particle.Position, position, factor );
		if ( _definition.LockRotation )
			particle.Velocity = factor >= 1f ? rotation * particle.Velocity : Vector3.Lerp( particle.Velocity, rotation * particle.Velocity, factor );
		_lockTransforms[particle] = current;
	}

	Transform LockTransform() => new( _points.IsValid() ? _points.GetControlPoint( _definition.LockControlPoint ) : WorldPosition, WorldRotation );

	// Retail C_INIT_RemapParticleCountToScalar (client 0x180703C60) scales the
	// existing value. Reversed ranges address the final knot; the two initializers
	// combine, so a short tracer's muzzle and hit endpoints both become transparent.
	internal static float EvaluateCountRemap( ParticleCountRemapDefinition remap, int index, int count )
	{
		var input = remap.FromEnd ? count - 1 - index : index;
		if ( remap.OnlyWithinRange && (input < remap.InputMin || input > remap.InputMax) ) return float.NaN;
		return Remap( input, remap.InputMin, remap.InputMax, remap.OutputMin, remap.OutputMax );
	}

	/// <summary>
	/// Source 1 Oscillate Vector on position: each step adds rate * dt * sin(2π phase).
	/// Movement Basic derives velocity from the position change, so the offset also
	/// persists as velocity, which is what makes retail debris drift.
	/// </summary>
	void Oscillate( Particle particle, OscillationState[] states, float delta )
	{
		for ( var index = 0; index < states.Length && index < _definition.Oscillations.Count; ++index )
		{
			var oscillation = _definition.Oscillations[index];
			var state = states[index];
			var lifeFraction = _definition.EndCapLifetime ? particle.Age / state.Lifespan : particle.LifeDelta;
			var time = oscillation.ProportionalTimes ? lifeFraction : particle.Age;
			if ( time < state.Start || time >= state.End ) continue;
			var phase = oscillation.Proportional
				? oscillation.Multiplier * lifeFraction * state.Frequency + oscillation.Phase
				: (oscillation.Multiplier * (particle.BornTime + particle.Age) + oscillation.Phase) * state.Frequency;
			var offset = state.Rate * delta * new Vector3( MathF.Sin( MathF.Tau * phase.x ), MathF.Sin( MathF.Tau * phase.y ),
				MathF.Sin( MathF.Tau * phase.z ) );
			if ( !offset.IsFinite ) continue;
			particle.Position += offset;
			particle.Velocity += offset / delta;
		}
	}

	static float RandomRange( float a, float b ) => Random.Shared.Float( MathF.Min( a, b ), MathF.Max( a, b ) );
	static Vector3 RandomVector( Vector3 a, Vector3 b ) => new( RandomRange( a.x, b.x ), RandomRange( a.y, b.y ), RandomRange( a.z, b.z ) );

	float EvaluateScalarRemap( ParticleControlScalarDefinition remap, Particle particle )
	{
		if ( remap.InputAttribute == 8 )
			return Remap( particle.BornTime - _collectionStart, remap.InputMin, remap.InputMax, remap.OutputMin, remap.OutputMax );
		if ( !_points.IsValid() || !_points.TryGetControlPoint( remap.ControlPoint, out var point ) ) return float.NaN;
		float input;
		if ( remap.EndPoint >= 0 )
		{
			if ( !_points.TryGetControlPoint( remap.EndPoint, out var end ) ) return float.NaN;
			input = point.Distance( end );
		}
		else input = remap.Component switch { 0 => point.x, 1 => point.y, _ => point.z };
		return Remap( input, remap.InputMin, remap.InputMax, remap.OutputMin, remap.OutputMax );
	}

	float GetVisibility()
	{
		if ( _definition.Visibility is not { } visibility || !Scene.Camera.IsValid() ) return 1f;
		var point = _points.GetControlPoint( visibility.ControlPoint );
		var direction = point - Scene.Camera.WorldPosition;
		var factor = 1f;
		if ( visibility.DistanceMin != visibility.DistanceMax )
			factor *= Remap( direction.Length, visibility.DistanceMin, visibility.DistanceMax, 0f, 1f );
		var dot = Vector3.Dot( direction.Normal, WorldRotation.Forward );
		if ( visibility.CullBackFacing && dot >= 0f ) return 0f;
		if ( visibility.DotMin != visibility.DotMax )
			factor *= Remap( dot, visibility.DotMin, visibility.DotMax, 0f, 1f );
		return factor;
	}

	static float Remap( float value, float inputMin, float inputMax, float outputMin, float outputMax ) =>
		MathX.Lerp( outputMin, outputMax, Math.Clamp( MathF.Abs( inputMax - inputMin ) < 0.00001f
			? 0f : (value - inputMin) / (inputMax - inputMin), 0f, 1f ) );

	protected override void OnDisabled()
	{
		base.OnDisabled();
		_pathFractions.Clear();
		_initialValues.Clear();
		_randomFades.Clear();
		_scalarInitialValues.Clear();
		_oscillations.Clear();
		_lockTransforms.Clear();
		_lockTransformReady = false;
		_randomWeights.Clear();
		_birthIndices.Clear();
		_nextParticle = 0;
	}
}
