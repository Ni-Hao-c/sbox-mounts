using Titanfall2.Formats;

partial class ModelLoader
{
	// Retail MDL53 autolayers frequently tag same-file aim grids with STUDIO_AL_LOCAL (0x1000).
	internal const int StudioAutoLayerLocalFlag = 0x1000;
	readonly Dictionary<string, Dictionary<string, float>> _actorBoneMasks = new( StringComparer.OrdinalIgnoreCase );

	internal static bool RoleSupportsFallbackAimGrid( string role ) =>
		role == IdleRole || role == FireRole || role == ReloadRole || IsLocomotionRole( role );

	internal static int RankFallbackAimGrid( string label, bool forLocomotion )
	{
		var name = label?.TrimStart( '@' ).ToLowerInvariant() ?? "";
		if ( name.Contains( "crouch", StringComparison.Ordinal ) || name.Contains( "ads", StringComparison.Ordinal )
			|| name.Contains( "pistol", StringComparison.Ordinal ) || name.Contains( "rocket", StringComparison.Ordinal )
			|| name.Contains( "satchel", StringComparison.Ordinal ) || name.Contains( "sword", StringComparison.Ordinal )
			|| name.Contains( "laser", StringComparison.Ordinal ) || name.Contains( "pain", StringComparison.Ordinal )
			|| name.Contains( "sprint", StringComparison.Ordinal ) || name.Contains( "s2s", StringComparison.Ordinal ) )
			return 0;

		if ( forLocomotion )
		{
			return name switch
			{
				"combat_aim_run" or "aim_combat_run_mp" or "aim_combat_walk_mp" or "aim_run_mp" => 150,
				"aim_move_mp" or "aim_ai_run" or "aim_combat_walk_f" or "aim_robo" => 140,
				"aim_static_sp" or "combat_aim_all" or "stand_aim_all" or "combat_aim_stand" => 110,
				"mp_stand_aim_all" or "aim_stand_mp" or "aim_static" or "aim_robo_stand" => 100,
				_ when name.Contains( "aim", StringComparison.Ordinal ) => 50,
				_ => 0
			};
		}

		return name switch
		{
			"aim_static_sp" or "combat_aim_all" or "combat_aim_stand" or "stand_aim_all" or "aim_robo_stand" => 150,
			"mp_stand_aim_all" or "aim_stand_mp" or "aim_static" => 135,
			"combat_aim_stand_no_ik" or "aim_robo" => 115,
			_ when name.Contains( "aim", StringComparison.Ordinal ) => 50,
			_ => 0
		};
	}

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
			var mask = RegisterMask( source, sourceNumber, candidate.Remap, sequence );
			// An absolute full-body sequence's all-ones weightlist does not define an
			// upper-body gesture. Keep the existing adaptation for that case, while
			// authored partial/fractional masks and additive poses retain exact weights.
			if ( clip.Additive || sequence.BoneWeights.Any( weight => weight < 1 ) ) clip.BoneMask = mask;
			var layers = new List<Titanfall2ActorAnimationData.AimLayer>();
			foreach ( var layer in sequence.AutoLayers )
			{
				// Unconditional local aim layers (flags 0 or STUDIO_AL_LOCAL 0x1000).
				if ( (layer.Flags & ~StudioAutoLayerLocalFlag) != 0 || layer.Start != 0 || layer.End != 0
					|| layer.SequenceIndex < 0 || layer.SequenceIndex >= source.Model.Sequences.Length ) continue;
				if ( TryBuildAimLayer( source, sourceNumber, candidate.Remap, layer.SequenceIndex, out var aimLayer ) )
					layers.Add( aimLayer );
			}

			// Recover authored 3x3 aim grids for combat/locomotion roles whose retail sequence
			// omitted an explicit autolayer (e.g. Grunt/Spectre CQB_Idle, Reaper stand_Aim_all, Titan reload).
			if ( layers.Count == 0 && RoleSupportsFallbackAimGrid( candidate.Role )
				&& TryFindFallbackAimLayer( sources, skeleton, candidate.Role, out var fallbackAim ) )
			{
				layers.Add( fallbackAim );
			}

