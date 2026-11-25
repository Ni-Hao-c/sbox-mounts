using Sandbox.Rendering;
using System.Runtime.InteropServices;

/// <summary>Retail display curve and bounded logarithmic auto exposure.</summary>
[Library, Hide]
public sealed partial class Titanfall2Tonemapping : BasePostProcess<Titanfall2Tonemapping>
{
	[Property] public float ExposureCompensation { get; set; }
	[Property] public float MinimumExposure { get; set; } = 0.5f;
	[Property] public float MaximumExposure { get; set; } = 2f;
	/// <summary>Retail mat_autoexposure_speed: amount per rendered frame.</summary>
	[Property] public float AdaptationRate { get; set; } = 0.1f;
	/// <summary>Positive values bypass adaptation, matching c_forceExposure.</summary>
	[Property] public float ForcedExposure { get; set; }

	static Material _displayShader;
	static Material Shader => _displayShader ??= Material.FromShader( "shaders/titanfall2_tonemapping.shader" );
	static ComputeShader _exposureShader;
	static ComputeShader ExposureShader => _exposureShader ??= new( "shaders/titanfall2_exposure_cs.shader" );
	static readonly string[] MeterNames = ["T2Meter0", "T2Meter1", "T2Meter2", "T2Meter3", "T2Meter4", "T2Meter5", "T2Meter6"];
	static readonly ProfilingSampler ExposureProfile = new( "T2 GPU exposure meter and adaptation" );
	static readonly ProfilingSampler DisplayProfile = new( "T2 GPU display transform" );
	readonly Dictionary<CameraComponent, CameraState> _cameras = [];
	readonly List<CameraComponent> _staleCameras = [];
	internal static Titanfall2Tonemapping CreateMapDisplay( GameObject owner, float exposureCompensation )
	{
		// Titanfall2Tonemapping supplies the full retail exposure, multi-stage bloom and display
		// curve directly, and suppresses redundant host Bloom/DefaultTonemapping per camera without
		// adding an extra linear no-op Tonemapping pass.
		var display = owner.AddComponent<Titanfall2Tonemapping>();
		display.ExposureCompensation = exposureCompensation;
		return display;
	}

	sealed class CameraState : IDisposable
	{
		internal readonly CommandList Commands = new( "Titanfall2Display" );
		internal readonly Texture Exposure = Texture.Create( 1, 1, ImageFormat.RGBA32323232F ).WithUAVBinding()
			.WithData( Titanfall2Binary.WriteArray( new float[] { 1, 0, 1, -1 } ) ).Finish();
		// The camera's auto exposure and host post-process components belong to the host scene,
		// not to this effect; remember them so disabling or destroying restores the host cleanly.
		internal bool AutoExposureCaptured;
		internal bool AutoExposureWasEnabled;
		internal readonly List<Component> SuppressedHostEffects = new();
		public void Dispose() => Exposure.Dispose();
	}

	public override void Render()
	{
		// Exposure and bloom are applied once by our display pass. Suppress redundant host
		// auto-exposure, Bloom and DefaultTonemapping/Tonemapping components on the same camera
		// so they do not burn ~1.2 ms GPU running twice, and restore them when removed.
		if ( !_cameras.TryGetValue( Camera, out var state ) )
		{
			state = new CameraState();
			_cameras.Add( Camera, state );
		}
		if ( !state.AutoExposureCaptured )
		{
			state.AutoExposureWasEnabled = Camera.AutoExposure.Enabled;
			state.AutoExposureCaptured = true;
			SuppressRedundantHostEffects( Camera, state );
		}
		Camera.AutoExposure.Enabled = false;
		_staleCameras.Clear();
		foreach ( var camera in _cameras.Keys )
			if ( !camera.IsValid() ) _staleCameras.Add( camera );
		foreach ( var stale in _staleCameras )
		{
			_cameras[stale].Dispose();
			_cameras.Remove( stale );
		}
		var commands = state.Commands;
		commands.Reset();
		var color = commands.Attributes.GrabFrameTexture( "ColorBuffer" );
		var input = color.ColorTexture;
		using ( commands.ProfileScope( ExposureProfile ) )
		{
			for ( var level = 0; level < 7; ++level )
			{
				var output = commands.GetRenderTarget( MeterNames[level], 1 << (2 * (level + 1)), ImageFormat.RGBA32323232F, ImageFormat.None );
				commands.Attributes.SetCombo( "D_STAGE", level == 0 ? 0 : 1 );
				commands.Attributes.Set( "T2MeterInput", input );
				commands.Attributes.Set( "T2MeterOutput", output.ColorTexture );
				commands.DispatchCompute( ExposureShader, output.Size );
				commands.ResourceBarrierTransition( output, ResourceState.NonPixelShaderResource );
				input = output.ColorTexture;
			}
			commands.Attributes.SetCombo( "D_STAGE", 2 );
			commands.Attributes.Set( "T2MeterInput", input );
			commands.Attributes.Set( "T2ExposureLimits", new Vector4(
				Finite( GetWeighted( x => x.MinimumExposure, 0.5f ), 0.5f, 0.000001f, 64 ),
				Finite( GetWeighted( x => x.MaximumExposure, 2f ), 2f, 0.000001f, 64 ),
				Finite( GetWeighted( x => x.ExposureCompensation ), 0, -16, 16 ),
				Finite( GetWeighted( x => x.AdaptationRate, 0.1f ), 0.1f, 0, 64 ) ) );
			commands.Attributes.Set( "T2ExposureFrame", new Vector2( RealTime.Now, Finite( GetWeighted( x => x.ForcedExposure ), 0, 0, 64 ) ) );
			commands.Attributes.Set( "T2ExposureHistory", state.Exposure );
			commands.ResourceBarrierTransition( state.Exposure, ResourceState.UnorderedAccess );
			commands.DispatchCompute( ExposureShader, 1, 1, 1 );
			commands.ResourceBarrierTransition( state.Exposure, ResourceState.PixelShaderResource );
		}
		commands.Attributes.Set( "T2ExposureTexture", state.Exposure );
		// Meter the original scene, before bloom. Feeding the glow back into the
		// meter would incorrectly darken the image when bloom is enabled.
		BuildBloom( commands, color.ColorTexture );
		BindInlineColorCorrection( commands );
		UpdateFastDepthNormals( Camera );
		using ( commands.ProfileScope( DisplayProfile ) ) commands.Blit( Shader );
		InsertCommandList( commands, Stage.Tonemapping, 0, "Titanfall2Display" );
	}

