using Titanfall2.Formats;

partial class ModelLoader
{
	readonly Dictionary<string, Dictionary<string, float>> _actorBoneMasks = new( StringComparer.OrdinalIgnoreCase );

	void AddActorSequenceData( Titanfall2ActorAnimationData.Clip clip, InfantryCandidate candidate, SkeletonData skeleton,
		List<AnimationSourceData> sources, Titanfall2Mdl53AnimationReader.DecodedAnimation decoded )
	{
		if ( candidate.SequenceIndex < 0 ) return;
		var source = candidate.Source;
		var sourceNumber = sources.IndexOf( source );
		try
		{
			var sequence = Titanfall2SequenceRuntimeData.Read( source.Data, source.Model, source.Model.Sequences[candidate.SequenceIndex] );
			clip.Sequence = sequence;
			if ( candidate.Role == MortarAttackRole && !decoded.Delta )
				clip.EventBonePoses = SampleActorEventBones( sequence, decoded, candidate.Remap, skeleton, sources[0].Model );
			var mask = RegisterMask( sequence );
			// An absolute full-body sequence's all-ones weightlist does not define an
			// upper-body gesture. Keep the existing adaptation for that case, while
			// authored partial/fractional masks and additive poses retain exact weights.
			if ( clip.Additive || sequence.BoneWeights.Any( weight => weight < 1 ) ) clip.BoneMask = mask;
			var layers = new List<Titanfall2ActorAnimationData.AimLayer>();
			foreach ( var layer in sequence.AutoLayers )
			{
				// These unconditional local aim layers are verified in both BT and
				// pete_core. Preserve other flags/envelopes in Sequence, do not pretend
				// Source 1's similarly named flags establish Respawn's runtime behavior.
				if ( layer.Flags != 0 || layer.Start != 0 || layer.End != 0
					|| layer.SequenceIndex < 0 || layer.SequenceIndex >= source.Model.Sequences.Length ) continue;
				var aim = Titanfall2SequenceRuntimeData.Read( source.Data, source.Model, source.Model.Sequences[layer.SequenceIndex] );
				if ( aim.AutoLayers.Length != 0 || aim.Width * aim.Height <= 1 || aim.AnimationIndices.Length != aim.Width * aim.Height
					|| !ValidAxis( aim, 0 ) || !ValidAxis( aim, 1 ) ) continue;
				if ( aim.AnimationIndices.Any( index => index < 0 || index >= source.Model.Animations.Length
					|| source.Model.Animations[index].Animation.FrameCount != 1
					|| (source.Model.Animations[index].Animation.Flags & 4) == 0 ) ) continue;
				var id = $"s{sourceNumber}_q{aim.Index}";
				var paths = new string[aim.AnimationIndices.Length];
				for ( var i = 0; i < paths.Length; ++i )
				{
					var key = $"aim_{id}_{i}";
					var index = aim.AnimationIndices[i];
					var desc = source.Model.Animations[index].Animation;
					_actorAnimationEntries[key] = new( key, sourceNumber == 0 ? null : source.Description, index,
						aim.Flags, desc.Flags, desc.FrameCount, desc.FramesPerSecond );
					paths[i] = $"{Path}/actor/{key}.anim";
				}
				layers.Add( new() { Id = id, Source = source.Description, Sequence = aim, Cells = paths, BoneMask = RegisterMask( aim ) } );
			}
			clip.AimLayers = layers.ToArray();
		}
		catch ( Exception error )
		{
			Titanfall2Log.Warning( $"TF2 sequence metadata '{source.Description}:{candidate.SequenceIndex}' unavailable: {error.Message}" );
		}

		string RegisterMask( Titanfall2SequenceRuntimeData sequence )
		{
			if ( sequence.BoneWeights.Length != source.Model.Bones.Length ) return null;
			var name = $"tf2_s{sourceNumber}_q{sequence.Index}";
			if ( !_actorBoneMasks.ContainsKey( name ) )
			{
				var weights = skeleton.Names.ToDictionary( bone => bone, _ => 0f, StringComparer.OrdinalIgnoreCase );
				for ( var i = 0; i < source.Model.Bones.Length; ++i )
					if ( candidate.Remap[i] >= 0 ) weights[skeleton.Names[candidate.Remap[i]]] = sequence.BoneWeights[i];
				_actorBoneMasks[name] = weights;
			}
			return name;
		}
		static bool ValidAxis( Titanfall2SequenceRuntimeData sequence, int axis )
		{
			var count = axis == 0 ? sequence.Width : sequence.Height;
			if ( count == 1 ) return true;
			if ( sequence.Axes[axis]?.Name is not ("aim_yaw" or "aim_pitch") || sequence.Keys[axis].Length != count ) return false;
			var keys = sequence.Keys[axis];
			for ( var i = 1; i < count; ++i ) if ( keys[i] <= keys[i - 1] ) return false;
			return true;
		}
	}

