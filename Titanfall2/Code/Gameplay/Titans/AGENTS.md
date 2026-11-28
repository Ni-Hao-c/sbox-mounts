# Code/Gameplay/Titans/ — Chassis, Kits, Cores, Defense & Cockpit

Implements all 8 drivable Titans (Monarch, BT-7274, Legion, Ion, Tone, Scorch, Northstar, Ronin), their 7 chassis kits and 7 cores, physical defense volumes, Doomed/Termination/Ejection lifecycle, and 1P cockpit HUD/view.

## Subfolders & Key Files
- `Chassis/`:
  - `TitanChassisProfile.cs` — Data-driven `Titanfall2TitanChassisCatalog` (dash counts, core names/durations/build times; supports `titans.chassis.json` overlay).
  - `TitanSeat.cs` — Pilot-only embarkation gate (`HasPilotCombatState` requires equipped Jump Kit), cancellable boarding transition, `SitMoveMode` driving, `AlignWithPilot`, exit/ejection.
  - `TitanLifeState.cs` — Active -> Doomed -> Dead transitions and self-destruct ejection.
  - `TitanTermination.cs` — Invulnerable 3P cinematic Titan-vs-Titan executions (including Monarch Battery Thief rewards).
  - `TitanArmament.cs` — Host-side `EquipTo(Titanfall2Npc)` weapon swapper (`entities/titanweapons/...`).
- `Kits/` — `TitanKit.cs`, `TitanKit.Utility.cs`, `TitanIon.cs` (shared energy pool & split-shot), `TitanPrimaryAds.cs` (Northstar ADS railgun charge-up, Legion Predator Cannon barrel spin-up, Ronin Leadwall ADS spread narrowing, Scorch ballistic arc solver), `TitanWeaponTracking.cs` (Tone 3-hit 40mm tracker lock), `TitanWeaponAbilityData.cs`.
- `Kits/TitanKit.Ai.cs`, `TitanKit.AiMovement.cs`, `TitanAiActionRules.cs` — Single host AI skill director, cancellable prepare/commit/recovery sessions, profile eligibility and resource rules; continuous collision-swept dash, flight/landing, navigation ownership. Core executes accepted actions but does not independently select AI activations.
- `Cores/` — `TitanCore.cs` (Upgrade Core, Smart Core, Laser Core, Salvo Core, Flame Core, Flight Core, Sword Core; host damage credit & activation).
- `Defense/` — `TitanDefense.cs`, `TitanDefense.Kit.cs` (physical defense volumes: Vortex Shield, Gun Shield, Thermal Shield, Sword Block), `TitanDeployables.cs` (Particle Wall, Incendiary Gas Canisters), `TitanTetherTrap.cs`, `TitanTetherConstraint.cs`, `TitanTetherRules.cs`.
- `Cockpit/` — `TitanCockpitView.cs` + `TitanCockpitView.AdsPreview.cs` (1P arms + Titan POV weapon rig + `atpov_titan_sword`, Scorch ADS trajectory arc/impact ring, Northstar/Legion 1P ADS audio), `TitanCockpitHud.cs` + `Artwork` / `Gauge` / `Styles` partials.
- `Chassis/TitanSeat.Interaction.cs` — Authored paired embark/disembark session; host clock, exclusive movement/camera ownership, safe cancellation and cockpit claim gates. Retail Pilot clip length owns timing; legacy serialized `EmbarkSeconds` is retained but no longer selects the duration.
- `Interaction/TitanInteractionPresentation.cs` — Temporary retail Pilot body/arms, per-sequence AG2 tracks, animated `ref` to `hijack` alignment, VDU/CAMERA and event metadata. Never apply Respawn tracks to a Citizen skeleton.
- `Interaction/TitanInteractionAssets.cs` — Spawn prewarm manifest sharing the runtime Rodeo sequence names. Includes selected body/arms/Titan clips, hand props, verified script audio aliases and MDL sound events. NPCs enqueue at `OnStart`; the existing mount coordinator loads incrementally. `InteractionPrewarmStatus`/errors expose completion.
- `Rodeo/` — Single external rider, battery theft/grenade/friendly resupply and armed friendly idle. See [Rodeo/AGENTS.md](Rodeo/AGENTS.md).

