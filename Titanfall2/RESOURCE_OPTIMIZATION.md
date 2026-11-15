# Resource construction and lifetime update

This work is based on the frozen sandbox migration, restored into a separate
`codex/titanfall2-resource-optimization` branch. The frozen branch and main checkout
are preserved. The GoldSrc review informed the resource boundaries; its map format,
lighting approximations and leaf partitioning are not substituted for Titanfall 2 data.

## Implemented

- Build a sparse material-to-fragments index once instead of scanning every spatial
  cell for every material. Preserve fragment order, actual geometry, bounds, lightmap
  page keys and the existing official AggregateRenderer backend.
- Keep transparent, water, decals, godrays and ordinary native instance batches on
  their existing paths. Do not replace repeated props with unique merged geometry.
- Make the texture preparation byte target and material look-ahead window adjustable.
  Deduplicate tracked texture loaders and report retained peak, total payload bytes,
  unique loaders and main-thread wait. The target is soft: in-flight reads can cross it.
  No worker waits for GPU creation to return budget during a synchronous import.
- Preserve dependency-driven material preparation and processed-cache behavior.
- Store cubemap source data in serialized components; make runtime probe helpers
  NotSaved/NotNetworked. Disable releases owned helpers/textures, enable restores them,
  and clones reconstruct independent GPU resources. Reconfiguration releases the old
  resources before taking ownership of the new set.
- Build the temporary skybox scene disabled. Its serialized objects are activated by
  the official Skybox3D copy, rather than queuing callbacks for an already destroyed
  template scene during nested SceneLoader construction.
- Retain separate world collision and trace/render responsibilities. Existing resource
  caches and scene-owner leases are preserved.
- Emit per-import phase timings, aggregate/fragment/material counts and RPAK upload
  deltas. Counts describe import work, not frame-rate performance.
- Track the required generated material ABI snapshot in Git. It previously matched
  the generated-file ignore pattern and could disappear in a fresh checkout.
- Handle empty packed record arrays without allocating a zero-sized ByteStream.
  Optional model/BSP arrays previously caused derived-cache writes to fail and fall
  back to repeated original imports. Non-empty layout and truncation checks remain.

## Official engine prerequisite

Use Facepunch/sbox-public commit `3e80ced851c1d02cc808dc3851bbeb745d646c2f`
or a descendant containing its mount registration fixes. Upstream resolved
[issue #12368](https://github.com/Facepunch/sbox-public/issues/12368) by enrolling
exposed DLL mount types in both menu and game libraries, restoring them during
Game.InitTypeLibrary rebuilds, and mounting Assets before source initialization.
The follow-up fixes also roll back failed registrations and refresh JSON upgraders.

The temporary local engine registration patch has been reverted. No mount-specific
runtime engine changes or sandbox allow-list changes are required by this package.
The mount continues to use the official compiler and public runtime APIs. Keep the
old branch and patch as historical recovery points, rather than applying them on top
of the official implementation.

## Validation

`Build/build.ps1 -EngineSource <checkout> -OutputDirectory <outside-game> -Test`
builds the sandboxed package, runs mount tests and processes its shaders/content.

`Validation/validate.ps1 -RuntimeRoot <game> -PackageDirectory <package> -Graphics`
checks deployed module identity, automatic registration before fixture enrollment and
after two library resets, scene/render lifetime,
cubemap disable/re-enable/clone ownership and failed-load rollback.

Add `-GameDirectory <Titanfall2 install> -MapName mp_angel_city` for two native scene
load/render/destroy cycles, actual world-material-buffer readiness and PNG captures.
The fixture's project filesystem is writable for mount thumbnails; game content stays
read-only. Frame timing needs a separate focused-camera A/B measurement. No FPS gain
or complete pixel equivalence follows from passing these lifecycle checks.
