using Sandbox.Animation;
using Titanfall2.Formats;

/// <summary>Pure rules shared by the visual clock and descriptor consumers.</summary>
internal static class Titanfall2InteractionAnimationRules
{
	internal static string ModelPath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;
		path = path.Replace( '\\', '/' );
		var mdl = path.IndexOf( ".mdl", StringComparison.OrdinalIgnoreCase );
		if ( mdl < 0 ) return null;
		path = path[..(mdl + 4)] + ".vmdl";
		return path.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase ) ? path : "mount://titanfall2/" + path.TrimStart( '/' );
	}

	internal static float Cycle( float elapsed, float duration, bool loop )
	{
		if ( !float.IsFinite( elapsed ) || !float.IsFinite( duration ) || duration <= 0 ) return 0;
		var cycle = MathF.Max( 0, elapsed ) / duration;
		return loop ? cycle % 1f : Math.Clamp( cycle, 0, 1 );
	}

	internal static string ExistingModelPath( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !path.Contains( ".mdl", StringComparison.OrdinalIgnoreCase ) ) return null;
		path = path.Replace( '\\', '/' );
		if ( !path.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) ) path += ".vmdl";
		return path.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase ) ? path : "mount://titanfall2/" + path.TrimStart( '/' );
	}

	internal static bool TryEvent( IEnumerable<Titanfall2Mdl53Reader.EventInfo> events, string name, out float fraction )
	{
		fraction = 0;
		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		var found = false;
		foreach ( var evt in events ?? [] )
		{
			// Script animation events carry the callback in Options; named engine events use Name.
			if ( !string.Equals( evt.Name, name, StringComparison.OrdinalIgnoreCase )
				&& !string.Equals( evt.Options, name, StringComparison.OrdinalIgnoreCase ) ) continue;
			if ( !float.IsFinite( evt.Cycle ) || evt.Cycle < 0 || evt.Cycle > 1 ) continue;
			if ( !found || evt.Cycle < fraction ) fraction = evt.Cycle;
			found = true;
		}
		return found;
	}
}

/// <summary>
/// Plays real mounted MDL clips on their own skeletons. Owns presentation only:
/// the caller owns interaction locks, authority, elapsed time, camera collision and commit.
/// </summary>
internal sealed class Titanfall2TitanInteractionPresentation : IDisposable
{
	internal const string DefaultPilotModel = "mount://titanfall2/models/humans/pilots/pilot_medium_reaper_m.mdl.vmdl";
	internal const string DefaultFirstPersonModel = "mount://titanfall2/models/weapons/arms/pov_pilot_medium_reaper_m.mdl.vmdl";
	internal const string BatteryHandPropModel = "models/props/titan_battery_static/titan_battery_static.mdl";
	internal const string GrenadeHandPropModel = "models/weapons/grenades/m20_f_grenade.mdl";
	readonly List<(ModelRenderer Renderer, bool Enabled)> _hidden = [];
	readonly SkinnedModelRenderer _titan;
	readonly string _attachment;
	readonly List<string> _diagnostics = [];
	Track _pilotTrack, _titanTrack, _firstPersonTrack;
	GameObject _pilotObject, _firstPersonObject;
	ModelRenderer _handPropThird, _handPropFirst;
	string _handPropPath, _handPropAttachment;
	bool _handPropVisible, _reportedMissingHandAttachment;
	bool _disposed;
	bool _reportedMissingRef, _reportedMissingAnchor, _reportedMissingCamera;
	Titanfall2Mdl53Reader.EventInfo[] _events = [];

	internal float PilotDuration => _pilotTrack?.Duration ?? 0;
	internal float TitanDuration => _titanTrack?.Duration ?? 0;
	// Pilot sequence is the retail interaction completion clock, not the longer Titan recovery.
	internal float Duration => PilotDuration;
	internal bool HasPilotAnimation => _pilotTrack is not null;
	internal bool HasTitanAnimation => _titanTrack is not null;
	internal bool HasFirstPersonAnimation => _firstPersonTrack is not null;
	internal string Diagnostic => string.Join( "; ", _diagnostics );