## Invariants & Pitfalls
- **Pilot-only embark**: Only players with an equipped `Titanfall2PilotParkour` (`Equipped == true`) may embark; `TitanSeat` registers/unregisters with `PilotInputInterceptor` in `OnEnabled`/`OnDisabled`.
- **Physical defense volumes**: Ion/Legion/Scorch/Ronin frontal guards operate via `TitanDefense` volumes rather than legacy `GuardMultiplier`.
- **Phase sharing**: Ronin Phase Dash shares `Titanfall2PhaseDimension` with Pilot Phase Shift (and handles overlap-telefrag resolution on exit).
- **AI action ownership**: Ordinary NPC movement/fire must respect `AiControlsMovement`/`AiBlocksPrimary`; traversal and Kit movement may not own the same actor simultaneously. Termination, death, disable and pilot takeover call `CancelAiActions` and clear pending volleys, guards and movement ownership.
- **Resource parity**: AI and cockpit dash share charge consumption. AI Ronin block uses `RoninBlocking`; AI Scorch thermal shield drains its meter. Ion NPC recovery reads AI profile fields while pilot defaults remain separate.
- **Animation commit**: AI offense actions use verified `AE_NPC_WEAPON_DIRECTED_FIRE`/`AE_NPC_WEAPON_FIRE` cycle metadata when present, sharing the visual clock across `*_prepare`, main and `*_recover` roles. `AiDecisionReason` distinguishes `animation-event`, `activity-end-fallback` and `timing-fallback`; never emit duplicate damage from visual animation callbacks. Sustained guards keep their available defense role after the activation request ends without taking Legion's primary gun.
- **Mode boundary**: `Actor.AiMode` distinguishes enemy and ordinary/enhanced Auto-Titan policies; Kit reads `Actor.AiProfile` to honor `UseRetailAiSettings`. Changing either setting during preparation cancels the pending request, and commit rechecks per-slot mode permission. Missing optional DLC weapon profiles use explicit compatibility fallback; unsupported declared use-check types remain denied.
- **Remote-client driving**: Currently unsupported (local player or listen host only); do not silently bypass the host checks in `TitanSeat.CanEnter`.
- **Cockpit versus external riding**: A hostile Titan or another pilot's owned Titan cannot be claimed. Use mounts an unclaimed/owned ally cockpit; crouch+use chooses its external rider slot. Enemy/occupied/other-owner interactions choose rodeo. BT follows retail `rodeo_allow 0`. The rider is not parented into `BaseChair`; driving and back riding have independent occupants.
- **Interaction commits**: Embark grants control only after a decoded Pilot clip finishes. Disembark rechecks safe exits after its animation; obstruction restores cockpit control. Rodeo effects commit once on the host using authored `rodeo_battery_rip` / `rodeo_battery_grenade_damage` cycles; a missing event uses explicit clip-end diagnostics, while missing required clips cancel. Ordinary batteries heal the current normal/doomed pool without removing Doom; rodeo damage retains normal shield absorption.
- **Shared prewarm ownership**: `Titanfall2Mount.InteractionResources` retains only immutable Model/Skeleton/Clip assets by normalized kind/path, including negative results. Both prewarm and presentation use it; repeated NPCs share pending/completed requests, and different chassis share common assets. Never cache per-renderer graphs or scene objects. Clear on mount reset; keep native loads on the main thread and audio preparation on the existing single-job coordinator. No GPU first-draw guarantee.
- **Cockpit controls**: `DefenseAction = "toolmenu"` (default MMB) with `DefenseAltAction = "duck"` (default Ctrl) triggers chassis defense; `attack2` (RMB) is reserved for universal primary-weapon ADS across all Titans (`PrimaryZoomFraction`, Ion retains `IonZoomFraction` for Split Shot). `EjectAction = "die"` (default U) ejects the pilot and destroys the Titan; `DashAction = "jump"` (default Space) dashes. Capture intercepted actions in `PilotInputInterceptor` before clearing so Sandbox cannot open ToolMenu, suicide the pilot, or process a seated jump. HUD labels follow the seat's configured actions; on-foot bindings remain unchanged.

## Ability presentation
- `Kits/TitanKit.RearmPresentation.cs` consumes accepted host Rearm state or the existing AI timeline. Body roles are `mt_rearm_out / charge / in`; `Cockpit/TitanCockpitView.Rearm.cs` owns the separate dual-pod overlay. Never use the pod's firing clip: retail Rearm skips it. Visual cues do not refresh ammo, cooldowns, or dashes.
- Use authored local/observer aliases and play only one mix per listener. Sustains and attached particles must follow the replicated ability state and be torn down on stop, loss of owner, death, and disable. Keep synthetic visual fallbacks separate from host damage and targeting.
- `Titanfall2TitanAbilityData.MountedProjectile` converts raw retail model paths before passing settings to a projectile. Do not pass `models/*.mdl` directly to `Model.Load`.

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~Titan"
```
