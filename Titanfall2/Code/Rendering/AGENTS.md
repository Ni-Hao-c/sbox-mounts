# Code/Rendering/ — GPU Scene, Batching & PerfKit

Low-level, non-`Component` rendering infrastructure for large mounted maps: world geometry aggregation, static-prop instanced GPU scenes, LOD/culling budgets, world material tables, and local-light GPU buffers.

## Layer Boundary
- **Depends on**: `Formats/` (vertex/probe/light structs) and s&box `Sandbox.Rendering` / `Graphics`.
- **Must not depend on**: `Gameplay/`. Avoid placing s&box `Component` classes here — scene components belong in `Code/Components/Rendering/` (e.g., `WorldMaterialTableController.cs`).

## Files
- `MountedScenePerfKit.cs` — Reusable, format-agnostic `Sandbox.Mounting.PerfKit` (`MountedAggregateBuilder`, `MountedInstanceBatch`, `MountedInstanceCull`).
- `StaticPropAggregate.cs`, `StaticPropAggregate.Cache.cs` — Aggregated static-prop mesh generation and caching.
- `StaticPropGpuScene.Layout.cs`, `StaticPropGpuScene.Budget.cs` — GPU-driven static-prop layout and per-frame memory/draw budgeting.
- `StaticPropMeshSimplifier.cs` — LOD simplification for prop batches.
- `WorldMaterialBatch.cs` — World material batch data and table packing (non-Component).
- `GpuSceneMaterial.Generated.cs` — Material/shader parameter bindings for GPU-driven static scenes.
- `GpuViewPool.cs` — Reusable GPU buffer/view pooling.
- `LocalLightBuffer.cs` — Packs per-cluster local lights for `titanfall2_local_lights.hlsl` (up to 4 local lights/pixel).

## Invariants & Pitfalls
- **PerfKit design rules (do not break)**:
  - Merged vertex buffers must pass `startVertex = 0` to `AddSubMesh` (indices are already rebased).
  - Never exceed `AggregateRenderer.MaxFragments`; split into multiple aggregates rather than dropping fragments.
  - Instance arrays are read by the render thread every frame — replace the array reference instead of mutating in place.
- **Keep `MountedScenePerfKit.cs` format-agnostic**: Do not introduce Titanfall2-specific types into `Sandbox.Mounting.PerfKit`.
- **Combat visibility**: Static aggregates retain every forward prop and authored LOD; no size/collision/cutout heuristic may delete the Far tier or synthesize a shrunken cover silhouette. Native view culling owns aggregate visibility; a depth-limited portal walk is not a visibility proof. Shadow casters must not be gated by main-camera PVS. Cache V9 invalidates the older omitted/simplified geometry.

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~WorldMaterialBatchTests|FullyQualifiedName~StaticPropSceneDataTests"
```
