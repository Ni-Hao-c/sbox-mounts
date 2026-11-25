using System;
using System.Linq;

namespace Titanfall2.Formats;

/// <summary>MDL53 sequence semantics, read from the same source as its animation cells.</summary>
public sealed class Titanfall2SequenceRuntimeData
{
	public sealed class PoseParameter
	{
		public string Name { get; set; }
		public int Index { get; set; }
		public int Flags { get; set; }
		public float Start { get; set; }
		public float End { get; set; }
		public float Loop { get; set; }
		public float ClampValue( float value )
		{
			if ( !float.IsFinite( value ) ) value = 0;
			// The retail SetPoseParameter path first wraps using the descriptor's
			// loop interval, then clamps against the descriptor's physical range.
			if ( Loop > 0 && float.IsFinite( Loop ) )
			{
				var lower = (Start + End - Loop) * 0.5f;
				value -= Loop * MathF.Floor( (value - lower) / Loop );
			}
			return Math.Clamp( value, MathF.Min( Start, End ), MathF.Max( Start, End ) );
		}
	}
	public sealed class AutoLayer
	{
		public int SequenceIndex { get; set; }
		public int PoseIndex { get; set; }
		public int Flags { get; set; }
		public float Start { get; set; }
		public float Peak { get; set; }
		public float Tail { get; set; }
		public float End { get; set; }
	}
	public int Index { get; set; }
	public string Label { get; set; }
	public string Activity { get; set; }
	public int Flags { get; set; }
	public float FadeIn { get; set; }
	public float FadeOut { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }
	public PoseParameter[] Axes { get; set; } = [];
	public float[][] Keys { get; set; } = [];
	public short[] AnimationIndices { get; set; } = [];
	public AutoLayer[] AutoLayers { get; set; } = [];
	public float[] BoneWeights { get; set; } = [];
	public Titanfall2Mdl53Reader.EventInfo[] Events { get; set; } = [];

	public static Titanfall2SequenceRuntimeData Read( ReadOnlySpan<byte> bytes, Titanfall2Mdl53Reader.ParsedModel model,
		Titanfall2Mdl53Reader.SequenceInfo info )
	{
		var s = info.Sequence;
		var result = new Titanfall2SequenceRuntimeData
		{
			Index = info.Index, Label = info.Label, Activity = info.ActivityName, Flags = s.Flags,
			FadeIn = Finite( s.FadeInTime ), FadeOut = Finite( s.FadeOutTime ),
			Width = Math.Max( 1, s.GroupSize[0] ), Height = Math.Max( 1, s.GroupSize[1] ),
			AnimationIndices = info.AnimationIndices.ToArray(), Events = info.Events,
			Axes = new PoseParameter[2], Keys = new float[2][]
		};
		if ( (long)result.Width * result.Height > 4096 ) throw new System.IO.InvalidDataException( "Invalid MDL53 sequence grid." );
		var keyBase = 0;
		for ( var axis = 0; axis < 2; ++axis )
		{
			var count = axis == 0 ? result.Width : result.Height;
			var index = s.ParameterIndex[axis];
			if ( index >= 0 && index < model.Header.LocalPoseParameterCount )
			{
				var offset = checked(model.Header.LocalPoseParameterOffset + index * 20);
				var pose = Titanfall2Mdl53Binary.ReadStruct<Mdl53PoseParameter>( bytes, offset );
				if ( !float.IsFinite( pose.Start ) || !float.IsFinite( pose.End ) || !float.IsFinite( pose.Loop ) )
					throw new System.IO.InvalidDataException( "Non-finite MDL53 pose descriptor." );
				result.Axes[axis] = new PoseParameter
				{
					Index = index, Name = Titanfall2Mdl53Binary.ReadRelativeString( bytes, offset, pose.NameOffset ),
					Flags = pose.Flags, Start = pose.Start, End = pose.End, Loop = pose.Loop
				};
			}
			var keys = result.Keys[axis] = new float[count];
			for ( var i = 0; i < count; ++i )
			{
				keys[i] = s.PoseKeyOffset > 0
					? Titanfall2Mdl53Binary.ReadStruct<float>( bytes, checked(info.Offset + s.PoseKeyOffset + (keyBase + i) * 4) )
					: s.ParameterStart[axis] + (s.ParameterEnd[axis] - s.ParameterStart[axis]) * i / Math.Max( 1, count - 1 );
				if ( !float.IsFinite( keys[i] ) ) throw new System.IO.InvalidDataException( "Non-finite MDL53 pose key." );
			}
			keyBase += count;
		}
		if ( s.WeightListOffset > 0 )
		{
			result.BoneWeights = new float[model.Bones.Length];
			for ( var i = 0; i < result.BoneWeights.Length; ++i )
			{
				var weight = Titanfall2Mdl53Binary.ReadStruct<float>( bytes, checked(info.Offset + s.WeightListOffset + i * 4) );
				if ( !float.IsFinite( weight ) || weight < 0 || weight > 1 ) throw new System.IO.InvalidDataException( "Invalid MDL53 bone weight." );
				result.BoneWeights[i] = weight;
			}
		}
		if ( s.AutoLayerCount > 0 && s.AutoLayerOffset > 0 )
		{
			if ( s.AutoLayerCount > 64 ) throw new System.IO.InvalidDataException( "Invalid MDL53 autolayer count." );
			result.AutoLayers = new AutoLayer[s.AutoLayerCount];
			for ( var i = 0; i < result.AutoLayers.Length; ++i )
			{
				var layer = Titanfall2Mdl53Binary.ReadStruct<Mdl53AutoLayer>( bytes, checked(info.Offset + s.AutoLayerOffset + i * 24) );
				result.AutoLayers[i] = new() { SequenceIndex = layer.SequenceIndex, PoseIndex = layer.PoseIndex, Flags = layer.Flags,
					Start = layer.Start, Peak = layer.Peak, Tail = layer.Tail, End = layer.End };
			}
		}
		return result;
		static float Finite( float value ) => float.IsFinite( value ) ? Math.Clamp( value, 0, 10 ) : 0.1f;
	}
}
