using Titanfall2;

static class Titanfall2ParticleFactory
{
	// Caps keep a malformed or kilometre-sized definition from covering the map.
	internal const float MaximumRadius = 256f;
	internal const float AtmosphereMaximumRadius = 8192f;
	internal const float MaximumEmitterExtent = 2048f;
	internal const int MaxParticlesPerSystem = 256;
	internal const float MaximumVelocity = 12000f;
	const int MaximumChildDepth = 8;

	internal static float GetRadiusLimit( Titanfall2ParticleDefinition definition ) =>
		definition.MaterialName?.StartsWith( "particle/smoke/", StringComparison.OrdinalIgnoreCase ) == true
			? MathF.Max( Titanfall2ParticleFactory.MaximumRadius, Titanfall2ParticleFactory.AtmosphereMaximumRadius )
			: Titanfall2ParticleFactory.MaximumRadius;

	internal static float GetInfluenceRadius( Titanfall2ParticleDefinition definition )
	{
		var radius = MathF.Max( definition.RadiusMinimum * definition.RadiusMultiplierMinimum,
			definition.RadiusMaximum * definition.RadiusMultiplierMaximum );
		if ( definition.RadiusScale is { } scale ) radius *= MathF.Max( 1, MathF.Max( scale.StartScale, scale.EndScale ) );
		foreach ( var remap in definition.DistanceRemaps.Where( remap => remap.Field == 3 && remap.ScaleInitialRange ) )
			radius *= MathF.Max( 1, MathF.Max( remap.OutputMin, remap.OutputMax ) );
		var offset = MathF.Max( definition.PositionMinimum.Length, definition.PositionMaximum.Length );
		return Math.Clamp( radius, 0, GetRadiusLimit( definition ) ) + MathF.Min( Titanfall2ParticleFactory.MaximumEmitterExtent,
			MathF.Max( offset, definition.ShapeMaximum ) );
	}

	internal readonly record struct CreatedSystem( ParticleEffect Effect, Titanfall2ParticleDefinition Definition );

	internal static bool TryCreate( GameObject entityObject, Titanfall2Mount mount,
		Titanfall2ParticleDefinition definition, out int createdSystems, out string error, List<CreatedSystem> systems = null )
	{
		createdSystems = 0;
		error = null;
		if ( entityObject is null || mount is null || definition is null )
		{
			error = "Particle factory received an invalid entity, mount or definition.";
			return false;
		}

		var stack = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		if ( !CreateDefinition( entityObject, mount, definition, stack, 0, ref createdSystems, out error, systems ) ) return false;
		return createdSystems > 0;
	}

	static bool CreateDefinition( GameObject parent, Titanfall2Mount mount, Titanfall2ParticleDefinition definition,
		HashSet<string> stack, int depth, ref int createdSystems, out string error, List<CreatedSystem> systems, float inheritedDelay = 0f )
	{
		error = null;
		if ( depth > MaximumChildDepth )
		{
			error = $"Particle child depth exceeded {MaximumChildDepth} at '{definition.Name}'.";
			return false;
		}
		if ( !stack.Add( definition.Name ) )
		{
			error = $"Particle child cycle detected at '{definition.Name}'.";
			return false;
		}

		var gameObject = new GameObject( parent, true, definition.Name );
		gameObject.LocalTransform = Transform.Zero;
		var controlPoints = gameObject.AddComponent<Titanfall2ParticleControlPoints>();
		controlPoints.Configure( parent.GetComponentInParent<Titanfall2ParticleControlPoints>( true ), definition.ControlPoints );
		var geometry = ConfigureSimulation( gameObject, definition, controlPoints, inheritedDelay );
		var effect = gameObject.GetComponent<ParticleEffect>( true );
		var hasRenderer = ConfigureRenderer( gameObject, mount, definition, geometry );
		var hasChildren = false;

		foreach ( var childName in definition.Children.Distinct( StringComparer.OrdinalIgnoreCase ) )
		{
			if ( !mount.TryGetParticleDefinition( childName, out var childDefinition ) ) continue;
			var childDelay = inheritedDelay + definition.ChildDelays.GetValueOrDefault( childName, 0f );
			hasChildren |= CreateDefinition( gameObject, mount, childDefinition, stack, depth + 1, ref createdSystems, out _, systems, childDelay );
		}

		stack.Remove( definition.Name );
		if ( hasRenderer || hasChildren )
		{
			// PCFs can author a pre-roll so low-rate fog emitters start filled.
			// Small steps preserve their normal lifetime curves and operator order.
			for ( var remaining = definition.WarmupTime; remaining > 0f; remaining -= 1f / 30f )
				effect.Step( MathF.Min( remaining, 1f / 30f ) );
			createdSystems++;
			systems?.Add( new CreatedSystem( effect, definition ) );
			return true;
		}
		gameObject.Destroy();
		error = $"Particle '{definition.Name}' has no supported renderer or child system.";
		return false;
	}

