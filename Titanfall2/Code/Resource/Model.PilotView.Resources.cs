using Titanfall2.Formats;

partial class ModelLoader
{
	// These are independent mounted resources, so the engine owns their caching.
	// No frame arrays or source MDL buffers survive a resource load.
	ResourceLoader ResolvePilotViewAnimationChild( string name, out ResourceType type )
	{
		type = default;
		if ( name.Equals( "pilot/skeleton.skel", StringComparison.OrdinalIgnoreCase ) )
		{
			type = ResourceType.AnimationSkeleton;
			return new PilotAnimationResource( this, null );
		}
		if ( !name.StartsWith( "pilot/", StringComparison.OrdinalIgnoreCase ) || !name.EndsWith( ".anim", StringComparison.OrdinalIgnoreCase ) ) return null;
		var role = name[6..^5];
		if ( role.Length == 0 || role.Contains( '/' ) ) return null;
		type = ResourceType.AnimationClip;
		return new PilotAnimationResource( this, role );
	}

	sealed class PilotAnimationResource( ModelLoader owner, string role ) : ResourceLoader<Titanfall2Mount>
	{
		protected override object Load()
		{
			var bytes = owner.ReadAnimationSource( null );
			var model = Titanfall2Mdl53Reader.Parse( bytes.AsSpan(), includeGeometry: false );
			if ( role is null )
			{
				var data = AddSkeleton( null, model );
				var builder = Sandbox.Animation.Skeleton.Builder;
				for ( var i = 0; i < data.Names.Length; ++i )
				{
					var parent = GetBoneParent( model, i );
					builder.AddBone( data.Names[i], NormalizeAnimationScale( data.LocalTransforms[i], data.Names[i] ),
						parent >= 0 && parent < i ? data.Names[parent] : null );
				}
				return builder.Create( Path );
			}
			var sequences = ResolvePilotViewSequences( model, owner.ImportPath );
			var sequence = role switch
			{
				Titanfall2PilotAnimationPlayer.IdleDelta => sequences.Idle,
				Titanfall2PilotAnimationPlayer.FireDelta => sequences.Fire,
				Titanfall2PilotAnimationPlayer.AdsPose => sequences.AdsIn,
				Titanfall2PilotViewModel.Idle => sequences.Idle,
				Titanfall2PilotViewModel.Fire => sequences.Fire,
				Titanfall2PilotViewModel.Draw => sequences.Draw,
				Titanfall2PilotViewModel.Reload => sequences.Reload,
				Titanfall2PilotViewModel.EmptyReload => sequences.EmptyReload,
				Titanfall2PilotViewModel.AdsIn => sequences.AdsIn,
				Titanfall2PilotViewModel.AdsOut => sequences.AdsOut,
				Titanfall2PilotViewModel.Sprint => sequences.Sprint,
				Titanfall2PilotViewModel.SprintRaise => sequences.SprintRaise,
				Titanfall2PilotViewModel.OneHandedIdle => sequences.OneHandedIdle,
				Titanfall2PilotViewModel.Charge => sequences.Charge,
				Titanfall2PilotViewModel.Cooldown or Titanfall2PilotViewModel.CooldownAds => sequences.Cooldown,
				_ => Enumerable.Range( 1, sequences.EmptyReloadLate.Length ).FirstOrDefault( i => Titanfall2PilotViewModel.EmptyReloadLate( i ) == role ) is > 0 and var lateIndex
					? sequences.EmptyReloadLate[lateIndex - 1] : null
			};
			if ( sequence is null ) return null;
			var decoded = role == Titanfall2PilotViewModel.CooldownAds && sequence.AnimationIndices.Length == 2
				? Titanfall2Mdl53AnimationReader.Decode( bytes, model, sequence.AnimationIndices[1], sequence.Label, sequence.Sequence.Flags ) : Decode( sequence );
			var additive = role is Titanfall2PilotAnimationPlayer.IdleDelta or Titanfall2PilotAnimationPlayer.FireDelta;
			if ( additive && !decoded.Delta ) return null;
			var skeleton = Sandbox.Animation.Skeleton.Load( $"{owner.Path}/pilot/skeleton.skel" );
			if ( !skeleton.IsValid() ) return null;
			Transform[] ready = null;
			if ( !additive && decoded.Delta )
			{
				var reload = Decode( sequences.Reload );
				var empty = Decode( sequences.EmptyReload );
				var draw = Decode( sequences.Draw );
				var frame = !reload.Delta && reload.Frames.Length > 0 ? reload.Frames[0]
					: !empty.Delta && empty.Frames.Length > 0 ? empty.Frames[0]
					: !draw.Delta && draw.Frames.Length > 0 ? draw.Frames[^1]
					: throw new InvalidDataException( $"Pilot ready stance unavailable: {owner.Path}" );
				ready = frame.Select( Pose ).ToArray();
				if ( role == Titanfall2PilotViewModel.CooldownAds && sequences.AdsIn is { } ads )
				{
					var adsPose = Decode( ads ); var last = adsPose.Frames[^1];
					for ( var i = 0; i < ready.Length; ++i )
					{
						var p = Pose( last[i] );
						ready[i] = adsPose.Delta ? new( ready[i].Position + p.Position, ready[i].Rotation * p.Rotation, ready[i].Scale * p.Scale ) : p;
					}
				}
			}
			var clip = Sandbox.Animation.Clip.Builder;
			clip.Skeleton = skeleton;
			clip.FrameRate = decoded.FramesPerSecond;
			clip.IsAdditive = additive;
			foreach ( var frame in decoded.Frames )
			{
				var transforms = new Transform[frame.Length];
				for ( var i = 0; i < frame.Length; ++i )
				{
					var pose = Pose( frame[i] );
					if ( additive ) pose = pose.WithScale( pose.Scale - Vector3.One );
					else if ( decoded.Delta ) pose = new Transform( ready[i].Position + pose.Position,
						ready[i].Rotation * pose.Rotation, ready[i].Scale * pose.Scale );
					transforms[i] = NormalizeAnimationScale( pose, model.Bones[i].Name );
				}
				clip.AddFrame( transforms );
			}
			return clip.Create( Path );

			Titanfall2Mdl53AnimationReader.DecodedAnimation Decode( Titanfall2Mdl53Reader.SequenceInfo selected ) =>
				Titanfall2Mdl53AnimationReader.Decode( bytes, model, selected.AnimationIndices[0], selected.Label, selected.Sequence.Flags );
			static Transform Pose( Titanfall2Mdl53AnimationReader.BonePose pose ) => new( ToSandbox( pose.Position ), ToSandbox( pose.Rotation ), ToSandbox( pose.Scale ) );
		}
	}
}
