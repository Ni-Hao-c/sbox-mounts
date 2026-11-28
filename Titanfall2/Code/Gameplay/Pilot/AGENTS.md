# Code/Gameplay/Pilot/ — Parkour, Combat & Tacticals

Implements the equipable Pilot Jump Kit, TF2 movement physics (double jump, retail-faithful wallrun, wallhang, slide-hop, mantle), 3P Citizen poses, Pilot melee/ordnance, input interception, and 4 tactical abilities.

## Subfolders & Key Files
- `Parkour/`:
  - `JumpKitItem.cs` — Pickup/equipment item (`Titanfall2JumpKitItem`) granting Pilot Combat State (`Equipped = true`).
  - `PilotParkour.cs` — Root coordinator (`Titanfall2PilotParkour`), synced state (`IsWallhanging`, `YieldedToSandbox`), jump-jet/wallrun thruster audio, 3P remote proxy pose sync.
  - `PilotMoveMode.cs` — Thin `MoveMode` shell (`Titanfall2PilotMoveMode`) coordinating ground/jump/input buffering, shared trace helpers, and 5 standalone composed state controllers:
    - `PilotWallrunController.cs` — Retail-derived wallrun integration (`wallrun_allowed_wall_dist = 13u`, `wallrun_pushAwayFallOffTime = 0.05s`, `wallrun_rotateMaxRate = 3 rad/s`, `wallrun_timeLimit = 1.75s`, no magnetic inward suction, `TryGlideObstacle` trim step-out). Feeds raw local input and full eye rotation into the wall-relative motor; never restores a cached target speed after collision.
    - `PilotWallhangController.cs` — Wallhang attachment (`S` + ADS from wallrun or mid-air near wall; retail `wallrunAdsType = "ADS"` preserved), slip warning, detachment.
    - `PilotMantleController.cs` — Ledge-mantle detection, interpolation, vault-jump cancel, crest boost.
    - `PilotSlideController.cs` — Slide boost cooldown, slope-projected friction, visual `Blend`.
    - `PilotThirdPersonPoseController.cs` — Citizen 3P body orientation, pitch/roll lean, synthetic climb velocity, and `special_movement_states` / `b_climb`.
  - `PilotParkourRules.cs` — Pure math/state helpers (`StepWallrunVelocity`, `IsPushingAwayFromWall`, `ComputeWallhang*`, `ComputeThirdPersonBodyPose`, etc.).
  - `PilotParkourState.cs`, `PilotSettings.cs` (reads `pilot_mp.set` + audible Jumpjet `*_Body_*` sound aliases), `PilotCameraAnchor.cs`.
- `Combat/`:
  - `PilotCombat.cs` — Pilot melee lunge, offhand `[G]` ordnance auto-bind & throw, melee impact classification.
  - `PilotInputInterceptor.cs` — O(1) `HashSet`-registered input interceptor (`OnEnabled`/`OnDisabled` on `PilotCombat` and `TitanSeat`) that intercepts `Flashlight` (`F`), `Drop` (`G`), `Spray` (`T`), `InspectMenu` (`C`), `Noclip` (`V`) while preserving `Q` (`SpawnMenu`).
  - `PilotCombatHud.cs` — One retail RPAK tactical icon beside the host health/armour readout; T key hint and charge bar. No right-side ability/melee/ordnance cards or synthetic Stim/Phase screen tints.
  - `PilotCombatRules.cs`.
- `Tacticals/`:
  - `PilotTactical.cs`, `PilotTacticalRules.cs` — Stim, Grapple, Phase Shift (cleanses EMP), Pulse Blade.
  - `PilotTacticalItem.cs` — Host-authoritative consumable pickup, one equipped kind at a time. Requires Jump Kit, rejects duplicate pickups without refilling, validates caller ownership/range/line of sight. No free cycle/select RPC; T (`spray`) activates, Mouse3 remains available to Sandbox. Empty slots do not intercept T. Picked-up kind persists with Jump Kit across respawn; unequip clears it.
  - `PilotTactical.Presentation.cs` — Mounted PCF graphs and Miles loops, cleaned up on replacement/disable/destroy. Grapple movement/rope and phase visibility remain engine adapters; see `docs/PILOT_TACTICAL_RESEARCH_2026-11-28.zh-CN.md` for evidence and limits.
  - `PilotTactical.ScreenEffects.cs` — Camera-local retail `P_heal` / Phase screen PCF and original 32³ Phase `.raw` LUT. Respect the local first-person camera controls and spectator `firstperson` exclusion, retain host grading/commands, remove only owned commands/tags, and never dispose mounted shared textures. Stim/Grapple have no separate authored LUT in the examined scripts.
  - `PulseBlade.cs` — Thrown sonar kunai.
  - `SonarRegistry.cs` — Scene-level sonar reveal registry.

