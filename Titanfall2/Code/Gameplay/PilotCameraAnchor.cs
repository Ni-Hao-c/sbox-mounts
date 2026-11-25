/// <summary>
/// Reads CAMERA_BASE directly from parent-space pose bones. A render root can change
/// before its cached world bones are rebuilt; pairing those two snapshots feeds a
/// camera turn back into viewmodel placement. Neither world snapshot is needed here.
/// </summary>
internal sealed class Titanfall2PilotCameraAnchor
{
	Model _model;
	int[] _parents = [];
	Transform _attachment;

	internal bool TryRead( SkinnedModelRenderer renderer, out Transform anchor )
	{
		anchor = global::Transform.Zero;
		if ( !renderer.IsValid() || !renderer.SceneModel.IsValid() || renderer.Model is null ) return false;
		if ( _model != renderer.Model ) Bind( renderer.Model );
		if ( _parents.Length == 0 ) return false;
		foreach ( var index in _parents )
		{
			var parentSpace = renderer.SceneModel.GetParentSpaceBone( index );
			if ( !parentSpace.IsValid ) return false;
			anchor = anchor.ToWorld( parentSpace );
		}
		anchor = anchor.ToWorld( _attachment );
		return anchor.IsValid;
	}

	void Bind( Model model )
	{
		_model = model;
		_parents = [];
		var attachment = model.Attachments.Get( "CAMERA_BASE" );
		if ( attachment?.Bone is not { } bone ) return;
		var chain = new Stack<int>();
		for ( var current = bone; current is not null; current = current.Parent )
		{
			if ( chain.Count >= model.BoneCount ) return;
			chain.Push( current.Index );
		}
		_parents = chain.ToArray();
		_attachment = attachment.LocalTransform;
	}
}