	internal static bool HasShapePositionOffset( Titanfall2ParticleDefinition definition ) =>
		definition.Shape is Titanfall2ParticleShape.Sphere or Titanfall2ParticleShape.Ring
		&& (definition.PositionMinimum != Vector3.Zero || definition.PositionMaximum != Vector3.Zero);

	internal static Titanfall2ParticleGeometry ConfigureSimulation( GameObject gameObject, Titanfall2ParticleDefinition definition,
		Titanfall2ParticleControlPoints controlPoints, float startDelay = 0f )
	{
		var effect = gameObject.AddComponent<ParticleEffect>();
		ConfigureEffect( effect, definition, controlPoints );
		Titanfall2ParticleGeometry geometry = null;
		if ( definition.OrientationType == 2 || definition.Path.HasValue || definition.MoveBetweenPoints.HasValue || definition.Visibility.HasValue || definition.EndCapLifetime || definition.DistanceRemaps.Count > 0
			|| definition.ColorRemaps.Count > 0 || definition.ScalarRemaps.Count > 0 || definition.RandomFadeIn.HasValue || definition.RandomFadeOut.HasValue || definition.PositionWarpMinimum != Vector3.One
			|| definition.PositionWarpMaximum != Vector3.One || (definition.Renderer & Titanfall2ParticleRenderer.Trail) != 0
			|| definition.Oscillations.Count > 0 || HasShapePositionOffset( definition ) || definition.LocalVelocity || definition.LockControlPoint >= 0
			|| definition.RadiusExponent != 1f || definition.AlphaExponent != 1f || definition.TrailExponent != 1f || definition.CountRemaps.Count > 0 )
		{
			geometry = gameObject.AddComponent<Titanfall2ParticleGeometry>();
			geometry.Configure( definition, controlPoints, CreateStretch( definition ) );
		}
		ConfigureEmitter( gameObject, definition, controlPoints, startDelay );
		return geometry;
	}

	internal static (float Min, float Max) EffectiveLifetimeRange( Titanfall2ParticleDefinition definition, Titanfall2ParticleControlPoints controlPoints )
	{
		var minLife = definition.LifetimeMinimum;
		var maxLife = definition.LifetimeMaximum;
		if ( !controlPoints.IsValid() || definition.ScalarRemaps.Count == 0 ) return (minLife, maxLife);
		foreach ( var remap in definition.ScalarRemaps )
		{
			if ( remap.Field != 1 || remap.InputAttribute >= 0 || remap.EndPoint >= 0 ) continue;
			if ( !controlPoints.TryGetControlPoint( remap.ControlPoint, out var point ) ) continue;
			var input = remap.Component switch { 0 => point.x, 1 => point.y, _ => point.z };
			var t = MathF.Abs( remap.InputMax - remap.InputMin ) < 0.00001f
				? 0f : Math.Clamp( (input - remap.InputMin) / (remap.InputMax - remap.InputMin), 0f, 1f );
			var value = MathX.Lerp( remap.OutputMin, remap.OutputMax, t );
			if ( !float.IsFinite( value ) || value <= 0f ) continue;
			minLife = Math.Clamp( remap.ScaleInitial ? minLife * value : value, 0.05f, 120f );
			maxLife = Math.Clamp( remap.ScaleInitial ? maxLife * value : value, 0.05f, 120f );
		}
		return (MathF.Min( minLife, maxLife ), MathF.Max( minLife, maxLife ));
	}

