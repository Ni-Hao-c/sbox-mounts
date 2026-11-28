# Code/Resource/Map/ — rBSP Scene Assembly & Cache

Imports Titanfall 2 rBSP maps into serializable s&box `SceneFile`s (`maps/<map>.bsp.scene` and `maps/<map>.bsp.world.scene`), derived binary caches, and map preview thumbnails.

## Layer Boundary
- **Depends on**: `Code/`, `Content/`, `Formats/Bsp/`, `Collision/`, `Rendering/`, `Resource/`, and `Components/` (attaches runtime map/prop/lighting/audio/nav/particle components onto scene objects).
- **Must not depend on**: `Gameplay/`.

## Subfolders & Key Files
- Root:
  - `Map.cs` — `MapLoader` entry point (`BuildScene`, full vs pure-world scene modes).
  - `MapImport.cs` — Scene JSON import / cloning helpers (`Titanfall2Map` embeds maps through this).
  - `Map.Cache.cs` — Binary derived cache serialization (versioned with `Titanfall2BuildIdentity` + snapshot fingerprint).
  - `Map.Thumbnail.cs` — Retail map thumbnail extraction without loading full rBSP geometry.
  - `MapRuntimeTypes.cs` — Shared runtime descriptors passed from map loader to scene components.
- `Geometry/`:
  - `Map.World.cs` — World render mesh construction (split cleanly from lightmap atlas building).
  - `Titanfall2WorldAggregator.cs` — Aggregates world geometry via `MountedAggregateBuilder`.
  - `Map.Collision.cs` — World CM brushes/tricolls into `PhysicsGroupDescription` / `PhysicsCollider` grouped by retail contents flags.
  - `BrushModel.cs` — Inline brush models (`maps/<map>.bsp/brush/<index>.vmdl`).
  - `Map.PropFade.cs`, `MapWorldTypes.cs` — Prop fade settings and world vertex/mesh types.
- `Lighting/`:
  - `Map.LightmapAtlas.cs` — Baked HDR lightmap atlas packing (`BSP_LIGHTMAPS=1`).
  - `Map.Environment.cs`, `EnvironmentEntities.cs` — `light_environment`, fog/environment volumes, cubemaps, sky_camera / `Skybox3D`.
- `Entities/`:
  - `Map.Spawns.cs` — Spawn points, dynamic props (with skin/body variants), spectator camera.
  - `ParticleMapEntities.cs` — Map-placed `info_particle_system` entities.

## Invariants & Pitfalls
- **Full vs Pure-World**: Pure-world (`.world.scene`) omits spawns, spectator camera, and dynamic props, does not force NavMesh on, keeps all prop collision resident, and sets `ResourceFlags.DeveloperOnly` so map pickers only list the playable full scene (`maps/<map>.bsp.scene`) while `.world.scene` remains addressable.
- **Dedicated servers**: Require complete rBSP CM; do not fall back to render geometry on dedicated servers.
- **Derived cache invalidation**: Any change to `Map.Cache.cs` binary layout must bump its format version / `Titanfall2BuildIdentity` so stale caches are ignored.

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~MapThumbnailTests|FullyQualifiedName~SkyboxTests|FullyQualifiedName~ParticleMapEntityTests|FullyQualifiedName~RetailProbeDataTests"
```