## Invariants & Pitfalls
- **Citizen 3P animation**: `CitizenAnimationHelper.IsClimbing` (`b_climbing`) is `[Obsolete]` / no-op. Use `special_movement_states` (`0=None`, `1=LedgeGrab`, `2=Roll`, `3=Slide`) and `b_climb` via `MoveMode.SetAnimatorParameter`; wrap legacy AnimGraph1 `SkinnedModelRenderer.Set(...)` fallback in `#pragma warning disable CS0618` (because `TreatWarningsAsErrors=true`).
- **Jump Kit thruster audio**: Retail `Jumpjet_*_Start_*` / `*_End_*` in `general.mbnk` are silent (`-96 dB` / `sound/null`). Use audible `Jumpjet_Jump_Body_*`, `Jumpjet_Jet_Body_*`, `Jumpjet_Wallrun_Body_*`, and `human_land_default` fallback.
- **Wallrun motor**: Verified retail `client.dll` (`0x18020cc30`, `0x1801f0ac0`, `0x18020e6f0`): wall-relative input basis from raw local input and full eye rotation, half gravity → plane projection → full 3D friction (even under W) → vertical/horizontal acceleration with total-speed limit → half gravity. Gravity ramps linearly; slip restores friction-reduced falling velocity and reduces upward propulsion. Do not reintroduce synthetic minimum entry speed, automatic curve speed preservation, a forced -70 slip velocity, or a no-reversal clamp. Reverse input can cross zero; backward target 50 is relative to the camera. See `docs/PILOT_WALLRUN_RETAIL_2026-11-27.zh-CN.md` for formulas and remaining adapter differences.
- **Wallrun detachment & curves**: World wish input away from wall (`Dot > 0.7`) detaches after `0.05s`; preserve input magnitude. `WallNormal` rotation is capped at `3.0 rad/s`, including slope Z, and subsequent velocity projection may lose speed. Keep no inward magnetic suction so tight pillars detach naturally.
- **Wall entry / kick**: `0x18020b390` adds wall-up boost without fabricating horizontal speed. `0x180201170` adds an input/outward impulse to existing velocity, attenuates outward impulse when both looking and pushing into the wall, and supplements Z with `min(target, Z + 1.5 * target)` only when below target. Do not normalize the tangential input after removing its inward component.
- **Input interceptor registration**: Always register/unregister via `OnEnabled`/`OnDisabled` — never scan the scene per frame.
- **Tactical timer ownership**: Host duration/healing/recharge consume elapsed frame time; do not cap them to a movement substep. Grapple movement must not double-increment host elapsed time. Stim speed has a separate 0.5s tail with 0.25s fade; it must not extend healing/screen/audio duration, and replacement clears it.
- **Ordinary ground / air motor**: Ground uses native `2500 u/s²` acceleration and `1500 u/s²` fixed deceleration, protecting the wish-speed component before friction (`0x1801f8090`), then `ApplyMovementAcceleration`. Do not restore the old speed-proportional `5.5` friction. Air uses input magnitude × `60`, acceleration `500`, plus the saturated-direction `2 u/s²` energy-limited turn branch. Jump-keyboard-grace and custom stance/status modifiers are not yet fully integrated.
- **Slide motor**: Reverse input (`Dot(wish,velocity)<0`) or releasing crouch selects `400` fixed deceleration; otherwise use `50`, followed by `Pow(0.7,dt)` retention. Steering is native acceleration `50` with energy limit, not a `2.2` multiplier. Slope horizontal gravity is applied in two half-steps using `g*n.xy*n.z`, without a `1.85` multiplier. End a slow slide before the second half-slope step. The host wish-speed and ground-plane rigidbody mapping remain explicit adapters.
- **Slide entry / cooldown**: Grounded starts test input facing, not velocity facing; landing skips the facing gate. Do not gate starts on `!Controller.IsDucking` because the engine updates duck first. Every successful start resets `_sinceStart`, including unboosted starts. Integrate sliding in support-relative velocity and add `GroundVelocity` only once.
- **Jump dispatch boundary**: `PlayerController.InputJump` can call mode `OnJump` before our landing update. Replenish double jump and clear previous-wall state on grounded `OnJump`. The separate host/Pilot coyote state still needs unified ownership; see `docs/PILOT_MOVEMENT_AUDIT_2026-11-27.zh-CN.md` before changing jump entry paths.

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~PilotParkourTests"
```

For tactical changes also run `PilotTacticalResourceTests` with `TF2_GAMEPLAY_TEST_GAME` set to the retail install. Native suites `--pilot-tactical` and `--pilot-tactical-hud` cover pickup/T input/lifecycle and actual mounted icon rendering independently of the broader `--pilot-parkour` Titan fixtures. HUD vitals in the latter fixture are simulated; do not report this as live host HUD acceptance.

For tactical sound/screen/timing changes, also run `--pilot-tactical-presentation`: actual native audio handles and authored event delays, fixed-camera screen pixels, Phase LUT ownership, view switches, and elapsed-time boundaries. Fixture input must run within the scene's `Time.Scope`; newly created colliders need a scene tick before tracing. Run `ParticleColorChannelTests` when changing PCF RGB/alpha conversion.
