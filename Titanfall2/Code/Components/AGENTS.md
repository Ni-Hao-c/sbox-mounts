# Code/Components/ — Runtime Scene Components

All non-gameplay s&box `Component`s attached to mounted maps, props, lighting, post-processing, navigation, audio, and particles.

## Layer Boundary
- **Depends on**: `Code/`, `Content/`, `Formats/`, `Collision/`, `Rendering/`, `Resource/`.
- **Must not depend on**: `Gameplay/`.

## Subfolders
- `Map/` — `Titanfall2Map` (embeddable map loader), `WorldReady` (`Titanfall2WorldReady` publishing `ttf2_map_ready` on `worldspawn`), `SceneResourceOwner` (lease lifetime), `PhysicsResource` (`Titanfall2PhysicsResource`), `DynamicMover`, `Titanfall2MapCamera`, `Titanfall2SpectatorCamera`.
- `StaticProps/` — `StaticPropStreamer*.cs` (partial files: `Batches`, `Census`, `Loading`, `Preparation`, `Runtime`, `Visibility`), `StaticPropCollisionRuntime.cs`, `StaticPropRenderResources.cs`, `StaticPropSceneData.cs`, `StaticPropLightProbe.cs`, `CompiledPropLayout.cs`, `StaticPropRuntimeTypes.cs`.
- `Particles/` — `ParticleStreamer.cs` (streaming/lifecycle), `ParticleFactory.cs` (PCF -> s&box particle controller construction), `ParticleResource.cs`, `ParticleControlPoints.cs`, `ParticleWarmup.cs`, `ParticleRampRenderer.cs`, `ParticleRefractionRenderer.cs`, `ParticleRopeRenderer.cs`.
- `Lighting/` — `EnvironmentController`, `CubemapController`, `HeightFog`, `LocalLightController`, `DynamicPropLighting`, `Skybox3D`.
- `PostProcessing/` — `Tonemapping.cs`, `Tonemapping.Bloom.cs`, `ColorCorrection.cs`, `ColorCorrectionDefinition.cs`.
- `Rendering/` — `WorldMaterialTableController.cs` (Component wrapper for `WorldMaterialBatch`), `WorldMaterialStreamer.cs`, `WaterRenderer.cs`, `MaterialAnimator.cs`.
- `Navigation/` — `DeferredNavMeshBuilder.cs` (on-demand NavMesh tile generation around players/agents/link endpoints; enables basic engine links and restores host NavMesh settings when the last map unloads); `NpcTraversalLink.cs` exposes authored typed roof connections with one-at-a-time occupancy. It waits for built-in and package `.navlink` resources before creating links. These compiled GameResources are registered by the package loader; both `ResourceLibrary.Get` and `LoadAsync<NavMeshLinkType>` return cached resources, so TestAppSystem fixtures must reproduce package registration explicitly. Missing types must never become untyped links. It must not depend on Gameplay types.
- `Audio/` — `MapAudio.cs` (`ambient_generic`, `trigger_soundscape`), `SoundEventPlayer.cs` (warning-only compatibility stub).

## Invariants & Pitfalls
- **Serialization stability**: NEVER rename public `Component` class names, serialized `[Property]` names, or move them into a different namespace — existing `.scene`/`.prefab` files and caches depend on exact names.
- **`Titanfall2WorldReady` contract**: Publish `ttf2_map_ready` ONLY when resident prop collision, shaped world physics bodies, and every collision-manifest contents group are present. Clear on load/enable and revoke if resources disappear.
- **Validation reflection**: `Validation/Program.cs` inspects `Titanfall2StaticPropStreamer._renderResources` via private reflection — keep that field name intact.
- **ParticleStreamer vs ParticleFactory**: Keep streaming/budget scheduling in `ParticleStreamer.cs` and PCF-to-component construction in `ParticleFactory.cs`.
- **PCF color and alpha**: Color Random / Color Fade author RGB independently of Alpha Random / alpha operators. Keep RGB tint alpha at 1; retail `P_heal` stores zero in the DMX color's fourth byte. Forwarding it as opacity makes valid particles invisible. Custom particle SceneObjects must copy current GameObject tags during PreRender so camera exclusions also apply to Refraction/Ramp/Rope renderers.
- **Resident collision retirement/repair**: Detach `CollisionDestroyed` before planned deferred destruction. Preserve notifications for unexpected loss. Repair only missing bodies from retained load-time resources; the aggregate-cache fast path does not populate every render `cell.Instances`, so it cannot reconstruct authoritative collision. Keep healthy bodies and readiness invalid until repair completes. Verify with `--static-prop-safety`.
