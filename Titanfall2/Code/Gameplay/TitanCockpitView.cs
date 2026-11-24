using Sandbox.Npcs;
using Chassis = Titanfall2TitanKit.Chassis;

/// <summary>The retail cockpit POV, rendered only for the local seated pilot.</summary>
[Title( "Titanfall 2 Cockpit View" ), Group( "Titanfall 2" )]
public sealed class Titanfall2TitanCockpitView : Component, ICameraModifier
{
	internal const string ClosedIdle = "atpov_cockpit_hatch_close_idle";
	internal const float CockpitFieldOfView = 70;
	const string RetailUiSurface = "models/weapons/arms/titan_atlas_int_screen";
	static Material _uiSurfaceMaterial;
	[Property] public Titanfall2TitanSeat Seat { get; set; }
	public bool HasRetailIdle { get; private set; }
	public int UiSurfaceOverrideCount { get; private set; }
	public bool IsShowing => _renderer.IsValid() && _renderer.RenderOptions.Overlay;
	GameObject _visual;
	SkinnedModelRenderer _renderer;
	string _requestedPath;
	TimeUntil _resourceRetry;
	// Engine tags are limited to 32 characters.
	readonly string _bodyTag = "tf2-cockpit-" + Guid.NewGuid().ToString( "N" )[..20];
	CameraComponent _hiddenCamera;
	GameObject _hiddenActor;

	// The seated pilot's view replaces the suspended hand weapon's camera effects.
	int ICameraModifier.CameraOrder => 250;

	internal static string SourceModel( Chassis chassis ) => chassis switch
	{
		Chassis.Ronin or Chassis.Northstar => "models/weapons/arms/pov_titan_light_cockpit.mdl",
		Chassis.Legion or Chassis.Scorch => "models/weapons/arms/pov_titan_heavy_cockpit.mdl",
		Chassis.Monarch or Chassis.Ion or Chassis.Tone => "models/weapons/arms/pov_titan_medium_cockpit.mdl",
		_ => null
	};

	PlayerController GetPilot()
	{
		if ( Application.IsDedicatedServer || Application.IsHeadless || !Seat.IsValid() || !Seat.Active
			|| Seat.Actor is not { Active: true, IsTitan: true, Dead: false } || Seat.IsEmbarking ) return null;
		var pilot = Seat.GetOccupant();
		return pilot is { Active: true, IsProxy: false } && pilot.Mode is Sandbox.Movement.SitMoveMode
			&& pilot.GetComponent<BaseNpc>() is not { Health: <= 0 } ? pilot : null;
	}

	protected override void OnUpdate()
	{
		if ( !GetPilot().IsValid() ) { Clear(); return; }
		var actor = Seat.Actor;
		var source = SourceModel( actor.GetComponent<Titanfall2TitanKit>()?.Kit ?? Chassis.None );
		if ( source is null || string.IsNullOrWhiteSpace( actor.MountIdent ) ) { Clear(); return; }
		// The full resource imports the authored two-frame closed-hatch pose. The
		// .visual resource deliberately has only bindpose, which is a different pose.
		var path = $"mount://{actor.MountIdent}/{source}.vmdl";
		if ( path != _requestedPath ) { Clear(); _requestedPath = path; }
		if ( _renderer.IsValid() || _resourceRetry > 0 ) return;
		_resourceRetry = 5;
		CreateVisual( path );
	}

	void CreateVisual( string path )
	{
		var model = Titanfall2GameplayVisuals.GetModel( path );
		if ( model is null || model.IsError || model.Attachments.Get( "CAMERA" )?.Bone is null ) return;
		_visual = new GameObject( Scene, false, "TF2 cockpit POV" );
		_visual.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
		_visual.Tags.Add( "firstperson", "viewmodel" );
		_renderer = _visual.AddComponent<SkinnedModelRenderer>();
		_renderer.Model = model;
		_renderer.UseAnimGraph = false;
		_renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		_renderer.RenderOptions.Game = false;
		_renderer.RenderOptions.Overlay = false;
		OverrideRetailUiSurface();
		HasRetailIdle = model.AnimationNames.Contains( ClosedIdle, StringComparer.OrdinalIgnoreCase );
		_renderer.Sequence.Name = HasRetailIdle ? ClosedIdle : "bindpose";
		_renderer.Sequence.Looping = true;
		_renderer.Sequence.Blending = false;
		_visual.Enabled = true;
	}

