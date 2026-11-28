using Sandbox.Rendering;

public sealed partial class Titanfall2PilotTactical
{
	internal const string PhaseLookupSource = "materials/correction/mp_ability_shifter.raw";
	CameraComponent _screenEffectCamera;
	GameObject _retailScreenEffect;
	GameObject _retailScreenEntryEffect;
	GameObject _retailScreenPersonalEffect;
	int _screenEffectMode;
	// GameObject tags are limited to 32 characters (camera TagSet does not validate).
	readonly string _screenEffectTag = "tf2-tac-view-" + Guid.NewGuid().ToString( "N" )[..16];
	readonly HashSet<CameraComponent> _screenExcludedCameras = [];
	CommandList _phaseLutCommands;
	Texture _phaseLut;
	Material _phaseLutMaterial;
	string _phaseLookupPath;
	int _phaseLookupGeneration;
	bool _phaseLookupLoading;
	bool _phaseLookupFailed;
	bool _phaseLutAttached;

	internal bool HasLocalScreenEffect => _screenEffectCamera.IsValid() && _retailScreenEffect.IsValid();
	internal bool HasLocalPhaseLookup => _phaseLutAttached && _screenEffectCamera.IsValid();

	void UpdateRetailScreenEffects()
	{
		var camera = LocalTacticalCamera();
		var mode = IsPhaseActive ? 2 : IsStimActive ? 1 : 0;
		if ( !camera.IsValid() || mode == 0 )
		{
			StopRetailScreenEffects();
			return;
		}

		if ( _screenEffectCamera != camera || _screenEffectMode != mode )
		{
			StopRetailScreenEffects();
			_screenEffectCamera = camera;
			_screenEffectMode = mode;
			// sh_stim.gnut uses P_heal and a speed-driven CP1. Neither Stim nor
			// Grapple has an authored color-correction table in the retail scripts.
			_retailScreenEffect = SpawnScreenParticle( mode == 1 ? "P_heal" : "P_phase_shift_screen" );
			if ( mode == 2 )
			{
				_retailScreenEntryEffect = SpawnScreenParticle( "P_phase_shift_screen_start", 2f );
				_retailScreenPersonalEffect = SpawnScreenParticle( "P_phase_shift_player" );
			}
		}

		UpdateScreenCameraExclusions();
		UpdateScreenParticleTransforms( camera.WorldTransform );
		if ( mode == 2 ) UpdatePhaseLookup();
	}

	CameraComponent LocalTacticalCamera()
	{
		if ( Application.IsHeadless || Application.IsDedicatedServer || IsProxy || !Active || !HasEquippedTactical
			|| Parkour is not { IsValid: true, Equipped: true } || _controller is not { IsValid: true, Active: true }
			|| _controller.IsProxy || _controller.ThirdPerson || !_controller.UseCameraControls
			|| _controller.Mode is not Titanfall2PilotMoveMode ) return null;
		var camera = Scene?.Camera;
		// Sandbox's spectator/free camera explicitly excludes firstperson. Do not
		// give that view the local pilot's filter, even while their ability is active.
		return camera is { IsValid: true, Active: true } && !camera.RenderExcludeTags.Has( "firstperson" ) ? camera : null;
	}

	protected override void OnPreRender()
	{
		// Camera modifiers run after OnUpdate. Resolve again so a camera/third-person
		// switch in that stage cannot leave one frame of stale first-person effects.
		UpdatePhaseCameraIsolation();
		UpdateRetailScreenEffects();
		if ( !_screenEffectCamera.IsValid() ) return;
		var view = _screenEffectCamera.View;
		UpdateScreenParticleTransforms( new Transform( view.Position, view.Rotation ) );
	}

	GameObject SpawnScreenParticle( string name, float lifetime = 0f )
	{
		var effect = Titanfall2PilotTacticalEffects.Spawn( Scene, MountIdent, name, _controller.EyeTransform.Position, true, lifetime );
		if ( !effect.IsValid() ) return null;
		// Custom PCF renderers copy these tags on every PreRender, just like native
		// renderers. Tags on all children also cover authored nested collections.
		foreach ( var child in effect.GetAllObjects( false ) ) child.Tags.Add( _screenEffectTag, "firstperson" );
		return effect;
	}

	void UpdateScreenParticleTransforms( Transform view )
	{
		if ( _retailScreenEffect.IsValid() )
		{
			_retailScreenEffect.WorldTransform = view;
			if ( _screenEffectMode == 1 )
			{
				var speed = _controller.Body.IsValid() ? _controller.Body.Velocity.Length : 0f;
				_retailScreenEffect.GetComponent<Titanfall2ParticleControlPoints>()?.SetControlPoint( 1,
					new Vector3( MathX.Lerp( 5f, 200f, Math.Clamp( speed / 360f, 0f, 1f ) ), 999f, 0f ) );
			}
		}
		if ( _retailScreenEntryEffect.IsValid() ) _retailScreenEntryEffect.WorldTransform = view;
		if ( _retailScreenPersonalEffect.IsValid() )
			_retailScreenPersonalEffect.WorldTransform = new Transform( WorldPosition + Vector3.Up * 48f, view.Rotation );
	}

