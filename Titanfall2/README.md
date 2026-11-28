# Titanfall 2
[English](#en) | [中文](#zh)

<a id="en"></a>

`Titanfall2Mount` ([GameMount.cs](Code/GameMount.cs)) exposes a locally installed Steam copy of Titanfall 2 (AppId `1237970`) as s&box resources under `mount://titanfall2/`. Both `r2/paks/Win64` and `vpk` must exist in the installation. Resources are read and converted on demand without extracting the game first.

[Code layout](#en-layout) · [Scope](#en-scope) · [Development](#en-development) · [Configuration](#en-configuration) · [Technical reference](#en-technical) · [Limitations](#en-limitations) · [References](#en-references)

<a id="en-layout"></a>
## Code layout
Like the SourceEngine mount, indexing and mount lifecycle live in the mount, binary readers in `Formats`, resource loaders in `Resource`, and runtime components in `Components`. Map construction is grouped under `Resource/Map`. Collision and rendering helpers have their own directories.

| Path | Responsibility |
| -- | -- |
| [Code/GameMount.cs](Code/GameMount.cs), [Code/Content/](Code/Content) | Installation discovery, immutable content snapshots, resource registration and shutdown. |
| [Code/Formats/](Code/Formats) | VPK/LZHAM, RPAK/StarPak/DXBC, rBSP, MDL53/VTX/VVD/VPHY, Miles/BCF, PCF, VMT, VTF and scripts. Some texture readers also contain main-thread creation entry points. |
| [Code/Resource/](Code/Resource) | Model/material/texture/sound loaders and related conversion helpers. |
| [Code/Resource/Map/](Code/Resource/Map) | Map loading/import/cache and grouped `Geometry`, `Entities`, `Lighting` builders. |
| [Code/Components/](Code/Components) | Runtime map, prop, particle (`ParticleStreamer` / `ParticleFactory`), audio, navigation, lighting, rendering and post-processing components. |
| [Code/Gameplay/](Code/Gameplay) | Modular gameplay systems grouped into `Catalog/` (data-driven registries & prefab builders), `Pilot/` (`Parkour`, `Combat`, `Tacticals`), `Weapons/` (`Data`, `Mechanics`, `Runtime`, `Ui`), `Projectiles/`, `Npc/`, `Titans/` (`Chassis`, `Kits`, `Cores`, `Defense`, `Cockpit`), `FrontierDefense/`, and `Shared/` (`Combat`, `Audio`, `Animation`, `Prewarm`). Supports optional JSON overlays in `scripts/titanfall2_sbox/*.json` or `FileSystem.Data/titanfall2/data/*.json`. |
| [Code/Collision/](Code/Collision) | Contents masks, source metadata, trace queries and diagnostics. |
| [Code/Rendering/](Code/Rendering) | Aggregate/instance drawing helpers, probe/local-light buffers and material-table layouts. |
| [MountedScenePerfKit.cs](Code/Rendering/MountedScenePerfKit.cs) | Reusable world aggregation, instance batches and culling in `Sandbox.Mounting.PerfKit`. |
| [Titanfall2StreamingSettings.cs](Code/Titanfall2StreamingSettings.cs) | Mount configuration from the data filesystem. |
| [Titanfall2GraphicsSettings.cs](Code/Titanfall2GraphicsSettings.cs) | Player texture quality policy. |
| [Assets/shaders/](Assets/shaders) | Mount shaders, named `titanfall2_*`. |

`Formats` prepares CPU data; loaders construct engine resources; `MapLoader.BuildScene` assembles serializable scenes. Workers read, decompress and decode. Engine textures, materials, meshes, models and scene objects are created on the main thread. Official addon compilation supplies callbacks and registers components/commands; the mount does not register private interfaces or bypass the sandbox allow-list.

<a id="en-scope"></a>
## Scope
The content snapshot fixes precedence for a mount session: enabled Northstar loose overrides first, then each mod's active RPAK/VPK content, then retail RPAK/VPK, generated sky material and Miles sounds. Higher `LoadPriority` wins; equal priorities use deterministic directory ordering. Without `NORTHSTAR_MAP`, RPAKs selected only through a `MapOnly` map declaration (including their Postload chain) are indexed after global/retail sources: unique custom-map assets remain available, while shared assets keep the global/retail winner. Select the custom map explicitly to activate its shared RPAK overrides. Diagnostics retain losing candidates for inspection.

| Source | Mounted content |
| -- | -- |
| `r2/paks/Win64/*.rpak`, StarPak | TXTR textures and MATL materials; patch packages precede ordinary packages, with `common.rpak` prioritized. High mips can come from StarPak. |
| `vpk/*_dir.vpk` | VMT materials, VTF textures, models, WAV sounds and map scenes. `englishclient_` directories resolve `client_` data volumes; mirrored `depot/` entries are hidden. |
| MDL v53 with embedded VTX/VVD/VPHY | `models/<name>.mdl.vmdl`, attachments, hitboxes, skins (`Skin N`), skeletons and supported sequences from the model and its includes. Body part model 0 is the default. A static-instance variant supports batched LODs. |
| `r2/sound/general.mbnk` | `sound/<name>.vsnd`, decoded from BCF to PCM by the managed Bink Audio 2 decoder. |
| rBSP | Full and pure-world scenes, plus inline brush models at `maps/<map>.bsp/brush/<index>.vmdl`. |
| PCF | Map effects and independent `particles/<escaped-name>.prefab` resources. |

`skies/skycube.vmat` wraps the retail cubemap in a native sky material.
### Loading maps
```text
mount://titanfall2/maps/mp_angel_city.bsp.scene
mount://titanfall2/maps/mp_angel_city.bsp.world.scene
```

The full scene includes world geometry/collision, environment, props, particles, audio, spawn points, dynamic props and a spectator camera. The pure-world scene omits spawns, cameras and dynamic props, does not force navigation on, and keeps all prop collision resident.

Add [Titanfall2Map](Code/Components/Map/Titanfall2Map.cs) to embed a map in another scene, similarly to `MapInstance`. `MapName` accepts the map name with optional `.bsp`/`.bsp.scene`; `PureWorld` selects the world scene. `SpawnSpectatorCamera` supplies a camera above the first spawn if none exists, except in pure-world mode. `LoadInEditor` is off by default because large maps preload many props. `GetAvailableMaps()` lists mounted names. Imported objects are unsaved children; the component destroys only its own objects and remaps IDs when cloning scene JSON.

Wait for `ttf2_map_ready` on `worldspawn` before placing players. [Titanfall2WorldReady](Code/Components/Map/WorldReady.cs) requires complete resident prop collision, shaped world physics bodies and every contents group in the collision manifest. It clears readiness on load/enable and revokes it if resources disappear; physics scans are throttled to 0.25 seconds. Corrupt prop data cannot publish readiness. Preparation completion, successful rendering resources and complete collision are separate states.
### Rendering and props
With `BSP_LIGHTMAPS=1`, world geometry uses baked HDR lightmaps, props use authored probes, and local lights are evaluated in shaders, up to four per pixel. `light_environment` provides the sun; environment/fog volumes select parameters around the camera. Vista models use public `Skybox3D` with `sky_camera`. Water uses semantic shader classification and framebuffer copies. Decal geometry has its own non-shadowing path. Exposure, display transform, global/local 32³ color correction and core/wide/streak Bloom are reconstructed by the mount.

Props preload during loading and live until scene destruction. Native instance batches group a model within a default 4×4 cluster of 512-unit cells. Four decoded LOD bands switch at 1536/4096/8192 units; remaining model slots alias the deepest available band. PVS uses up to 128 cells and 16 portal hops; invalid masks remain visible, and a batch is visible if any member is visible. Authored fades and the default 8192-unit cull apply, except landmarks with radius at least 256. Props with radius below 48 do not cast directional shadows. World/prop shadows and contact shadows default on; godray cards can flicker during distance fades.
### Independent resources

Models retain the compatible `models/<name>.mdl.vmdl` path. The additional `models/<name>.mdl.visual.vmdl` builds geometry, skeleton and a bind pose without embedding the sequence library or collision. Its `titanfall2_resources` model data lists the associated resources:

| Child of either model path | Engine resource | Use |
| --- | --- | --- |
| `skeleton.skel` | `Sandbox.Animation.Skeleton` | Load the MDL53 reference skeleton without creating render meshes. |
| `animations.json` | Text | Discover clip paths, include sources, sequence activities, flags and blend-grid cells. |
| `animations/<escaped-name>.anim` | `Sandbox.Animation.Clip` | Decode one requested clip, including additive scale conversion, using the TF2 MDL53 decoder. |
| `physics.prefab` | `PrefabFile` | Clone independently generated model collision using a `PhysicsCollider`. |

Append the child path to the full mounted model path, for example `mount://titanfall2/models/humans/pilots/pilot_light_ged_m.mdl.visual.vmdl/skeleton.skel`. Clip names are URI escaped; consume the paths in `animations.json`. Blend-grid cells are separate clips; this does not evaluate TF2 sequence layers, IK, events or gameplay animation graphs. A host graph can consume the standard skeleton and clips. Physics prefabs retain the existing VPHY/hitbox/render-fallback policy and do not apply an sprp instance's effective contents or add a rigidbody automatically. Each clone uses `Titanfall2PhysicsResource` to bind its own runtime `PhysicsCollider`; `Prepared` reports success. Prefabs serialize the model source, skin and body rather than the shared anonymous native physics name.

`mount://titanfall2/particles` returns a text index of effect names and prefab paths. Load `mount://titanfall2/particles/<escaped-name>.prefab` as a `PrefabFile`. The PCF catalog is parsed on first explicit particle-resource discovery/load; ordinary mount registration does not parse it. Each clone reconstructs the same TF2 particle controllers as map effects, owns its runtime children and exposes `Titanfall2ParticleResource.Prepared`. Set endpoints through its root `Titanfall2ParticleControlPoints`. The prefab remains dependent on the mounted game and supported PCF operators.

### Weapons, NPCs and Titans

The mount publishes ordinary `PrefabFile` resources tagged `weapon`, `npc`, or `npc` + `titan`. Mount Titanfall 2 first, then use `GameObject.GetPrefab( uri )?.Clone( position )` or a host that lists tagged mount prefabs.

| Path under `mount://titanfall2/entities/` | Contents |
| --- | --- |
| `weapons/mp_weapon_<id>.prefab` (hitscan) | `rspn101`, `car`, `r97`, `vinson`, `g2`, `semipistol`, `autopistol`, `alternator_smg`, `hemlok_smg`, `lmg`, `esaw`, `dmr`, `rspn101_og`, `wingman`, `shotgun`, `hemlok`, `smart_pistol` |
| `weapons/mp_weapon_<id>.prefab` (projectiles) | `epg`, `lstar`, `sniper`, `smr`, `doubletake`, `wingman_n`, `softball`, `pulse_lmg`, `mastiff`, `shotgun_pistol` |
| `weapons/mp_weapon_<id>.prefab` (anti-Titan) | `defender`, `mgl`, `rocket_launcher`, `arc_launcher` |
| `weapons/mp_weapon_<id>.prefab` (ordnance) | `frag_grenade`, `grenade_emp`, `thermite_grenade`, `grenade_gravity`, `grenade_electric_smoke`, `satchel` |
| `items/pilot_jump_kit.prefab` | Equipable Pilot Jump Kit (`Titanfall2PilotParkour`, `Titanfall2PilotCombat`, `Titanfall2PilotTactical`: double jump, wallrun/wallkick, slide/slide-hop, ledge mantle, melee, offhand ordnance, Stim, Grapple, Phase Shift, Pulse Blade). |
| `items/pilot_tactical_{stim,grapple,phase,pulse}.prefab` | Pick up with E after equipping the Jump Kit; replaces the single tactical slot. T activates. The Jump Kit starts with an empty slot; duplicate pickups do not refill charges. Retail tactical icon appears beside health. |
| `npcs/{ally,enemy,neutral}/<id>.prefab` | 32 infantry/robot/wildlife actors split into `[Ally]` (15), `[Enemy]` (15), and `[Neutral]` (2 MRVN) mounts (`npc_soldier` / `npc_militia_grunt`, `npc_reaper` / `npc_reaper_ally`, `npc_plasma_turret`, `npc_attack_drone`, `npc_prowler`, Spectres, Stalkers, Pilots, Shield Captains, MRVNs). |
| `titans/{ally,enemy}/<id>.prefab` | 8 drivable Titans split into `[Ally]` & `[Enemy]` mounts (`monarch`, `bt7274`, `legion`, `ion`, `tone`, `scorch`, `northstar`, `ronin`) + 4 `[Enemy]` Frontier Defense AI Titans (`arc_titan`, `nuke_titan`, `elite_monarch`, `mortar_titan`). |
| `titanweapons/mp_titanweapon_<id>.prefab` | `xo16_shorty`, `xo16_vanguard`, `predator_cannon`, `particle_accelerator`, `leadwall`, `sniper`, `sticky_40mm`, `meteor`. Tagged `titan-weapon`; use `Titanfall2TitanArmament.EquipTo(actor)` on the host. |

Weapons read PC multiplayer `WeaponData` (including `#base`, `MP_BASE`, and `Mods`). They use `BaseCombatWeapon` for inventory, hitscan and reload, plus retail charge (Charge Rifle), continuous heat/overheat venting (L-STAR), spin-up (Devotion), smart-lock (Smart Pistol), burst (Hemlok), and multi-pellet patterns (EVA-8, Double Take, Mastiff, Mozambique). A standalone host needs a `PlayerController` and `BaseInventoryComponent` configured for pickup and control; reserve ammo is unlimited. Pilot guns use retail POV rigs and bone-merged pilot arms with supported ADS, sprint, reload and animation events. Authored bodygroups, muzzle flashes, shells, tracers, trails and bullet decals use mounted retail resources. Gunshot samples are selected by verified weapon family, perspective and firing stage; the Miles event mixer is not reproduced. See [presentation and current limits](docs/COMBAT_SYSTEMS.zh-CN.md).

Projectile weapons consume a round on the host and create a swept flight, applying gravity, direct damage and (where configured) occluded splash. Fast bolts use the same sweep as slower plasma and rockets. Models come from the mount; bolts represented by the original empty model use a colored engine streak. Double Take uses a horizontal pattern, and L-STAR uses continuous heat buildup with overheat venting.

Combat NPCs have host-side sight checks, faction combat and death, with `NavMeshAgent` movement where there is a usable mesh. The Reaper, stationary plasma turret and hovering attack drone fire integrated projectiles; MRVN is passive, and the Prowler uses short-range melee. The drone uses a swept local hover controller, not a flight navigation graph. The `.infantry.vmdl` variants decode a small set of idle/move/death clips where available.

Titans use chassis-sized collision, regenerating shields and separate anti-Titan weapon damage. The local player or listen host can press Use to embark through a short, cancellable connection sequence. The native cockpit HUD displays hull, shields, ammunition, targeting, ability cooldowns and core charge. Use leaves through a checked, grounded player-sized exit; moving and looking steers, primary attack fires, Reload reloads, secondary attack uses ordnance, Duck uses defense, and Run dashes. The core uses the configured action, Flashlight/F by default. Entering aligns the Titan with the pilot; exiting, disabling or destroying the Titan restores pilot inventory and movement. Seven chassis kits and seven core abilities (including Northstar Flight Core, Monarch Upgrade Core, Ion Laser Core, Legion Smart Core, Ronin Sword Core, Tone Salvo Core, Scorch Flame Core) are reusable prefab components, with shared Phase Dimension mechanics for Pilot Phase Shift and Ronin Phase Dash. Tone requires three 40mm hits on a target before firing its tracking rockets. Remote-client driving and complete retail gameplay animation graphs remain unsupported. See [combat systems and current approximations](docs/COMBAT_SYSTEMS.zh-CN.md).

Player/NPC hits and projectiles share retail impact-table particle/sound lookup with native spark, smoke, flash and procedural bullet/scorch decal fallbacks. Decals follow hit props and project into the surface; skinning, water and unavailable PCF operators have limited coverage. A scene retains at most 128 marks and 64 impact bursts. Titan armament prefabs install retail ballistics and replace the held model, then reload; they are not pilot inventory pickups.

The local `Validation/validate.ps1 -GameplayOnly -Graphics` fixture checks the compiled package against a local TF2 installation in an isolated runtime.

### Collision, surfaces and decals
World collision uses rBSP CM brushes/tricolls, grouped by original contents. Groups without player-solid bits become triggers. Clients warn and fall back to filtered render geometry if CM is incomplete; dedicated servers require complete CM. Prop collision prefers VPHY hulls, then hitboxes, then render meshes; meshes above 8192 triangles use a bounds hull. Effective contents combine sprp solid type, MDL contents and the remove mask; zero means no collision. Ordinary scenes default to resident collision. With `RESIDENT_WORLD=0`, props activate within 1536 units and release beyond 2560; pure-world scenes and servers remain resident.

Objects carry `ttf2_contents`, `ttf2_content_<bit>` and applicable `playerclip`, `titanclip`, `nograpple`, `nowallrun` tags. [Titanfall2CollisionQueries.Run](Code/Collision/CollisionQueries.cs) accepts `Titanfall2TraceMask`, such as `Shot`; masks use retail constants. `titanfall2_collision_look [distance=512] [radius=0]` inspects collision source, contents, shapes and tags without changing them.

World CM and fallback collision use standalone `PhysicsGroupDescription` resources and `PhysicsCollider`, independently of rendering models. Original contents groups, query tags and native-body readiness checks are retained. Existing serialized `ModelCollider` readiness references remain supported.

[Titanfall2Surface](Code/Resource/Surface.cs) first resolves the installed game's `scripts/surfaceproperties_manifest.txt` and its ordered surface scripts from the same immutable content snapshot. Original names, inheritance, density, friction and elasticity become mounted `Surface` resources. MDL model surfaces apply to bodies and bone surfaces to hitboxes; VMT `$surfaceprop` becomes the full mounted `PhysicsSurfaceProperties` path. Engine aliases are a fallback for absent definitions. World CM and unparsed RPAK surface names retain engine defaults; render materials are not guessed to describe CM collision surfaces.

`mount://titanfall2/surfaceproperties` returns a text index. Each `<escaped-name>.surface` child is a standard `Surface`; its `<escaped-name>.json` companion retains inherited source properties, including TF2 step/impact identifiers and movement factors. These remain metadata where the engine has no matching consumer; Miles events, movement factors and Source gamematerial letters are not automatically converted into engine sound or gameplay behavior.

[Titanfall2DecalFactory](Code/Resource/Decals.cs) exposes `TryCreateDefinition`, `Spawn` and `RotationForNormal`. `titanfall2_decal <material> [size=32] [depth=8]` places a decal at the crosshair. Native decals use albedo/normal/emissive textures and color alpha, without the original layer blending, LUT or separate opacity semantics. Static BSP decals remain geometry.
### Navigation and audio
[Titanfall2DeferredNavMeshBuilder](Code/Components/Navigation/DeferredNavMeshBuilder.cs) shares navigation state across mounted maps in a scene and restores host settings when the last map leaves. On-demand generation skips the whole-map load pass and requests tagged world collision tiles around players, agents and agent targets (default radius 2048). It leaves navigation disabled if the host disabled it, unless `DEFERRED_NAVMESH=1` requests navigation for a full scene. `NAVMESH_STATIC_PROPS=1` adds prop collision and makes it resident. Link costs and path searches remain the official engine's responsibility.

Map audio reads `_snd.ent`: `ambient_generic` creates bounded point sources (default cap 12, fallback radius 1024), and `trigger_soundscape` creates spherical soundscape triggers. Playback needs `Sound.Listener`. Managed decoding supports mono/stereo and downmixes 4/6 channels to stereo; 3/5/7 channels fail with a diagnostic. The native Miles event interpreter is unavailable; `Titanfall2SoundEventPlayer` remains a warning-only compatibility component. Use mounted `.vsnd`, `Sound` or `Soundscape` instead.

<a id="en-development"></a>
## Development
Open `.sbproj` in s&box as with the other mount projects. Compilation uses the normal addon sandbox, without unsafe code, private engine reflection, allow-list extensions, P/Invoke or a bundled native bridge. Use a matching official source/runtime containing the mount registration fixes at `3e80ced851c1d02cc808dc3851bbeb745d646c2f` or later.

From this repository's root:

```powershell
$env:FACEPUNCH_ENGINE = "D:\path\to\sbox-public\game"
.\BuildMounts.bat
```

`FACEPUNCH_ENGINE` points to the folder containing `sbox.exe`. The batch file invokes SboxBuild from its parent, so this entry point requires the engine source layout. It accepts neither `--list` nor `--engine-source`. It builds every `.sbproj` and uses the tool's default `game/mounts/` output; check the target installation before running it.

To package just this mount into a separate output, run from the engine source root:

```powershell
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- build-mount --project "D:/path/to/sbox-mounts/Titanfall2/.sbproj" --out "D:/path/to/mount-packages"
```

The official pipeline compiles sandboxed C#, shaders and content into `Titanfall2/package.local.titanfall2.dll` and `Assets/`. Install that completed directory under the matching engine's `game/mounts/Titanfall2/`; the old `mount/titanfall2` path is obsolete.

`Build/`, `Tests/` and `Validation/` are optional local helpers excluded from version control. A fresh checkout uses the official entry points above. Compilation, managed tests, native lifecycle checks, real-map visual checks and fixed-camera frame-time measurements are separate validation claims. Retail-content checks require the user's own game installation.

<a id="en-configuration"></a>
## Configuration
[Titanfall2StreamingSettings](Code/Titanfall2StreamingSettings.cs) reads overrides from `FileSystem.Data/titanfall2.settings.json`, not process environment variables. Names retain the historical `SBOX_TITANFALL2_` prefix and values are JSON strings. Restart after changing overrides; numbers use invariant decimal notation and are clamped, and `1/true/yes/on` enable booleans.

```json
{
"SBOX_TITANFALL2_TEXTURE_MAX": "2048",
"SBOX_TITANFALL2_PROP_SHADOWS": "1",
"SBOX_TITANFALL2_NORTHSTAR_PROFILE": "R2Northstar"
}
```
### Player quality and override priority
| Player setting | Mount behavior | When changes apply |
| -- | -- | -- |
| Texture low / medium / high / very high | RPAK, VTF 2D and VTF cubemap source limits: 1024 / 2048 / 4096 / original. Existing mips only; no upscaling. | Saving updates the import policy. Existing textures remain until released and reloaded; retained editor caches may require remount/restart. |
| Shadow low / medium / high / very high | Directional component permits four cascades; engine quality clamps to 1 / 2 / 3 / 4 and controls resolution, distance and contact shadows. | Normal engine quality updates, without rebuilding the map. |
| AA, render scale, upscaling, native post-processing | Existing engine implementations and quality controls. | Normal engine behavior. |

Explicit JSON overrides take priority over player/engine settings. `TEXTURE_MAX` overrides texture resolution. Shadow overrides are ceilings under the engine quality ceiling. TF2 height/directional fog, godrays, particles and refractive water do not automatically follow similarly named native quality controls. If render settings are unavailable, texture policy falls back to 1024.

The following tables omit `SBOX_TITANFALL2_`. On/off values are booleans. The CPU prefetch budget is soft, not a VRAM limit; in-flight reads can exceed it and peak usage is logged. Workers stop new reads without waiting for GPU consumption, avoiding synchronous-import deadlocks.
### Cache and textures
| Setting | Default | Range | Effect |
| -- | -- | -- | -- |
| `TEXTURE_PREFETCH_MB` | 128 | 8–1024 | Retained CPU texture payload target in MiB; logs actual peak. |
| `MATERIAL_PREFETCH_WINDOW` | 4 | 1–16 | Number of materials prepared ahead; trades I/O concurrency against memory. |
| `PROCESSED_CACHE` | On | On/off | Cache map parse data, models, textures, material descriptions and prop fades; no runtime GPU objects. |
| `CACHE_MB` | 2048 | 128–8192 | Derived-cache disk budget in MiB. |
| `SHOW_WEAKPOINT_MARKERS` | Off | On/off | Show retail AR weakpoint overlay meshes on mounted models. Restart to apply; on restores the mesh, not the retail targeting rules. |
| `TEXTURE_MAX` | Player quality | 64–16384 | Maximum source dimension; missing/empty/invalid follows player quality. Explicit 512 is available for low-memory use. |
| `LIGHTMAP_16BIT` | On | On/off | Single-level SkyB BGR565 and RealTimeB BGRA4444 reduce lightmap channels from 17 to 13 B/texel. BGR565 uses R high/B low; RGB565 is not interchangeable. Mipped RealTimeB stays RGBA8888 due to lower-mip corruption with native BGRA4444. Off restores RGBA8888. |
| `LIGHTMAP_MIPS` | Off | On/off | L0–L2 for SkyA/SkyB/RealTimeB, with four coverage-dilation steps before mip generation. Uploads smallest mip first. Slightly softer chart edges and additional memory. |
### Props and submission granularity
| Setting | Default | Range | Effect |
| -- | -- | -- | -- |
| `RESIDENT_WORLD` | On | On/off | Prepare and retain prop collision while loading ordinary maps. Off restores distance streaming; pure-world/server collision always stays resident. Reimport existing scenes. |
| `PROP_RENDER_DISTANCE` | 8192 | 0–65536 | Per-instance distance culling; 0 disables it, large landmarks are exempt. |
| `PROP_AUTHORED_FADE_SCALE` | 1 | 0.25–16 | Multiplier for authored fades. |
| `PROP_SHADOWS` | On | On/off | Dynamic shadows for props of radius at least 48. |
| `PROP_SHADOW_DISTANCE` | 0 | 0–65536 | 0 disables distance gating. Native batches change shadow flags with hysteresis, at most 32 per frame. No GPU MDI route is enabled. |
| `PROP_LOD_SCALE` | 1 | 0.1–4 | Multiplies 1536/4096/8192 LOD distances; smaller values reduce distant detail. |
| `BATCH_CLUSTER` | 4 | 1/2/4/8/16 | Prop cells per batch axis; larger clusters reduce submitted objects but coarsen PVS/shadow decisions. Intermediate values snap down. |
| `WORLD_CELL_SIZE` | 2048 | 512–8192 | World geometry partition size; larger cells reduce fragments with coarser culling. |
| `WORLD_MATERIAL_TABLE` | On | On/off | Compatible opaque materials on one lightmap page share aggregate submission material while preserving fragments/bounds/ranges. Animation/cutout/other shaders retain their paths. One table world per scene. Fewer draws do not prove faster frames. |
### Lighting, audio and navigation
| Setting | Default | Range | Effect |
| -- | -- | -- | -- |
| `BSP_LIGHTMAPS` | On | On/off | Baked world lighting, prop probes and local lights; off uses native lighting for diagnosis. |
| `MAP_SHADOWS` | On | On/off | World directional CSM shadows. |
| `SHADOW_GEOMETRY` | On | On/off | Dedicated opaque depth geometry per world cell; alpha-tested materials keep their own shadow shaders. Off lets the original aggregates cast. |
| `SHADOW_CASCADES` | 4 | 1–4 | Component ceiling under player quality; forced to 1 with map shadows off. |
| `CONTACT_SHADOWS` | On | On/off | Native screen-space contact shadows. |
| `EXPOSURE_EV` | 0 | -5–5 | Exposure compensation in stops. |
| `GODRAYS` | On | On/off | Additive distance-faded cards. |
| `MAP_AUDIO` | On | On/off | Ambient point sources and soundscapes. |
| `AUDIO_VOLUME` | 0.7 | 0.05–2 | Ambient map volume. |
| `NAVMESH_ON_DEMAND` | On | On/off | Generate nearby tagged world tiles if host navigation is enabled. Off restores whole-map generation, historically around 1.5–2 minutes extra on large maps; not a timing guarantee. |
| `NAVMESH_RADIUS` | 2048 | 512–65536 | Tile-generation radius. |
| `DEFERRED_NAVMESH` | Off | On/off | Enable navigation for a full scene even if the host did not. |
| `NAVMESH_STATIC_PROPS` | Off | On/off | Include prop collision in navigation and retain it. |
### Diagnostics and Northstar sessions
| Setting | Default | Effect |
| -- | -- | -- |
| `VERBOSE_LOG` | Off | Detailed load logs, warnings and over-budget timings. |
| `NORTHSTAR_PROFILE` | Existing `R2Northstar` under game install, otherwise retail | Relative game-filesystem profile containing `mods/`, `packages/`, `enabledmods.json`. Explicit empty string disables it. Launcher source directories are not profiles. |
| `NORTHSTAR_MAP` | Unset | Fixed map context, e.g. `mp_angel_city`, for conditional RPAK/VPK content; publishes only that map's scenes. Unset `MapOnly` packages preload according to declared maps and emit diagnostics. Change in a new process. |

`CACHE_PATH` cannot select an arbitrary directory: cache stays in `FileSystem.Data/cache/titanfall2/processed`. `GPU_STATIC_SCENE`, `GPU_MIXED_PROPS`, `GPU_STATIC_SCENE_MB` and `GPU_VIEW_SLOTS` cannot enable GPU MDI in this version. Logging is implemented in [Titanfall2Log.cs](Code/Titanfall2Log.cs), cache in [ProcessedCache.cs](Code/Content/ProcessedCache.cs).

Use `titanfall2_content_stats`, `titanfall2_content_list "models/" 0 100` and `titanfall2_content_explain "models/example.mdl"`. For RPAK queries pass virtual resource names and `matl`/`txtr` types. Listing scripts does not execute them. Import statistics report phase timings, aggregates/fragments/material rows, RPAK upload deltas and prefetch peaks; these are load metrics, not frame/GPU timings.

<a id="en-technical"></a>
## Technical reference
"Retail" refers to the Steam Titanfall 2 client and its data. The following records importer behavior and format findings; it does not establish visual or performance parity.
### Content snapshots, archives and textures
`ContentDiscovery` freezes a `Titanfall2ContentSnapshot`; `ContentMount` publishes convertible resources while retaining dependencies, archive entries, loose files and logical RPAK/Miles resources in the virtual catalog. Paths, partition entities, companion files, include animations and materials use the same snapshot. Bound sources retain that snapshot and do not query a newer mount. Loose sources have SHA-256 identities and reject changed content. Container revisions use length, bounded head/tail samples and format checks; these are not strong whole-container hashes. RPAK/StarPak locations are fixed before publication; VPK validation covers directories and referenced volumes. Reads use independent streams rather than a shared position surviving unmount.

VPK strings require terminators and data ranges are bounded. Archive index `0x7FFF` selects directory-file payloads; other indices select `_NNN.vpk`. Missing volumes fail explicitly. `vpk.json` accepts `client_x`, `client_x.bsp` and full directory names. `MapOnly` is a third-party convention treated as preload for its map and supplies RPAK map context; a different selected map defers it. LZHAM uses a 2²⁰ dictionary, implemented entirely in C# under `Formats/Rpak`.

RPAK holds low mips; StarPak supplies high mips. Duplicate/out-of-range StarPak entries invalidate the index without caching the failed result. DXBC reflection maps constants/registers; variables without `D3D_SVF_USED` are treated as unused. Texture policy skips source mips above the selected limit. BC uploads stop at 4×4, and animated VTFs additionally register a full-mip atlas.
### Map construction and compiled scene data
Build order is BSP decode; CM completeness/fallback; world render geometry; collision/manifest; navigation; client environment/local lights/cubemaps/skybox; full-scene spawns/camera; particles/audio/dynamic props as applicable; static props and readiness. Dedicated servers omit rendering, environment, particles and map audio. Source XYZ coordinates are unchanged, one unit is one inch, and entity rotations use `Angles(pitch, yaw, roll)`.

Opaque geometry uses aggregates; translucent, decal, godray, water and shadow-only geometry have separate paths, with `Titanfall2WaterRenderer` for water. TSP2 v9 stores instances, probe/skin/fade/collision fields plus compiled spatial/model/skin groups and SHA-256 checks. Dense InstanceIds are separate from sparse BSP SourceIndex. v3–v8 payloads compile groups once at load. TSV2 v2 stores the location tree, portals and precomputed region masks with SHA-256; v1 builds masks at load, retaining depth/direction/always-visible/empty-mask semantics.

`GetCompiledSceneStatistics()` explicitly snapshots generation, grouping/PVS sizes, actual batch histograms, material identities and model LOD bytes. It does not run per frame, retain CPU vertices or extend native model lifetime. Material identity count is not pipeline mergeability. Particle indexing/system creation run in bounded `OnLoad` loops; `OnUpdate` activates/simulates prepared systems. Pending/failed counts are not successful readiness.

External partitions are `<map>_env.ent`, `_spawn.ent`, `_fx.ent`, `_script.ent`, `_snd.ent`. The BSP entity lump retains `worldspawn` and nonduplicated volume entities; missing environment partitions fall back to it.
### rBSP collision and lighting details
CM references use bits 0–7 for contents index, 8–28 for the 21-bit primitive index, 29–31 for type (0 brush, 2 tricoll, 3 static prop). World model 0 takes `[0, model1.first_geo_set)`; model N uses `CM_GRID_CELLS[grid_w * grid_h + N]`. Deduplication uses the full reference. Invalid primitives increment `RejectedPrimitiveCount`; brushes over 128 sides are rejected. Tricoll winding becomes engine winding with one-sided collision. All-zero contents fail loading.

| Mask/bit | Value |
| -- | -- |
| `PlayerSolid` | `0x0201400B` |
| `TitanSolid` | `0x0220400B` |
| `Shot` | `0x46404033` |
| `PlayerClip` / `MonsterClip` | `0x10000` / `0x20000` |
| `NoClimb` / `TitanClip` / `BulletClip` | `0x100000` / `0x200000` / `0x400000` |
| `NoGrapple` | `0x800` |

Trace masks split into groups of at most eight tags, cache that split and return the nearest hit. Empty masks hit no imported collision; `includeOtherObjects` also queries ordinary objects. sprp v13 solid type is at `+0x1e`, reserved at `+0x38`, remove mask at `+0x3c`. For nonzero solid type, start with MDL contents: zero or `Solid` clears bit 0 and ORs `0x00EB0280`; `Window` clears `0x00080002` and ORs `0x00E30240`; `Grate` clears `0x00480008` and ORs `0x00A30240`; finally AND with `~Remove`. Clip tags are added only to non-bullet-blocking objects. `NoClimb` → `nowallrun` remains an inference.

`WORLD_LIGHTS` v1/v2/v3 records are 100/108/112 bytes with linear HDR intensity. Lump 122 contains pages of 63 shorts. t35 packs four 6-bit light indices (63 means none) and an 8-bit page; t36 stores filterable occlusion. Page indices address the compact `flags & 8` light list. Probe IDs instead exclude `SkyAmbientType` records and are mapped to full world-light indices; invalid IDs stay `0xFFFF`. Each shading point evaluates at most four lights through a GPU buffer, without individual engine PointLights.

Prop probe coefficients are signed Q13 (1/8192). An 80-byte GPU record stores three RGB float4 coefficients, sky direction/sun visibility, flags, four byte weights and four ushort light IDs. Shading uses `max(dot(float4(n,1), c),0)` plus sky. Native probe volume face order is +X,+Y,+Z,-X,-Y,-Z. Terrain selects sun occlusion per pixel. Prop direct sunlight uses per-pixel cascade shadows where covered, blending back to baked probe visibility at cascade edges or when shadows are absent.

Environment volumes use bounds and convex planes; outside all volumes prefer an unbound `light_environment`. Scene render attributes update sun/sky around the camera. Fog volumes rebuild inline `*N` CM shapes as prioritized convex unions; use the first controller or last master, highest priority first, with `fogenable=0` clearing fog. Selection uses `Camera.View.Position`; skybox `useworldfog` shares parameters. Imported height fog binds its constants with `ImportedHeightFogBound` and writes zero fog when disabled; sprite constant binding elsewhere belongs to the corresponding engine version.
### Material semantics and display
Shaders follow retail semantics, not name guesses. Lyr weight is `saturate((ramp+1)*vertexAlpha+ramp*(mask-1))`; Lyrtrans coverage is `1-weight` times albedo alpha and opacity. Layer normal slopes decode as `xy*2.867661-1.439453` with Z=1. UV selectors follow texture-register order; `Emul` follows `Emission`. Cavity affects only specular and has neutral value 1; RGB specular textures are F0. The depth/normal prepass returns `DepthNormals::Output` before color/fog. TextureScroll applies texture scale and `(scale-1)/2` center compensation; Sine uses `time-timeoffset`, replacing only zero periods with 1.

Retail animated grass uses pixel UV distortion, not vertex sway: UV2 scrolls by `c_uv2Translate*c_gameTime`; `uv=uv1+(rg*2-1)*c_uvDistortionIntensity*(c_uv1RotScaleX.x,c_uv1RotScaleY.y)`. Typical grass UV2 scale is 0.5, translation (-0.25,0) or (-0.3,0), intensity (0.2,0). `GetDistortionUvChannel` writes `g_flT2DistortionUvChannel`; currently only cutout consumes it, falling back to UV1. Atmospheric cards fade by `saturate(d*scale+bias)` at constants 0x90/0x94 and `lerp(inner,outer,pow(1-|n·v|,exp))`; culling follows DoubleSided. Vista peak limiting defaults `c_maxLightingValue` to 1; HDR planets/sun have additional fixed limits.

`Titanfall2Tonemapping` uses an infinite PostProcessVolume at priority -100, allowing game volumes to override. Seven log-luminance levels feed `L=exp2(meanLog)`, `target=(1.03-2/(ln(L+1.0002)+2))/L*2^EV`, adapting with retail speed 0.1 and clamping to 0.5–2 per camera. Native camera auto-exposure is disabled and restored on teardown. The linear display curve is `x*(10x+0.3)/(x*(10x+0.5)+1.5)`.

Core Bloom prefilters at quarter resolution with `pow(max(rgb-cutoff,0),exponent)`, then uses horizontal/vertical 13-tap bilinear blur over a 25-pixel footprint (center weight 0.122765). Composition adds `0.5*BloomStrength` before exposure/display; defaults are strength 0.15, cutoff/exponent 1. Wide Bloom independently prefilters/downsamples to 1/16; streak uses successive horizontal radii. Wide defaults are amount 0.2, pre-exponent 2, post-exponent 0.7; streak defaults are amount 0.5, cutoff 5, pre/post exponents 1. Core post-exponent defaults to 1. These are component properties, separate from the JSON settings. This is a reconstruction, not retail pixel-parity evidence.

Color correction uses 32³ RGB tables (98,304 bytes), such as `materials/correction/sp_beacon.raw`, registered as mounted volume textures at `<source>.raw.vtex`. Scenes store paths/static definitions, not the sample bytes. Global, distance and brush weights/fades are evaluated per camera, without retail script execution. The mount retains 32³ precision instead of resampling to native 16³ grading. Missing tables disable their contribution.
### Models, batching, particles and audio details
MDL body `Base` is a packing multiplier, not a default index. VTX strip `IndexOffset` counts elements, not bytes. Bad VVD ranges/embedded VTX fail explicitly. Models retain `titanfall2_collision` source metadata; hitboxes get `titanfall2_hitgroup_<n>` and nonzero critical overrides get `titanfall2_critical_override`. Legacy embedded animations map included-model bones by name, add bindpose and import the first animation of each sequence. Independent animation children also expose the remaining blend-grid cells on demand.

Identity instance-index buffers upload once and reuse prefixes; LOD/visibility subsets use dynamic buffers. Batch teardown releases its three GPU buffers. Probe batches skip probe bindings in depth prepass, retaining them for forward/shadows. Serialized arrays validate magic/version/count against actual bytes. PVS, LOD, distance and shadows use native batches; removed `GpuSceneCompatibility`/GPU MDI cannot be re-enabled by settings. Remaining layout/budget utilities and shaders are offline material, not an active GPU scene.

PCF libraries are parsed in bounded work; systems activate near the camera. Names containing sun/star/sky/weather are global. Orientation 1 is an upright billboard, 2 is world XY. Supported paths include ropes, velocity trails and ramp-textured SpriteCards. Control points 0–63 come from `cpoint1`…`cpoint63`, with runtime updates via `SetControlPoint`. Systems honor draw distance/dormancy and warm looping effects on wake. Skybox scenes contain vista models, not particles.

BCF decoding reads its 24-byte header/packet size table, band coefficients, inverse DCT and overlap-add into int16 PCM for `SoundFile.FromPcm`. Historical comparisons recorded 94.07% bit-identical samples, all within ±2 LSB (mean 0.059), with FFmpeg as an additional reference; these are prior decoder observations, not a fresh audio acceptance result. 4/6-channel downmix uses center 0.7071, surrounds 0.5, omits LFE, and normalizes four channels by 1/√2. Historical RMS ratio was 0.999–1.000, median correlation 1.0 (minimum 0.9136); the private retail mix is not identical. The native event-session/handle APIs are no longer provided.
### Resource lifetime, cache and budgets
The official SceneLoader owns its registered SceneFile. `_cachedWorldBuild` survives destruction of the temporary build scene and is discarded by `OnSceneUnloaded`; failures roll back with `DiscardWorldBuild`/`WorldBuildResult.Dispose`. Concurrent full/world builds share a reference-counted BSP parse via `Titanfall2MapImport`, dropping failed entries. Each live scene owns its local-light buffers, native props and collision; leases protect shared CPU caches. Loader caches/cross-map resource references remain engine-owned, without private reflection, forced GC or retry-GC loops. Particle textures release mount references at shutdown while live instances retain theirs. Map components remove only their own roots/cameras and clean up partial loads.

Workers use `GameTask.RunInThreadAsync` with a separate completion signal for synchronous imports; parallel loops use public `Sandbox.Utility.Parallel`. Packed MDL value fields and public ByteStream avoid unsafe code. Graphics policy publishes immutable snapshots, subscribes/unsubscribes video-setting events at mount boundaries, and pins one policy per RPAK/VTF decode/cache key. It does not mutate visible textures after a quality change. Diagnostics' conversion/package/backend identities are not the DLL MVID; verify built artifacts using SHA-256.

The cache at `FileSystem.Data/cache/titanfall2/processed` stores `map`, `model`, `texture`, `material`, `propfade` derived CPU data, not native models/textures/scene objects. T2FC headers carry version and a 32-byte SHA-256 key; bodies are GZip with CRC/end checks. Map parsing has its own T2MP version/tag and a 384 MiB uncompressed ceiling. Keys combine `Titanfall2BuildIdentity.VersionId`, content revision/container samples and source identity. Rebuild conversion IDs when conversion rules change. Full loose hashes and bounded container samples cannot detect every same-size internal edit; restart and clear derived cache after manual package replacement. Publication uses temporary paths and process-local read/write locks; the public filesystem has no atomic rename, so failed/truncated files become misses. Recency is process-local; trim checks run every 64 writes against the disk budget.

| Phase | Budget / rule |
| -- | -- |
| Loading prewarm | Up to 512 props / 35 ms per yield, up to 4 colliders. |
| Post-load completion | Up to 16 props / 3 ms per frame. |
| Visibility | Up to 16 cells / 1.5 ms per frame. |
| Distance collision streaming | Up to 2 create/releases / 1 ms; 1536 in, 2560 out, when resident mode is disabled. |
| Render retention | 512 extra units after entering draw distance; radius ≥256 bypasses distance culling. |
| Shadow updates | Radius ≥48 eligibility; default distance gate off. When enabled, restore around half the disable distance, compare 256-unit buckets, re-evaluate after 512-unit movement/PVS-cell change, apply at most 32 flag writes per frame. |
| LOD camera | Quantized to 32 units to reduce index-buffer reshuffles. |

Stable resident scenes do not rebuild collision queues merely because the camera moves; lost owned bodies trigger recovery. Failed collision resources cannot publish readiness. Prefetch counts each unique payload once, retains the overshoot payload for main-thread consumption, returns all accounting on upload/cancel, and never waits for GPU consumption. It is a CPU-data policy, not a resolution or VRAM controller.

Historical CPU-batch measurements on Angel City recorded 2,840 render objects, 3,044 draws, 3.2M triangles, including 2,717 prop batches, with approximately 1.5 ms C# Update work. These are historical context only. Fewer draws do not establish a frame-rate gain; compare fixed camera/resolution/focus in one process and record both whole-frame and GPU time.

<a id="en-limitations"></a>
## Limitations
- World CM and RPAK materials retain default physical surfaces; fallback prop hulls/meshes are approximations. `NoClimb` to wall-running semantics is inferred.

- Gameplay, scripts, trigger behavior and networking belong to game code. General legacy models embed the first animation per sequence; independent clips expose blend-grid cells. Gameplay actors additionally export selected MDL53 role clips into AG2, with a locomotion/body selector and masked fire/reload overlays adapted from the official Source animation player. Automatic sequence blend grids, authored aim matrices, flinch autolayers and IK are not yet evaluated for TF2. The engine's standard animation resources require uniform scale: tiny retail roundoff is normalized, while genuinely nonuniform scaling fails explicitly. See `docs/UPSTREAM_AUTHORITY_2026-11-25.zh-CN.md` for the integration and validation scope.

- PCF operators/initializers and VMT proxies are partial. Runtime control-point movement requires game code; arbitrary texture transforms/entity-driven inputs are unsupported.

- Soundscapes approximate one active looping region with spherical triggers; point sources are capped, DSP is absent, native Miles sound events are unavailable, and multichannel downmix differs from the private retail mix.

- Fog/environment choose one camera per scene rather than coordinating several cameras/maps. Retail script/network fog transitions and entity exposure controls are not imported. Local grading and Bloom require map-by-map visual comparison.

- There is no cross-map GPU LRU or VRAM budget. Texture quality affects future imports and does not upgrade existing textures in place. Do not transfer old GPU MDI performance conclusions to the current native-batch backend.
<a id="en-references"></a>
## References and acknowledgements

The links below record the projects consulted during development and the role of each reference. The architecture research snapshots were read on 2026-11-06; their fixed commit links preserve the versions examined. Runtime integration continues to use the official s&box mount APIs.

### Official foundation

| Project / documentation | Reference scope |
| --- | --- |
| [Facepunch/sbox-public](https://github.com/Facepunch/sbox-public) | Mount APIs, resource loaders, scene construction, addon compilation, rendering and resource lifecycle. See also the [Game Mounts documentation](https://sbox.game/dev/doc/game-mounts/). |
| [Facepunch/sbox-mounts](https://github.com/Facepunch/sbox-mounts), particularly [SourceEngine](https://github.com/Facepunch/sbox-mounts/tree/main/SourceEngine) | Official mount project layout, resource registration, Source-format conversion patterns and scene-resource ownership. |

### File formats, codecs and Source behavior

| Project | Reference scope and source links |
| --- | --- |
| [r-ex/LegionPlus](https://github.com/r-ex/LegionPlus) | RPAK texture-format mapping cross-checks; see the TXTR-to-DXGI table in [Legion/rtech.h](https://github.com/r-ex/LegionPlus/blob/main/Legion/rtech.h). |
| [snake-biscuits/bsp_tool](https://github.com/snake-biscuits/bsp_tool) | Public Respawn rBSP layout definitions used alongside retail-data checks during collision/inline-brush auditing; see [titanfall2.py](https://github.com/snake-biscuits/bsp_tool/blob/master/bsp_tool/branches/respawn/titanfall2.py). |
| [MRVN-Radiant/MRVN-Radiant](https://github.com/MRVN-Radiant/MRVN-Radiant) | Respawn Source map editing and BSP generation reference, covering Titanfall 2 and related games. Its compiler/editor work provides context for map structures; coverage varies by game and lump. |
| [snake-biscuits/bsp_regen](https://github.com/snake-biscuits/bsp_regen) | Titanfall BSP conversion to Titanfall / Titanfall 2, including physics needed for navigation-data generation. Its scope is BSP conversion, with other asset formats handled separately. |
| [r-ex/RePak](https://github.com/r-ex/RePak) | Asset-to-RPak compilation for Titanfall 2 / Northstar and Apex Legends / R5Reloaded; a reference for package construction and asset layout from the writer side. |
| [r-ex/rsx](https://github.com/r-ex/rsx) | reSource Xtractor: RPak asset parsing/conversion and Titanfall 2 model reading, providing a counterpart to the mount's resource readers. |
| [Mauler125/r5sdk](https://github.com/Mauler125/r5sdk) | Apex Legends development-package source used as a related Respawn Source implementation reference. Apex-specific structures and runtime behavior require separate verification against Titanfall 2. |
| [R2Northstar/NorthstarLauncher](https://github.com/R2Northstar/NorthstarLauncher) | Read-only interpretation of Northstar mod profiles, content priority and conditional package configuration. |
| [richgel999/lzham_alpha](https://github.com/richgel999/lzham_alpha) | Source lineage for the managed LZHAM Alpha8 decoder, including entropy coding, prefix tables and checksums; see [lzham_lzdecomp.cpp](https://github.com/richgel999/lzham_alpha/blob/master/lzhamdecomp/lzham_lzdecomp.cpp). Alpha streams differ from LZHAM 1.x streams. |
| [FFmpeg/FFmpeg](https://github.com/FFmpeg/FFmpeg) | Bink Audio decoding reference and historical comparison tool. The managed decoder identifies the band-frequency and zero-run tables taken from [wma_freqs.c](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/wma_freqs.c) and [binkaudio.c](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/binkaudio.c) in its source comments. |
| [ValveSoftware/source-sdk-2013](https://github.com/ValveSoftware/source-sdk-2013) | Shared Source color-correction falloff/volume semantics and SpriteCard parameter behavior; see [c_colorcorrection.cpp](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/client/c_colorcorrection.cpp), [colorcorrectionvolume.cpp](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/server/colorcorrectionvolume.cpp) and [spritecard.cpp](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/materialsystem/stdshaders/spritecard.cpp). TF2-specific behavior still requires its own validation. |

### Mount architecture research

These projects informed the earlier reusable mount-framework design. The research covered source structure and design approaches; it did not compile or benchmark the projects, or establish their runtime performance for Titanfall 2.

| Project and examined snapshot | Design topic |
| --- | --- |
| [chrisspieler/sbox-gamemaker-mount — 0a576db](https://github.com/chrisspieler/sbox-gamemaker-mount/tree/0a576db0feff7055c198165014ebf0cc08646ecd) | Shared engine-family mounting with per-game configuration and logical archive resources. |
| [chrisspieler/sbox-overgrowth-mount — f4f2b7e](https://github.com/chrisspieler/sbox-overgrowth-mount/tree/f4f2b7e3d9f1834f1c519b9d1717e647b22b9f07) | Separation of installation discovery, resource descriptions and engine registration. |
| [atrblizzard/vtmb-sbox-mounter — 4cd3888](https://github.com/atrblizzard/vtmb-sbox-mounter/tree/4cd3888079eb8224f67333850b70cb7f2784e5da) | Deterministic patch/loose-file precedence and unified virtual-path reads. |
| [cmg-divined/gta5-mount-sbox — ab5ff79](https://github.com/cmg-divined/gta5-mount-sbox/tree/ab5ff79cc8b19ce48a04c37ad59c85035e691e98) | Archive dependency indexing, hash-addressed assets and texture-parent relationships. |
| [chrisspieler/sbox-mount-formats — ce1908d](https://github.com/chrisspieler/sbox-mount-formats/tree/ce1908d637611c7e4a210d896e3d528ed690dbe0) | Separation of reusable format/decoder utilities from the mount lifecycle. |

---
<a id="zh"></a>
## 中文
`Titanfall2Mount`（[GameMount.cs](Code/GameMount.cs)）以 `titanfall2` 为标识，将Steam Titanfall 2（AppId `1237970`）转换为 s&box 资源。安装中必须同时存在 `r2/paks/Win64` 与 `vpk`。资源按需读取和转换，无需预先解包。

[代码结构](#zh-layout) · [挂载范围与使用](#zh-scope) · [开发与安装](#zh-development) · [配置](#zh-configuration) · [技术参考](#zh-technical) · [已知限制](#zh-limitations) · [参考项目](#zh-references)

<a id="zh-layout"></a>
## 代码结构
| 目录 | 内容 |
|--|--|
| [GameMount.cs](Code/GameMount.cs)、[Content/](Code/Content) | 安装检测、固定内容快照、资源注册与卸载。 |
| [Formats/](Code/Formats) | 容器和格式解析（部分纹理解码文件也包含主线程创建入口）：`Bsp`、`Mdl`、`Miles`、`Pcf`、`Rpak`、`Scripts`、`Vmt`、`Vpk`、`Vtf`。VPK 的 LZHAM 压缩块和 Miles 的 Bink Audio 流都由这里的纯 C# 解码器处理。 |
| [Resource/](Code/Resource) | 模型、材质、纹理、声音加载器及转换工具。 |
| [Resource/Map/](Code/Resource/Map) | 地图加载、解析缓存与导入；几何、实体、光照构建分别归入 `Geometry`、`Entities`、`Lighting`。 |
| [Components/](Code/Components) | 地图、静态道具、粒子（`ParticleStreamer` / `ParticleFactory`）、音频、导航、光照、渲染和后处理运行时组件。 |
| [Gameplay/](Code/Gameplay) | 模块化玩法子系统：按 `Catalog/`（数据驱动目录与预制体构建器）、`Pilot/`（`Parkour` / `Combat` / `Tacticals`）、`Weapons/`（`Data` / `Mechanics` / `Runtime` / `Ui`）、`Projectiles/`、`Npc/`、`Titans/`（`Chassis` / `Kits` / `Cores` / `Defense` / `Cockpit`）、`FrontierDefense/` 与 `Shared/`（`Combat` / `Audio` / `Animation` / `Prewarm`）划分；支持 `scripts/titanfall2_sbox/*.json` 与 `FileSystem.Data/titanfall2/data/*.json` 可选 JSON 数据覆写。 |
| [Collision/](Code/Collision) | contents 掩码、来源元数据、射线查询和诊断。 |
| [Rendering/](Code/Rendering) | 世界聚合、实例批次、局部光缓冲及材质表布局。 |
| [MountedScenePerfKit.cs](Code/Rendering/MountedScenePerfKit.cs) | 通用世界聚合、实例批次与剔除工具，命名空间为 `Sandbox.Mounting.PerfKit`。 |
| [Titanfall2StreamingSettings.cs](Code/Titanfall2StreamingSettings.cs) | 从 `FileSystem.Data/titanfall2.settings.json` 读取配置。 |
| [Titanfall2GraphicsSettings.cs](Code/Titanfall2GraphicsSettings.cs) | 玩家纹理质量与导入策略。 |

着色器在 `Assets/shaders/` 下，文件名以 `titanfall2_` 开头。

<a id="zh-scope"></a>
## 挂载的内容
内容快照固定同名资源的优先级：启用的 Northstar Mod loose 覆盖优先，然后按 Mod 顺序读取 RPAK/VPK，再读取原版 RPAK/VPK。候选来源可通过诊断命令查询；细节见下方技术参考。

| 来源 | 注册为 | 说明 |
|--|--|--|
| `r2/paks/Win64/*.rpak` 和 StarPak | 纹理（`<name>.vtex`）、材质 | RPAK 的 TXTR/MATL 资源，高分辨率 mip 从 StarPak 读取。补丁包优先，然后是 `common.rpak`。 |
| `vpk/*_dir.vpk` | VMT 材质、VTF 纹理、模型、WAV 声音、场景 | `englishclient_` 目录映射到 `client_` 数据包，`depot/` 下的镜像条目被隐藏。 |
| MDL v53（含内嵌 VTX/VVD/VPHY） | `models/<x>.mdl.vmdl` | 默认 bodygroup 取每个 body part 的模型 0。带有附着点、hitbox set、皮肤（`Skin N`），以及模型本身和 include 模型中的动画序列。另注册静态实例模型，包含批次使用的前四档几何，其余 LOD 槽位复用最深的可用档。 |
| `r2/sound/general.mbnk` | `sound/<name>.vsnd` | Miles 银行中的 BCF 声音，由挂载内置的纯 C# Bink Audio 2 解码器解码为 PCM。 |
| PCF | 地图粒子系统、`particles/<escaped-name>.prefab` | 独立 prefab 与地图实体复用同一套 TF2 转换器。`mount://titanfall2/particles` 提供名字与路径索引。 |
| rBSP 地图 | `maps/<map>.bsp.scene`、`maps/<map>.bsp.world.scene` | 见下一节。 |
| rBSP 内联模型 | `maps/<map>.bsp/brush/<index>.vmdl` | 门、移动平台等 `*N` 模型。 |

`skies/skycube.vmat` 是挂载生成的天空材质，它把零售版的天空立方体贴图包装成原生天空着色器。
## 地图加载
每张地图注册两个场景，都由 [Resource/Map/Map.cs](Code/Resource/Map/Map.cs) 中的 `MapLoader` 构建：

- `mount://titanfall2/maps/<map>.bsp.scene` 是完整场景。它包含世界几何、碰撞、环境、静态道具、粒子、地图音频、出生点、动态道具和一个旁观相机，可以直接打开运行。

- `mount://titanfall2/maps/<map>.bsp.world.scene` 是纯世界场景。它不创建出生点、相机和动态道具，不强制开启导航，并把所有静态道具碰撞常驻在物理世界中。它适合加载进游戏自己的场景。

在自己的场景里使用地图时，添加 `Titanfall2Map` 组件（[Components/Map/Titanfall2Map.cs](Code/Components/Map/Titanfall2Map.cs)），用法与 Hammer 地图的 `MapInstance` 相同：

- `MapName` 是地图名，例如 `mp_angel_city`。末尾的 `.bsp`、`.bsp.scene` 会被去掉。

- `PureWorld` 选择加载 `.world.scene`。

- `SpawnSpectatorCamera` 在场景没有主相机时，在第一个出生点上方创建旁观相机。纯世界模式不创建相机。

- `LoadInEditor` 让编辑器视图也加载地图。默认关闭，因为大地图要预加载数万个静态道具。

地图对象作为不保存的子对象加入场景，组件只销毁它自己创建的对象。`Titanfall2Map.GetAvailableMaps()` 返回当前挂载中所有可用的地图名。

纯世界场景加载完成后，`worldspawn` 对象上的 `Titanfall2WorldReady` 组件会在以下条件都满足时添加标签 `ttf2_map_ready`：世界碰撞体已进入物理世界，所有内容分组都有物理体，常驻道具碰撞全部创建完成。任一条件失效时标签会被移除。游戏代码应在看到这个标签后再放置玩家。
## 武器、NPC 与泰坦

挂载成功后可以通过 `GameObject.GetPrefab( uri )?.Clone( position )` 生成下列实体。路径均以 `mount://titanfall2/entities/` 开头：

| 相对路径 | 实体 ID |
| --- | --- |
| `weapons/mp_weapon_<id>.prefab` | `rspn101`、`car`、`r97`、`vinson`、`g2`、`semipistol`、`autopistol`、`alternator_smg`、`hemlok_smg`、`lmg`、`esaw`、`dmr`、`rspn101_og`、`wingman`、`shotgun`、`hemlok`、`smart_pistol` |
| `weapons/mp_weapon_<id>.prefab`（投射物） | `epg`、`lstar`、`sniper`、`smr`、`doubletake`、`wingman_n`、`softball`、`pulse_lmg`、`mastiff`、`shotgun_pistol` |
| `weapons/mp_weapon_<id>.prefab`（反泰坦） | `defender`、`mgl`、`rocket_launcher`、`arc_launcher` |
| `weapons/mp_weapon_<id>.prefab`（投掷物） | `frag_grenade`、`grenade_emp`、`thermite_grenade`、`grenade_gravity`、`grenade_electric_smoke`、`satchel` |
| `items/pilot_jump_kit.prefab` | 可穿戴铁驭跃升背包（`Titanfall2PilotParkour` / `Titanfall2PilotCombat` / `Titanfall2PilotTactical`：二段跳、跑墙蹬墙、滑铲跳、攀爬、铁驭近战、副手投掷物、兴奋剂、钩爪、相位转移、脉冲刀） |
| `items/pilot_tactical_{stim,grapple,phase,pulse}.prefab` | 穿戴跃升背包后按 E 拾取，替换唯一技能槽，T 使用。初始为空槽，同类拾取不会刷新充能；血量旁显示原版技能图标。资源依据与适配差异见 [铁御技能研究](docs/PILOT_TACTICAL_RESEARCH_2026-11-28.zh-CN.md)。 |
| `npcs/enemy/<id>.prefab` | 15 种敌方单位（`[Enemy]` 前缀）：`npc_soldier`、`npc_spectre`、`npc_stalker`、`npc_imc_heavy`、`npc_reaper`、`npc_plasma_turret`、`npc_attack_drone`、`npc_prowler`、`npc_imc_smg`、`npc_imc_shotgun`、`npc_imc_captain`、`npc_stalker_red`、`npc_imc_pilot`、`npc_corporate_spectre`、`npc_mossy_stalker` |
| `npcs/ally/<id>.prefab` | 15 种友方单位（`[Ally]` 前缀）：`npc_militia_grunt`、`npc_militia_heavy`、`npc_militia_smg`、`npc_militia_shotgun`、`npc_militia_pilot`、`npc_militia_spectre`、`npc_militia_stalker`，以及死神/炮塔/无人机/猎兽/护盾队长/红潜行者/企业幽灵/苔藓潜行者的 `*_ally` 队友变体 |
| `npcs/neutral/<id>.prefab` | 2 种中立单位（`[Neutral]` 前缀）：`npc_marvin`、`npc_mossy_marvin` |
| `titans/{ally,enemy}/<id>.prefab`（可驾驶） | 8 台可驾驶泰坦均拆为 `[Ally]`（`ally/`）与 `[Enemy]`（`enemy/`）两份：`monarch`（帝王）、`bt7274`（BT）、`legion`（军团）、`ion`（离子）、`tone`（强力）、`scorch`（烈焰）、`northstar`（北极星）、`ronin`（浪人） |
| `titans/enemy/<id>.prefab`（FD 敌方 AI） | `arc_titan`、`nuke_titan`、`elite_monarch`、`mortar_titan`；仅敌方且无驾驶座 |
| `titanweapons/mp_titanweapon_<id>.prefab` | `xo16_shorty`、`xo16_vanguard`、`predator_cannon`、`particle_accelerator`、`leadwall`、`sniper`、`sticky_40mm`、`meteor`（标签 `titan-weapon`，主机调用 `Titanfall2TitanArmament.EquipTo(actor)` 装备） |

当前目录包含 37 件铁驭武器／投掷物、1 件铁驭跃升背包、32 种普通 NPC（15 友方 / 15 敌方 / 2 中立）、20 种泰坦（8 友方可驾驶 / 8 敌方可驾驶 / 4 FD 敌方 AI）与 8 件泰坦武装。铁驭武器读取零售 PC 多人 `WeaponData`（含 `#base`、`MP_BASE` 与 `Mods`），支持充能步枪蓄力、L-STAR 连续热量与过热散热、Devotion 渐进射速、智能手枪自动锁定、Hemlok 三连发与 EVA-8/Double Take/Mastiff/Mozambique 弹丸分布。独立项目需配置 `PlayerController` 和启用拾取及控制的 `BaseInventoryComponent`。第一人称使用原版 POV 骨架与骨骼合并的铁驭手臂，支持可用的 ADS、冲刺、换弹和动画事件；按原版 bodygroup 保留机瞄等部件。枪口、抛壳、曳光、尾迹与弹痕读取挂载的原版素材，枪声按已核对的武器音源族、视角和首发／循环／收尾选择，不使用跨武器模糊匹配。Miles 原生事件混音仍未恢复，详见[战斗系统说明](docs/COMBAT_SYSTEMS.zh-CN.md)。备用弹仍无限。

智能手枪目前锁定准星附近、无遮挡的敌对 NPC，持续约 0.6 秒后辅助命中；开镜时使用手动瞄准，尚无专用锁定 HUD。遥控炸药主射击投掷并黏附碰撞表面，副射击仅引爆当前武器投出的炸药，未引爆炸药在 45 秒后清理。电烟雷碰撞后生成 5 秒烟雾和受掩体阻挡的电击区域。

投射物在主机上消耗弹药并逐步飞行，读取零售参数中的速度、重力和溅射；薄障碍物由扫掠碰撞检测，掩体能阻挡爆炸伤害。Double Take 发射横向三枚弹体，L-STAR 采用连续热量累积与过热散热循环。

NPC 继承引擎 `BaseCombatNpc`。普通地面单位使用官方 `SensesLayer`、`NavigationLayer` 和任务调度器；`Titanfall2EngageSchedule` 在同一任务中并行处理移动和内置武器射击。参考 Northstar 的 `behavior_soldier`、`behavior_spectre`、`behavior_pilot_elite` 和 AI settings，增加低弹量换弹找掩体、近距离后撤、射击位置调整、队友位置通报和丢失目标后搜索。搜索使用最后目击／通报位置，默认 6 秒后忘记，不跟踪遮挡后的实时位置；友军挡住枪线时暂停射击。步兵、持续推进单位和铁驭分别配置反应及点射节奏。战术点要求完整 NavMesh 路径，并避开队友正在使用的位置；每次规划最多检查 12 个点、计算 3 条路径。无目标时复用官方待命、调查和跟随，友军步兵使用键切换跟随。新增企业幽灵、苔藓潜行者、民兵幽灵、民兵潜行者和苔藓 MRVN（均为零售模型／阵营变体）。死神、潜行者、幽灵、持枪步兵和 AI 泰坦可在近距离切换到独立近战；前摇后重新扫掠命中、可躲避且受墙体阻挡，不消耗弹药。死神优先播放 `sspec_attack_groundslam`，潜行者优先播放 `st_stand_melee_swipe`；近战姿势优先于步态及装填。数值和命中时序属于本地实现，未宣称逐帧还原原作。Reaper 使用内置投射物，并通过现有 PCF 转换器加载脚本指定的 `P_plasma_proj_MD_DLight`、附带飞行光源和命中放电；冷启动时按帧准备粒子目录，准备期间保留发光弹迹。固定炮塔、悬停无人机和 FD 特殊单位保留专用逻辑；Prowler 近战，MRVN 中立。移动需要可用 NavMesh。`Faction`、`Friendly` 和 `AttackPlayers` 控制阵营及玩家敌对；序列化 `Friendly=false` 不会覆盖 wildlife/neutral 等自定义阵营。

移动动画分别保留步行／跑步的前进、后退、左移和右移，优先选择 NPC 持枪或对应机体的循环。带方向混合的序列按参数零值选择中间格，而不是固定取第一格（如 `Run_forward_mp` 的三格范围是 −90°～+90°）。R2 frame movement 的轨道偏移以整个 24 字节结构起点为基准，不能沿用骨骼轨道的指针组基准；否则会多偏移 16 字节、跳过前几帧并在结尾读入相邻数据。模型的 `titanfall2_actor_animation` 数据记录实际使用的源片段和原版移动速度；运行时按身体局部移动方向选片段、按实际水平速度调整步频，换方向保留步态相位。导航控制世界位移，移动片段不重复应用水平根骨平移。尚未完整复现 Respawn 的方向混合图、移动开火叠加层、转身／起停过渡、脚部 IK、跑墙或原版 C++ 行为选择器。

官方 Q 菜单的 `PreviewModel` 使用独立 `.preview.vmdl`：NPC 只解码待机的一帧，再把姿势烘焙进静态顶点；武器使用校正朝向的默认身体部件。预览没有骨架、动作库、碰撞或额外皮肤组，`PreviewSequence` 留空。CPU 网格缓存命中后连原始 MDL 和关联动画文件都不读取；缓存沿用内容指纹校验与损坏回退。材质通过 `/preview.vmat` 子资源绑定 `/preview.vtex` 贴图子资源，最高使用 512 像素的原版 mip（仍遵守更低的用户画质上限），与游戏材质／贴图资源分别缓存。生成角色仍使用 `.infantry.vmdl`，实际武器仍使用 `.held.vmdl`／原版 POV；预览不会替换它们。首轮未缓存的转换仍有开销，官方缩略图队列继续负责可见项排队及会话内成图缓存。

泰坦有大型碰撞体、可恢复护盾和机载武器的对泰坦伤害值。空座时由 AI 操作；本地玩家或监听主机可用 **使用键** 开始短暂登乘连接，走远或被遮挡时取消。入舱后显示原生中文 HUD，包含生命、护盾、弹药、目标、技能冷却和核心充能。移动键和鼠标驾驶，主射击开火、换弹键装填、**副射击** 攻击技能、**蹲下** 防御、**疾跑** 冲刺，**F／手电筒 action** 激活核心。离舱会检查完整玩家体积、路径和地面；出口受阻时保留驾驶，死亡或删除机体前解绑驾驶员并恢复库存。`Titanfall2TitanKit` 接入七种基础技能，`Titanfall2TitanCore` 接入君主、离子、军团、浪人、强力和烈焰六种核心；BT 使用君主路线。强力需对同一目标累计三次 40mm 命中才能发射追踪火箭。北极星飞行核心、远程客户端驾驶与完整原版技能／动画状态机仍未实现。详细用法及简化范围见 [战斗系统说明](docs/COMBAT_SYSTEMS.zh-CN.md)。

玩家、NPC 和投射物共用命中特效入口：读取零售版 impact table 中的粒子／声音，以原生火花、烟尘、闪光和程序生成的弹孔／焦痕兜底。贴花依附命中物体并向表面内部投射；每个场景最多保留 128 张弹痕、64 组瞬时命中特效。`entities/titanweapons/mp_titanweapon_<id>.prefab` 是独立的 `titan-weapon` 武装预制体；主机调用 `Titanfall2TitanArmament.EquipTo(actor)` 安装，替换模型并重新装填，不占用铁驭背包。命中特效未完整复刻 RPAK 粒子和原版弹痕美术。

游戏实体的第三人称动画按原版 MDL 活动与骨架选取。BT 的战斗待机为 1001 帧循环；有对应动作时，静止交火、装填、近战和死亡分别播放全身序列，移动时优先保留步态。持枪模型若与角色有共同的枪根骨骼（如 `ja_c_propGun`），按武器参考姿势对齐该动画骨骼的旋转与位置。只放置 `.vmdl` 不会启动 NPC 行为；请使用 gameplay prefab。第一人称动画图、移动中上半身分层与双手 IK 不在此实现内。

宿主可以不加载预制体就按 `weapon`、`npc`、`titan` 或 `frontier-defense` 标签枚举。Sandbox 的官方 Q → 武器／NPC → Titanfall 2 入口展示这些实体；菜单属于游戏项目，而非挂载 DLL。

```csharp
var mount = Sandbox.Mounting.Directory.Get( "titanfall2" );
var assets = mount.GetAll( Sandbox.Mounting.ResourceType.PrefabFile );
var titans = assets.Where( asset => asset.Tags.Contains( "titan" ) );
```

## 渲染
以下功能在默认设置（`SBOX_TITANFALL2_BSP_LIGHTMAPS=1`）下启用。

- **光照贴图和探针**：世界几何使用 BSP 中烘焙的 HDR 光照贴图。静态道具使用按道具索引存储的光照探针。

- **局部光源**：`WORLD_LIGHTS` 中的点光源和聚光灯通过实时光照页表在着色器中计算，每个像素最多 4 盏，不为每盏灯创建引擎光源。

- **太阳和天空**：方向光来自 `light_environment`。地图有多个 `light_environment_volume` 时，`Titanfall2EnvironmentController` 按相机位置切换太阳和天空颜色。

- **雾**：`env_fog_controller` 和 `fog_volume` 生成高度雾和方向雾，雾体积按相机位置选择。

- **3D 天空盒**：远景（vista）模型放进独立的天空盒世界，从地图的 `sky_camera` 观察。

- **水面**：按材质着色器语义识别，使用帧缓冲拷贝渲染。

- **贴花**：BSP 贴花几何单独渲染，不投射阴影。

- **显示变换**：`Titanfall2Tonemapping` 实现零售版的自动曝光和显示曲线，替代引擎的自动曝光。颜色校正保留全局、距离衰减及 brush volume 的静态定义和相机淡化，使用 32³ RGB 挂载体纹理；场景保存定义和资源路径。

- **泛光**：在曝光和显示曲线之前合成。

世界和静态道具默认开启方向光阴影，屏幕空间接触阴影也默认开启。半径小于 48 的静态道具不投射阴影。`SBOX_TITANFALL2_MAP_SHADOWS=0` 和 `SBOX_TITANFALL2_PROP_SHADOWS=0` 可分别关闭世界和道具阴影。Godray 光束面片默认显示，可用 `SBOX_TITANFALL2_GODRAYS=0` 关闭；它们随相机距离淡入，移动时可能出现闪烁。
## 静态道具
`Titanfall2StaticPropStreamer`（[Components/StaticProps/StaticPropStreamer.cs](Code/Components/StaticProps/StaticPropStreamer.cs)）在加载画面期间预加载地图中的全部静态道具，之后它们一直保留到场景销毁。

- **实例化**：相同模型的道具按 4×4 个 512 单位的单元分组，合并成一个实例批次绘制。

- **LOD**：静态实例模型解码批次使用的前四档 MDL/VTX LOD，模型其余 LOD 槽位复用最深的可用档。实例批次按每个实例到相机的距离选级（1536 / 4096 / 8192 单位切到 LOD1 / 2 / 3），按级分组绘制。

- **PVS**：沿 rBSP 的 cell/portal 图从相机所在单元向外遍历（最多 16 跳），隐藏不可见单元中的道具。没有有效单元的道具始终可见。

- **淡出距离**：每个道具使用 sprp 或 MDL 中设定的淡出距离，可用 `SBOX_TITANFALL2_PROP_AUTHORED_FADE_SCALE` 整体缩放。

- **全局距离**：超过 `SBOX_TITANFALL2_PROP_RENDER_DISTANCE`（默认 8192）的道具不再绘制。半径不小于 256 的大型道具不受这一限制。
## 碰撞
世界碰撞以 rBSP 的 CM 数据（brush 和 tricoll）为准：

- 碰撞几何按原始 contents 值分组，每组生成一个静态 `ModelCollider`。

- 不阻挡玩家（不含 `PLAYERSOLID` 位）的分组设为触发器，例如子弹阻挡和泰坦阻挡。

- CM 数据缺失或有被拒绝的图元时，客户端退回到经过过滤的渲染几何碰撞，并输出警告。专用服务器要求完整的 CM 碰撞，否则加载失败。

静态道具碰撞来自模型的 VPHY 凸包。没有 VPHY 时使用 hitbox；两者都没有时使用渲染网格，超过 8192 个三角形的网格用一个包围凸包代替。道具的有效 contents 由 sprp 的 solid 字段、MDL contents 和 remove 掩码计算，结果为 0 的道具没有碰撞。普通场景默认也常驻道具碰撞（`RESIDENT_WORLD=1`）；设为 0 时才按玩家距离启用：1536 单位内创建，超过 2560 单位释放。纯世界场景和专用服务器始终保留全部道具碰撞。

导入的碰撞对象带有以下标签：

- `ttf2_contents`

- 每个置位的 contents 位对应一个 `ttf2_content_<bit>`

- 可读标签 `playerclip`、`titanclip`、`nograpple`、`nowallrun`

用原始掩码做射线检测时，调用 `Titanfall2CollisionQueries.Run( trace, Titanfall2TraceMask.Shot )`（[Collision/CollisionQueries.cs](Code/Collision/CollisionQueries.cs)）。`Titanfall2Contents` 和 `Titanfall2TraceMask` 枚举（[Collision/CollisionContents.cs](Code/Collision/CollisionContents.cs)）使用零售版脚本中的常量值。

控制台命令 `titanfall2_collision_look [距离=512] [半径=0]` 从当前相机发出射线或球体扫描，以 JSON 输出命中物体的碰撞来源、contents、形状和标签。它只读取，不修改碰撞。
### 独立资源

原有 `models/<name>.mdl.vmdl` 保持兼容。新增 `models/<name>.mdl.visual.vmdl` 只构建渲染几何、骨架和绑定姿态，不嵌入序列库和碰撞。两种模型路径下都可以按需读取以下子资源；模型的 `titanfall2_resources` 数据也提供关联路径。

| 子路径 | 引擎类型 | 用途 |
| --- | --- | --- |
| `skeleton.skel` | `Sandbox.Animation.Skeleton` | 只读取 MDL53 参考骨架，不创建渲染网格。 |
| `animations.json` | 文本 | 查询片段路径、include 来源、活动、标志和混合网格。 |
| `animations/<escaped-name>.anim` | `Sandbox.Animation.Clip` | 用 TF2 解码器按需转换一个片段，处理叠加动画的缩放差值。 |
| `physics.prefab` | `PrefabFile` | 独立克隆模型碰撞，内部使用 `PhysicsCollider`。 |

例如：`mount://titanfall2/models/humans/pilots/pilot_light_ged_m.mdl.visual.vmdl/skeleton.skel`。片段名字使用 URI 转义，建议直接采用 `animations.json` 给出的路径。混合网格中的各单元作为独立片段提供；这不等于实现了 TF2 的序列分层、IK、事件或游戏动画图。宿主可用标准骨架和片段构建自己的动画图。物理 prefab 沿用 VPHY、hitbox、渲染近似的回退顺序，不自动套用 sprp 实例 contents，也不自动添加 Rigidbody。每个克隆通过 `Titanfall2PhysicsResource` 绑定自己的运行时 `PhysicsCollider`，以 `Prepared` 报告结果；prefab 保存模型来源、皮肤和 body，避免引擎匿名物理资源重名导致串用。

`mount://titanfall2/particles` 返回粒子名字和 prefab 路径的文本索引；`mount://titanfall2/particles/<escaped-name>.prefab` 可作为普通 `PrefabFile` 消费。首次主动读取粒子资源时才索引 PCF，挂载注册不预先解析。每个克隆实例重建地图粒子使用的 TF2 控制器，独立持有运行时子对象，以 `Titanfall2ParticleResource.Prepared` 报告准备结果；通过根对象的 `Titanfall2ParticleControlPoints` 更新端点。它仍依赖已挂载的游戏和当前支持的 PCF 算子。

世界 CM 和渲染回退碰撞已改用独立 `PhysicsGroupDescription` 与 `PhysicsCollider`，保留原始 contents 分组、查询标签及原生物理体就绪检查；原有序列化 `ModelCollider` 的就绪引用仍可读取。

### 表面语义
挂载优先读取同一内容快照中的原版 `scripts/surfaceproperties_manifest.txt` 及其按序声明的表面脚本，将原始名字、继承关系、密度、摩擦与弹性转换为引擎 `Surface`（[Resource/Surface.cs](Code/Resource/Surface.cs)）：

- **MDL**：模型级 surfaceprop 用于物理体，骨骼级 surfaceprop 用于 hitbox。这是静态道具和大多数模型碰撞的表面来源。

- **VMT**：`$surfaceprop` 写入渲染材质的 `PhysicsSurfaceProperties` 属性。

- **rBSP 世界和 RPAK MATL**：CM 碰撞数据没有材质索引，零售 MATL 的表面名在资产头的字符串指针里、尚未解析，因此世界碰撞和 RPAK 材质保持引擎默认表面。

- 原始定义不存在时才使用引擎别名表；未知名字回退到 `default`，每个名字只提示一次。材质和 hitbox 使用完整挂载路径，避免与其他游戏的同名表面冲突。

`mount://titanfall2/surfaceproperties` 提供文本索引，`<escaped-name>.surface` 子资源是标准 `Surface`；对应的 `<escaped-name>.json` 保留继承后的全部原始属性，包括 TF2 脚步/撞击标识、移动系数等。没有对应引擎消费者的字段仅作为元数据提供，不自动转换 Miles 事件、移动行为或 Source gamematerial 字母。rBSP CM 和未解析的 RPAK 表面仍保留默认表面，不用渲染材质猜测 CM 表面。
## 运行时贴花
`Titanfall2DecalFactory`（[Resource/Decals.cs](Code/Resource/Decals.cs)）把挂载的泰坦陨落 2 贴花材质解包成原生 `Decal` 需要的贴图，供游戏代码做弹孔、焦痕等动态效果：

- `Titanfall2DecalFactory.TryCreateDefinition( materialName, out definition, out error )` 接受材质名（可带 `materials/` 前缀或 `.vmt`），返回 `DecalDefinition`。

- `Titanfall2DecalFactory.Spawn( parent, materialName, position, rotation, size, depth )` 直接创建贴花对象。

- `Titanfall2DecalFactory.RotationForNormal( normal )` 生成贴花朝向。

- 调试命令：`titanfall2_decal <材质名> [大小=32] [深度=8]`，在相机准星命中处生成贴花。

原生 `Decal` 只吃颜色/法线/自发光贴图，因此泰坦陨落 2 材质的层混合、颜色 LUT 和独立不透明度贴图不会还原；遮挡遮罩取颜色贴图的 alpha。静态地图贴花仍走上面的 BSP 几何路径。
## 导航
`Titanfall2DeferredNavMeshBuilder`（[Components/Navigation/DeferredNavMeshBuilder.cs](Code/Components/Navigation/DeferredNavMeshBuilder.cs)）接管挂载地图的导航网格生成。

默认的按需模式下：

- 挂载跳过加载时的整图生成。

- 只使用带 `titanfall2_navmesh` 标签的世界碰撞作为输入。

- 玩家、NavMesh 代理和代理目标周围 `SBOX_TITANFALL2_NAVMESH_RADIUS`（默认 2048）单位内的 tile 在需要时才生成。

宿主场景没有开启导航时，挂载不会修改它。`SBOX_TITANFALL2_DEFERRED_NAVMESH=1` 让完整场景主动开启导航。`SBOX_TITANFALL2_NAVMESH_STATIC_PROPS=1` 把静态道具碰撞也加入导航输入，同时让道具碰撞常驻。同一场景中的多张挂载地图共享导航状态，最后一张地图移除时恢复宿主的设置。
## 处理缓存
派生数据写入 `FileSystem.Data/cache/titanfall2/processed`，预算由配置项 `SBOX_TITANFALL2_CACHE_MB` 控制。缓存包括地图解析数据、模型、贴图、材质描述和道具淡出距离；原生 Model、Texture、SceneObject 不序列化到缓存。

缓存键使用转换格式 ID 和内容快照。loose 覆盖采用完整 SHA-256；大型容器采用长度和头尾各最多 4 KiB 的采样，因为官方只读文件系统没有公开 mtime。它不能检测所有同长度的内部修改；手动替换包后应重启并清除派生缓存。修改转换规则时须更新 `Titanfall2BuildIdentity.VersionId`。

写入通过官方数据文件系统发布，同进程读写互斥，读取校验格式、GZip CRC 和结尾。公开 API 没有原子重命名；中断或失败的文件按未命中重新生成。最近使用顺序仅在当前进程中记录。

<a id="zh-development"></a>
## 开发与安装
与仓库中的其他挂载一样，在 s&box 编辑器中打开 `.sbproj`。项目按正常 addon 编译，遵守官方沙箱允许列表，禁用 unsafe；LZHAM 和 Bink Audio 使用纯 C#，没有随包原生桥接 DLL。

需要支持沙箱 addon 挂载的官方引擎及匹配的运行库。引擎应包含 `3e80ced851c1d02cc808dc3851bbeb745d646c2f` 或后续的挂载类型注册修复。挂载类型由官方菜单/游戏类型库注册，并在类型库重建后恢复，无需本地引擎补丁。

从挂载仓库根目录构建全部项目：

```powershell
$env:FACEPUNCH_ENGINE = "D:\path\to\sbox-public\game"
.\BuildMounts.bat
```

`FACEPUNCH_ENGINE` 指向含 `sbox.exe` 的 `game` 目录；现有批处理从其父目录调用 `engine/Tools/SboxBuild/SboxBuild.csproj`，因此该入口需要完整引擎源码布局。它不支持 `--list` 或 `--engine-source` 参数。

只构建本挂载时，从引擎源码根目录运行官方工具，路径替换为本机位置：

```powershell
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- build-mount --project "D:/path/to/sbox-mounts/Titanfall2/.sbproj" --out "D:/path/to/mount-packages"
```

官方流程执行沙箱 C#、shader 和内容编译，输出 `Titanfall2/package.local.titanfall2.dll` 与 `Assets/`。自定义输出目录中的成品可安装到匹配引擎的 `game/mounts/Titanfall2/`；旧 `mount/titanfall2` 路径不适用。`BuildMounts.bat` 使用工具默认的游戏 `mounts/` 输出，运行前确认目标安装。

`Build/`、`Tests/`、`Validation/` 是可选的本地辅助目录，已从版本控制排除；全新检出应使用上述官方构建入口，不依赖这些目录。编译成功、托管测试、原生生命周期、实机地图画面和固定机位性能测试是不同的验证结果，不能互相替代。零售地图验证需要用户自己的完整游戏安装。
## 官方引擎接入
安装内容通过 `InitializeContext.GetFileSystem(1237970)` 返回的只读文件系统读取。缓存和设置通过 `FileSystem.Data`。Northstar profile 只能位于同一游戏文件系统内，配置值使用相对路径。

普通道具使用现有 `MountedInstanceBatch`，特殊材质仍使用原生对象，保留 LOD、PVS、距离和阴影策略。旧 GPU MDI 提交与 `GpuSceneCompatibility` 已移除，`GPU_STATIC_SCENE` 无法启用；保留的纯布局工具和 shader 不代表运行时使用 MDI。帧率影响需要固定机位实测。

地图生成资源跟随官方 `SceneLoader.OnSceneUnloaded()` 释放，场景实例各自释放局部光缓冲、渲染对象和碰撞。普通 loader 缓存由官方引擎管理；不反射清空缓存或强制 GC，因此不能保证与旧版相同的切图回收时机。3D 天空盒使用公开 `Skybox3D`，动画采样由官方 renderer 调度。

组件和控制台命令交给正常 addon 加载流程注册。无需引擎修改、私有接口反射、允许列表扩展或旁路程序集。

<a id="zh-configuration"></a>
## 配置
挂载后端与诊断覆盖集中在 [Titanfall2StreamingSettings.cs](Code/Titanfall2StreamingSettings.cs)，玩家画质适配在 [Titanfall2GraphicsSettings.cs](Code/Titanfall2GraphicsSettings.cs)。内容配置在首次挂载时读取；日志和缓存目录分别见 [Titanfall2Log.cs](Code/Titanfall2Log.cs) 和 [Content/ProcessedCache.cs](Code/Content/ProcessedCache.cs)。
### 玩家画质设置
正常游玩直接使用游戏的画质菜单，无需设置覆盖项：

| 玩家设置 | TF2 挂载行为 | 修改后生效时机 |
| -- | -- | -- |
| 纹理低 / 中 / 高 / 极高 | RPAK、VTF 2D 与 VTF cubemap 的单张源纹理上限为 1024 / 2048 / 4096 / 原始尺寸；只有现有 mip，不放大源图 | 保存设置后更新导入策略；已有贴图保持不变，完全离开地图、待资源释放后重新加载。编辑器保留资源缓存时需重新挂载或重启编辑器 |
| 阴影低 / 中 / 高 / 极高 | 地图方向光默认允许 4 级级联，引擎质量配置实际限制为 1 / 2 / 3 / 4 级，并控制分辨率、距离和接触阴影 | 引擎按其画质设置即时应用，不必重建地图 |
| 抗锯齿、渲染比例、超分、原生后处理 | 使用引擎已有实现与质量配置 | 按引擎设置的生效方式 |

JSON 单项配置覆盖优先于玩家设置 / 引擎质量配置。纹理可通过 `TEXTURE_MAX` 显式覆盖。阴影覆盖是组件上限，仍受引擎全局质量上限裁剪。

TF2 高度/方向雾、godray 面片和粒子不是引擎体积雾，TF2 自定义折射水面也不使用原生水面质量实现；这次没有把这些内容错误绑定到同名的菜单选项。烘焙光照、曝光标定、碰撞、导航、GPU/CPU 后端、缓存预算及内容优先级保持各自职责。专用服务器或不兼容引擎无法取得画质对象时，纹理回退为 1024，显式覆盖仍有效。
### 怎么设置
新官方沙箱版本从 `FileSystem.Data/titanfall2.settings.json` 读取覆盖项，不再读取进程环境变量。保留原 `SBOX_TITANFALL2_*` 名称，值使用 JSON 字符串。例如：

```json
{
"SBOX_TITANFALL2_TEXTURE_MAX": "2048",
"SBOX_TITANFALL2_PROP_SHADOWS": "1",
"SBOX_TITANFALL2_NORTHSTAR_PROFILE": "R2Northstar"
}
```

配置在首次使用时读取，修改后重启。数值按英文小数点解析并夹到范围；开关的 `1/true/yes/on` 表示开。玩家画质仍通过公开 `Application.RenderSettings` 读取；菜单/编辑器没有该对象时使用回退。

`GPU_STATIC_SCENE`、`GPU_MIXED_PROPS`、`GPU_STATIC_SCENE_MB` 和 `GPU_VIEW_SLOTS` 在此版本不启用 GPU MDI。`CACHE_PATH` 不再支持任意目录；固定使用 `FileSystem.Data/cache/titanfall2/processed`。`NORTHSTAR_PROFILE` 是游戏文件系统内的相对路径。

下表省略了 `SBOX_TITANFALL2_` 前缀。
### 缓存与贴图
纹理预取增加两个设置，仍通过上述 `FileSystem.Data/titanfall2.settings.json` 配置：

| 名称（省略前缀） | 默认值 | 范围 | 作用 |
| -- | -- | -- | -- |
| `TEXTURE_PREFETCH_MB` | 128 | 8–1024 | 保留的 CPU 纹理数据目标预算；已经在执行的读取可以跨过目标，实际峰值会写日志 |
| `MATERIAL_PREFETCH_WINDOW` | 4 | 1–16 | 提前准备的材质任务数；增加可能提高 I/O 并发，也会提高内存峰值 |

预算耗尽时不继续开始新的纹理读取，也不阻塞工作线程等待 GPU 消费，避免同步地图导入时死锁。纹理创建仍由主线程完成。`Titanfall 2 import statistics` 日志包含导入阶段耗时、世界 aggregate/fragment/材质行数量、RPAK 上传增量及预取峰值；这些是加载统计，不是 FPS 或 GPU 耗时。

| 变量 | 默认 | 范围 | 作用 |
| -- | -- | -- | -- |
| `PROCESSED_CACHE` | 开 | 开/关 | 缓存地图解析、模型、贴图、材质描述和道具淡出距离；不保存运行时 GPU 资源 |
| `CACHE_MB` | 2048 | 128–8192 | 处理缓存的磁盘预算（MB） |
| `SHOW_WEAKPOINT_MARKERS` | 关闭 | 开/关 | 显示挂载模型的 AR 弱点图块。重启生效；开启恢复原网格，不模拟瞄准条件，不改变伤害。 |
| `TEXTURE_MAX` | 跟随玩家纹理设置 | 64–16384 | 显式覆盖贴图最大边长；未设置、空值或非法数值时跟随玩家设置。低显存可显式设 512 |
| `LIGHTMAP_16BIT` | 开 | 开/关 | 默认单级光照页使用 SkyB（BGR565）与 RealTimeB（BGRA4444）：光照片 17→13 B/px。SkyB 使用原生上传验证过的 BGR565（R 高位、B 低位），不能替换为会返回有效但数据失真的 RGB565。多级 mip 下 RealTimeB 使用 RGBA8888，避开原生 BGRA4444 下级 mip 失真。设 0 全部回到 RGBA8888 |
| `LIGHTMAP_MIPS` | 关 | 开/关 | 给 SkyA / SkyB / RealTimeB 生成 L0–L2 mip 链，生成前先按几何覆盖率把图集 chart 向外 dilate 4 步，避免 mip 把 chart 外的空洞混进来。按原生 API 要求以最小 mip 在前上传；RealTimeB 多级页使用 RGBA8888。代价是远景 chart 边缘略糊和额外显存占用 |
### 静态 prop
| 变量 | 默认 | 范围 | 作用 |
| -- | -- | -- | -- |
| `RESIDENT_WORLD` | 开 | 开/关 | 普通地图在场景加载期间完成静态道具碰撞准备并常驻，减少移动时创建/释放碰撞体；增加加载时间与内存。设 0 恢复普通地图的距离流式碰撞。纯世界和专用服务器始终常驻。此设置写入构建出的场景，旧场景需重新导入 |
| `PROP_RENDER_DISTANCE` | 8192 | 0–65536 | 逐实例距离剔除半径，大型地标不受影响。0 关闭距离剔除 |
| `PROP_AUTHORED_FADE_SCALE` | 1 | 0.25–16 | 乘到地图为每个 prop 设定的淡出距离上 |
| `PROP_SHADOWS` | 开 | 开/关 | 让半径不小于 48 的静态 prop 投射动态阴影 |
### 帧率与提交粒度
以下独立配置控制每帧提交的渲染对象数量、阴影和深度通道的三角形数量。减少提交工作时应逐项调整并测量实际帧时间。

| 变量 | 默认 | 范围 | 作用 |
| -- | -- | -- | -- |
| `PROP_SHADOW_DISTANCE` | 0 | 0–65536 | 道具方向光阴影距离。0 关闭距离门控，保留原批次的投影资格。CPU 参照路径按批次门控，每帧最多改 32 次标志；当前官方沙箱版本不启用 GPU MDI 路径 |
| `PROP_LOD_SCALE` | 1 | 0.1–4 | 乘到静态 prop 的 LOD 切换距离（1536/4096/8192）上。小于 1 更早切到低模，减少前向与深度通道三角形 |
| `BATCH_CLUSTER` | 4 | 1/2/4/8/16 | 多少个 prop cell（512 单位）合成一个实例批次。越大提交对象越少，但 PVS 和阴影按批次聚合，粒度变粗 |
| `WORLD_CELL_SIZE` | 2048 | 512–8192 | 世界几何的空间 cell 边长。越大 fragment/draw 越少，视锥剔除越粗 |
| `WORLD_MATERIAL_TABLE` | 开 | 开/关 | 世界材质表：同一 lightmap 页的兼容不透明材质共享原生 aggregate 提交材质，保留原空间片段、边界和三角形范围。动画、cutout 及其他 shader 走原路径；一个 scene 只支持一个使用材质表的世界。可减少 CPU 提交，材质间接读取可能增加 GPU 成本，不能按 draw 降幅推算 FPS 收益。设为 0 恢复原路径 |

提交预算见本页技术参考的"流式加载预算"。比较性能时使用同一进程、固定机位、同一相机，并记录 `draws`/`triangles`/`objects`。
### 光照与画面
| 变量 | 默认 | 范围 | 作用 |
| -- | -- | -- | -- |
| `BSP_LIGHTMAPS` | 开 | 开/关 | 使用地图烘焙的 lightmap、光照探针和本地光。关掉后改用 s&box 原生光照，只用于排查不支持的地图 |
| `MAP_SHADOWS` | 开 | 开/关 | 让导入的世界几何参与方向光级联阴影（CSM） |
| `SHADOW_GEOMETRY` | 开 | 开/关 | 按世界 cell 创建专用不透明阴影几何；alpha-tested 材质保留自己的阴影 shader。设 0 让原 aggregate 投影 |
| `SHADOW_CASCADES` | 4（由引擎按玩家画质裁剪） | 1–4 | 方向光组件的级联上限。`MAP_SHADOWS=0` 时强制为 1。显式设置仍不能超过引擎全局质量允许的级联数 |
| `CONTACT_SHADOWS` | 开 | 开/关 | s&box 屏幕空间接触阴影 |
| `EXPOSURE_EV` | 0 | -5–5 | 在零售曝光标定上再加的曝光补偿（档） |
| `GODRAYS` | 开 | 开/关 | 显示 godray 光束面片。它们随相机距离淡入，移动时可能闪烁；设为 0 可关闭 |
### 声音
| 变量 | 默认 | 范围 | 作用 |
| -- | -- | -- | -- |
| `MAP_AUDIO` | 开 | 开/关 | 地图环境音发声点和 soundscape |
| `AUDIO_VOLUME` | 0.7 | 0.05–2 | 地图环境音音量 |
### 导航网格
| 变量 | 默认 | 范围 | 作用 |
| -- | -- | -- | -- |
| `NAVMESH_ON_DEMAND` | 开 | 开/关 | 宿主场景已开启 NavMesh 时，跳过加载阶段的整图生成，只用带标签的世界碰撞，按需生成玩家和 NavMeshAgent 附近的 tile。关掉后恢复引擎的整图生成，在 angel_city 这类大图上要多等约 1.5–2 分钟 |
| `NAVMESH_RADIUS` | 2048 | 512–65536 | 按需生成的半径 |
| `DEFERRED_NAVMESH` | 关 | 开/关 | 宿主没有开启 NavMesh 时，由挂载为完整地图场景开启，并在加载后生成 |
| `NAVMESH_STATIC_PROPS` | 关 | 开/关 | 静态 prop 的碰撞也参与导航网格 |
### 诊断
| 变量 | 默认 | 范围 | 作用 |
| -- | -- | -- | -- |
| `VERBOSE_LOG` | 关 | 开/关 | 输出详细加载日志、全部警告和超出预算的耗时记录（spike） |
### Northstar 内容会话
| 变量 | 默认 | 作用 |
| -- | -- | -- |
| `NORTHSTAR_PROFILE` | 原游戏安装下存在的 `R2Northstar`，否则仅原版 | Northstar 配置根，包含 `mods/`、`packages/`、`enabledmods.json`。显式空字符串禁用。不是启动器源码目录。 |
| `NORTHSTAR_MAP` | 未指定 | 固定会话地图名，例如 `mp_angel_city`。启用匹配的条件 RPAK/VPK；指定后仅发布该地图场景，避免地图专属覆盖泄漏到其他地图。未指定时，`vpk.json` 声明为 `MapOnly` 的包仍按其声明地图预载（与 `Preload` 等效）并留下诊断。切换需要新进程。 |

例如在上述 JSON 中设置 `"SBOX_TITANFALL2_NORTHSTAR_PROFILE": "R2Northstar"`。该路径相对 Titanfall 2 安装文件系统；启动器源码目录不作为 Mod profile。

诊断命令：`titanfall2_content_stats`；`titanfall2_content_list "models/" 0 100`；`titanfall2_content_explain "models/example.mdl"`。查询 RPAK 材质时使用虚拟资源名并传 `matl`，纹理传 `txtr`。原始目录与引擎目录分离；列出脚本不意味着执行脚本。

<a id="zh-technical"></a>
## 技术参考
文中"零售版"指 Steam Titanfall 2 客户端及资源。以下格式结论、预算和生命周期规则描述挂载实现，不代表完整视觉或性能验收。
### 分层和线程
代码分三层，依赖方向只能从上到下：

1. **Formats**（[Formats/](Code/Formats)）：容器与二进制格式读取。大部分解析输出普通 .NET 数据；当前 RPAK/VTF 纹理文件仍同时包含引擎纹理创建方法，调用方必须区分 CPU 读取与主线程创建入口。

2. **Resource / Components**（[Resource/](Code/Resource)、[Components/](Code/Components)）：资源加载器与运行时组件分目录维护，把解析结果转换成 `Texture`、`Material`、`Model`、`SoundFile` 和场景对象。

3. **场景**：`MapLoader.BuildScene` 把组件装配成可序列化的场景。

线程规则：

- 后台线程只读取文件、解压和准备 CPU 数据。

- `Texture`、`Material`、`Mesh`、`Model` 和场景对象只在主线程创建。

- 材质预取（[Resource/MaterialPrefetch.cs](Code/Resource/MaterialPrefetch.cs)）最多同时准备 4 个材质，以 128 MiB 为已准备但未消费数据的目标预算。所有保留的 payload 都记账，包括跨过目标的那一份；达到目标后停止后续贴图预取，未预取的绑定仍由普通加载器加载。已经开始的解码允许暂时超出目标，不把这个值当作硬内存上限。峰值、目标与释放后的剩余字节分别记录。

挂载经过官方项目编译器改写，回调接口由生成器加入，组件和 `[ConCmd]` 由正常 addon 加载流程注册。源代码不手工声明内部回调接口，不自行反射注册 TypeLibrary 或命令程序集。
### 内容上下文、资源注册和归档
挂载先通过 `Code/Content/ContentDiscovery.cs` 建立 `Titanfall2ContentSnapshot`，再由 `Code/Content/ContentMount.cs` 生成并注册资源目录。运行期间不重新扫描包或 Mod。全部 VPK 成员、loose 文件、没有独立 loader 的依赖以及 RPAK/Miles 逻辑资源保留在虚拟目录；引擎目录只发布可转换资产。

同一路径按固定层顺序 first-wins，候选来源保留用于 explain。基础顺序为全部启用 Northstar Mod 的 loose 覆盖（LoadPriority 降序；同级按目录标识 ordinal 排序），再按同样 Mod 顺序处理 RPAK → VPK，然后 retail RPAK → retail VPK → 生成天空材质 → Miles。loose 优先于所有 VPK，符合 Northstar 文件替换 hook。旧原版路径保持兼容。

路径、BSP 分区、模型伴随文件、动画 include 和材质源从同一个固定快照读取。bound source 持有创建它的快照，不回查当前 mount。loose 文件有 SHA-256 身份；源内容改变后旧读取失败。包以长度、头尾采样和既有包格式校验管理版本，不能声称它等同于强内容哈希。RPAK/STARPAK 位置与版本在发布前固定；VPK 检查目录及实际引用的分卷。读取时使用独立文件流，没有跨卸载持有的共享 Stream.Position。
#### VPK
- 目录树和数据区间都做边界检查，字符串必须有明确的终止符。

- 数据来源由条目的归档索引决定：`0x7FFF` 表示目录文件自身，其他值表示 `_NNN.vpk` 分卷。外部分卷读取失败时报错，不返回空数据。

- 本地化目录 `englishclient_*.vpk` 的数据分卷映射到 `client_*.vpk`。

- `vpk.json` 的 `MapOnly`（第三方约定，Northstar main 只读 `Preload`）与 `Preload` 一样预载对应 VPK：未指定 `NORTHSTAR_MAP` 时按其声明地图载入并留下诊断，选中其他地图时仍推迟。`Preload`/`MapOnly` 的键容忍 `client_x`、`client_x.bsp` 与完整目录文件名三种写法。
- 未指定地图时，仅因 `MapOnly` 声明地图才选中的 RPAK 及其 Postload 依赖链排在全局 Mod／零售资源之后；保留独有资源供自定义地图浏览，避免其同名材质或相同 GUID 资源污染零售地图。显式选择该 `NORTHSTAR_MAP` 后恢复正常 Mod 覆盖优先级。无条件 Preload、由全局包触发的 Postload 保持原优先级；现有解析仍把全部零售包作为已加载依赖，不代表完整模拟 Northstar 的逐地图运行时加载过程。此规则不改变 VPK 或 loose 文件的优先级。

- 压缩块使用 LZHAM，字典大小为 2^20。解压由 [Formats/Rpak/](Code/Formats/Rpak) 中的纯 C# 移植负责（`LzhamDecoder` → `LzhamDecompressor` → 符号解码、前缀码、码长生成和校验），不再有原生桥或随包 DLL。

- `depot/` 路径下的镜像条目不注册。
#### RPAK 和 StarPak
- 纹理的低分辨率 mip 在 RPAK 中，高分辨率 mip 在 StarPak 中。

- StarPak 索引要求每个条目都位于数据区内。出现重复或越界条目时整个索引作废，并且不缓存这次结果。

- 着色器使用 DXBC 反射得到常量缓冲区和纹理寄存器布局。反射数据中没有 `D3D_SVF_USED` 标志的变量视为未使用。
#### 纹理
- 纹理边长上限由 `SBOX_TITANFALL2_TEXTURE_MAX` 决定，超出的高分辨率 mip 不读取。

- BC 压缩纹理只上传到 4×4 的 mip 级别，更小的级别不上传（[Resource/TextureMipUpload.cs](Code/Resource/TextureMipUpload.cs)）。

- VTF 动画纹理额外注册为一张图集（`animatedVtfAtlas`），图集带完整 mip 链。
### 地图构建流程
`MapLoader.BuildScene` 的步骤：

1. 读取 BSP，解析世界几何、CM 碰撞、光照贴图和静态道具光照探针。专用服务器不解析光照。

2. 判断 CM 碰撞是否完整：不为空，且没有被拒绝的图元。专用服务器在不完整时抛出异常。客户端在不完整时，从渲染几何构建备用碰撞。

3. 构建世界渲染几何：

- 不透明几何聚合成少量大网格。

- 半透明、贴花、godray、水面和只投射阴影的几何各自生成 `ModelRenderer`，水面使用 `Titanfall2WaterRenderer`。

4. 创建碰撞：

- 按 contents 分组的 CM 碰撞体，配合 `Titanfall2CollisionManifest` 使用。

- 或者使用备用碰撞。

5. 添加 `Titanfall2DeferredNavMeshBuilder`。

6. 非专用服务器上继续创建：

- 环境：太阳、雾、显示后处理、颜色校正

- 局部光源控制器

- 立方体贴图探针

- 3D 天空盒

7. 完整场景创建出生点和 `Titanfall2MapCamera`。纯世界场景不创建。

8. 创建粒子、地图音频（专用服务器除外）和动态道具（纯世界场景除外）。

9. 创建 `Titanfall2StaticPropStreamer`。普通地图默认也使用常驻碰撞；纯世界与专用服务器始终常驻。所有常驻模式添加 `Titanfall2WorldReady`，验证真实物理资源后才发布就绪标签。
#### 编译式静态场景数据
新导入使用 TSP2 v9，保留原有实例、探针、skin、淡出和碰撞来源字段，附加编译好的空间分区、模型/skin 分组及 SHA256 校验。布局用稠密 InstanceId 引用实例；它与可能稀疏或缺失的 BSP SourceIndex 分开。实际场景直接恢复分组，旧 v3–v8 载荷在加载时编译一次。

TSV2 v2 保存定位树、门户与预计算的逐区域可达 mask，并带 SHA256 校验。运行期间只做区域定位和查表；旧 v1 在加载时编译可达表。深度限制、单向门户、AlwaysVisibleCells 与空 mask 的可见策略保持原语义。

`GetCompiledSceneStatistics()` 显式生成统计快照，包含场景代际、实例/模型/skin/空间分区数量、PVS 表大小、真实批次直方图、材质身份数和每模型 LOD 几何字节数。统计不在逐帧执行，也不保留 CPU 顶点数组或延长 native Model 生命周期。材质身份数不等于最终可合并的 GPU pipeline 组数。

粒子库索引和地图系统创建在组件 OnLoad 的分段预算内完成，OnUpdate 只处理已有系统的激活与模拟恢复。`ResourcesPrepared`、`PendingSystemCount` 和 `FailedSystemCount` 提供准备结果；坏载荷或缺失资源不会被记录为完整准备成功。

当前官方沙箱版本使用原生实例批次。世界材质表独立保留；GPU MDI 运行时提交已移除，特殊材质保留原生绘制。

坐标直接沿用 Source 约定：XYZ 不变换，1 单位 = 1 英寸。实体角度按 `new Angles( pitch, yaw, roll )` 转换为旋转。
#### 实体来源
零售版把大部分实体放在 BSP 之外的分区文件中：

| 分区文件 | 内容 |
|--|--|
| `<map>_env.ent` | 环境实体 |
| `<map>_spawn.ent` | 出生点 |
| `<map>_fx.ent` | 粒子 |
| `<map>_script.ent` | 脚本实体 |
| `<map>_snd.ent` | 声音 |

BSP 自身的 ENTITIES lump 很小，但总是包含 `worldspawn`，以及分区文件中没有重复的体积实体（例如 `fog_volume`）。环境分区缺失时，退回到 ENTITIES lump。
#### 就绪信号
`Titanfall2WorldReady`（[Components/Map/WorldReady.cs](Code/Components/Map/WorldReady.cs)）在 `worldspawn` 上维护标签 `ttf2_map_ready`：

- 标签在 `OnLoad` 和 `OnEnabled` 时清除。

- 发布条件：

- 道具流式组件已完成常驻碰撞预加载。

- 世界碰撞体在物理世界中有带形状的物理体。

- 物理世界中道具碰撞体的数量等于预期数量。

- 碰撞清单中每个内容分组都有物理体。

- 发布后每帧确认缓存的物理体仍然有效，条件失效时立即撤销标签。

- 遍历物理世界的检查最多每 0.25 秒执行一次，碰撞清单检查也在这个节流之后执行。

- 道具数据损坏时，常驻预加载不会完成，标签也不会发布。
### rBSP 格式要点
#### CM 碰撞
代码在 [Formats/Bsp/Titanfall2BspCollisionReader.cs](Code/Formats/Bsp/Titanfall2BspCollisionReader.cs)。

- 每个碰撞图元引用是 32 位：

- 位 0–7：contents 表索引

- 位 8–28：21 位图元索引

- 位 29–31：类型（0 = brush，2 = tricoll，3 = 静态道具）

- 模型 0（世界）使用几何集合的前缀 `[0, model1.first_geo_set)`。模型 N 使用 `CM_GRID_CELLS[grid_w * grid_h + N]`。

- 去重以完整的原始引用为键。

- 损坏的图元会被拒绝并计入 `RejectedPrimitiveCount`。

- 边数超过 128 的 brush 视为损坏。

- tricoll 的三角形绕序会转换为 s&box 的约定。碰撞是单面的。

- 如果所有分组的 contents 都为 0，地图加载失败（`InvalidDataException`），不会生成一个没有固体的世界。
#### contents 和检测掩码
`Titanfall2Contents` 和 `Titanfall2TraceMask`（[Collision/CollisionContents.cs](Code/Collision/CollisionContents.cs)）的值取自零售版 client.dll 的脚本常量。常用值：

| 名称 | 值 |
|--|--|
| `PlayerSolid` 掩码 | `0x0201400B` |
| `TitanSolid` 掩码 | `0x0220400B` |
| `Shot` 掩码 | `0x46404033` |
| `PlayerClip` | `0x10000` |
| `MonsterClip` | `0x20000` |
| `NoClimb` | `0x100000` |
| `TitanClip` | `0x200000` |
| `BulletClip` | `0x400000` |
| `NoGrapple` | `0x800` |

每个碰撞对象的 `ModelCollider` 在 contents 与 `PlayerSolid` 掩码没有交集时设为触发器。这样普通 s&box 移动只被玩家固体阻挡，其他掩码通过 `Titanfall2CollisionQueries.Run` 查询。

`Titanfall2CollisionQueries.Run` 的实现细节：

- `PhysicsTrace` 的"任一标签"查询最多使用 8 个标签，因此掩码的置位被拆成每组 8 个标签，每组单独检测，取最近的命中。

- 拆分结果按掩码缓存。

- 空掩码不命中任何导入的碰撞。

- `includeOtherObjects` 为真时，还会检测场景中的普通物体。
#### 静态道具 contents
sprp v13 中与碰撞有关的字段：

| 偏移 | 字段 |
|--|--|
| `+0x1e` | solid 类型 |
| `+0x38` | Reserved |
| `+0x3c` | Remove 掩码 |

solid 类型不为 0 时，有效 contents 由 `Titanfall2CollisionSource.StaticPropContents` 按零售版 engine.dll 的初始化逻辑计算，起点是 MDL contents：

1. 为 0 或含 `Solid` 位时：清除位 0，OR `0x00EB0280`。

2. 含 `Window` 位时：清除 `0x00080002`，OR `0x00E30240`。

3. 含 `Grate` 位时：清除 `0x00480008`，OR `0x00A30240`。

4. 最后与 `~Remove` 相与。

结果为 0 的道具不创建碰撞。

可读标签由 contents 推导：

- `playerclip` 和 `titanclip` 只在对象不阻挡子弹时添加。固体道具展开后带有全部 clip 位，不应被当作 clip。

- `nograpple` 对应 `NoGrapple` 位。

- `nowallrun` 对应 `NoClimb` 位。这一对应是推断，没有对照零售版墙跑代码确认。
#### 光照贴图和局部光源
- `WORLD_LIGHTS` 有 v1、v2、v3 三种记录，大小分别为 100、108、112 字节。强度是线性 HDR 值，可以直接用于着色，不需要按 LDR 限幅。

- 实时光照页表在 lump 122 中，每页 63 个 `short`。

- 光照贴图的 t35 通道每个像素存 4 个 6 位光源索引和一个 8 位页号，索引 63 表示没有光源。t36 存储可双线性过滤的遮挡值。

- 页表中的索引指向 `flags & 8` 的光源组成的紧凑列表，不是 `WORLD_LIGHTS` 的原始下标（[Formats/Bsp/Titanfall2WorldLight.cs](Code/Formats/Bsp/Titanfall2WorldLight.cs)）。

- 地形按像素选择太阳遮挡通道。静态道具从光照探针获取太阳可见性。

- 探针中的灯光编号不计入 `SkyAmbientType` 记录。BSP 解析时先把共享探针 lump 中的编号映射到完整 `WORLD_LIGHTS` 下标，再供静态道具和动态实体采样；这与实时光照贴图页表的紧凑列表是两套映射。无效编号保持为 `0xFFFF`，不猜测附近灯光。

- 每个着色点最多计算 4 盏局部光源。光源数据通过 `Titanfall2LocalLightController` 上传为 GPU 缓冲区，不创建引擎的 `PointLight`。
#### 静态道具光照探针
- 系数是带符号的 Q13 定点数（乘以 1/8192），按静态道具索引查找。

- 每个探针上传为 80 字节的 GPU 记录：3 个 `float4` RGB 系数、天空方向和太阳可见性、标志位、4 个字节权重和 4 个 `ushort` 光源 ID。

- 着色公式是 `max( dot( float4( n, 1 ), c ), 0 )` 加天空项。

- 原生探针体积的面顺序是 +X、+Y、+Z、-X、-Y、-Z。
#### 光照环境
- `light_environment_volume` 由局部包围盒和一组凸平面定义。

- 不在任何体积内的位置，优先使用没有绑定体积的 `light_environment`。

- `Titanfall2EnvironmentController` 跟随当前相机，通过场景 RenderAttributes 更新太阳和天空颜色。
#### 雾
- 导入的高度雾常量通过场景渲染属性绑定，并设置 `ImportedHeightFogBound`。`Titanfall2HeightFog` 禁用时写入全零雾参数，标记仍表示缓冲区已绑定。引擎的 `SpriteBatchSceneObject` 的常量绑定由对应引擎版本负责；不能仅凭挂载侧标记断言其他场景的 sprite 缓冲区已绑定。

- `fog_volume` 的 `*N` 模型由 CM 数据重建为带优先级的凸体并集（[Formats/Bsp/Titanfall2BspFogVolumes.cs](Code/Formats/Bsp/Titanfall2BspFogVolumes.cs)）。

- 默认雾控制器取第一个 `env_fog_controller`，存在 `master` 控制器时取最后一个 `master`。

- 雾体积按优先级从高到低检查，`fogenable=0` 的体积清除雾。

- 选择雾时使用 `Camera.View.Position`。

- 3D 天空盒在 `useworldfog` 为真时使用同一份雾参数。
#### PVS
- rBSP 包含 CellBSP 树和由 portal 连接的可见性单元。挂载最多支持 128 个单元（用 128 位掩码表示）。

- 道具可见性从相机所在单元沿 portal 遍历，最多 16 跳。

- 没有有效单元掩码的道具始终可见；批次中任一成员可见时，整个批次可见。
### 材质和着色器
着色器在 `Assets/shaders/` 下。

材质着色器按零售版着色器语义选择，不按材质名猜测，例如水面按语义选择，不看名字中是否有 "water"。
#### 层混合
- 层混合（Lyr）权重：`saturate( (ramp + 1) * vertexAlpha + ramp * (mask - 1) )`。

- 半透明层（Lyrtrans）覆盖率：`1 - weight`，再乘以 albedo alpha 和 opacity。

- 层法线的斜率解码：`xy * 2.867661 - 1.439453`，Z 取 1。
#### 纹理槽位
- UV 选择字符串按绑定纹理寄存器的顺序对应。

- `Emul` 紧跟在 `Emission` 之后。

- `EmissiveMultiply` 和 `Cavity` 是两个独立语义。`Cavity` 只乘在镜面项上，中性值为 1。

- RPAK 的 RGB 镜面贴图直接作为 F0 使用。
#### 渲染通道和 VMT 代理
- 深度/法线预通道在计算颜色和雾之前返回 `DepthNormals::Output`。

- VMT `TextureScroll` 代理应用 `texturescale`，并用 `(scale - 1) / 2` 的偏移补偿缩放中心。

- `Sine` 代理使用 `time - timeoffset`，只把周期 0 改成 1。
#### UV 失真（草的摆动）
零售版的草（`models/foliage/grass_*_animated`）不移动顶点，顶点程序只做普通变换。摆动来自像素程序中的 `Uvd1` UV 失真：

- `uvDistortionTexture` 按它自己的 UV 选择符采样。草使用 UV2，`Uv2at` 让它按 `c_uv2Translate * c_gameTime` 滚动。

- 偏移量：`uv = uv1 + (rg * 2 - 1) * c_uvDistortionIntensity * (c_uv1RotScaleX.x, c_uv1RotScaleY.y)`，所有贴图都用这个 UV 采样。

- 草的材质参数：UV2 缩放 0.5，平移 (-0.25, 0) 或 (-0.3, 0)，强度 (0.2, 0)。

`Titanfall2MaterialDescriptor.GetDistortionUvChannel` 从着色器集名称解析失真贴图的选择符，写入 `g_flT2DistortionUvChannel`。目前只有 `titanfall2_cutout` 读取它，选择符无法解析时仍在 UV1 上采样失真贴图。
#### 大气卡片
大气卡片材质的淡出：

- 距离淡出：`saturate( d * scale + bias )`，`scale` 和 `bias` 位于材质常量的 `0x90` 和 `0x94`。

- 视角淡出：`lerp( inner, outer, pow( 1 - |n·v|, exp ) )`。

- 背面剔除跟随材质的 `DoubleSided`。
#### 远景（vista）
- 远景着色器按零售版 `c_maxLightingValue` 的方式限制峰值亮度。这个常量是引擎的相机参数，不存储在资源中，挂载默认使用 1。

- HDR 行星和太阳贴图另有固定的强度和峰值限制。
#### 表面和贴花
- `Titanfall2Surface`（[Resource/Surface.cs](Code/Resource/Surface.cs)）把 surfaceprop 名归一化（去路径/扩展名/引号）、查别名表，再 `Surface.FindByName`；找不到回退 `default`，每个未知名字只提示一次。

- 数据来源：MDL 模型级 surfaceprop 传给 `ModelBuilder.AddBody` 的物理体，骨骼级传给 hitbox 的 `.WithSurface`；legacy VMT 的 `$surfaceprop` 写成材质的 `PhysicsSurfaceProperties` 属性。rBSP 的 CM 数据与 RPAK MATL 头不提供可解析的表面名，保持默认。

- `Titanfall2DecalFactory`（[Resource/Decals.cs](Code/Resource/Decals.cs)）从贴花材质属性（`g_tAlbedo`/`g_tNormal`/`g_tEmissive`）生成原生 `DecalDefinition`；原生贴花只吃贴图，层混合/LUT/独立不透明度贴图不还原，遮罩取颜色 alpha。
### 显示管线
`Titanfall2Tonemapping`（[Components/PostProcessing/Tonemapping.cs](Code/Components/PostProcessing/Tonemapping.cs)）挂在一个无限范围、优先级 -100 的 `PostProcessVolume` 上，所以游戏自己的后处理体积可以覆盖它。函数定义在 `titanfall2_display.hlsl`。
#### 曝光
1. 对颜色缓冲区做 7 级对数亮度降采样。

2. 由平均 log2 亮度计算目标曝光：`L = exp2( meanLog )`，`target = (1.03 - 2 / (ln( L + 1.0002 ) + 2)) / L * 2^EV`。`EV` 是 `SBOX_TITANFALL2_EXPOSURE_EV`，默认 0。

3. 每帧按相对差值向目标逼近，速率 0.1（零售版 `mat_autoexposure_speed`）。

4. 结果限制在 0.5–2。

5. 每个相机有独立的曝光历史。

挂载在渲染时关闭相机自带的自动曝光，并记录原来的设置，组件移除时恢复。
#### 显示曲线
零售版 `engine_post` 的默认分支：`x * (10x + 0.3) / (x * (10x + 0.5) + 1.5)`，输入和输出都是线性值。
#### 泛光
1. 预过滤：在 1/4 分辨率上计算 `pow( max( rgb - cutoff, 0 ), exponent )`。

2. 模糊：水平和垂直两次，每次 13 个双线性采样点，覆盖 25 像素，中心权重 0.122765。

3. 合成：乘以 `0.5 * BloomStrength` 加到场景颜色上，然后应用曝光和显示曲线。

默认强度 0.15、阈值 1、指数 1，这些是挂载的校准值，不是零售版数值。另有 wide 和 streak 分支，分别使用独立预过滤/指数和更宽的降采样模糊、水平扩散；这些是重建实现，尚不代表零售像素一致。
#### 颜色校正
- `color_correction` 实体指向一个 32³ RGB 查找表文件（98,304 字节），例如 `materials/correction/sp_beacon.raw`。

- 支持启用实体的全局权重、距离衰减与 brush volume，并按相机维护进入/离开淡化状态；不执行原版脚本。

- 权重取 `maxweight`。

- 查找表本身按自己的内容路径注册成挂载体纹理（`mount://titanfall2/materials/correction/<名字>.raw.vtex`），

场景里的 `Titanfall2ColorCorrection` 只保存这个资源路径。表不进场景数据，因此每张图的场景不再多出 98,304

字节的样本；纹理属于挂载而不属于组件，缺挂载时该图只是不做校正。保留 32³ 精度，不重采样成引擎原生的 16³ grading LUT。
### 模型
- 每个 body part 默认选择模型 0。`Mdl53BodyPart.Base` 是打包乘数，不是默认索引。

- VTX strip 的 `IndexOffset` 是组内索引数组的元素下标，不是字节偏移。

- VVD 修正表的区间越界时报错。内嵌 VTX 的解析错误向上传递，不静默生成空模型。

- 碰撞来源的优先级：VPHY 凸包，然后是 hitbox，最后是渲染网格。超过 8192 个三角形的渲染网格用一个包围凸包代替。

- 碰撞来源写入模型数据 `titanfall2_collision`（`Titanfall2ModelCollisionData`），诊断命令从这里读取。

- 每个 hitbox 带标签 `titanfall2_hitgroup_<n>`。critical override 不为 0 的 hitbox 还带 `titanfall2_critical_override`。

- 每个 MDL 还注册一个静态实例模型（`.t2static`），它包含批次使用的前四档几何，其余 LOD 槽位复用最深的可用档，供实例批次切换。

- 动画来自模型自身和它 include 的模型，按骨骼名称映射到目标骨架。兼容模型每个序列只取第一个动画；独立片段还提供其余混合网格单元。模型先添加 `bindpose`。

- 模型缓存键包含转换格式 ID 和内容快照指纹（`ContentFingerprint`）；转换规则改变时须更新 `Titanfall2BuildIdentity.VersionId`。
### 静态道具批次
- 同一模型、同一批次簇（4×4 个 512 单位单元）的道具组成一个实例批次。

- 索引缓冲区：

- 恒等索引缓冲区只上传一次，按前缀复用。

- LOD 重排和可见子集使用动态缓冲区。

- 批次销毁时释放它的 3 个 GPU 缓冲区。

- 带光照探针的批次（`Titanfall2ProbeInstanceBatch`）在深度预通道中跳过探针绑定（`SkipDepthProbeBindings`），前向和阴影通道绑定全部数据。

- 模型太阳直射光在实时级联阴影覆盖范围内使用表面逐像素遮挡，不再重复乘上单个烘焙探针的太阳可见度。

各级联球的边缘平滑回退到烘焙可见度；未覆盖或未启用实时阴影时仍使用探针。天空光方向、间接光和局部灯光权重保持各自含义。

- 场景中序列化的道具数据带魔数和版本号，读取时检查版本范围，并按实际字节数限制解码出的数量。
### 粒子
- PCF 库在加载画面之后分帧解析。地图粒子实例在相机附近分帧激活。

- 名称包含 `sun`、`star`、`sky` 或 `weather` 的效果视为全局效果，不受激活半径限制。

- `orientation_type` 1 表示竖直公告板，2 表示世界 XY 平面。

- 支持以下渲染器和功能：

- 绳索渲染器（`Titanfall2ParticleRopeRenderer`）

- 速度拖尾

- 带 `$ramptexture` 的 SpriteCard（`Titanfall2ParticleRampRenderer`）

- 控制点编号范围是 0–63，来自实体键 `cpoint1`..`cpoint63`。游戏代码可以通过 `Titanfall2ParticleControlPoints.SetControlPoint` 更新控制点。

- 每个系统遵守 PCF 中的最大绘制距离和"不可见时休眠"时间。唤醒的循环效果会预热，避免重新出现时是空的。
### 音频
- `general.mbnk` 中的 BCF 声音由 [Formats/Miles/](Code/Formats/Miles) 的纯 C# Bink Audio 2 解码器解码：BCF 头（24 字节 + packet 尺寸表）→ 每个 packet 的位流、分带量化系数、逆 DCT 与 overlap-add → int16 PCM。结果按 `SoundFile.FromPcm` 交给引擎，没有原生桥或随包 DLL。

- 解码保真度：与零售 Miles 运行时对比，94.07% 的样本逐位相同，100% 在 ±2 LSB 以内（最大 2 LSB，平均 0.059 LSB）。这点差异来自零售 `binkawin64.dll` 自身的浮点与取整管线，在不复制 RAD 实现的前提下不可消除；用 FFmpeg 作为第三个 oracle 交叉验证，本实现比 FFmpeg 更接近零售。听感与零售一致。

- 声道：单/双声道逐样本对比；4/6 声道流先按原始声道解码，再在解码过程中折叠为双声道（Miles 近似：中心 0.7071、环绕 0.5、省略 LFE；4 声道另做 1/√2 等功率归一化），始终向 `SoundFile.FromPcm` 交付与旧包相同的 2 声道 PCM。4/6 声道电平与零售一致（RMS 比 0.999–1.000），波形相关系数中位数 1.0；3/5/7 声道布局返回失败并给出消息。

- 声音事件（Miles 事件解释器）依赖零售 Miles 运行时，属于原生代码，已随原生桥移除：`Titanfall2SoundEventPlayer`（组件名 `titanfall2_sound_event`）保留为兼容占位、不再播放；`CreateSoundEventSession` / `Titanfall2SoundEventSession` / `Titanfall2SoundEventHandle` 不再提供。`sound/<name>.vsnd` 样本、点声源和 soundscape 仍正常工作。

- 地图音频（[Components/Audio/MapAudio.cs](Code/Components/Audio/MapAudio.cs)）来自 `_snd.ent`：

- `ambient_generic` 生成点声源。零售地图的 `radius` 都是 0，因此使用默认半径 1024。

- `trigger_soundscape` 生成球形 `SoundscapeTrigger`。

- 播放依赖场景中的 `Sound.Listener`。
### 生命周期和所有权
- `_cachedWorldBuild` 与官方 loader 注册的 SceneFile 同寿命。临时构建场景销毁时不释放它；`OnSceneUnloaded()` 回调才清理生成模型、光照纹理和关联状态。

- 构建失败会回滚：`WorldBuildResult : IDisposable`，异常调用 `DiscardWorldBuild`，后续请求重新构建。

- `.bsp.scene` 和 `.bsp.world.scene` 的并发构建通过 `Titanfall2MapImport` 租约共享 BSP 解析，最后的解析租约释放后可清除 CPU 数据。失败条目不复用。

- 每个场景实例独立拥有局部光缓冲、原生道具对象和碰撞调度，销毁时只释放本实例对象。普通 loader 缓存仍由官方引擎管理，不反射清空，不强制 GC。

- 场景 lease 阻止在其他实例使用时清理共享 CPU 缓存。编辑器克隆和临时构建场景同样遵守资源所有权；没有以前的延迟重试/GC 回收循环。

- 共享粒子纹理在挂载关闭时解除引用；仍在运行的实例保留自己的引用。导航通过按 Scene 共享的状态恢复宿主设置。

- `Titanfall2Map` 只销毁自己创建的根对象和相机；加载时深拷贝 JSON 并重映射 ID，部分失败清理已创建的根对象。

- 专用服务器使用 CM 碰撞，跳过渲染、环境、粒子与地图音频。
### 流式加载预算
这些数值是 [Components/StaticProps/StaticPropStreamer.cs](Code/Components/StaticProps/StaticPropStreamer.cs) 中的常量：

| 阶段 | 预算 |
|--|--|
| 加载画面预加载 | 每次让出最多 512 个道具、35 ms，最多 4 个碰撞体 |
| 加载后补全 | 每帧最多 16 个道具、3 ms |
| 运行时可见性 | 每帧最多 16 个单元、1.5 ms |
| 碰撞流式 | 每帧最多创建或释放 2 个碰撞体、1 ms；1536 单位内启用，2560 单位外释放 |
| 距离保留带 | 道具进入绘制距离后，再多保留 512 单位 |
| 大型道具 | 半径不小于 256 的道具不做距离剔除 |
| 阴影 | 道具阴影资格沿用半径 48 的门槛及原批次聚合。`PROP_SHADOW_DISTANCE` 默认 0（距离门控关闭）；开启后 CPU 按批次门控、GPU 按实例门控，恢复阈值约为关闭距离的一半并量化到 256 单位桶 |

普通地图默认在加载期准备并常驻静态道具碰撞（`RESIDENT_WORLD=1`）；设 0 才采用上表的距离流式碰撞。纯世界和专用服务器始终常驻。已稳定的常驻场景不因相机移动重建碰撞扫描队列；拥有的碰撞体丢失时触发恢复。缺失碰撞资源仍保留失败状态，不能发布就绪标签。

地图粒子目录索引和系统创建也在 `OnLoad` 的有界循环中完成，运行时保留激活/休眠及模拟。`PreparationComplete` 表示道具准备流程结束，`ResourcesPrepared` 表示渲染资源没有加载失败，`ResidentCollisionsReady` 则要求实际常驻碰撞完整；三者不互相替代。粒子组件独立报告 pending/failed 数量。
#### 帧率预算
下方数字来自之前的 CPU 批次路径测量，仅作历史资料；本次官方沙箱适配没有新的 FPS 或 GPU 时间结论。

每帧的成本主要不是这些预算，而是**一次性创建、之后每帧被渲染线程提交的对象数**：`mp_angel_city` 实测 2,840 个渲染对象、3,044 draw、3.2M 三角形，其中 2,717 个是静态道具实例批次。C# 每帧回调（`Titanfall2StaticPropStreamer` 的三条流式通道合计 5.5 ms 预算）只占 `Update` 的 1.5 ms 量级。

单项旋钮：`PROP_SHADOW_DISTANCE`（道具批次投影距离，0 = 关闭门控）、`PROP_LOD_SCALE`（乘到 1536/4096/8192 三个 LOD 切换距离）、`WORLD_CELL_SIZE`（世界几何 cell 边长，越大 fragment 越少、剔除越粗）、`BATCH_CLUSTER`（多少个 prop cell 合成一个实例批次）。

阴影门控不在可见性 sweep 里，而是独立 pass：只在相机移动 512 单位或换 PVS cell 时重算，重算只把差异排队，`ApplyPendingShadowChanges` 每帧最多写 32 次 `Flags.CastShadows`。一次 sweep 内批量翻转一百多个批次的标志会让整片实例在 shadow pass 进出，走动时表现为一顿一顿；这个上限是为此加的。阈值按 256 单位粗桶比较，关闭距离 D 与恢复距离 D×0.5 之间留足回滞。`Flags.CastShadows` 的切换走引擎原生的 `ChangeFlags` + `NotifyChanged(Flags)`，与 `LineRenderer`、`MeshComponent` 的运行时切换同一条路径。

LOD 判定使用 32 单位量化的相机位置：探针批次的逐实例 LOD 分组一变就要重传索引缓冲，量化把走动时的重排从每帧降到每跨一格。
#### 官方沙箱的静态道具后端
静态道具使用上述原生实例批次。GPU MDI 的内部命令记录/播放兼容层已经移除，设置不能重新启用它。残留的几何布局、预算计算和 shader 为离线资料，不创建运行时 GPU scene。特殊材质不为合批而丢弃。

LOD、PVS、距离和阴影处理回到既有原生批次路径；不能沿用旧 MDI 的 CPU/GPU 性能结论。比较需要同进程、固定机位与分辨率、窗口聚焦，并记录帧时间及 GPU 时间。
### 处理缓存
- 缓存文件格式：

- 文件头：魔数 `T2FC`、版本号、32 字节 SHA-256 键。

- 正文：GZip 压缩的数据。

- 文件先写临时路径，再通过 `FileSystem.Data` 发布；同进程读写互斥。公开文件系统没有原子 rename，失败或中断后通过读取校验回退。

- 读取时校验文件头，并读到 GZip 结尾以检查 CRC 和截断。任何异常都按未命中处理。

- 缓存种类：`map`、`model`、`texture`、`material`、`propfade`。`map` 保存托管 BSP 解析数据，不保存运行时世界模型、光照纹理或 GPU 对象。

- 缓存键包含两部分：

- `ContentFingerprint`：转换格式 ID、内容快照 revision 与源容器的长度/有限采样版本。官方只读文件系统不提供 mtime；采样不是完整容器哈希。

- 资源自身的来源标识。

- 最近使用时间只在当前进程中记录，用于预算淘汰；不通过修改源文件 mtime 实现。

- 淘汰在每 64 次写入时检查一次，删除最旧的文件，直到总大小不超过 `SBOX_TITANFALL2_CACHE_MB`。

- 缓存只保存派生数据，从不保存引擎或 GPU 对象。
### 官方资源生命周期
地图 loader 继承官方 `SceneLoader<Titanfall2Mount>`，仅在 `OnSceneUnloaded()` 丢弃 WorldBuild。缓存的 SceneFile 和生成模型保持同寿命，临时构建 scene 销毁不会提前释放它们。

场景实例的局部光 GPU buffer、原生渲染对象和碰撞由各自 owner 在销毁时释放。普通 ResourceLoader 缓存与跨地图引用由官方引擎控制；不使用私有字段反射、强制 GC 或延迟重复 GC。现有 per-scene lease 仅用于避免在还有用户时清理解析缓存。

后台解析通过 `GameTask.RunInThreadAsync` 调度，以单独完成信号供同步导入等待，避免依赖主线程 continuation 而死锁；并行循环使用公开 `Sandbox.Utility.Parallel`。MDL 固定缓冲改为紧凑值字段，通过公开 ByteStream 读取，禁用 unsafe。

配置诊断记录转换格式 ID、包标识与 NativeInstanceBatch 后端；它不是实际 DLL 的 MVID。构建产物身份应在工具端用 SHA-256 验证。
### 玩家画质与挂载配置边界
- `Titanfall2GraphicsSettings` 负责把已保存的玩家纹理质量转为不可变导入策略；挂载时读取一次，监听 `RenderSettings.OnVideoSettingsChanged`，卸载时取消订阅。后台解码只读原子发布的快照，不访问视频设置 Cookie。

- 独立运行使用公开 `Application.RenderSettings`；菜单/编辑器的该入口为 null，仅在挂载连接阶段集中访问既有托管 singleton。取不到时回退 1K，不引入对菜单/编辑器程序集的依赖，不改引擎源码。

- RPAK 缓存键和 mip 选择使用同一个局部分辨率快照，避免保存设置与 worker 解码交错时污染磁盘缓存。VTF 2D/cubemap 同样在单次解码入口固定策略。

- 方向光默认组件上限恢复为原生支持的 4 级，由引擎质量配置裁剪；不复制原生阴影、后处理、抗锯齿或超分控制器。支持通过独立配置项显式覆盖。

- 删除无人调用的 `ITitanfall2TextureSource` / `TryCreateTexture` / RPAK 直接创建包装，正式链路统一为 source → CPU decode/cache → 主线程 upload。

- 质量改变不会销毁可见场景资源或强制切图。现有纹理在正常离图释放后重建；编辑器缓存仍遵守原有资源所有权规则。TF2 自定义水面、高度雾和粒子需要独立的效果质量设计，不能仅按菜单名称绑定。

玩家入口、覆盖优先级与生效时机见 [配置](#zh-configuration)。
### 高画质下的预取背压
`TexturePreparationBudget` 统一管理预取数据的保留量与实际峰值。材质描述缓存路径和原始 RPAK 绑定路径都遵守回调返回的停止信号。跨预算 payload 保留供主线程直接消费，并在上传或取消时返还其完整记账；共享贴图不重复读取、记账或释放。预算判断不等待主线程，避免主线程等待材质 worker 时形成循环等待。这个预算只覆盖 CPU 预取数据，不代表 GPU 显存预算，也不降低玩家选择的纹理分辨率。
### 局部调色与 Bloom 分支
局部颜色校正使用 [ColorCorrectionDefinition.cs](Code/Components/PostProcessing/ColorCorrectionDefinition.cs) 中的全局、距离和 brush volume 权重，并按相机维护进入/离开的连续淡化。原始 32³ LUT 由挂载持有，场景保存定义与资源路径；[ColorCorrection.cs](Code/Components/PostProcessing/ColorCorrection.cs) 在需要时创建每相机的组合纹理，释放时清理自己的 GPU 资源。

[Tonemapping.Bloom.cs](Code/Components/PostProcessing/Tonemapping.Bloom.cs) 分别保留基础、wide 和 streak 的预过滤、强度及指数。wide 默认强度 0.2、预指数 2、后指数 0.7；streak 默认强度 0.5、阈值 5、前后指数 1；基础后指数为 1。这些是组件属性，与 JSON 覆盖项分开。wide 使用更低分辨率的宽模糊，streak 使用多轮水平扩散；它们不构成零售画面逐像素一致性的证明。

<a id="zh-limitations"></a>
## 已知限制
- **碰撞**：

- surfaceprop 只覆盖 MDL（模型级用于物理体、骨骼级用于 hitbox）和 legacy VMT；rBSP 世界碰撞和 RPAK MATL 仍是默认表面。

- `nowallrun` 标签由 `NOCLIMB` 位推断，没有对照零售版的墙跑代码确认。

- 没有 VPHY 和 hitbox 的模型，其碰撞是渲染网格或包围凸包的近似。

- **游戏逻辑**：门、触发器、脚本和网络同步属于游戏代码，挂载只提供几何、碰撞和实体数据。

- **动画**：兼容模型仍只嵌入每个序列的第一个动画；独立资源提供其他混合网格单元，但不实现序列混合、分层和 IK。引擎标准动画资源要求统一缩放；零售数据的微小浮点误差会归一化，真正非均匀缩放会明确报错。

- **粒子**：

- 只实现了 PCF operator 和 initializer 的一部分。

- 天空盒世界只承载 vista 模型，不包含粒子。

- 控制点在加载时从实体数据确定，运行时移动需要游戏代码调用 `SetControlPoint`。

- **音频**：

- 每个 `trigger_soundscape` 区域只播放一个循环声音，用引擎的球形 `SoundscapeTrigger` 实现，同一时间只有一个区域生效。

- `ambient_generic` 发声点数量有上限（默认 12 个）。

- 没有 DSP 效果。

- **声音事件不可用**：零售的 Miles 事件解释器是原生运行时，已随原生桥移除。`titanfall2_sound_event` 组件保留但只记录一次警告；使用 `.vsnd` 样本、`Sound` 或 `Soundscape` 可以达到相近效果。

- BCF 解码与零售 Miles 运行时 94.07% 样本逐位相同，100% 在 ±2 LSB 以内（最大 2 LSB），听感一致。

- 4/6 声道 BCF 声音先用 C# 解码再折叠为双声道（Miles 近似：中心 0.7071、环绕 0.5、省略 LFE；4 声道另做 1/√2 等功率归一化）。与旧原生桥的 Miles 私有混音不完全相同（相关系数中位数 1.0，最低 0.9136，4/6 声道电平与零售一致），但这些声音不再丢失；3/5/7 声道布局会返回失败并跳过。

- **雾和后处理**：

- 雾、光照环境和后处理在每个场景中按单个相机选择，不在多个相机或多张地图之间协调。

- 不支持脚本或网络驱动的雾渐变；局部颜色校正导入静态范围与相机淡化，不执行零售脚本。

- 泛光包含基础、wide 和 streak 分支；参数和画面仍需逐图对照零售版。

- 地图实体中的曝光控制不会被导入。

- **材质**：VMT 代理只实现了一部分，不支持任意 2D 纹理变换和由实体驱动的输入。

- **显存**：没有跨地图的 GPU 资源 LRU 或显存预算。纹理上限跟随公开玩家画质设置，可用 `SBOX_TITANFALL2_TEXTURE_MAX` 覆盖；无法取得设置时回退为 1024，运行中不会升级到更高的 mip。

<a id="zh-references"></a>
## 参考项目与致谢

以下链接记录开发过程中查阅过的工程及其用途。挂载架构研究于 2026-11-06 阅读了下列固定提交，链接保留当时所查看的版本。运行时接入继续使用 s&box 官方挂载 API。

### 官方基础

| 工程 / 文档 | 参考范围 |
| --- | --- |
| [Facepunch/sbox-public](https://github.com/Facepunch/sbox-public) | 挂载 API、资源加载器、场景构建、addon 编译、渲染和资源生命周期；另见 [Game Mounts 官方文档](https://sbox.game/dev/doc/game-mounts/)。 |
| [Facepunch/sbox-mounts](https://github.com/Facepunch/sbox-mounts)，尤其是 [SourceEngine](https://github.com/Facepunch/sbox-mounts/tree/main/SourceEngine) | 官方挂载项目结构、资源注册、Source 格式转换方式及场景资源所有权。 |

### 文件格式、解码与 Source 行为

| 工程 | 参考范围与源码链接 |
| --- | --- |
| [r-ex/LegionPlus](https://github.com/r-ex/LegionPlus) | 交叉核对 RPAK 纹理格式编号，相关 TXTR → DXGI 格式表见 [Legion/rtech.h](https://github.com/r-ex/LegionPlus/blob/main/Legion/rtech.h)。 |
| [snake-biscuits/bsp_tool](https://github.com/snake-biscuits/bsp_tool) | CM 碰撞和内联 brush 审计时，与零售数据一起交叉核对公开的 Respawn rBSP 布局；见 [titanfall2.py](https://github.com/snake-biscuits/bsp_tool/blob/master/bsp_tool/branches/respawn/titanfall2.py)。 |
| [MRVN-Radiant/MRVN-Radiant](https://github.com/MRVN-Radiant/MRVN-Radiant) | Respawn Source 地图编辑与 BSP 生成参考，涵盖 Titanfall 2 等相关游戏。用于了解地图结构及编译端实现；不同游戏、不同 lump 的覆盖程度不同。 |
| [snake-biscuits/bsp_regen](https://github.com/snake-biscuits/bsp_regen) | Titanfall BSP 向 Titanfall / Titanfall 2 转换，以及供导航数据生成使用的物理数据参考。项目聚焦 BSP 转换，其他资产格式由独立工具处理。 |
| [r-ex/RePak](https://github.com/r-ex/RePak) | 为 Titanfall 2 / Northstar 和 Apex Legends / R5Reloaded 编译 RPak 资产，从写入端参考包构造与资产布局。 |
| [r-ex/rsx](https://github.com/r-ex/rsx) | reSource Xtractor：RPak 资产解析、转换及 Titanfall 2 模型读取，用于对照挂载的资源读取实现。 |
| [Mauler125/r5sdk](https://github.com/Mauler125/r5sdk) | Apex Legends 开发包源码，作为同源 Respawn Source 实现参考。Apex 专有结构和运行时行为仍需与 Titanfall 2 分别核对。 |
| [R2Northstar/NorthstarLauncher](https://github.com/R2Northstar/NorthstarLauncher) | 只读解释 Northstar Mod profile、内容优先级及条件包配置。 |
| [richgel999/lzham_alpha](https://github.com/richgel999/lzham_alpha) | 托管 LZHAM Alpha8 解码器的移植来源，包括熵编码、前缀表及校验；见 [lzham_lzdecomp.cpp](https://github.com/richgel999/lzham_alpha/blob/master/lzhamdecomp/lzham_lzdecomp.cpp)。Alpha 与 LZHAM 1.x 的码流不兼容。 |
| [FFmpeg/FFmpeg](https://github.com/FFmpeg/FFmpeg) | Bink Audio 解码参考及历史交叉比较工具。托管解码器源码注释标明频带表和零游程表分别来自 [wma_freqs.c](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/wma_freqs.c) 与 [binkaudio.c](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/binkaudio.c)。 |
| [ValveSoftware/source-sdk-2013](https://github.com/ValveSoftware/source-sdk-2013) | 共享 Source 实体的颜色校正径向衰减、体积语义及 SpriteCard 参数行为；见 [c_colorcorrection.cpp](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/client/c_colorcorrection.cpp)、[colorcorrectionvolume.cpp](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/game/server/colorcorrectionvolume.cpp) 和 [spritecard.cpp](https://github.com/ValveSoftware/source-sdk-2013/blob/master/src/materialsystem/stdshaders/spritecard.cpp)。TF2 特有行为仍需独立核对。 |

### 挂载架构研究

以下项目用于早期可复用挂载框架的源码结构和设计研究。研究没有编译或基准测试这些项目，因此不能据此推断它们在 Titanfall 2 内容上的运行期性能。

| 工程与已查看版本 | 参考的设计方向 |
| --- | --- |
| [chrisspieler/sbox-gamemaker-mount — 0a576db](https://github.com/chrisspieler/sbox-gamemaker-mount/tree/0a576db0feff7055c198165014ebf0cc08646ecd) | 引擎族公共挂载能力、逐游戏配置与归档内逻辑资源注册。 |
| [chrisspieler/sbox-overgrowth-mount — f4f2b7e](https://github.com/chrisspieler/sbox-overgrowth-mount/tree/f4f2b7e3d9f1834f1c519b9d1717e647b22b9f07) | 安装发现、资源描述与引擎注册分离。 |
| [atrblizzard/vtmb-sbox-mounter — 4cd3888](https://github.com/atrblizzard/vtmb-sbox-mounter/tree/4cd3888079eb8224f67333850b70cb7f2784e5da) | 确定的补丁 / loose 文件覆盖顺序与统一虚拟路径读取。 |
| [cmg-divined/gta5-mount-sbox — ab5ff79](https://github.com/cmg-divined/gta5-mount-sbox/tree/ab5ff79cc8b19ce48a04c37ad59c85035e691e98) | 大容器依赖索引、按 hash 寻址的资产与纹理父依赖。 |
| [chrisspieler/sbox-mount-formats — ce1908d](https://github.com/chrisspieler/sbox-mount-formats/tree/ce1908d637611c7e4a210d896e3d528ed690dbe0) | 共享格式 / 解码工具与挂载生命周期分开维护。 |