	Titanfall2TitanInteractionPresentation( SkinnedModelRenderer titan, string attachment )
	{
		_titan = titan;
		_attachment = string.IsNullOrEmpty( attachment ) ? "hijack" : attachment;
	}

	internal static bool TryBegin( SkinnedModelRenderer titan, GameObject pilot, string pilotModelPath,
		string pilotSequence, string titanSequence, string attachment,
		out Titanfall2TitanInteractionPresentation presentation, out string diagnostic,
		string firstPersonModelPath = null, string firstPersonSequence = null )
	{
		presentation = null;
		diagnostic = "interaction requires a valid Titan renderer, Pilot and authored Pilot sequence";
		if ( !titan.IsValid() || !pilot.IsValid() || string.IsNullOrWhiteSpace( pilotSequence ) ) return false;
		var result = new Titanfall2TitanInteractionPresentation( titan, attachment );
		try
		{
			var pilotPath = Titanfall2InteractionAnimationRules.ModelPath( pilotModelPath ?? DefaultPilotModel );
			result._pilotObject = CreateBody( pilot, pilotPath, "TF2 interaction pilot", out var body );
			result._pilotTrack = new Track( body, pilotPath, pilotSequence );
			result._events = ReadEvents( pilotPath, pilotSequence );
			if ( !string.IsNullOrEmpty( titanSequence ) )
			{
				// Keep the loaded variant identity. An infantry model owns ordinary
				// skeleton/animations children too; its unregistered normal sibling may not.
				var titanPath = Titanfall2InteractionAnimationRules.ExistingModelPath( titan.Model?.ResourcePath )
					?? Titanfall2InteractionAnimationRules.ExistingModelPath( titan.GetComponentInParent<Titanfall2Npc>()?.ModelPath );
				result._titanTrack = new Track( titan, titanPath, titanSequence );
			}
			var controller = pilot.GetComponent<PlayerController>();
			if ( !string.IsNullOrEmpty( firstPersonSequence ) && controller is { IsProxy: false }
				&& !Application.IsHeadless && !Application.IsDedicatedServer )
			{
				try
				{
					var firstPath = Titanfall2InteractionAnimationRules.ModelPath( firstPersonModelPath ?? DefaultFirstPersonModel );
					result._firstPersonObject = CreateBody( pilot, firstPath, "TF2 interaction first person", out var firstBody );
					if ( firstBody.Model.Attachments.Get( "CAMERA" ) is null && firstBody.Model.Attachments.Get( "CAMERA_BASE" ) is null )
						throw new InvalidDataException( "First-person model has no CAMERA/CAMERA_BASE attachment" );
					result._firstPersonTrack = new Track( firstBody, firstPath, firstPersonSequence );
					result._firstPersonObject.Tags.Add( "firstperson", "viewmodel" );
					firstBody.RenderType = ModelRenderer.ShadowRenderType.Off;
					firstBody.RenderOptions.Game = false;
					firstBody.RenderOptions.Overlay = true;
					body.RenderOptions.Game = false;
				}
				catch ( Exception error )
				{
					result._firstPersonTrack?.Dispose();
					result._firstPersonTrack = null;
					result._firstPersonObject?.Destroy();
					result._firstPersonObject = null;
					result._diagnostics.Add( $"first-person unavailable ({firstPersonSequence}): {error.Message}; using third-person camera" );
				}
			}
			// Never put Respawn animation tracks onto a Citizen skeleton. Hide only the
			// character body hierarchy; independent weapon viewmodels remain caller-owned.
			var original = controller?.Renderer;
			if ( original.IsValid() )
				foreach ( var renderer in original.GameObject.GetComponentsInChildren<ModelRenderer>( true ) )
				{
					result._hidden.Add( (renderer, renderer.Enabled) );
					renderer.Enabled = false;
				}
			result._diagnostics.Add( $"retail clip {pilotSequence}; alignment=animated-ref/{result._attachment}; physical pilot motion is caller-owned" );
			result.Tick( 0 );
			presentation = result;
			diagnostic = result.Diagnostic;
			return true;
		}
		catch ( Exception error )
		{
			result.Dispose();
			diagnostic = $"retail interaction unavailable: {error.Message}";
			return false;
		}
	}

