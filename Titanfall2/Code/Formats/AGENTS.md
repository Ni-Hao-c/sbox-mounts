# Code/Formats/ — Pure CPU Binary & Script Readers

Self-contained decoders for Titanfall 2 archive, model, map, shader, texture, audio, particle, and script formats.

## Layer Boundary
- **Depends on**: .NET BCL and internal `Formats/` primitives (`AssetSource.cs`, `Binary.cs`, `SourceIdentity.cs`).
- **Must not depend on**: `Resource/`, `Components/`, `Gameplay/`, or s&box scene/runtime APIs. (A few texture decoders expose narrow main-thread creation helpers — do not expand engine dependencies here.)

## Subfolders & Key Files
- Root: `AssetSource.cs` (`ITitanfall2AssetSource`), `Binary.cs`, `SourceIdentity.cs`.
- `Vpk/` — `VpkArchive.cs`: Respawn VPK directory/volume reader (`englishclient_` -> `client_`, hides mirrored `depot/`).
- `Rpak/` — `RpakArchive.cs`, `RpakReader.cs`, `PakFileDecoder.cs`, `StarPakIndex.cs`, `Lzham*.cs` (managed LZHAM decompressor), `DxbcReflection.cs`, `RpakTextureDecoder.cs`, `RpakUiAtlas.cs`, `RpakWeaponFont.cs`, `RpakWeaponUiLayout.cs` (diegetic gun HUD layouts & `Titanfall2WeaponUiCatalog`).
- `Bsp/` — `Titanfall2BspReader.cs`, `Titanfall2BspCollisionReader.cs` (rBSP CM brushes/tricolls), `Titanfall2BspBrushModels.cs`, `Titanfall2CompiledVisibility.cs` (PVS), `Titanfall2EntityText.cs`, `Titanfall2BspFogVolumes.cs`, `Titanfall2FogEnvironment.cs`, `Titanfall2LightEnvironment.cs`, `Titanfall2LightProbe.cs`, `Titanfall2LightProbeLookup.cs`, `Titanfall2WorldLight.cs`.
- `Mdl/` — `Titanfall2Mdl53Reader.cs`, `Titanfall2Mdl53Structures.cs`, `Titanfall2MeshExtractor.cs`, `Titanfall2VtxReader.cs`, `Titanfall2VtxStructures.cs`, `Titanfall2Mdl53AnimationReader.cs`, `Titanfall2SequenceRuntimeData.cs`, `Titanfall2Mdl53PhysicsReader.cs`, `PackedArrays.cs`.
- `Miles/` — `MilesBankArchive.cs`, `MilesBinkDecoder.cs` (managed Bink Audio 2 -> PCM), `MilesSoundEvents.cs` (`general.mbnk` event catalog).
- `Pcf/` — `Titanfall2PcfReader.cs` (binary PCF particle system parser).
- `Vmt/` — `Titanfall2VmtReader.cs` (legacy VMT material parser).
- `Vtf/` — `VtfTextureDecoder.cs` (legacy VTF texture decoder).
- `Scripts/` — `Titanfall2SoundscapeScript.cs`.

## Invariants & Pitfalls
- **Worker-safe**: Every reader must be safe to invoke concurrently on background worker threads with immutable input spans/streams.
- **Bounds checking**: Binary readers parse untrusted retail/mod files; validate header counts, offsets, and ranges before allocating or slicing.
- **MDL53 R2 frame movement**: Track offsets are relative to the start of the 24-byte movement struct, not the bone-track pointer group base.
- **Miles audio**: `*_Start_*` / `*_End_*` sound events in `general.mbnk` can resolve to `-96 dB` / `sound/null`; verify actual audible wave layers when binding gameplay audio.
- **Weapon UI layouts (`RpakWeaponUiLayout.cs`, `RpakWeaponOpticLayouts.cs`)**: Preserve numeric anchor checks and image/UV-window intersection checks when selecting diegetic surfaces. Optic colors extracted from the retail UI DLL are linear HDR, not Painter's sRGB input.
- **RUI atlas padding**: `uimg` header+16 points to 32-byte per-image transforms. The second float4 maps original UVs into the trimmed sprite. `rui_full/<image>.vtex` restores its authored transparent canvas; ordinary icon/thumbnail paths retain cropped output.

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~LzhamDecoderTests|FullyQualifiedName~PackedArrayTests|FullyQualifiedName~MilesAudioTests|FullyQualifiedName~MilesSoundEventTests|FullyQualifiedName~UiAtlasTests|FullyQualifiedName~WeaponHudTests"
```
