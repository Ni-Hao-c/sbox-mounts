# Code/Resource/ — Engine Resource Loaders

Converts CPU data decoded by `Formats/` into s&box engine resources (`Model`, `Material`, `Texture`, `SoundFile`, `Surface`, `PrefabFile`, `UiAtlas`, decals).

## Layer Boundary
- **Depends on**: `Code/`, `Content/`, `Formats/`, `Collision/`, `Rendering/`.
- **Must not depend on**: `Gameplay/` (Gameplay consumes mounted resources via `mount://titanfall2/...` URIs or `Model.GetData<T>()`).

## Key File Groups
- **Models** (`Model*.cs`):
  - `Model.cs`, `Model.Cache.cs`, `Model.Metadata.cs`, `Model.GpuGeometry.cs`, `Model.Census.cs` — `ModelLoader` builds `*.mdl.vmdl`, `*.mdl.visual.vmdl`, and skin/body variants (`Skin N` material groups).
  - `Model.Animations.cs`, `Model.ActorSequences.cs`, `Model.Infantry.cs` — `*.mdl.infantry.vmdl`, independent child resources (`skeleton.skel`, `animations.json`, `animations/<clip>.anim`, `physics.prefab`), and `Titanfall2ActorAnimationData`.
  - `Model.PilotView.cs`, `Model.PilotView.Resources.cs` — `*.mdl.pov.vmdl` / `*.mdl.titanpov.vmdl` first-person viewmodels & bone-merged pilot arms (`Titanfall2PilotViewData`).
  - `Model.PreviewGeometry.cs` — Fast static-baked `*.mdl.preview.vmdl` thumbnails for the Q-menu (no skeleton/collision/extra skins, 512px mip cap).
  - `Model.UI.cs`, `ModelExporter.cs` — Weapon UI surface binding and DMX/model export utilities.
- **Materials & Textures**:
  - `Material.cs`, `Material.Cache.cs`, `MaterialDescriptor.cs`, `MaterialParameters.cs`, `MaterialPrefetch.cs`, `LegacyMaterial.cs`, `SkyMaterial.cs`.
  - `Texture.cs`, `Texture.Cache.cs`, `TextureMipUpload.cs`, `TexturePreparationBudget.cs`, `ColorLookup.cs`, `UiAtlas.cs`.
- **Audio, Physics, Decals & Particles**:
  - `Sound.cs` — `SoundLoader` (`*.vsnd` from WAV or `general.mbnk` Bink Audio 2 PCM; supports mono/stereo and downmixes 4/6ch to stereo).
  - `Surface.cs` — `Titanfall2Surface` (`surfaceproperties/<name>.surface` + `.json`).
  - `Decals.cs` — `Titanfall2DecalFactory` (`TryCreateDefinition`, `Spawn`, `RotationForNormal`, `titanfall2_decal`).
  - `Particle.cs` — Independent `particles/<escaped-name>.prefab` loader.

## Invariants & Pitfalls
- **Main-thread creation**: `ModelBuilder`, `Material.Create`, `Texture.Create`, and `SoundFile` construction must run on the main thread; heavy decompression/mesh extraction belongs on workers.
- **Hidden weakpoint markers**: Strip `models/titans/titan_ar_marker/titan_ar_marker` on actor models so AR debug boxes never draw on live NPCs/Titans.
- **Preview model separation**: Never substitute `.preview.vmdl` for gameplay `.infantry.vmdl` or `.held.vmdl`.
- **Actor traversal motion**: `Model.Infantry.cs` preserves original root XYZ/yaw curves in `Titanfall2ActorAnimationData.Clip`. Traversal and Northstar airborne action clips are exported in place by `HasExternalRootMotion` in both native model and independent AG2 paths. Their world movement belongs to the traversal/Titan movement owner; do not apply the root curve twice. Ordinary walk/run only strip horizontal motion as before.
- **Titan action roles**: Use the exact verified retail labels in `TitanActionAnimationRank`; never map generic `ACT_MP_OFFHAND_CHARGING` to every kit. Northstar `lt_npc_flight_core` is a dedicated core role, not a generic jump despite its retail `ACT_JUMP` tag.
- **Map loaders**: See [Map/AGENTS.md](Map/AGENTS.md).

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~TexturePreparationTests|FullyQualifiedName~LegacyTexturePathTests|FullyQualifiedName~ModelBrowserTests|FullyQualifiedName~OfficialLoaderTests|FullyQualifiedName~ColorCorrectionTests"
```