	static Titanfall2ActorAnimationData.EventBonePose[] SampleActorEventBones( Titanfall2SequenceRuntimeData sequence,
		Titanfall2Mdl53AnimationReader.DecodedAnimation animation, int[] remap, SkeletonData target, Titanfall2Mdl53Reader.ParsedModel primary )
	{
		// Event poses are intentionally independent of a renderer, camera pass, or
		// local animation clock. Dedicated hosts can launch from the authored pose.
		// This first path accepts absolute, single-cell mortar sequences only.
		if ( sequence.AnimationIndices.Length != 1 || sequence.AutoLayers.Length != 0 || animation.Frames.Length == 0 ) return [];
		var anchors = Enumerable.Range( 0, target.Names.Length ).Where( bone =>
			target.Names[bone].Contains( "gun", StringComparison.OrdinalIgnoreCase )
			|| target.Names[bone].Contains( "prophand", StringComparison.OrdinalIgnoreCase ) ).ToArray();
		var result = new List<Titanfall2ActorAnimationData.EventBonePose>();
		for ( var eventIndex = 0; eventIndex < sequence.Events.Length; ++eventIndex )
		{
			var evt = sequence.Events[eventIndex];
			if ( evt.Name != "AE_NPC_WEAPON_FIRE" || !float.IsFinite( evt.Cycle ) ) continue;
			var frame = Math.Clamp( evt.Cycle, 0, 1 ) * (animation.Frames.Length - 1);
			var from = (int)MathF.Floor( frame );
			var to = Math.Min( from + 1, animation.Frames.Length - 1 );
			var fraction = frame - from;
			var locals = target.LocalTransforms.ToArray();
			for ( var sourceBone = 0; sourceBone < remap.Length; ++sourceBone )
			{
				var targetBone = remap[sourceBone];
				if ( targetBone < 0 ) continue;
				var a = animation.Frames[from][sourceBone];
				var b = animation.Frames[to][sourceBone];
				var poseA = new Transform( ToSandbox( a.Position ), ToSandbox( a.Rotation ), ToSandbox( a.Scale ) );
				var poseB = new Transform( ToSandbox( b.Position ), ToSandbox( b.Rotation ), ToSandbox( b.Scale ) );
				locals[targetBone] = Transform.Lerp( poseA, poseB, fraction, true );
			}
			var modelSpace = new Transform[locals.Length];
			for ( var bone = 0; bone < locals.Length; ++bone ) locals[bone] = NormalizeAnimationScale( locals[bone], target.Names[bone] );
			var states = new byte[locals.Length];
			foreach ( var bone in anchors )
				result.Add( new() { EventIndex = eventIndex, Cycle = evt.Cycle, Bone = target.Names[bone], Transform = Accumulate( bone ) } );
			Transform Accumulate( int bone )
			{
				if ( states[bone] == 2 ) return modelSpace[bone];
				if ( states[bone] == 1 ) throw new InvalidDataException( "Cyclic actor skeleton in event pose." );
				states[bone] = 1;
				var parent = GetBoneParent( primary, bone );
				modelSpace[bone] = parent >= 0 ? Accumulate( parent ).ToWorld( locals[bone] ) : locals[bone];
				states[bone] = 2;
				return modelSpace[bone];
			}
		}
		return result.ToArray();
	}
}
