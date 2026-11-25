using System.Text.Json;
using Titanfall2.Formats;

partial class ModelLoader
{
	readonly object _animationGate = new();
	AnimationCatalog _animationCatalog;
	readonly Dictionary<string, AnimationEntry> _actorAnimationEntries = new( StringComparer.OrdinalIgnoreCase );

	sealed record AnimationEntry( string Name, string SourcePath, int Index, int Flags, int AnimationFlags, int FrameCount, float FrameRate );
	sealed record AnimationCatalog( SkeletonData Skeleton, int[] Parents, AnimationEntry[] Clips, string Metadata );

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		if ( _staticInstance || collisionOnly || heldWeapon || previewOnly ) return null;
		if ( pilotViewAnimations && ResolvePilotViewAnimationChild( name, out type ) is { } pilotChild ) return pilotChild;
		if ( name.Equals( "physics.prefab", StringComparison.OrdinalIgnoreCase ) )
		{
			type = ResourceType.PrefabFile;
			return new PhysicsPrefabResource( this );
		}
		if ( name.Equals( "skeleton.skel", StringComparison.OrdinalIgnoreCase ) )
		{
			type = ResourceType.AnimationSkeleton;
			return new AnimationResource( this, null, skeleton: true );
		}
		if ( infantryAnimations && name.Equals( "actor.skel", StringComparison.OrdinalIgnoreCase ) )
		{
			type = ResourceType.AnimationSkeleton;
			return new AnimationResource( this, null, skeleton: true, actor: true );
		}
		if ( name.Equals( "animations.json", StringComparison.OrdinalIgnoreCase ) )
		{
			type = ResourceType.Text;
			return new AnimationResource( this, null );
		}
		if ( infantryAnimations && name.StartsWith( "actor/", StringComparison.OrdinalIgnoreCase ) && name.EndsWith( ".anim", StringComparison.OrdinalIgnoreCase ) )
		{
			type = ResourceType.AnimationClip;
			return new AnimationResource( this, name[6..^5], actor: true );
		}
		if ( !name.StartsWith( "animations/", StringComparison.OrdinalIgnoreCase ) || !name.EndsWith( ".anim", StringComparison.OrdinalIgnoreCase ) ) return null;
		var clip = name[11..^5];
		if ( clip.Length == 0 || clip.Contains( '/' ) ) return null;
		type = ResourceType.AnimationClip;
		// No file IO or native resources during child lookup; the engine owns child caching.
		return new AnimationResource( this, clip );
	}

	AnimationCatalog GetAnimationCatalog()
	{
		lock ( _animationGate ) return _animationCatalog ??= ReadAnimationCatalog();
	}

	byte[] ReadAnimationSource( string path )
	{
		byte[] data;
		string error;
		var read = path is null ? _source.TryReadAllBytes( out data, out error )
			: _source is ITitanfall2PathAssetSource paths ? paths.TryReadPath( path, out data, out error )
			: throw new InvalidDataException( "This model source cannot resolve include models." );
		if ( !read ) throw new InvalidDataException( $"Cannot read TF2 animation source '{path ?? _source.Description}': {error}" );
		return data;
	}

	AnimationCatalog ReadAnimationCatalog()
	{
		var primary = Titanfall2Mdl53Reader.Parse( ReadAnimationSource( null ).AsSpan(), includeGeometry: false );
		var skeleton = AddSkeleton( null, primary );
		var parents = Enumerable.Range( 0, primary.Bones.Length ).Select( index => GetBoneParent( primary, index ) ).ToArray();
		var pending = new Queue<string>();
		var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { NormalizeAssetPath( _source.Description ) };
		var clips = new List<AnimationEntry>();
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var sequences = new List<object>();
		ReadModel( primary, null );
		while ( pending.Count > 0 )
		{
			var path = pending.Dequeue();
			try { ReadModel( Titanfall2Mdl53Reader.Parse( ReadAnimationSource( path ).AsSpan(), includeGeometry: false ), path ); }
			catch ( Exception error )
			{
				Titanfall2Log.Warning( $"TF2 animation include '{path}' was skipped: {error.Message}" );
			}
		}
		// Retain only names, source identities and metadata. Frame arrays and MDL buffers are
		// transient; loading a skeleton does not build meshes, materials or every animation.
		var metadata = JsonSerializer.Serialize( new
		{
			Version = 1, Format = "Titanfall2.MDL53", Skeleton = $"{Path}/skeleton.skel",
			ScalePolicy = "Uniform scale; axis differences up to 1e-5 relative tolerance are rounded. Larger differences fail explicitly.",
			Clips = clips.Select( entry => new
			{
				entry.Name, Path = $"{Path}/animations/{Uri.EscapeDataString( entry.Name )}.anim", entry.SourcePath,
				AnimationIndex = entry.Index, entry.Flags, entry.AnimationFlags, entry.FrameCount, entry.FrameRate,
				Looping = ((entry.Flags | entry.AnimationFlags) & 1) != 0
			} ),
			Sequences = sequences
		} );
		return new( skeleton, parents, clips.ToArray(), metadata );

		void ReadModel( Titanfall2Mdl53Reader.ParsedModel model, string sourcePath )
		{
			foreach ( var include in model.IncludedModels )
			{
				var path = NormalizeAssetPath( include.Name );
				if ( _source is ITitanfall2PathAssetSource && !string.IsNullOrEmpty( path ) && visited.Count < 64 && visited.Add( path ) ) pending.Enqueue( path );
			}
			if ( model.Sequences.Length == 0 )
			{
				foreach ( var animation in model.Animations ) Add( animation.Name, animation.Index, animation.Animation.Flags, sourcePath, model );
				return;
			}
			foreach ( var sequence in model.Sequences )
			{
				var cellClips = new string[sequence.AnimationIndices.Length];
				var first = true;
				for ( var cell = 0; cell < cellClips.Length; cell++ )
				{
					var index = sequence.AnimationIndices[cell];
					if ( index < 0 || index >= model.Animations.Length ) continue;
					var name = string.IsNullOrWhiteSpace( sequence.Label ) ? model.Animations[index].Name : sequence.Label;
					cellClips[cell] = Add( first ? name : $"{name}~blend{cell}", index, sequence.Sequence.Flags, sourcePath, model );
					first = false;
				}
				sequences.Add( new
				{
					sequence.Label, sequence.ActivityName, sequence.Index, SourcePath = sourcePath,
					sequence.Sequence.Activity, sequence.Sequence.ActivityWeight, sequence.Sequence.Flags,
					sequence.Sequence.FadeInTime, sequence.Sequence.FadeOutTime,
					GroupSize = new[] { sequence.Sequence.GroupSize[0], sequence.Sequence.GroupSize[1] },
					ParameterIndex = new[] { sequence.Sequence.ParameterIndex[0], sequence.Sequence.ParameterIndex[1] },
					ParameterStart = new[] { sequence.Sequence.ParameterStart[0], sequence.Sequence.ParameterStart[1] },
					ParameterEnd = new[] { sequence.Sequence.ParameterEnd[0], sequence.Sequence.ParameterEnd[1] },
					sequence.AnimationIndices, Clips = cellClips,
					// Preserve TF2 descriptors without claiming Source 1's layer/IK evaluation applies.
					sequence.Sequence.AutoLayerCount, sequence.Sequence.EventCount, sequence.Sequence.IkRuleCount
				} );
			}
		}

		string Add( string requested, int index, int flags, string sourcePath, Titanfall2Mdl53Reader.ParsedModel model )
		{
			var name = requested?.Trim().TrimStart( '@' );
			if ( string.IsNullOrWhiteSpace( name ) ) name = $"animation_{index}";
			var description = model.Animations[index].Animation;
			if ( names.Add( name ) ) clips.Add( new( name, sourcePath, index, flags, description.Flags, Math.Max( 1, description.FrameCount ),
				description.FramesPerSecond > 0 && float.IsFinite( description.FramesPerSecond ) ? description.FramesPerSecond : 30f ) );
			return $"{Path}/animations/{Uri.EscapeDataString( name )}.anim";
		}
	}

	sealed class AnimationResource( ModelLoader owner, string clipName, bool skeleton = false, bool actor = false ) : ResourceLoader<Titanfall2Mount>
	{
		protected override object Load()
		{
			var catalog = owner.GetAnimationCatalog();
			if ( skeleton )
			{
				if ( catalog.Skeleton.Names.Length == 0 ) return null;
				var builder = Sandbox.Animation.Skeleton.Builder;
				for ( var bone = 0; bone < catalog.Skeleton.Names.Length; bone++ )
				{
					var parent = catalog.Parents[bone];
					builder.AddBone( catalog.Skeleton.Names[bone], NormalizeAnimationScale( catalog.Skeleton.LocalTransforms[bone], catalog.Skeleton.Names[bone] ),
						parent >= 0 && parent < bone ? catalog.Skeleton.Names[parent] : null );
				}
				builder.AddBoneMask( "tf2_upper", Titanfall2ActorAnimationData.UpperBodyWeights( catalog.Skeleton.Names, catalog.Parents ), 0 );
				if ( actor )
					foreach ( var mask in owner._actorBoneMasks ) builder.AddBoneMask( mask.Key, mask.Value, 0 );
				return builder.Create( Path );
			}
			if ( clipName is null ) return catalog.Metadata;
			var entry = actor ? owner._actorAnimationEntries.GetValueOrDefault( clipName )
				: catalog.Clips.FirstOrDefault( entry => Uri.EscapeDataString( entry.Name ).Equals( clipName, StringComparison.OrdinalIgnoreCase ) );
			if ( entry is null ) return null;
			var data = owner.ReadAnimationSource( entry.SourcePath );
			var model = Titanfall2Mdl53Reader.Parse( data.AsSpan(), includeGeometry: false );
			var decoded = Titanfall2Mdl53AnimationReader.Decode( data, model, entry.Index, entry.Name, entry.Flags );
			var target = catalog.Skeleton;
			var sourceToTarget = model.Bones.Select( bone => Array.FindIndex( target.Names, name => name.Equals( bone.Name, StringComparison.OrdinalIgnoreCase ) ) ).ToArray();
			var nativeSkeleton = Sandbox.Animation.Skeleton.Load( actor ? $"{owner.Path}/actor.skel" : $"{owner.Path}/skeleton.skel" );
			if ( !nativeSkeleton.IsValid() ) return null;
			var clip = Sandbox.Animation.Clip.Builder;
			clip.Skeleton = nativeSkeleton;
			clip.FrameRate = decoded.FramesPerSecond;
			clip.IsAdditive = decoded.Delta;
			foreach ( var frame in decoded.Frames )
			{
				var pose = decoded.Delta ? Enumerable.Repeat( Transform.Zero.WithScale( 0 ), target.Names.Length ).ToArray() : target.LocalTransforms.ToArray();
				for ( var bone = 0; bone < frame.Length; bone++ )
				{
					var index = sourceToTarget[bone];
					if ( index < 0 ) continue;
					pose[index] = new Transform( ToSandbox( frame[bone].Position ), ToSandbox( frame[bone].Rotation ),
						ToSandbox( frame[bone].Scale ) - (decoded.Delta ? Vector3.One : Vector3.Zero) );
					if ( actor && IsLocomotionRole( entry.Name ) && GetBoneParent( model, bone ) < 0 )
					{
						var start = ToSandbox( decoded.Frames[0][bone].Position );
						pose[index] = pose[index].WithPosition( new Vector3( start.x, start.y, pose[index].Position.z ) );
					}
				}
				for ( var bone = 0; bone < pose.Length; bone++ ) pose[bone] = NormalizeAnimationScale( pose[bone], target.Names[bone] );
				clip.AddFrame( pose );
			}
			return clip.Create( Path );
		}
	}

	static Transform NormalizeAnimationScale( Transform pose, string bone )
	{
		var scale = pose.Scale;
		if ( scale.x == scale.y && scale.x == scale.z ) return pose;
		// Retail reference matrices leave ~1e-7 axis differences on nominally unit-scale
		// bones. AG2 requires exact equality. Round numerical noise, never flatten authored
		// nonuniform scaling: that cannot be represented faithfully by this engine API.
		var tolerance = 0.00001f * MathF.Max( 1f, MathF.Max( MathF.Abs( scale.x ), MathF.Max( MathF.Abs( scale.y ), MathF.Abs( scale.z ) ) ) );
		if ( !IsFinite( scale ) || MathF.Abs( scale.x - scale.y ) > tolerance || MathF.Abs( scale.x - scale.z ) > tolerance )
			throw new InvalidDataException( $"TF2 bone '{bone}' uses nonuniform scale {scale}; the engine animation resource requires uniform scale." );
		return pose.WithScale( (scale.x + scale.y + scale.z) / 3f );
	}

	sealed class PhysicsPrefabResource( ModelLoader owner ) : ResourceLoader<Titanfall2Mount>
	{
		PrefabFile _prefab;
		Model _collisionModel;
		protected override object Load()
		{
			var result = owner.GetPhysicsPrefabModel();
			_collisionModel = result.Model;
			if ( !IsUsableModel( _collisionModel ) || _collisionModel.Physics is not { Parts.Count: > 0 } ) return null;
			var builder = new PrefabBuilder().WithName( Path );
			using ( builder.Scope() )
			{
				Game.ActiveScene.Enabled = false;
				var root = new GameObject( true, owner.Name + " physics" );
				// Embedded native physics has the shared name "procedural_physics.vphys".
				// Persist the source identity instead; serializing that physics reference would
				// let a second model replace the first prefab's collision during cloning.
				var resource = root.AddComponent<Titanfall2PhysicsResource>();
				resource.MountIdent = Host.Ident;
				resource.ModelPath = owner.Path;
				owner.ConfigurePhysicsPrefab( resource );
			}
			return _prefab = builder.Create();
		}
		protected override void Shutdown()
		{
			PrefabBuilder.Destroy( _prefab );
			_prefab = null;
			_collisionModel = null;
		}
	}

	Titanfall2CollisionModelResult GetPhysicsPrefabModel() => GetCollisionVariant( skin, body );
	void ConfigurePhysicsPrefab( Titanfall2PhysicsResource resource ) { resource.Skin = skin; resource.Body = body; }

	protected override void Shutdown()
	{
		lock ( _animationGate ) _animationCatalog = null;
		_actorAnimationEntries.Clear();
	}
}
