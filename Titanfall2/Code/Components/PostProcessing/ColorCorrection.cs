using Sandbox.Rendering;
using System.Text.Json;

/// <summary>Retail 32-cubed RGB correction, sampled after the HDR display transform.</summary>
[Library, Hide]
public sealed class Titanfall2ColorCorrection : BasePostProcess<Titanfall2ColorCorrection>
{
	/// <summary>The mounted volume-texture resource that holds the 32-cubed table.</summary>
	[Property, Hide] public string GradingTexturePath { get; set; }
	[Property, Hide] public string SourcePath { get; set; }
	[Property, Range( 0, 1 )] public float Weight { get; set; } = 1;
	[Property, Hide] public string MountIdent { get; set; }
	[Property, Hide] public string EncodedCorrections { get; set; }
	Entry[] _entries;
	string _loadedConfiguration, _loadedMount, _loadedLegacyPath;
	readonly Dictionary<CameraComponent, ViewState> _views = [];
	readonly List<CameraComponent> _staleViews = [];
	static Material _shader;
	// Mount assemblies are initialized before their asset search path is attached.
	// Resolve shaders when a scene first needs them, after mount discovery finishes.
	static ComputeShader _blendShader;
	static ComputeShader BlendShader => _blendShader ??= new( "shaders/titanfall2_color_lut_blend_cs.shader" );
	static readonly ProfilingSampler BlendProfile = new( "T2 GPU LUT composition" );

	sealed class Entry( string path, Titanfall2ColorCorrectionDefinition definition )
	{
		internal readonly string Path = path;
		internal readonly Titanfall2ColorCorrectionDefinition Definition = definition;
		internal Texture Texture;
		internal bool Failed;
		static bool IsLookup( Texture texture ) => texture is { IsValid: true, IsError: false, Width: 32, Height: 32, Depth: 32 };

		internal void Restore()
		{
			if ( IsLookup( Texture ) || Failed ) return;
			Texture = Sandbox.Texture.Load( Path, false );
			if ( IsLookup( Texture ) ) return;
			Texture = null;
			Failed = true;
			Titanfall2Log.Warning( $"Unable to load the Titanfall 2 color-correction table '{Path}'." );
		}
	}

	sealed class ViewState( int count ) : IDisposable
	{
		internal readonly CommandList Commands = new( "Titanfall2ColorCorrection" );
		internal readonly float[] Weights = new float[count];
		internal readonly Titanfall2ColorCorrectionDefinition.FadeState[] Fades = new Titanfall2ColorCorrectionDefinition.FadeState[count];
		internal Texture[] Composed;
		internal Texture Result;

		internal void EnsureTextures()
		{
			if ( Composed is not null ) return;
			var first = Create();
			try { Composed = [first, Create()]; }
			catch { first.Dispose(); throw; }
		}
		static Texture Create() => Texture.CreateVolume( 32, 32, 32 ).WithFormat( ImageFormat.RGBA16161616F ).WithUAVBinding().Finish();
		public void Dispose()
		{
			Commands.Reset();
			if ( Composed is not null ) foreach ( var texture in Composed ) texture.Dispose();
		}
	}

	internal void Configure( string gradingTexturePath, string sourcePath, float weight )
	{
		EncodedCorrections = null;
		GradingTexturePath = gradingTexturePath;
		SourcePath = sourcePath;
		Weight = float.IsFinite( weight ) ? Math.Clamp( weight, 0, 1 ) : 0;
		RestoreEntries();
	}

	internal void ConfigureStack( string mountIdent, IReadOnlyList<Dictionary<string, string>> entities )
	{
		MountIdent = mountIdent;
		EncodedCorrections = JsonSerializer.Serialize( entities );
		Weight = 1;
		RestoreEntries();
	}

	protected override Task OnLoad( LoadingContext context )
	{
		RestoreEntries();
		foreach ( var entry in _entries ) entry.Restore();
		return Task.CompletedTask;
	}
	protected override void OnEnabled()
	{
		RestoreEntries();
		foreach ( var entry in _entries ) entry.Failed = false;
	}

	void RestoreEntries()
	{
		if ( _entries is not null && _loadedConfiguration == EncodedCorrections && _loadedMount == MountIdent
			&& _loadedLegacyPath == GradingTexturePath ) return;
		ReleaseViews();
		_loadedConfiguration = EncodedCorrections;
		_loadedMount = MountIdent;
		_loadedLegacyPath = GradingTexturePath;
		var entries = new List<Entry>();
		if ( !string.IsNullOrEmpty( EncodedCorrections ) )
		{
			foreach ( var entity in JsonSerializer.Deserialize<Dictionary<string, string>[]>( EncodedCorrections ) ?? [] )
			{
				var definition = new Titanfall2ColorCorrectionDefinition( MountIdent, entity );
				if ( definition.TexturePath is null ) continue;
				if ( !definition.HasUsableShape )
				{
					Titanfall2Log.Warning( $"Color-correction definition '{definition.Name}' has no usable spatial data; omitted." );
					continue;
				}
				entries.Add( new( definition.TexturePath, definition ) );
			}
		}
		else if ( !string.IsNullOrEmpty( GradingTexturePath ) ) entries.Add( new( GradingTexturePath, null ) );
		_entries = entries.ToArray();
	}

	internal bool TryPrepareInline( CameraComponent camera, CommandList targetCommands, out Texture lut, out float lutWeight )
	{
		lut = null;
		lutWeight = 0;
		if ( !Active || !camera.IsValid() ) return false;
		RestoreEntries();
		CleanupStaleViews();
		if ( _entries.Length == 0 ) return false;
		if ( !_views.TryGetValue( camera, out var view ) ) _views.Add( camera, view = new( _entries.Length ) );
		if ( !EvaluateLut( camera, view, targetCommands, Weight, out lut, out lutWeight ) ) return false;
		return true;
	}