			clip.AimLayers = layers.ToArray();
		}
		catch ( Exception error )
		{
			Titanfall2Log.Warning( $"TF2 sequence metadata '{source.Description}:{candidate.SequenceIndex}' unavailable: {error.Message}" );
		}

		bool TryBuildAimLayer( AnimationSourceData aimSource, int aimSourceNumber, int[] remap, int sequenceIndex, out Titanfall2ActorAnimationData.AimLayer result )
		{
			result = null;
			var aim = Titanfall2SequenceRuntimeData.Read( aimSource.Data, aimSource.Model, aimSource.Model.Sequences[sequenceIndex] );
			if ( aim.AutoLayers.Length != 0 || aim.Width * aim.Height <= 1 || aim.AnimationIndices.Length != aim.Width * aim.Height
				|| !ValidAxis( aim, 0 ) || !ValidAxis( aim, 1 ) ) return false;
			if ( aim.AnimationIndices.Any( index => index < 0 || index >= aimSource.Model.Animations.Length
				|| aimSource.Model.Animations[index].Animation.FrameCount != 1
				|| (aimSource.Model.Animations[index].Animation.Flags & 4) == 0 ) ) return false;

			var id = $"s{aimSourceNumber}_q{aim.Index}";
			var paths = new string[aim.AnimationIndices.Length];
			for ( var i = 0; i < paths.Length; ++i )
			{
				var key = $"aim_{id}_{i}";
				var index = aim.AnimationIndices[i];
				var desc = aimSource.Model.Animations[index].Animation;
				_actorAnimationEntries[key] = new( key, aimSourceNumber == 0 ? null : aimSource.Description, index,
					aim.Flags, desc.Flags, desc.FrameCount, desc.FramesPerSecond );
				paths[i] = $"{Path}/actor/{key}.anim";
			}
			result = new() { Id = id, Source = aimSource.Description, Sequence = aim, Cells = paths, BoneMask = RegisterMask( aimSource, aimSourceNumber, remap, aim ) };
			return true;
		}

		bool TryFindFallbackAimLayer( List<AnimationSourceData> allSources, SkeletonData targetSkeleton, string role, out Titanfall2ActorAnimationData.AimLayer result )
		{
			result = null;
			var forLocomotion = IsLocomotionRole( role );
			var bestScore = 0;
			var bestSourceIndex = -1;
			var bestSeqIndex = -1;
			int[] bestRemap = null;
			var targetBones = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
			for ( var i = 0; i < targetSkeleton.Names.Length; i++ ) targetBones[targetSkeleton.Names[i]] = i;

			for ( var sIdx = 0; sIdx < allSources.Count; sIdx++ )
			{
				var src = allSources[sIdx];
				int[] remap = null;
				for ( var qIdx = 0; qIdx < src.Model.Sequences.Length; qIdx++ )
				{
					var seq = src.Model.Sequences[qIdx];
					if ( seq.Sequence.GroupSize[0] * seq.Sequence.GroupSize[1] < 4 ) continue;
					var score = RankFallbackAimGrid( seq.Label, forLocomotion );
					if ( score <= bestScore ) continue;
					if ( seq.Sequence.AutoLayerCount != 0 ) continue;
					if ( seq.AnimationIndices.Length != seq.Sequence.GroupSize[0] * seq.Sequence.GroupSize[1] ) continue;
					if ( seq.AnimationIndices.Any( idx => idx < 0 || idx >= src.Model.Animations.Length
						|| src.Model.Animations[idx].Animation.FrameCount != 1
						|| (src.Model.Animations[idx].Animation.Flags & 4) == 0 ) ) continue;
					var runtime = Titanfall2SequenceRuntimeData.Read( src.Data, src.Model, seq );
					if ( !ValidAxis( runtime, 0 ) || !ValidAxis( runtime, 1 ) ) continue;
					if ( runtime.Width < 2 || runtime.Height < 2 ) continue;
					remap ??= src.Model.Bones.Select( bone => string.IsNullOrEmpty( bone.Name ) ? -1 : targetBones.GetValueOrDefault( bone.Name, -1 ) ).ToArray();
					if ( remap.Count( x => x >= 0 ) < Math.Min( targetSkeleton.Names.Length, 6 ) ) break;
					bestScore = score;
					bestSourceIndex = sIdx;
					bestSeqIndex = qIdx;
					bestRemap = remap;
				}
			}

			return bestSourceIndex >= 0 && TryBuildAimLayer( allSources[bestSourceIndex], bestSourceIndex, bestRemap, bestSeqIndex, out result );
		}

		string RegisterMask( AnimationSourceData maskSource, int maskSourceNumber, int[] remap, Titanfall2SequenceRuntimeData sequence )
		{
			if ( sequence.BoneWeights.Length != maskSource.Model.Bones.Length ) return null;
			var name = $"tf2_s{maskSourceNumber}_q{sequence.Index}";
			if ( !_actorBoneMasks.ContainsKey( name ) )
			{
				var weights = skeleton.Names.ToDictionary( bone => bone, _ => 0f, StringComparer.OrdinalIgnoreCase );
				for ( var i = 0; i < maskSource.Model.Bones.Length; ++i )
					if ( remap[i] >= 0 ) weights[skeleton.Names[remap[i]]] = sequence.BoneWeights[i];
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
