/// <summary>
/// Mount backend, compatibility and diagnostic overrides, read once at startup.
/// Player-facing texture quality is adapted by Titanfall2GraphicsSettings; native
/// rendering quality is controlled by the engine. See ENVIRONMENT_SWITCHES.md.
/// </summary>
static class Titanfall2StreamingSettings
{
	// Until the per-view retail AR proxy is supported, do not draw its overlay by default.
	public static readonly bool ShowWeakpointMarkers = GetBool( "SBOX_TITANFALL2_SHOW_WEAKPOINT_MARKERS", false );
	// Share compatible world materials by lightmap page; 0 selects the compatibility backend.
	public static readonly bool WorldMaterialTable = GetBool( "SBOX_TITANFALL2_WORLD_MATERIAL_TABLE", true );
	// Aggregate compatible static props (titanfall2_default/cutout) into native AggregateRenderers
	// backed by shared prop material and light-probe GPU buffers; 0 restores per-cluster instance batches.
	public static readonly bool PropAggregate = GetBool( "SBOX_TITANFALL2_PROP_AGGREGATE", true );
	// 0 uses the CPU renderer for compatibility.
	public static bool GpuStaticScene => false;
	public static readonly bool GpuMixedProps = GetBool( "SBOX_TITANFALL2_GPU_MIXED_PROPS", true );
	public static readonly int GpuStaticSceneBudgetMb = GetInt( "SBOX_TITANFALL2_GPU_STATIC_SCENE_MB", 512, 64, 2048 );
	public static readonly int GpuStaticSceneViewSlots = GetInt( "SBOX_TITANFALL2_GPU_VIEW_SLOTS", 8, 1, 64 );
	public static readonly bool ProcessedCache = GetBool( "SBOX_TITANFALL2_PROCESSED_CACHE", true );
	public static readonly int ProcessedCacheBudgetMb = GetInt( "SBOX_TITANFALL2_CACHE_MB", 2048, 128, 8192 );
	// Prefer load-time preparation to walking-time body/resource creation. Explicitly disable
	// on memory-constrained clients; dedicated servers and pure-world maps always stay resident.
	public static readonly bool ResidentWorld = GetBool( "SBOX_TITANFALL2_RESIDENT_WORLD", true );
	public static int TextureMaxDimension => Titanfall2GraphicsSettings.Current.TextureMaxDimension;
	public static readonly int TexturePrefetchMb = GetInt( "SBOX_TITANFALL2_TEXTURE_PREFETCH_MB", 128, 8, 1024 );
	public static readonly int MaterialPrefetchWindow = GetInt( "SBOX_TITANFALL2_MATERIAL_PREFETCH_WINDOW", 4, 1, 16 );

	// Upload the two largest lightmap channels as 16-bit textures: SkyB as BGR565 and RealTimeB as
	// BGRA4444. The shader keeps sampling float4, so only precision changes (retail RealTimeB is
	// 93.7-96.7% binary, SkyB loses ~1% of sky visibility) while the lightmap block drops from 17 to
	// 13 bytes per texel - the bandwidth the world shader reads every pixel. 0 restores RGBA8888.
	// With lightmap mips enabled, RealTimeB stays RGBA8888: native BGRA4444 lower mips are unreliable.
	public static readonly bool Lightmap16Bit = GetBool( "SBOX_TITANFALL2_LIGHTMAP_16BIT", true );

	// Frame-rate preset: one switch for the knobs that trade distant detail for submitted work.
	// 0 leaves every knob at its own default, 60 is those same defaults, 120 raises the values
	// that shrink the shadow/depth passes and the per-frame submission count. An explicit
	// SBOX_TITANFALL2_* variable always wins over the preset, so a single knob can be tuned back.
	public static readonly int FpsPreset = GetInt( "SBOX_TITANFALL2_FPS_PRESET", 0, 0, 240 );
	static bool AggressiveFps => FpsPreset >= 120;