	void BindInlineColorCorrection( CommandList commands )
	{
		foreach ( var correction in Scene.GetAllComponents<Titanfall2ColorCorrection>() )
		{
			if ( !correction.TryPrepareInline( Camera, commands, out var lut, out var lutWeight ) ) continue;
			commands.ResourceBarrierTransition( lut, ResourceState.PixelShaderResource );
			commands.Attributes.SetCombo( "D_T2_COLOR_LUT", 1 );
			commands.Attributes.Set( "T2ColorLut", lut );
			commands.Attributes.Set( "T2ColorLutWeight", lutWeight );
			return;
		}
		commands.Attributes.SetCombo( "D_T2_COLOR_LUT", 0 );
		commands.Attributes.Set( "T2ColorLutWeight", 0f );
	}

	void UpdateFastDepthNormals( CameraComponent camera )
	{
		if ( !Scene.IsValid() || !camera.IsValid() || !camera.GameObject.IsValid() ) return;
		var needsNormals = false;
		foreach ( var component in camera.GameObject.Components.GetAll() )
		{
			if ( !component.IsValid() || !component.Enabled ) continue;
			if ( component is AmbientOcclusion )
			{
				needsNormals = true;
				break;
			}
		}
		Scene.RenderAttributes.Set( "T2FastDepthNormals", needsNormals ? 0 : 1 );
	}

	static float Finite( float value, float fallback, float minimum, float maximum ) =>
		float.IsFinite( value ) ? Math.Clamp( value, minimum, maximum ) : fallback;

	static void SuppressRedundantHostEffects( CameraComponent camera, CameraState state )
	{
		if ( !camera.IsValid() || !camera.GameObject.IsValid() ) return;
		foreach ( var component in camera.GameObject.Components.GetAll() )
		{
			if ( !component.IsValid() || !component.Enabled ) continue;
			var isRedundant = component is Bloom or Tonemapping or Vignette
				|| string.Equals( component.GetType().Name, "DefaultTonemapping", StringComparison.Ordinal );
			if ( !isRedundant ) continue;
			component.Enabled = false;
			state.SuppressedHostEffects.Add( component );
		}
	}

	/// <summary>Hands every camera we touched back to its previous auto-exposure and post-process state.</summary>
	void RestoreAutoExposure()
	{
		if ( Scene.IsValid() ) Scene.RenderAttributes.Set( "T2FastDepthNormals", 0 );
		foreach ( var (camera, state) in _cameras )
		{
			if ( !state.AutoExposureCaptured ) continue;
			state.AutoExposureCaptured = false;
			if ( camera.IsValid() ) camera.AutoExposure.Enabled = state.AutoExposureWasEnabled;
			foreach ( var suppressed in state.SuppressedHostEffects )
				if ( suppressed.IsValid() ) suppressed.Enabled = true;
			state.SuppressedHostEffects.Clear();
		}
	}

	protected override void OnDisabled() => RestoreAutoExposure();

	protected override void OnDestroy()
	{
		RestoreAutoExposure();
		foreach ( var state in _cameras.Values ) state.Dispose();
		_cameras.Clear();
	}
}