	void UpdateScreenCameraExclusions()
	{
		foreach ( var camera in Scene.GetAllComponents<CameraComponent>() )
		{
			if ( camera == _screenEffectCamera || !camera.IsValid() ) continue;
			if ( camera.RenderExcludeTags.Has( _screenEffectTag ) ) continue;
			camera.RenderExcludeTags.Add( _screenEffectTag );
			_screenExcludedCameras.Add( camera );
		}
	}

	void UpdatePhaseLookup()
	{
		var path = $"mount://{MountIdent ?? "titanfall2"}/{PhaseLookupSource}.vtex";
		if ( path != _phaseLookupPath )
		{
			DetachPhaseLookup();
			_phaseLookupPath = path;
			_phaseLut = null; // Mounted textures are shared; never dispose one here.
			_phaseLookupGeneration++;
			_phaseLookupLoading = false;
			_phaseLookupFailed = false;
		}
		if ( _phaseLut is not { IsValid: true, IsError: false, Width: 32, Height: 32, Depth: 32 } )
		{
			if ( !_phaseLookupLoading && !_phaseLookupFailed ) LoadPhaseLookup( path, ++_phaseLookupGeneration );
			return;
		}
		if ( _phaseLutAttached ) return;
		_phaseLutMaterial ??= Material.FromShader( "shaders/titanfall2_color_correction.shader" );
		_phaseLutCommands = new CommandList( "TF2 Pilot Phase Lookup" );
		_phaseLutCommands.Attributes.GrabFrameTexture( "ColorBuffer" );
		_phaseLutCommands.ResourceBarrierTransition( _phaseLut, ResourceState.PixelShaderResource );
		_phaseLutCommands.Attributes.Set( "T2ColorLut", _phaseLut );
		_phaseLutCommands.Attributes.Set( "T2ColorLutWeight", 1f );
		_phaseLutCommands.Blit( _phaseLutMaterial );
		// The existing shader samples untouched 32-cubed display RGB. The native
		// ColorGrading LUT uses another layout; assigning this texture there is wrong.
		// Keep this camera-local pass after map correction without replacing host effects.
		_screenEffectCamera.AddCommandList( _phaseLutCommands, Stage.AfterPostProcess, 4000 );
		_phaseLutAttached = true;
	}

	async void LoadPhaseLookup( string path, int generation )
	{
		_phaseLookupLoading = true;
		try
		{
			var texture = await Texture.LoadAsync( path, false );
			if ( !IsValid || generation != _phaseLookupGeneration || path != _phaseLookupPath ) return;
			if ( texture is not { IsValid: true, IsError: false, Width: 32, Height: 32, Depth: 32 } )
			{
				_phaseLookupFailed = true;
				Titanfall2Log.Warning( $"Pilot Phase lookup is not a valid retail 32-cubed table: '{path}'." );
				return;
			}
			_phaseLut = texture;
		}
		catch ( Exception e )
		{
			if ( generation != _phaseLookupGeneration ) return;
			_phaseLookupFailed = true;
			Titanfall2Log.Warning( $"Cannot load Pilot Phase lookup '{path}': {e.Message}" );
		}
		finally
		{
			if ( generation == _phaseLookupGeneration ) _phaseLookupLoading = false;
		}
	}

	void DetachPhaseLookup()
	{
		if ( _phaseLutCommands is not null )
		{
			if ( _screenEffectCamera.IsValid() ) _screenEffectCamera.RemoveCommandList( _phaseLutCommands );
			_phaseLutCommands.Reset();
			_phaseLutCommands = null;
		}
		_phaseLutAttached = false;
	}

	void StopRetailScreenEffects()
	{
		DetachPhaseLookup();
		if ( _phaseLookupLoading ) { _phaseLookupGeneration++; _phaseLookupLoading = false; }
		DestroyScreenParticle( ref _retailScreenEffect );
		DestroyScreenParticle( ref _retailScreenEntryEffect );
		DestroyScreenParticle( ref _retailScreenPersonalEffect );
		foreach ( var camera in _screenExcludedCameras )
			if ( camera.IsValid() ) camera.RenderExcludeTags.Remove( _screenEffectTag );
		_screenExcludedCameras.Clear();
		_screenEffectCamera = null;
		_screenEffectMode = 0;
	}

	static void DestroyScreenParticle( ref GameObject effect )
	{
		if ( effect.IsValid() ) { effect.Enabled = false; effect.Destroy(); }
		effect = null;
	}
}
