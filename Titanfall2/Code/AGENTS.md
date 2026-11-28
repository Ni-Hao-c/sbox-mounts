# Code/ — Mount Core, Settings & Workers

Owns `Titanfall2Mount` (`BaseGameMount`), mount lifecycle (`Initialize`, `Mount`, `Shutdown`), runtime options/settings, logging, and the shared background worker pool.

## Files
- `GameMount.cs` — `Titanfall2Mount` entry point, registration tables, Steam AppId `1237970` discovery, `Mount()` / `Shutdown()`.
- `Titanfall2Options.cs` — Unified config parsing (`Get`, `GetBool`, `GetOptionalInt`, `ParseOptionalInt`) with `volatile` + `lock` initialization.
- `Titanfall2StreamingSettings.cs` — Data-filesystem streaming config (`RESIDENT_WORLD`, `BSP_LIGHTMAPS`, `DEFERRED_NAVMESH`, `NAVMESH_STATIC_PROPS`, etc.).
- `Titanfall2GraphicsSettings.cs` — Texture quality policy and engine graphics-setting callbacks.
- `Titanfall2Log.cs` — Mount logging helpers (guarded to avoid static init cycles with `Titanfall2Options`).
- `Titanfall2Workers.cs` — Shared background worker queue (`TaskCreationOptions.RunContinuationsAsynchronously`).
- `Assembly.cs` — Assembly attributes and build metadata (`Titanfall2BuildIdentity`).

## Invariants & Pitfalls
- **Thread safety**: Worker-read mount tables (`_texturePaths`, `_shaderSetNames`, `_shaderSetSources`, `_shaderSources`, `_materialSources`, `_legacyMaterialSources`, `_legacyTextureSources`, `_modelSources`) MUST remain `ConcurrentDictionary`. Sound profiling counters use `Interlocked`.
- **No static init cycles**: `Titanfall2Options`, `Titanfall2StreamingSettings`, `Titanfall2GraphicsSettings`, and `Titanfall2Log` must keep lazy/locked init without circular static constructors.
- **No `Volatile.Read/Write`**: Rejected by `MenuBuild.exe`; use `volatile` fields + `lock`.
- **Validation reflection contract**: Do NOT rename private fields on `Titanfall2Mount` (`_contentSnapshot`, `_milesSoundAssets`, `_soundFileCache`, `_nextParticleFile`, etc.) without updating `Validation/`.
- **`Shutdown()` cleanup**: Must clear registration tables, release map leases, and call `_gameplayCache.Reset()`.

## Sub-Layers
- [Content/AGENTS.md](Content/AGENTS.md) · [Formats/AGENTS.md](Formats/AGENTS.md) · [Collision/AGENTS.md](Collision/AGENTS.md) · [Rendering/AGENTS.md](Rendering/AGENTS.md) · [Resource/AGENTS.md](Resource/AGENTS.md) · [Components/AGENTS.md](Components/AGENTS.md) · [Gameplay/AGENTS.md](Gameplay/AGENTS.md)