	static void ConfigureEffect( ParticleEffect effect, Titanfall2ParticleDefinition definition, Titanfall2ParticleControlPoints controlPoints )
	{
		effect.MaxParticles = Math.Clamp( definition.MaxParticles, 1, Titanfall2ParticleFactory.MaxParticlesPerSystem );
		var (minLife, maxLife) = EffectiveLifetimeRange( definition, controlPoints );
		effect.Lifetime = definition.EndCapLifetime ? float.MaxValue : ExponentRange( minLife, maxLife, definition.LifetimeExponent );
		// PCF local velocity is a birth transform, not a simulation-space flag.
		// The controller rotates it once and handles only explicitly authored CP locks.
		effect.LocalSpace = 0f;
		var maximumVelocity = Titanfall2ParticleFactory.MaximumVelocity;
		effect.InitialVelocity = new ParticleVector3
		{
			X = Range(
				Math.Clamp( definition.VelocityMinimum.x, -maximumVelocity, maximumVelocity ),
				Math.Clamp( definition.VelocityMaximum.x, -maximumVelocity, maximumVelocity ) ),
			Y = Range(
				Math.Clamp( definition.VelocityMinimum.y, -maximumVelocity, maximumVelocity ),
				Math.Clamp( definition.VelocityMaximum.y, -maximumVelocity, maximumVelocity ) ),
			Z = Range(
				Math.Clamp( definition.VelocityMinimum.z, -maximumVelocity, maximumVelocity ),
				Math.Clamp( definition.VelocityMaximum.z, -maximumVelocity, maximumVelocity ) )
		};
		effect.Damping = MathF.Max( 0f, definition.Damping );

		effect.ApplyColor = true;
		effect.Tint = Color.White;
		effect.Gradient = CreateColor( definition );

		effect.ApplyAlpha = true;
		effect.Alpha = CreateAlpha( definition );
		effect.ApplyShape = true;
		effect.Scale = CreateScale( definition );
		// Sprite trails are sized after simulation using velocity * trail seconds,
		// with the PCF renderer's separate world-space min/max lengths.

		if ( MathF.Abs( definition.RotationMinimum ) > float.Epsilon
			|| MathF.Abs( definition.RotationMaximum ) > float.Epsilon
			|| MathF.Abs( definition.RotationRate ) > float.Epsilon
			|| MathF.Abs( definition.RotationRateMinimum ) > float.Epsilon
			|| MathF.Abs( definition.RotationRateMaximum ) > float.Epsilon
			|| definition.ScalarGraphs.Any( static graph => graph.OutputField == 4 ) )
		{
			effect.ApplyRotation = true;
			effect.Roll = CreateRotation( definition );
		}
		if ( definition.RandomYawFlip
			|| MathF.Abs( definition.YawMinimum ) > float.Epsilon
			|| MathF.Abs( definition.YawMaximum ) > float.Epsilon )
		{
			effect.ApplyRotation = true;
			effect.Yaw = definition.RandomYawFlip
				? CreateYawFlip()
				: Range( definition.YawMinimum, definition.YawMaximum );
		}
		// World-Z sprites still need their source angles reset each simulation
		// step before the geometry controller rotates the card into the XY plane.
		if ( definition.OrientationType == 2 ) effect.ApplyRotation = true;

		if ( definition.Gravity.LengthSquared > float.Epsilon )
		{
			var gravity = ToSandbox( definition.Gravity );
			effect.Force = true;
			effect.ForceDirection = gravity.Normal;
			effect.ForceScale = gravity.Length;
			// Movement Basic gravity is world-space even when velocity was local to an
			// ejection port. Rotating -Z by that port points gravity down the barrel.
			effect.ForceSpace = ParticleEffect.SimulationSpace.World;
		}

		if ( definition.SequenceMaximum > 0 )
		{
			effect.SheetSequence = true;
			effect.SequenceId = Range( definition.SequenceMinimum, definition.SequenceMaximum );
			effect.SequenceTime = 0f;
			effect.SequenceSpeed = definition.AnimationRate;
		}
	}

	internal static int GetBurstCount( Titanfall2ParticleDefinition definition, Titanfall2ParticleControlPoints points )
	{
		var maximum = Math.Clamp( definition.MaxParticles, 1, Titanfall2ParticleFactory.MaxParticlesPerSystem );
		var count = Math.Clamp( definition.Burst, 0f, maximum );
		if ( definition.BurstSpacing > 0f && definition.Path is { } path && points.IsValid() )
		{
			var distance = points.GetControlPoint( path.Start ).Distance( points.GetControlPoint( path.End ) );
			if ( float.IsFinite( distance ) )
				count += MathF.Max( 2f, MathF.Ceiling( Math.Clamp( distance / definition.BurstSpacing, 0f, maximum ) ) + 1f );
		}
		return (int)Math.Clamp( MathF.Max( count, definition.BurstMinimum ), 0f, maximum );
	}

	static void ConfigureEmitter( GameObject effectObject, Titanfall2ParticleDefinition definition, Titanfall2ParticleControlPoints points, float startDelay = 0f )
	{
		var emitterObject = new GameObject( effectObject, true, "emitter" );
		var maximumExtent = Titanfall2ParticleFactory.MaximumEmitterExtent;
		var minimum = ClampVector( definition.PositionMinimum, maximumExtent );
		var maximum = ClampVector( definition.PositionMaximum, maximumExtent );
		var center = (minimum + maximum) * 0.5f;
		emitterObject.LocalPosition = ToSandbox( center );

		ParticleEmitter emitter;
		if ( definition.Shape == Titanfall2ParticleShape.Ring )
		{
			var ring = emitterObject.AddComponent<ParticleRingEmitter>();
			ring.Radius = Math.Clamp( definition.ShapeMaximum, 0f, maximumExtent );
			ring.Thickness = Math.Clamp( definition.ShapeThickness, 0f, maximumExtent );
			emitter = ring;
		}
		else if ( definition.Shape == Titanfall2ParticleShape.Sphere )
		{
			var sphere = emitterObject.AddComponent<ParticleSphereEmitter>();
			sphere.Radius = Math.Clamp( definition.ShapeMaximum, 0f, maximumExtent );
			sphere.OnEdge = definition.ShapeMinimum > 0f && MathF.Abs( definition.ShapeMinimum - definition.ShapeMaximum ) < 0.01f;
			sphere.Velocity = 0f;
			emitter = sphere;
		}
		else if ( (maximum - minimum).LengthSquared > float.Epsilon )
		{
			var box = emitterObject.AddComponent<ParticleBoxEmitter>();
			var size = maximum - minimum;
			box.Size = new Vector3( MathF.Abs( size.x ), MathF.Abs( size.y ), MathF.Abs( size.z ) );
			emitter = box;
		}
		else
		{
			var point = emitterObject.AddComponent<ParticleSphereEmitter>();
			point.Radius = 0f;
			point.Velocity = 0f;
			emitter = point;
		}

		// Continuous PCF emission does not restart at every particle lifetime.
		// Bursts occur once; zero-duration continuous emitters run until stopped.
		var stopDuration = 0f;
		if ( definition.StopDuration > 0f )
		{
			var cpScale = definition.StopDurationControlPoint >= 0 && points.IsValid()
				&& points.TryGetControlPoint( definition.StopDurationControlPoint, out var stopCp ) && stopCp.x > 0f
				? stopCp.x : 1f;
			stopDuration = MathF.Max( 0.05f, definition.StopDuration * cpScale );
		}
		emitter.Loop = false;
		emitter.DestroyOnEnd = false;
		emitter.Duration = definition.EmissionDuration > 0f ? definition.EmissionDuration
			: stopDuration > 0f ? stopDuration
			: definition.Looping ? float.MaxValue : MathF.Max( 0.05f, definition.Duration );
		emitter.Delay = definition.EmissionStartTime + MathF.Max( 0f, startDelay );
		emitter.Burst = GetBurstCount( definition, points );
		emitter.Rate = MathF.Max( 0f, definition.EmissionRate );
		emitter.RateOverDistance = definition.EmissionRateOverDistance;
		// OnEnabled ran when the component was attached, before these PCF values
		// were assigned. Refresh its cached burst, duration and start delay now.
		emitter.ResetEmitter();
	}

