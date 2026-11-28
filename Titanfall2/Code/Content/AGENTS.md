# Code/Content/ — Discovery, Snapshots & Caches

Discovers retail TF2 and optional Northstar mod content, builds an immutable `Titanfall2ContentSnapshot` with deterministic precedence, manages derived cache files, and indexes particles/surfaces.

## Layer Boundary
- **Depends on**: `Code/` options/storage, `Code/Formats/` (for VPK/RPAK/PCF/script discovery).
- **Must not depend on**: `Components/` or `Gameplay/`.

## Files
- `ContentDiscovery.cs` — Scans retail `vpk/` + `r2/paks/Win64/` and Northstar profile mods; fixes session-wide override order (Northstar loose > active mod RPAK/VPK > retail RPAK/VPK > unselected MapOnly RPAK fallbacks > generated sky/Miles). Fallbacks keep unique custom-map assets without replacing global/retail names or native GUIDs; explicit map selection restores normal mod priority.
- `ContentSnapshot.cs` — Immutable snapshot of winning asset sources (`Winners`, `GetSource`, `TryGet`, `Revision`) plus losing candidates for diagnostics.
- `ContentMount.cs` — Registers discovered assets from the snapshot into `Titanfall2Mount`'s catalog.
- `NorthstarProfile.cs` — Parses `R2Northstar` mod lists, enabled flags, and `LoadPriority`.
- `SnapshotVpkSource.cs` — VPK-backed `ITitanfall2AssetSource` adapter bound to a snapshot.
- `Storage.cs`, `Storage.Metadata.cs` — Virtualized filesystem abstraction (`Titanfall2Storage`) backed by s&box `BaseFileSystem` (never bypass with raw `System.IO` in mount code).
- `ProcessedCache.cs` — Derived disk cache keys and invalidation bound to `Titanfall2BuildIdentity.VersionId` + snapshot revision.
- `EffectiveConfiguration.cs`, `ContentDiagnostics.cs` — Effective mount configuration snapshot and conflict/winner inspection.
- `ParticleCatalog.cs` — Lazy PCF particle catalog (`IndexParticleFiles`, thread-safe `_particleFailureLogs`). Not parsed during normal mount registration; indexed on first explicit particle query or combat warmup.
- `SurfaceCatalog.cs` — Resolves `scripts/surfaceproperties_manifest.txt` and ordered surface property scripts.
- `SceneResourceRelease.cs` — Scene lease tracking and resource release hooks when mounted scenes unload.

## Invariants & Pitfalls
- **Snapshot immutability**: Never mutate a published `Titanfall2ContentSnapshot`; revision mismatches across remounts throw by design (`_sessionContentRevision`).
- **Lazy particle parsing**: Keep PCF parsing deferred out of `Mount()` to preserve fast startup.
- **Locking**: Any failure-log collection or cache map touched by workers (e.g., `_particleFailureLogs`) must be locked or concurrent.

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~ContentSnapshotTests|FullyQualifiedName~SandboxStorageTests|FullyQualifiedName~SceneResourceLifetimeTests|FullyQualifiedName~WeaponParticleCatalogTests"
```
