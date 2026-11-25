namespace GoldSrc.HalfLife;

internal sealed class MountedGun
{
	BoneCollection.Bone _yawBone, _pitchBone;

	readonly List<(BoneCollection.Bone Bone, bool Elevated)> _barrel = [];

	internal void Bind( ModelLoader loader, SkinnedModelRenderer renderer )
	{
		if ( loader.Controllers is not [{ Bone: var yaw }, { Bone: var pitch }, ..] ) return;
		_yawBone = renderer.Model.Bones.GetBone( yaw );
		_pitchBone = renderer.Model.Bones.GetBone( pitch );
		if ( _yawBone is not null ) AddBone( _yawBone, false );

		void AddBone( BoneCollection.Bone bone, bool elevated )
		{
			elevated |= bone == _pitchBone;
			_barrel.Add( (bone, elevated) );
			foreach ( var child in bone.Children ) AddBone( child, elevated );
		}
	}

	internal void Pose( SkinnedModelRenderer renderer, Angles aim )
	{
		if ( _yawBone is null || _pitchBone is null
			|| !renderer.TryGetBoneTransformAnimation( _yawBone, out var yaw )
			|| !renderer.TryGetBoneTransformAnimation( _pitchBone, out var pitch ) ) return;
		var basis = renderer.WorldTransform;
		var pivot = basis.ToLocal( yaw ).Position;
		var hinge = basis.ToLocal( pitch ).Position;
		var swivel = Rotation.FromYaw( aim.yaw );
		var tilt = Rotation.FromPitch( aim.pitch );
		foreach ( var (bone, elevated) in _barrel )
		{
			if ( !renderer.TryGetBoneTransformAnimation( bone, out var pose ) ) continue;
			var local = basis.ToLocal( pose );
			if ( elevated )
			{
				local.Position = hinge + tilt * (local.Position - hinge);
				local.Rotation = tilt * local.Rotation;
			}
			local.Position = pivot + swivel * (local.Position - pivot);
			local.Rotation = swivel * local.Rotation;
			renderer.SetBoneTransform( bone, local );
		}
	}
}