	static bool ConfigureRenderer( GameObject gameObject, Titanfall2Mount mount, Titanfall2ParticleDefinition definition,
		Titanfall2ParticleGeometry geometry )
	{
		var created = false;
		if ( (definition.Renderer & (Titanfall2ParticleRenderer.Sprite | Titanfall2ParticleRenderer.Trail)) != 0
			&& !string.IsNullOrWhiteSpace( definition.MaterialName )
			&& mount.TryGetOrCreateLegacyMaterialDescriptor( definition.MaterialName, out var materialDescriptor, out _ )
			&& mount.TryGetLegacyMaterialDefinition( definition.MaterialName, out var materialDefinition, out _ ) )
		{
			if ( materialDescriptor.IsRefract )
			{
				var refract = gameObject.AddComponent<Titanfall2ParticleRefractionRenderer>();
				if ( refract.Configure( mount, definition, materialDefinition ) ) created = true;
				else refract.Destroy();
			}
			else
			{
			var textureName = materialDescriptor.TextureBindings
				.FirstOrDefault( binding => binding.Semantic == Titanfall2TextureSemantic.Albedo ).TexturePath;
			var frameRate = definition.AnimationRate;
			if ( materialDefinition.TryGetAnimatedTexture( false, out var materialFrameRate ) && materialFrameRate > 0f )
				frameRate = materialFrameRate;
			// Runtime textures carry no native sheet data, so the batch cannot pick a sheet
			// sequence per particle. Use one sequence from the PCF range for the system
			// rather than drawing the whole atlas (e.g. 25 one-frame chips in fleks.vtf).
			var sequence = definition.SequenceMaximum > definition.SequenceMinimum
				? Random.Shared.Int( definition.SequenceMinimum, definition.SequenceMaximum )
				: definition.SequenceMinimum;
			if ( !string.IsNullOrWhiteSpace( textureName )
				&& mount.TryGetLegacyParticleSprite( textureName, frameRate, out var sprite, out _, Math.Max( 0, sequence ) ) )
			{
				var metadata = materialDescriptor.Metadata;
				// The sprite batch preserves HDR colour via RGBE. Keep the VMT's
				// overbright factor in that path rather than clipping it to an 8-bit tint.
				var tint = materialDefinition.GetVector( "$color", Vector4.One );
				var brightness = Math.Clamp( materialDefinition.GetFloat( "$overbrightfactor", 1f ), 0f, 64f ) * definition.ColorHdrScale;
				gameObject.GetComponent<ParticleEffect>( true ).Tint = new Color(
					tint.x * brightness, tint.y * brightness, tint.z * brightness,
					Math.Clamp( materialDefinition.GetFloat( "$alpha", 1f ), 0f, 1f ) );
				var renderer = gameObject.AddComponent<ParticleSpriteRenderer>();
				if ( materialDefinition.GetString( "$startfadesize" ) is not null || materialDefinition.GetString( "$endfadesize" ) is not null )
				{
					if ( geometry is null )
					{
						geometry = gameObject.AddComponent<Titanfall2ParticleGeometry>();
						geometry.Configure( definition, gameObject.GetComponent<Titanfall2ParticleControlPoints>( true ), CreateStretch( definition ) );
					}
					geometry.ConfigureSizeFade( materialDefinition.GetFloat( "$startfadesize", 10f ), materialDefinition.GetFloat( "$endfadesize", 20f ) );
				}
				var trail = (definition.Renderer & Titanfall2ParticleRenderer.Trail) != 0;
				if ( trail )
				{
					// Pin the head to the particle. Stretching a centred sprite pushes
					// half the streak ahead of its actual position.
					sprite = new Sprite
					{
						Animations = sprite.Animations.Select( animation => new Sprite.Animation
						{
							Name = animation.Name,
							Frames = animation.Frames,
							FrameRate = animation.FrameRate,
							LoopMode = animation.LoopMode,
							Origin = new Vector2( 0.5f, 0f )
						} ).ToList()
					};
					var frame = sprite.Animations.FirstOrDefault()?.Frames.FirstOrDefault()?.Texture;
					geometry?.Configure( definition, gameObject.GetComponent<Titanfall2ParticleControlPoints>( true ),
						CreateStretch( definition ), frame is not null ? frame.Width / (float)frame.Height : 1f );
				}
				renderer.Sprite = sprite;
				var rampPath = Titanfall2LegacyPath.NormalizeTextureName( materialDefinition.GetString( "$ramptexture" ) );
				Texture colorRamp = null;
				if ( !string.IsNullOrEmpty( rampPath ) )
				{
					var ramp = Texture.Load( $"mount://{mount.Ident}/{rampPath}.vtex", false );
					if ( ramp is not null && ramp.IsValid && !ramp.IsError ) colorRamp = ramp;
					else Titanfall2Log.Warning( $"Unable to load particle color ramp '{rampPath}' ({definition.MaterialName})." );
				}
				renderer.FaceVelocity = trail;
				renderer.MotionBlur = false;
				renderer.PlaybackSpeed = 1f;
				if ( definition.AnimationFitLifetime && definition.LifetimeMaximum > 0.01f
					&& sprite.Animations.FirstOrDefault() is { Frames.Count: > 1, FrameRate: > 0f } fitted )
				{
					var averageLife = MathF.Max( 0.01f, (definition.LifetimeMinimum + definition.LifetimeMaximum) * 0.5f );
					renderer.PlaybackSpeed = Math.Clamp( fitted.Frames.Count / (averageLife * fitted.FrameRate), 0.1f, 64f );
				}
				// Source PCF size is a radius; the native sprite scale is a diameter.
				// Trails already construct their full width/length in the controller.
				renderer.Scale = trail ? 1f : 2f;
				renderer.Additive = metadata.Mode == Titanfall2MaterialMode.Additive;
				renderer.Lighting = !metadata.IsUnlit;
				renderer.Opaque = false;
				renderer.Shadows = false;
				renderer.SortMode = renderer.Additive
					? ParticleSpriteRenderer.ParticleSortMode.Unsorted
					: ParticleSpriteRenderer.ParticleSortMode.ByDistance;
				renderer.Alignment = definition.OrientationType switch
				{
					1 => ParticleSpriteRenderer.BillboardAlignment.RotateToCamera,
					2 => ParticleSpriteRenderer.BillboardAlignment.Particle,
					3 => ParticleSpriteRenderer.BillboardAlignment.Particle,
					_ => ParticleSpriteRenderer.BillboardAlignment.LookAtCamera
				};
				var depthFeather = definition.DepthFeather > 0f ? definition.DepthFeather
					: materialDefinition.GetBool( "$depthblend" ) ? materialDefinition.GetFloat( "$depthblendscale", 50f )
					: materialDefinition.GetString( "$depthblend" ) is not null ? 0f : 4f;
				renderer.DepthFeather = Math.Clamp( depthFeather, 0f, 8192f );
				renderer.FogStrength = Math.Clamp( materialDefinition.GetFloat( "$vertexfogamount", 1f ), 0, 1 );
				renderer.CameraFadeNear = 4f;
				renderer.CameraFadeFar = 24f;
				if ( colorRamp is not null || trail ) gameObject.AddComponent<Titanfall2ParticleRampRenderer>()
					.Configure( renderer, colorRamp, materialDefinition.GetBool( "$texcolorfromalpha" ), trail );
				created = true;
			}
			}
		}

		if ( (definition.Renderer & Titanfall2ParticleRenderer.Rope) != 0
			&& !string.IsNullOrWhiteSpace( definition.MaterialName )
			&& mount.TryGetOrCreateLegacyMaterialDescriptor( definition.MaterialName, out var ropeMaterial, out _ )
			&& mount.TryGetLegacyMaterialDefinition( definition.MaterialName, out var ropeDefinition, out _ ) )
		{
			var texturePath = ropeMaterial.TextureBindings.FirstOrDefault( binding => binding.Semantic == Titanfall2TextureSemantic.Albedo ).TexturePath;
			if ( !string.IsNullOrWhiteSpace( texturePath ) )
			{
				var texture = Texture.Load( $"mount://{mount.Ident}/{texturePath}.vtex", false );
				if ( texture is not null && texture.IsValid && !texture.IsError )
				{
					gameObject.AddComponent<Titanfall2ParticleRopeRenderer>().Configure( definition, texture, ropeDefinition,
						ropeMaterial.Metadata.Mode == Titanfall2MaterialMode.Additive );
					created = true;
				}
			}
		}

		if ( (definition.Renderer & Titanfall2ParticleRenderer.Light) != 0 )
		{
			var light = gameObject.AddComponent<ParticleLightRenderer>();
			light.Ratio = 1f;
			light.MaximumLights = 4;
			light.CastShadows = false;
			light.Scale = definition.LightRadiusScale;
			light.Brightness = definition.LightColorScale;
			light.UseParticleColor = true;
			created = true;
		}

		if ( (definition.Renderer & Titanfall2ParticleRenderer.Model) != 0 && !string.IsNullOrWhiteSpace( definition.ModelName ) )
		{
			var model = Model.Load( $"mount://{mount.Ident}/{definition.ModelName}.vmdl" );
			if ( ModelLoader.IsUsableModel( model ) )
			{
				var modelRenderer = gameObject.AddComponent<ParticleModelRenderer>();
				modelRenderer.Choices = [new ParticleModelRenderer.ModelEntry { Model = model }];
				modelRenderer.RotateWithGameObject = true;
				modelRenderer.Scale = 1f;
				modelRenderer.CastShadows = false;
				// PCF "Render models" has no colour inputs: retail draws the model with its
				// own materials. The particle colour (often the editor default, e.g. green
				// 124 159 99 in env_training.pcf) would otherwise become the model ColorTint,
				// which the mount shaders multiply into albedo.
				if ( (definition.Renderer & (Titanfall2ParticleRenderer.Sprite | Titanfall2ParticleRenderer.Trail
					| Titanfall2ParticleRenderer.Rope | Titanfall2ParticleRenderer.Light)) == 0 )
					gameObject.GetComponent<ParticleEffect>( true ).ApplyColor = false;
				created = true;
			}
		}
		return created;
	}