	void OverrideRetailUiSurface()
	{
		// Retail common.rpak uses ubercockpit3samp2222_fix on this curved UI
		// carrier. The generic opaque material hides the outside view. Its UI is
		// provided by our native cockpit HUD; leave the physical frame unchanged.
		for ( var slot = 0; slot < _renderer.Materials.Count; slot++ )
		{
			var name = _renderer.Materials.GetOriginal( slot )?.Name?.Replace( '\\', '/' );
			if ( name is null ) continue;
			while ( name.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase )
				|| name.EndsWith( ".vmt", StringComparison.OrdinalIgnoreCase ) )
				name = name[..name.LastIndexOf( '.' )];
			if ( !name.Equals( RetailUiSurface, StringComparison.OrdinalIgnoreCase )
				&& !name.EndsWith( "/" + RetailUiSurface, StringComparison.OrdinalIgnoreCase ) ) continue;
			_uiSurfaceMaterial ??= MaterialLoader.CreateRuntimeMaterial( "titanfall2/cockpit/native_ui_surface",
				new Titanfall2MaterialMetadata( Titanfall2MaterialMode.AlphaBlend, IsUnlit: true ) );
			// titanfall2_alpha discards opacity < 0.0001 and never writes depth.
			_uiSurfaceMaterial.Set( "g_flT2MaterialOpacity", 0f );
			_renderer.Materials.SetOverride( slot, _uiSurfaceMaterial );
			UiSurfaceOverrideCount++;
		}
	}

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		var pilot = GetPilot();
		if ( camera != Scene.Camera || !pilot.IsValid() || !TryGetLocalCamera( out _ ) ) return;
		var eye = pilot.EyeTransform;
		view.Position = eye.Position;
		view.Rotation = eye.Rotation;
		view.FieldOfView = CockpitFieldOfView;
	}

	void ICameraModifier.PostCameraSetup( CameraComponent camera, in CameraView view )
	{
		if ( camera != Scene.Camera || !_renderer.IsValid() ) return;
		if ( !GetPilot().IsValid() || !TryGetLocalCamera( out var localCamera ) )
		{
			_renderer.RenderOptions.Overlay = false;
			RestoreExterior();
			return;
		}
		_visual.WorldTransform = view.Transform.ToWorld( localCamera.ToLocal( global::Transform.Zero ) );
		_renderer.RenderOptions.Overlay = true;
		HideExterior( camera );
	}

	void HideExterior( CameraComponent camera )
	{
		var actor = Seat.Actor.GameObject;
		if ( _hiddenCamera == camera && _hiddenActor == actor ) return;
		RestoreExterior();
		_hiddenActor = actor;
		_hiddenCamera = camera;
		// Exclude only from this pilot's camera, retaining world rendering/shadows
		// for other cameras. The cockpit visual is a separate scene root.
		actor.Tags.Add( _bodyTag );
		camera.RenderExcludeTags.Add( _bodyTag );
	}

	void RestoreExterior()
	{
		if ( _hiddenCamera.IsValid() ) _hiddenCamera.RenderExcludeTags.Remove( _bodyTag );
		if ( _hiddenActor.IsValid() ) _hiddenActor.Tags.Remove( _bodyTag );
		_hiddenCamera = null;
		_hiddenActor = null;
	}

	bool TryGetLocalCamera( out Transform camera )
	{
		camera = default;
		if ( !_visual.IsValid() || !_renderer.IsValid() || !_renderer.SceneModel.IsValid() ) return false;
		var attachment = _renderer.Model.Attachments.Get( "CAMERA" );
		if ( attachment?.Bone is not { } bone
			|| !_renderer.TryGetBoneTransform( bone, out var pose ) ) return false;
		camera = _visual.WorldTransform.ToLocal( pose.ToWorld( attachment.LocalTransform ) );
		return camera.Position.IsFinite;
	}

	void Clear()
	{
		RestoreExterior();
		if ( _visual.IsValid() ) _visual.Destroy();
		_visual = null;
		_renderer = null;
		_requestedPath = null;
		HasRetailIdle = false;
		UiSurfaceOverrideCount = 0;
		_resourceRetry = 0;
	}

	protected override void OnDisabled() => Clear();
	protected override void OnDestroy() => Clear();
}
