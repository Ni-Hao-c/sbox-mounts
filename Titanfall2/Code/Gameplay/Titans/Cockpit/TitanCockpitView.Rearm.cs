public sealed partial class Titanfall2TitanCockpitView
{
	readonly Titanfall2PilotCameraAnchor _rearmCameraAnchor = new( "CAMERA_BASE" );
	GameObject _rearmVisual;
	SkinnedModelRenderer _rearmRenderer;
	string _rearmModelPath;
	Model _rearmModel;

	void UpdateRearmVisual( Titanfall2Npc actor, Titanfall2TitanKit kit )
	{
		var pose = Titanfall2RearmPresentation.Sample( kit?.RearmVisualElapsed ?? float.PositiveInfinity, firstPerson: true );
		if ( pose.Clip is null ) { ClearRearmVisual(); return; }
		var path = $"mount://{actor.MountIdent}/{Titanfall2RearmPresentation.ViewModel}.vmdl";
		if ( _rearmModelPath != path )
		{
			ClearRearmVisual();
			_rearmModelPath = path;
			_rearmModel = Titanfall2GameplayVisuals.GetModel( path );
		}
		if ( _rearmModel is null || _rearmModel.IsError || !_rearmModel.AnimationNames.Contains( pose.Clip )
			|| _rearmModel.Attachments.Get( "CAMERA_BASE" )?.Bone is null ) return;
		if ( !_rearmVisual.IsValid() )
		{
			_rearmVisual = new GameObject( Scene, false, "TF2 Monarch Rearm Pods POV" );
			_rearmVisual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
			_rearmVisual.Tags.Add( "firstperson", "viewmodel" );
			_rearmRenderer = _rearmVisual.AddComponent<SkinnedModelRenderer>();
			_rearmRenderer.Model = _rearmModel;
			_rearmRenderer.UseAnimGraph = false;
			_rearmRenderer.Sequence.Blending = false;
			_rearmRenderer.RenderOptions.Game = false;
			_rearmRenderer.RenderOptions.Overlay = false;
			_rearmVisual.Enabled = true;
		}
		if ( _rearmRenderer.Sequence.Name != pose.Clip ) _rearmRenderer.Sequence.Name = pose.Clip;
		_rearmRenderer.Sequence.Looping = false;
		_rearmRenderer.Sequence.PlaybackRate = 0;
		_rearmRenderer.Sequence.TimeNormalized = pose.Cycle;
	}

	void PlaceRearmVisual( bool active, in CameraView view )
	{
		if ( !_rearmRenderer.IsValid() ) return;
		Transform anchor = default;
		var ready = active && _rearmCameraAnchor.TryRead( _rearmRenderer, out anchor ) && anchor.Position.IsFinite;
		if ( ready ) _rearmVisual.WorldTransform = new Transform( view.Position, view.Rotation ).ToWorld( anchor.ToLocal( global::Transform.Zero ) );
		_rearmRenderer.RenderOptions.Overlay = ready;
	}

	void ClearRearmVisual()
	{
		if ( _rearmVisual.IsValid() ) _rearmVisual.Destroy();
		_rearmVisual = null;
		_rearmRenderer = null;
		// A failed lookup may retry on the next activation, not every frame.
		if ( _rearmModel is null || _rearmModel.IsError ) _rearmModelPath = null;
	}
}