	static ParticleGradient CreateColor( Titanfall2ParticleDefinition definition )
	{
		if ( definition.ColorFade is not { } fade )
		{
			return new ParticleGradient
			{
				Type = ParticleGradient.ValueType.Range,
				Evaluation = ParticleGradient.EvaluationType.Particle,
				ConstantA = ToColor( definition.ColorMinimum ),
				ConstantB = ToColor( definition.ColorMaximum )
			};
		}

		var start = Color.Lerp( ToColor( definition.ColorMinimum ), ToColor( definition.ColorMaximum ), 0.5f );
		var target = ToColor( fade );
		var gradient = new Gradient();
		gradient.AddColor( 0f, start );
		// fade_start_time is raw PCF data; clamp it before it becomes a Clamp minimum,
		// or a value above 1 throws from the streamer's OnUpdate.
		var fadeStart = Math.Clamp( definition.ColorFadeStart, 0f, 1f );
		gradient.AddColor( fadeStart, start );
		gradient.AddColor( Math.Clamp( definition.ColorFadeEnd, fadeStart, 1f ), target );
		gradient.AddColor( 1f, target );
		return new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Gradient,
			Evaluation = ParticleGradient.EvaluationType.Life,
			GradientA = gradient
		};
	}

	static ParticleFloat CreateAlpha( Titanfall2ParticleDefinition definition )
	{
		var graphs = definition.ScalarGraphs.Where( static graph => graph.OutputField is 7 or 16 ).ToArray();
		var minimum = definition.AlphaMinimum * definition.AlphaMultiplierMinimum;
		var maximum = definition.AlphaMaximum * definition.AlphaMultiplierMaximum;
		if ( definition.EndCapLifetime || (definition.AlphaFade is null && graphs.Length == 0) ) return Range( minimum, maximum );
		return new ParticleFloat
		{
			Type = ParticleFloat.ValueType.CurveRange,
			Evaluation = ParticleFloat.EvaluationType.Life,
			CurveA = CreateComposedCurve( definition.LifetimeMinimum, graphs,
				time => minimum * EvaluateFade( definition.AlphaFade, time ), GetFadeTimes( definition.AlphaFade ) ),
			CurveB = CreateComposedCurve( definition.LifetimeMaximum, graphs,
				time => maximum * EvaluateFade( definition.AlphaFade, time ), GetFadeTimes( definition.AlphaFade ) )
		};
	}

	static ParticleFloat CreateScale( Titanfall2ParticleDefinition definition )
	{
		var graphs = definition.ScalarGraphs.Where( static graph => graph.OutputField == 3 ).ToArray();
		var radiusLimit = GetRadiusLimit( definition );
		var minimum = Math.Clamp( definition.RadiusMinimum * definition.RadiusMultiplierMinimum, 0.01f, radiusLimit );
		var maximum = Math.Clamp( definition.RadiusMaximum * definition.RadiusMultiplierMaximum, minimum, radiusLimit );
		if ( definition.EndCapLifetime || (definition.RadiusScale is null && graphs.Length == 0) ) return Range( minimum, maximum );
		return new ParticleFloat
		{
			Type = ParticleFloat.ValueType.CurveRange,
			Evaluation = ParticleFloat.EvaluationType.Life,
			CurveA = CreateComposedCurve( definition.LifetimeMinimum, graphs,
				time => minimum * EvaluateScale( definition.RadiusScale, time ),
				GetScaleTimes( definition.RadiusScale ), 0.01f, radiusLimit ),
			CurveB = CreateComposedCurve( definition.LifetimeMaximum, graphs,
				time => maximum * EvaluateScale( definition.RadiusScale, time ),
				GetScaleTimes( definition.RadiusScale ), 0.01f, radiusLimit )
		};
	}

	static ParticleFloat CreateStretch( Titanfall2ParticleDefinition definition )
	{
		var graphs = definition.ScalarGraphs.Where( static graph => graph.OutputField == 10 ).ToArray();
		var minimum = definition.TrailMinimum > 0f ? definition.TrailMinimum : 1f;
		var maximum = definition.TrailMaximum > 0f ? definition.TrailMaximum : minimum;
		if ( graphs.Length == 0 ) return Range( minimum, maximum );
		return new ParticleFloat
		{
			Type = ParticleFloat.ValueType.CurveRange,
			Evaluation = ParticleFloat.EvaluationType.Life,
			CurveA = CreateComposedCurve( definition.LifetimeMinimum, graphs, _ => minimum ),
			CurveB = CreateComposedCurve( definition.LifetimeMaximum, graphs, _ => maximum )
		};
	}

	internal static float SampleExponentRange( float minimum, float maximum, float exponent, float unit ) =>
		minimum + (maximum - minimum) * MathF.Pow( Math.Clamp( unit, 0f, 1f ), exponent );

	static ParticleFloat ExponentRange( float minimum, float maximum, float exponent )
	{
		if ( exponent == 1f || minimum == maximum ) return Range( minimum, maximum );
		// Seed-evaluated native curves preserve Source RandomFloatExp at birth.
		return new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve, Evaluation = ParticleFloat.EvaluationType.Seed,
			CurveA = new Curve( Enumerable.Range( 0, 129 ).Select( index => new Curve.Frame( index / 128f,
				SampleExponentRange( minimum, maximum, exponent, index / 128f ) ) { Mode = Curve.HandleMode.Linear } ) )
		};
	}

	static ParticleFloat CreateRotation( Titanfall2ParticleDefinition definition )
	{
		var graphs = definition.ScalarGraphs.Where( static graph => graph.OutputField == 4 ).ToArray();
		var minimumRate = definition.RotationRate + definition.RotationRateMinimum;
		var maximumRate = definition.RotationRate + definition.RotationRateMaximum;
		return new ParticleFloat
		{
			Type = ParticleFloat.ValueType.CurveRange,
			Evaluation = ParticleFloat.EvaluationType.Life,
			CurveA = CreateComposedCurve( definition.LifetimeMinimum, graphs,
				time => definition.RotationMinimum + minimumRate * definition.LifetimeMinimum * time ),
			CurveB = CreateComposedCurve( definition.LifetimeMaximum, graphs,
				time => definition.RotationMaximum + maximumRate * definition.LifetimeMaximum * time )
		};
	}

	static ParticleFloat CreateYawFlip()
	{
		return new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve,
			Evaluation = ParticleFloat.EvaluationType.Seed,
			CurveA = new Curve(
				new Curve.Frame( 0f, 0f ) { Mode = Curve.HandleMode.Stepped },
				new Curve.Frame( 0.5f, 180f ) { Mode = Curve.HandleMode.Stepped },
				new Curve.Frame( 1f, 180f ) { Mode = Curve.HandleMode.Stepped } )
		};
	}

	static Curve CreateComposedCurve( float lifetime, IReadOnlyList<ParticleScalarGraphDefinition> graphs,
		Func<float, float> baseValue, IEnumerable<float> extraTimes = null,
		float minimumValue = float.MinValue, float maximumValue = float.MaxValue )
	{
		lifetime = MathF.Max( lifetime, 0.01f );
		var times = new SortedSet<float> { 0f, 1f };
		if ( extraTimes is not null )
		{
			foreach ( var time in extraTimes ) times.Add( Math.Clamp( time, 0f, 1f ) );
		}
		foreach ( var graph in graphs ) AddGraphTimes( times, graph, lifetime );

		var frames = new List<Curve.Frame>( times.Count );
		foreach ( var time in times )
		{
			var value = baseValue( time );
			foreach ( var graph in graphs )
			{
				var graphValue = EvaluateGraph( graph, time, lifetime );
				value = graph.OutputOperation == 1 ? value * graphValue : graphValue;
			}
			value = Math.Clamp( value, minimumValue, maximumValue );
			frames.Add( new Curve.Frame( time, value ) { Mode = Curve.HandleMode.Linear } );
		}
		return new Curve( frames );
	}

	static void AddGraphTimes( SortedSet<float> times, ParticleScalarGraphDefinition graph, float lifetime )
	{
		if ( graph.TimeInLifespans )
		{
			foreach ( var point in graph.Points ) times.Add( Math.Clamp( point.x, 0f, 1f ) );
			return;
		}

		var cycles = graph.Loop ? Math.Min( 32, (int)MathF.Ceiling( lifetime / graph.Duration ) ) : 1;
		for ( var cycle = 0; cycle < cycles; cycle++ )
		{
			foreach ( var point in graph.Points )
			{
				var normalized = (cycle * graph.Duration + point.x * graph.Duration) / lifetime;
				if ( normalized > 1f ) break;
				times.Add( Math.Clamp( normalized, 0f, 1f ) );
			}
		}
	}

	internal static float EvaluateLiveGraphs( Titanfall2ParticleDefinition definition, int field, float value, float age )
	{
		var lifetime = MathF.Max( 0.01f, definition.LifetimeMaximum );
		foreach ( var graph in definition.ScalarGraphs )
		{
			if ( graph.OutputField != field ) continue;
			var sample = EvaluateGraph( graph, age / lifetime, lifetime );
			value = graph.OutputOperation == 1 ? value * sample : sample;
		}
		return value;
	}

	static float EvaluateGraph( ParticleScalarGraphDefinition graph, float normalizedTime, float lifetime )
	{
		var graphTime = normalizedTime;
		if ( !graph.TimeInLifespans )
		{
			graphTime = normalizedTime * lifetime / graph.Duration;
			graphTime = graph.Loop ? graphTime - MathF.Floor( graphTime ) : Math.Clamp( graphTime, 0f, 1f );
		}
		var points = graph.Points;
		if ( points.Count == 0 ) return 1f;
		if ( graphTime <= points[0].x ) return RemapGraphValue( graph, points[0].y );
		for ( var index = 1; index < points.Count; index++ )
		{
			if ( graphTime > points[index].x ) continue;
			var previous = points[index - 1];
			var current = points[index];
			var fraction = MathF.Abs( current.x - previous.x ) <= float.Epsilon
				? 1f
				: Math.Clamp( (graphTime - previous.x) / (current.x - previous.x), 0f, 1f );
			return RemapGraphValue( graph, MathX.Lerp( previous.y, current.y, fraction ) );
		}
		return RemapGraphValue( graph, points[^1].y );
	}

	static float RemapGraphValue( ParticleScalarGraphDefinition graph, float value ) =>
		MathX.Lerp( graph.OutputMinimum, graph.OutputMaximum, value );

	static IEnumerable<float> GetFadeTimes( ParticleFadeDefinition? fade )
	{
		if ( fade is not { } value ) yield break;
		yield return value.FadeInStart;
		yield return value.FadeInEnd;
		yield return value.FadeOutStart;
		yield return value.FadeOutEnd;
	}

	static float EvaluateFade( ParticleFadeDefinition? fade, float time )
	{
		if ( fade is not { } value ) return 1f;
		var fadeInStart = Math.Clamp( value.FadeInStart, 0f, 1f );
		var fadeInEnd = Math.Clamp( value.FadeInEnd, fadeInStart, 1f );
		var fadeOutStart = Math.Clamp( value.FadeOutStart, fadeInEnd, 1f );
		var fadeOutEnd = Math.Clamp( value.FadeOutEnd, fadeOutStart, 1f );
		if ( time < fadeInStart ) return 0f;
		if ( time < fadeInEnd && fadeInEnd > fadeInStart ) return (time - fadeInStart) / (fadeInEnd - fadeInStart);
		if ( time <= fadeOutStart ) return 1f;
		if ( time < fadeOutEnd && fadeOutEnd > fadeOutStart ) return 1f - (time - fadeOutStart) / (fadeOutEnd - fadeOutStart);
		return 0f;
	}

	static IEnumerable<float> GetScaleTimes( ParticleScaleDefinition? scale )
	{
		if ( scale is not { } value ) yield break;
		yield return value.StartTime;
		yield return value.EndTime;
	}

	static float EvaluateScale( ParticleScaleDefinition? scale, float time )
	{
		if ( scale is not { } value ) return 1f;
		var start = Math.Clamp( value.StartTime, 0f, 1f );
		var end = Math.Clamp( value.EndTime, start, 1f );
		if ( time <= start ) return value.StartScale;
		if ( time >= end || end <= start ) return value.EndScale;
		return MathX.Lerp( value.StartScale, value.EndScale, (time - start) / (end - start) );
	}

	static ParticleFloat Range( float minimum, float maximum ) =>
		MathF.Abs( maximum - minimum ) < 0.0001f ? minimum : new ParticleFloat( minimum, maximum );

	static Vector3 ClampVector( Vector3 value, float extent ) => new(
		Math.Clamp( value.x, -extent, extent ),
		Math.Clamp( value.y, -extent, extent ),
		Math.Clamp( value.z, -extent, extent ) );

	static Color ToColor( RgbaColor value ) => new( value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f );
	static Vector3 ToSandbox( Vector3 value ) => value;
}

readonly record struct Titanfall2ParticleMapControlPoint( int Index, Vector3 Position );
readonly record struct Titanfall2ParticleMapEntity( string EffectName, string TargetName, Vector3 Position, Angles Rotation, float Scale,
	IReadOnlyList<Titanfall2ParticleMapControlPoint> ControlPoints = null, float WarmupTime = 0f, bool WarmupAndPause = false );