	// Lightmap mips: dilate each chart outward and upload a short chain (L0..L2) for the three
	// channels the world shader samples with Tex2DS. Off by default because it softens chart edges
	// slightly at distance; the 120 fps preset turns it on for the bandwidth win. An explicit
	// SBOX_TITANFALL2_LIGHTMAP_MIPS overrides either default.
	public static readonly bool LightmapMips = GetBool( "SBOX_TITANFALL2_LIGHTMAP_MIPS", AggressiveFps );

	// Per-instance distance cull for static props; large landmarks are exempt. 0 disables it.
	public static readonly float PropRenderDistance = GetFloat( "SBOX_TITANFALL2_PROP_RENDER_DISTANCE", AggressiveFps ? 6144f : 8192f, 0f, 65536f );
	// Multiplies the fade distances authored on each static prop.
	public static readonly float PropAuthoredFadeScale = GetFloat( "SBOX_TITANFALL2_PROP_AUTHORED_FADE_SCALE", 1f, 0.25f, 16f );
	// Render-submission granularity: how many prop cells per axis share one instanced batch.
	// 4 is the proven default; 8 and 16 remain selectable via SBOX_TITANFALL2_BATCH_CLUSTER for A/B.
	public static readonly int PropBatchClusterCells = GetClusterCells( "SBOX_TITANFALL2_BATCH_CLUSTER", AggressiveFps ? 8 : 4 );
	// Camera distance past which a prop batch stops casting directional shadows.
	// 0 keeps every shadow batch casting by default; selectable via SBOX_TITANFALL2_PROP_SHADOW_DISTANCE for A/B.
	public static readonly float PropShadowDistance = GetFloat( "SBOX_TITANFALL2_PROP_SHADOW_DISTANCE", AggressiveFps ? 1024f : 0f, 0f, 65536f );
	// Multiplies the static prop LOD switch distances. Below 1 falls to a cheaper LOD sooner,
	// which cuts triangles in the forward and depth passes at the cost of distant silhouettes.
	// 1 keeps the authored 1536/4096/8192 so a default run matches the previous behaviour.
	public static readonly float PropLodDistanceScale = GetFloat( "SBOX_TITANFALL2_PROP_LOD_SCALE", AggressiveFps ? 0.5f : 1f, 0.1f, 4f );
	// Edge length of the spatial cells world geometry is grouped into. Larger cells submit fewer
	// fragments (fewer draws), at the cost of a coarser frustum cull; 2048 is the measured default.
	public static readonly int WorldRenderCellSize = GetInt( "SBOX_TITANFALL2_WORLD_CELL_SIZE", AggressiveFps ? 4096 : 2048, 512, 8192 );