	static GameObject CreateBody( GameObject pilot, string modelPath, string name, out SkinnedModelRenderer renderer )
	{
		if ( string.IsNullOrEmpty( modelPath ) ) throw new InvalidDataException( "Interaction model has no mounted MDL path" );
		var model = Titanfall2TitanInteractionAssets.MountFor( modelPath )?.GetInteractionModel( modelPath ) ?? Model.Load( modelPath );
		if ( model is null || model.IsError ) throw new InvalidDataException( $"Missing interaction model {modelPath}" );
		var body = new GameObject( pilot.Scene, true, name );
		body.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
		body.WorldTransform = pilot.WorldTransform;
		renderer = body.AddComponent<SkinnedModelRenderer>();
		renderer.Model = model;
		return body;
	}

	static Titanfall2Mdl53Reader.EventInfo[] ReadEvents( string modelPath, string sequence )
	{
		var ident = "titanfall2";
		if ( modelPath?.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase ) == true )
		{
			var slash = modelPath.IndexOf( '/', 8 );
			if ( slash > 8 ) ident = modelPath[8..slash];
		}
		return (Sandbox.Mounting.Directory.Get( ident )?.GetByPath( modelPath ) as ModelLoader)?.GetInteractionEvents( sequence ) ?? [];
	}

	internal bool TryGetEventFraction( string name, out float fraction ) =>
		Titanfall2InteractionAnimationRules.TryEvent( _events, name, out fraction );

	internal void Tick( float elapsedSeconds, bool loop = false )
	{
		if ( _disposed || !_titan.IsValid() ) return;
		_titanTrack?.Tick( elapsedSeconds, loop );
		_pilotTrack?.Tick( elapsedSeconds, loop );
		_firstPersonTrack?.Tick( elapsedSeconds, loop );
		var anchor = _titan.GetAttachment( _attachment );
		if ( anchor is null && !_reportedMissingAnchor )
		{
			_reportedMissingAnchor = true;
			_diagnostics.Add( $"missing Titan {_attachment}; explicit fallback=Titan model origin" );
		}
		var target = anchor ?? _titan.WorldTransform;
		Align( _pilotTrack?.Renderer, target );
		Align( _firstPersonTrack?.Renderer, target );
		UpdateHandProps();
	}

	/// <summary>Presentation-only prop; the interaction session drives authored show/hide events.</summary>
	internal bool SetHandProp( string modelPath, string attachment, bool visible )
	{
		if ( _disposed ) return false;
		if ( string.IsNullOrWhiteSpace( modelPath ) ) { ClearHandProps(); return true; }
		var path = Titanfall2InteractionAnimationRules.ModelPath( modelPath );
		if ( path is null ) { ClearHandProps(); return false; }
		attachment = string.IsNullOrWhiteSpace( attachment ) ? "R_HAND" : attachment;
		if ( path != _handPropPath || attachment != _handPropAttachment )
		{
			ClearHandProps();
			try
			{
				var model = Titanfall2TitanInteractionAssets.MountFor( path )?.GetInteractionModel( path ) ?? Model.Load( path );
				if ( model is null || model.IsError ) throw new InvalidDataException( "Missing hand prop " + path );
				_handPropPath = path;
				_handPropAttachment = attachment;
				_handPropThird = CreateHandProp( _pilotTrack.Renderer, model, false );
				if ( HasFirstPersonAnimation ) _handPropFirst = CreateHandProp( _firstPersonTrack.Renderer, model, true );
			}
			catch ( Exception error )
			{
				ClearHandProps();
				var message = $"hand prop unavailable: {error.Message}";
				if ( !_diagnostics.Contains( message ) ) _diagnostics.Add( message );
				return false;
			}
		}
		_handPropVisible = visible;
		UpdateHandProps();
		return true;
	}

	static ModelRenderer CreateHandProp( SkinnedModelRenderer body, Model model, bool firstPerson )
	{
		var obj = new GameObject( body.Scene, true, firstPerson ? "TF2 interaction hand prop first person" : "TF2 interaction hand prop" );
		obj.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
		var renderer = obj.AddComponent<ModelRenderer>();
		renderer.Model = model;
		renderer.Enabled = false;
		renderer.RenderOptions.Game = !firstPerson && body.RenderOptions.Game;
		renderer.RenderOptions.Overlay = firstPerson;
		if ( firstPerson )
		{
			obj.Tags.Add( "firstperson", "viewmodel" );
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
		return renderer;
	}

	void UpdateHandProps()
	{
		UpdateHandProp( _handPropThird, _pilotTrack?.Renderer );
		UpdateHandProp( _handPropFirst, _firstPersonTrack?.Renderer );
	}

	void UpdateHandProp( ModelRenderer prop, SkinnedModelRenderer body )
	{
		if ( !prop.IsValid() ) return;
		var anchor = body.IsValid() ? body.GetAttachment( _handPropAttachment ) : null;
		if ( anchor is null && body.IsValid() && body.TryGetBoneTransform( _handPropAttachment, out var bone ) ) anchor = bone;
		prop.Enabled = _handPropVisible && anchor is not null;
		if ( anchor is { } transform ) prop.WorldTransform = transform;
		else if ( !_reportedMissingHandAttachment )
		{
			_reportedMissingHandAttachment = true;
			_diagnostics.Add( $"hand prop hidden: missing authored {_handPropAttachment} attachment/bone" );
		}
	}

	void ClearHandProps()
	{
		if ( _handPropThird.IsValid() ) _handPropThird.GameObject.Destroy();
		if ( _handPropFirst.IsValid() ) _handPropFirst.GameObject.Destroy();
		_handPropThird = null;
		_handPropFirst = null;
		_handPropPath = null;
		_handPropAttachment = null;
		_handPropVisible = false;
		_reportedMissingHandAttachment = false;
	}

	void Align( SkinnedModelRenderer renderer, Transform target )
	{
		if ( !renderer.IsValid() ) return;
		// The clip keeps its authored bone translations. Remove the evaluated ref
		// transform from the visual root so that ref coincides with Titan.hijack.
		// Sampling uses the last evaluated pose; the scene animation stage evaluates
		// the new normalized time. No synthetic movement curve is substituted.
		if ( renderer.GetAttachment( "ref" ) is { } reference )
		{
			var localRef = renderer.WorldTransform.ToLocal( reference );
			renderer.WorldTransform = target.ToWorld( localRef.ToLocal( Transform.Zero ) );
		}
		else
		{
			renderer.WorldTransform = target;
			if ( !_reportedMissingRef )
			{
				_reportedMissingRef = true;
				_diagnostics.Add( "missing Pilot animated ref; explicit fallback=authored root pose at Titan anchor" );
			}
		}
	}

	internal bool TryGetPilotTransform( out Transform transform )
	{
		transform = Transform.Zero;
		if ( _disposed || !_pilotObject.IsValid() ) return false;
		transform = _pilotObject.WorldTransform;
		return true;
	}

	internal bool TryGetCamera( out Transform transform )
	{
		transform = Transform.Zero;
		if ( _disposed ) return false;
		if ( _firstPersonTrack?.Renderer.IsValid() == true )
		{
			var camera = _firstPersonTrack.Renderer.GetAttachment( "CAMERA" )
				?? _firstPersonTrack.Renderer.GetAttachment( "CAMERA_BASE" );
			if ( camera is { } first ) { transform = first; return true; }
		}
		if ( _pilotTrack?.Renderer.IsValid() == true && _pilotTrack.Renderer.GetAttachment( "VDU" ) is { } third )
		{
			transform = third;
			return true;
		}
		if ( !_reportedMissingCamera )
		{
			_reportedMissingCamera = true;
			_diagnostics.Add( "missing animation camera VDU/CAMERA; caller must select an explicit camera fallback" );
		}
		return false;
	}

	public void Dispose()
	{
		if ( _disposed ) return;
		_disposed = true;
		ClearHandProps();
		_titanTrack?.Dispose();
		_pilotTrack?.Dispose();
		_firstPersonTrack?.Dispose();
		if ( _pilotObject.IsValid() ) _pilotObject.Destroy();
		if ( _firstPersonObject.IsValid() ) _firstPersonObject.Destroy();
		foreach ( var (renderer, enabled) in _hidden ) if ( renderer.IsValid() ) renderer.Enabled = enabled;
		_hidden.Clear();
	}

	sealed class Track : IDisposable
	{
		internal SkinnedModelRenderer Renderer { get; }
		internal float Duration { get; }
		readonly Graph _graph, _previousGraph;
		readonly bool _previousUseGraph;
		bool _disposed;
		internal Track( SkinnedModelRenderer renderer, string modelPath, string sequence )
		{
			if ( string.IsNullOrEmpty( modelPath ) ) throw new InvalidDataException( "Renderer has no mounted MDL identity" );
			var mount = Titanfall2TitanInteractionAssets.MountFor( modelPath );
			var skeleton = mount?.GetInteractionSkeleton( $"{modelPath}/skeleton.skel" ) ?? Skeleton.Load( $"{modelPath}/skeleton.skel" );
			if ( !skeleton.IsValid() ) throw new InvalidDataException( $"Missing skeleton: {modelPath}" );
			var clipPath = $"{modelPath}/animations/{Uri.EscapeDataString( sequence )}.anim";
			var clip = mount?.GetInteractionClip( clipPath ) ?? Clip.Load( clipPath );
			if ( clip is null || !float.IsFinite( clip.Duration ) || clip.Duration <= 0 )
				throw new InvalidDataException( $"Missing/invalid retail sequence {sequence} on {modelPath}" );
			if ( clip.IsAdditive ) throw new InvalidDataException( $"Interaction {sequence} is additive and cannot serve as a complete body pose" );
			Renderer = renderer;
			Duration = clip.Duration;
			_previousGraph = renderer.Animation.Graph;
			_previousUseGraph = renderer.UseAnimGraph;
			var builder = Graph.Builder;
			builder.Skeleton = skeleton.Name;
			var cycle = builder.AddControlParameter( "interaction_cycle", GraphBuilder.NodeKind.Float,
				new() { DefaultFloat = 0, Min = 0, Max = 1 } );
			var pose = builder.TimeControlledClip( clip, cycle, new() { SampleRootMotion = false } );
			_graph = builder.Create( pose, "Titanfall 2 interaction " + sequence );
			renderer.Animation.Graph = _graph;
			renderer.UseAnimGraph = true;
		}
		internal void Tick( float elapsed, bool loop )
		{
			if ( Renderer.IsValid() && Renderer.Animation.IsValid && Renderer.Animation.Graph == _graph )
				Renderer.Animation.Parameters.TrySet( "interaction_cycle", Titanfall2InteractionAnimationRules.Cycle( elapsed, Duration, loop ) );
		}
		public void Dispose()
		{
			if ( _disposed ) return;
			_disposed = true;
			if ( Renderer.IsValid() && Renderer.Animation.Graph == _graph )
			{
				Renderer.Animation.Graph = _previousGraph;
				Renderer.UseAnimGraph = _previousUseGraph;
			}
			_graph?.Dispose();
		}
	}
}