	public override void Render()
	{
		// When Titanfall2Tonemapping is active in this scene, it samples the composed LUT inline
		// inside its single full-screen display blit, avoiding a second 2560x1600 screen pass.
		if ( Scene.GetAllComponents<Titanfall2Tonemapping>().Any( static display => display.Active ) ) return;

		RestoreEntries();
		CleanupStaleViews();
		if ( _entries.Length == 0 ) return;
		if ( !_views.TryGetValue( Camera, out var view ) ) _views.Add( Camera, view = new( _entries.Length ) );
		var commands = view.Commands;
		commands.Reset();
		if ( !EvaluateLut( Camera, view, commands, GetWeighted( value => value.Weight ), out var lut, out var lutWeight ) ) return;

		commands.Attributes.GrabFrameTexture( "ColorBuffer" );
		_shader ??= Material.FromShader( "shaders/titanfall2_color_correction.shader" );
		commands.ResourceBarrierTransition( lut, ResourceState.PixelShaderResource );
		commands.Attributes.Set( "T2ColorLut", lut );
		commands.Attributes.Set( "T2ColorLutWeight", lutWeight );
		commands.Blit( _shader );
		InsertCommandList( commands, Stage.AfterPostProcess, 3990, "Titanfall2ColorCorrection" );
	}

	void CleanupStaleViews()
	{
		_staleViews.Clear();
		foreach ( var camera in _views.Keys ) if ( !camera.IsValid() ) _staleViews.Add( camera );
		foreach ( var camera in _staleViews ) { _views[camera].Dispose(); _views.Remove( camera ); }
	}

	bool EvaluateLut( CameraComponent camera, ViewState view, CommandList commands, float weight, out Texture lut, out float lutWeight )
	{
		lut = null;
		lutWeight = 0;
		if ( !float.IsFinite( weight ) || weight <= 0 ) return false;
		var total = 0d;
		var active = 0;
		Texture fallback = null;
		for ( var i = 0; i < _entries.Length; ++i )
		{
			var entry = _entries[i];
			var target = entry.Definition?.SpatialWeight( camera.WorldPosition ) ?? 1;
			if ( entry.Definition is { IsBrush: true } definition )
				target = view.Fades[i].Evaluate( target, Time.Now, definition.FadeIn, definition.FadeOut );
			if ( target > 0 ) entry.Restore();
			view.Weights[i] = entry.Texture is { IsValid: true } ? target : 0;
			if ( view.Weights[i] > 0 ) { fallback ??= entry.Texture; active++; }
			total += view.Weights[i];
		}
		if ( fallback is null || total <= 0 || !double.IsFinite( total ) ) return false;
		for ( var i = 0; i < _entries.Length; ++i )
			view.Weights[i] = (float)(view.Weights[i] / total);
		// The common single-table map needs no compute or temporary volume textures.
		// Multiple tables are composed in this recording: an earlier camera build may
		// have been culled before execution, so CPU bookkeeping cannot prove GPU validity.
		if ( active == 1 ) view.Result = fallback;
		else Compose( view, fallback, commands );
		lut = view.Result;
		lutWeight = Math.Clamp( weight, 0, 1 ) * (float)Math.Clamp( total, 0, 1 );
		return lut is { IsValid: true } && lutWeight > 0;
	}

	void Compose( ViewState view, Texture fallback, CommandList commands )
	{
		view.EnsureTextures();
		using var profile = commands.ProfileScope( BlendProfile );
		var first = true;
		var outputIndex = 0;
		for ( var offset = 0; offset < _entries.Length; offset += 4 )
		{
			float W( int n ) => offset + n < _entries.Length ? view.Weights[offset + n] : 0;
			var weights = new Vector4( W( 0 ), W( 1 ), W( 2 ), W( 3 ) );
			if ( weights.x + weights.y + weights.z + weights.w <= 0 ) continue;
			Texture T( int n ) => offset + n < _entries.Length && _entries[offset + n].Texture is { IsValid: true } texture ? texture : fallback;
			var output = view.Composed[outputIndex];
			commands.ResourceBarrierTransition( output, ResourceState.UnorderedAccess );
			for ( var n = 0; n < 4; n++ ) commands.ResourceBarrierTransition( T( n ), ResourceState.NonPixelShaderResource );
			commands.Attributes.Set( "T2LutBlend0", T( 0 ) ); commands.Attributes.Set( "T2LutBlend1", T( 1 ) );
			commands.Attributes.Set( "T2LutBlend2", T( 2 ) ); commands.Attributes.Set( "T2LutBlend3", T( 3 ) );
			commands.Attributes.Set( "T2LutBlendPrevious", first ? fallback : view.Result );
			commands.Attributes.Set( "T2LutBlendWeights", weights );
			commands.Attributes.Set( "T2LutBlendFirst", first ? 1 : 0 );
			commands.Attributes.Set( "T2LutBlendOutput", output );
			commands.DispatchCompute( BlendShader, 32, 32, 32 );
			commands.ResourceBarrierTransition( output, ResourceState.NonPixelShaderResource );
			view.Result = output;
			first = false;
			outputIndex = 1 - outputIndex;
		}
	}

	void ReleaseViews()
	{
		foreach ( var view in _views.Values ) view.Dispose();
		_views.Clear();
		_staleViews.Clear();
	}
	protected override void OnDisabled() => ReleaseViews();
	// Source tables are mounted shared resources. Only the composed camera LUTs are ours.
	protected override void OnDestroy() { ReleaseViews(); _entries = null; }
}