	// World lightmaps, prop probes, local lights and atmospheric radiance share one linear
	// HDR path before the map's display transform. 0 uses s&box native lighting instead,
	// for diagnosing an unsupported map.
	public static readonly bool UseBspLightmaps = GetBool( "SBOX_TITANFALL2_BSP_LIGHTMAPS", true );
	// Imported world and large props cast directional shadows alongside their baked lighting by default.
	public static readonly bool MapShadows = GetBool( "SBOX_TITANFALL2_MAP_SHADOWS", true );
	// Draw a dedicated depth-only copy of the opaque world (one mesh per spatial cell, all sharing a
	// single runtime material) and stop the forward aggregates from casting. The world keeps
	// projecting through the configured cascades, but the shadow pass binds one shader and issues one draw per
	// cell instead of one per (cell, material) fragment with 300+ materials. Off restores the old
	// behaviour (aggregates cast with their own materials) for A/B; independent of SHADOW_CASCADES.
	public static readonly bool ShadowGeometry = GetBool( "SBOX_TITANFALL2_SHADOW_GEOMETRY", true );
	// Directional shadow cascade count. Every cascade renders all shadow casters into its own depth
	// target, so this multiplies the whole shadow pass: 1 halves it, 3 suits very long shadow
	// distances. Native player quality clamps this ceiling at draw time, so the default
	// allows all four cascades. Explicit overrides and the legacy FPS preset remain ceilings.
	public static readonly int ShadowCascadeCount = MapShadows
		? GetInt( "SBOX_TITANFALL2_SHADOW_CASCADES", AggressiveFps ? 1 : 4, 1, 4 )
		: 1;
	public static readonly bool PropShadows = GetBool( "SBOX_TITANFALL2_PROP_SHADOWS", true );
	// Screen-space contact shadows supplement directional shadows on small details. The 120 fps
	// preset turns them off: they are a full-screen depth pass on top of the cascades.
	public static readonly bool NativeContactShadows = GetBool( "SBOX_TITANFALL2_CONTACT_SHADOWS", !AggressiveFps );
	// Bias in stops on top of the map's retail exposure calibration.
	public static readonly float BakedExposureCompensation = GetFloat( "SBOX_TITANFALL2_EXPOSURE_EV", 0f, -5f, 5f );
	// Godray cards are additive and camera-distance faded. They are enabled by default;
	// turn them off when diagnosing bright streaks or flashing while moving. The 120 fps preset
	// drops them: they are translucent additive cards that cost fill rate for little geometry.
	public static readonly bool Godrays = GetBool( "SBOX_TITANFALL2_GODRAYS", !AggressiveFps );

	public static readonly bool MapAudio = GetBool( "SBOX_TITANFALL2_MAP_AUDIO", true );
	public static readonly float MapAudioVolume = GetFloat( "SBOX_TITANFALL2_AUDIO_VOLUME", 0.7f, 0.05f, 2f );

	// A host scene with navigation enabled otherwise generates every tile of the map from
	// every collider before loading finishes (about 1.5-2 minutes on angel_city and timeshift).
	// On demand skips that pass, uses only tagged world collision, and builds tiles near
	// players and NavMesh agents. Off restores the engine's whole-map pass.
	public static readonly bool NavMeshOnDemand = GetBool( "SBOX_TITANFALL2_NAVMESH_ON_DEMAND", true );
	public static readonly float NavMeshOnDemandRadius = GetFloat( "SBOX_TITANFALL2_NAVMESH_RADIUS", 2048f, 512f, 65536f );
	// Enables navigation for a full map scene whose host did not, generated after loading.
	public static readonly bool DeferredNavMesh = GetBool( "SBOX_TITANFALL2_DEFERRED_NAVMESH", false );
	public static readonly bool NavMeshStaticProps = GetBool( "SBOX_TITANFALL2_NAVMESH_STATIC_PROPS", false );

	// Batching is only measured on a 1/2/4/8/16 cell grid. A value in between snaps down to the
	// nearest measured grid instead of inventing a layout nobody has profiled.
	static int GetClusterCells( string name, int fallback )
	{
		var value = GetInt( name, fallback, 1, 16 );
		return value switch
		{
			>= 16 => 16,
			>= 8 => 8,
			>= 4 => 4,
			>= 2 => 2,
			_ => 1
		};
	}

	static int GetInt( string name, int fallback, int minimum, int maximum ) =>
		Titanfall2Options.GetOptionalInt( name, minimum, maximum ) ?? fallback;

	internal static int? GetOptionalInt( string name, int minimum, int maximum ) =>
		Titanfall2Options.GetOptionalInt( name, minimum, maximum );

	internal static int? ParseOptionalInt( string text, int minimum, int maximum ) =>
		Titanfall2Options.ParseOptionalInt( text, minimum, maximum );

	static float GetFloat( string name, float fallback, float minimum, float maximum )
	{
		return float.TryParse( Titanfall2Options.Get( name ), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out var value ) && float.IsFinite( value )
			? Math.Clamp( value, minimum, maximum )
			: fallback;
	}

	static bool GetBool( string name, bool fallback ) => Titanfall2Options.GetBool( name, fallback );
}
