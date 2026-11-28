# Titanfall 2 s&box Mount — Root Agent Guide

`Titanfall2` is an s&box game mount (`Ident = "titanfall2"`, Steam AppId `1237970`) that streams and converts retail Titanfall 2 assets (VPK, RPAK/StarPak, rBSP, MDL53, Miles/BCF, PCF, VMT/VTF) and gameplay systems on demand.

This file defines **global standards** and the **directory map**. Layer-specific architecture, invariants, and verification commands live in each layer's own `AGENTS.md`. 当完成修改完代码后，必要的情况下同步修改 AGENTS.md。

---

## Global Rules & Invariants

### 1. s&box Sandbox Allow-List & Build Constraints
- `Build/Titanfall2.csproj` compiles with `TreatWarningsAsErrors=true`. Zero compiler warnings allowed.
- Every release change MUST pass official sandboxed compilation via `MenuBuild.exe` (enforces the s&box API whitelist).
- **Prohibited APIs**: Do not use `System.Threading.Volatile.Read/Write`, arbitrary reflection, `System.IO` direct host paths outside `Titanfall2Storage`, or unwhitelisted engine internals. Use `volatile` fields + `lock` or `ConcurrentDictionary` / `Interlocked`.
- **Main-thread boundary**: Background workers may read, decompress, and parse CPU data (`Formats/`). Engine `Texture`, `Material`, `Model`, `Mesh`, `SoundFile`, and `GameObject`/`Component` creation must stay on the main thread.

### 2. Serialization & Compatibility
- Keep public `Component` class names and global namespace intact — `.scene` and `.prefab` files serialize them by full type name.
- Moving a `PanelComponent` requires moving its matching `*.cs.scss` file under `Assets/` (`ClassFileLocationAttribute + ".scss"`).
- Do not rename `Titanfall2Mount` or runtime private fields inspected by `Validation/` reflection tests (see `Code/AGENTS.md` and `Validation/AGENTS.md`).

### 3. Layer Boundaries
```
Formats/ (pure CPU readers)
  ↑
Content/ (discovery, immutable snapshot, cache)
  ↑
Collision/ & Rendering/ (low-level trace masks & GPU batch primitives)
  ↑
Resource/ & Resource/Map/ (engine loaders & scene builders)
  ↑
Components/ (runtime map/prop/particle/lighting/audio scene components)
  ↑
Gameplay/ (Catalog, Pilot, Weapons, Npc, Titans, Projectiles, FrontierDefense, Shared)
```
- Each layer calls only the layers below it. Never import `Gameplay` types into `Formats`, `Content`, `Collision`, `Rendering`, or `Resource` (except `Catalog` registration hooks invoked from `Titanfall2Mount`).
- Keep member visibility minimal (`private` / `internal`); expose `public` only for engine-serialized properties, components, and public mount APIs.
- Handle errors explicitly; never swallow failures silently. Avoid speculative abstractions (Rule of Three).

### 4. Standard Build, Test & Deploy Commands
Set `$engineRoot = if ($env:FACEPUNCH_ENGINE) { $env:FACEPUNCH_ENGINE } else { "D:\Game\steams\steamapps\common\sbox" }`:

- **Unit tests**:
  ```powershell
  dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$engineRoot" --no-restore -v minimal
  ```
- **Sandboxed build (whitelist verification)**:
  ```powershell
  & "$engineRoot\bin\managed\MenuBuild.exe" --project "D:\sbox\sbox-mounts\Titanfall2\.sbproj" --out "$env:TEMP\tf2-menubuild"
  ```
- **Validation project build**:
  ```powershell
  dotnet build Titanfall2/Validation/RuntimeValidation.csproj -c Release "-p:SboxEngineRoot=$engineRoot" "-p:Titanfall2AssemblyPath=<builtDll>" -v minimal
  ```
- **Deploy to Steam s&box**: Copy `package.local.titanfall2.dll` to `$engineRoot\mounts\Titanfall2\package.local.titanfall2.dll`.

---

## Project Directory Map

| Directory | Purpose | Layer Guide |
| --- | --- | --- |
| `Code/` | `Titanfall2Mount` lifecycle, options, settings, logging, worker pool | [Code/AGENTS.md](Code/AGENTS.md) |
| `Code/Content/` | Steam/Northstar discovery, immutable `ContentSnapshot`, caches, particle/surface catalogs | [Code/Content/AGENTS.md](Code/Content/AGENTS.md) |
| `Code/Formats/` | Pure CPU binary/script readers (VPK, RPAK/LZHAM, rBSP, MDL53, Miles, PCF, VMT, VTF) | [Code/Formats/AGENTS.md](Code/Formats/AGENTS.md) |
| `Code/Collision/` | Retail contents bitmasks, `Titanfall2TraceMask`, batched `CollisionQueries`, diagnostics | [Code/Collision/AGENTS.md](Code/Collision/AGENTS.md) |
| `Code/Rendering/` | Non-Component GPU scene, static prop batching, world material batch, `MountedScenePerfKit` | [Code/Rendering/AGENTS.md](Code/Rendering/AGENTS.md) |
| `Code/Resource/` | Engine `ResourceLoader`s (models, materials, textures, sounds, surfaces, decals, particles) | [Code/Resource/AGENTS.md](Code/Resource/AGENTS.md) |
| `Code/Resource/Map/` | rBSP scene import, world/prop geometry, entities, lightmap atlas, derived map cache | [Code/Resource/Map/AGENTS.md](Code/Resource/Map/AGENTS.md) |
| `Code/Components/` | Scene `Component`s (maps, static props, particles, lighting, post-processing, nav, audio) | [Code/Components/AGENTS.md](Code/Components/AGENTS.md) |
| `Code/Gameplay/` | Pilot, Weapons, NPCs, Titans, Projectiles, Frontier Defense, Catalog & Shared combat | [Code/Gameplay/AGENTS.md](Code/Gameplay/AGENTS.md) |
| `Assets/` | Mount shaders (`titanfall2_*.shader` / `.hlsl`) and `PanelComponent` SCSS styles | [Assets/AGENTS.md](Assets/AGENTS.md) |
| `Tests/` | MSTest unit & retail-VPK regression suite (`Titanfall2.Tests.csproj`) | [Tests/AGENTS.md](Tests/AGENTS.md) |
| `Validation/` | Isolated s&box native runtime integration harness (`RuntimeValidation.csproj`, `validate.ps1`) | [Validation/AGENTS.md](Validation/AGENTS.md) |
| `Build/` | Local MSBuild project (`Titanfall2.csproj`) & build helpers (mirrors `.sbproj` compilation) | — |
| `docs/` | Technical design and combat-system reference docs | — |
