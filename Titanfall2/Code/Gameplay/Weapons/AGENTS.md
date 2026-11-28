# Code/Gameplay/Weapons/ — Data, Mechanics, Runtime & Diegetic UI

Implements all 37 Pilot hitscan/projectile/anti-Titan/ordnance weapons on `BaseCombatWeapon`, retail PC multiplayer `WeaponData` parsing, POV animation, and in-world gun ammo screens.

## Subfolders & Key Files
- `Data/` — Retail `WeaponData` parsing (`WeaponData.cs`, `WeaponData.Mods.cs`, `WeaponData.Ui.cs`, `WeaponData.ViewKick.cs`), `WeaponScriptOverrides.cs` (overlay overrides such as `AttackPressesAds`, `RequireFullAdsToFire`, `FlightSound`, Arc Launcher 1P/3P fire sounds), `ReloadProfile.cs`, `WeaponModProfile.cs`, `WeaponPresentation.cs` (bodygroups, muzzle/tracer/shell metadata).
- `Mechanics/` — Pure rule structs + `Titanfall2Weapon` partial mechanics:
  - Firing & ballistics: `WeaponFiring.cs`, `WeaponDamage.cs`, `WeaponSpread.cs`, `WeaponViewKick.cs`, `Weapon.ViewKick.cs`, `Weapon.Reload.cs`, `Weapon.Mods.cs`, `Weapon.Ordnance.cs`, `WeaponOrdnance.cs`, `Weapon.Specials.cs`.
  - Specialized retail weapons (composed via `ITitanfall2WeaponMechanic` + thin `[Property]/[Sync]/[Rpc]` bridge in `Weapon.Mechanics.cs`): `Weapon.Charge.cs` (`Titanfall2WeaponChargeController`) / `WeaponCharge.cs` / `Weapon.ChargePresentation.cs`, `Weapon.Heat.cs` (`Titanfall2WeaponHeatController` + `Titanfall2HeatInventory`) / `WeaponHeat*.cs`, `Weapon.SpinUp.cs` (`Titanfall2WeaponSpinUpController`) / `WeaponSpinUp.cs`, `Weapon.SmartAmmo.cs` (`Titanfall2WeaponSmartAmmoController`) / `WeaponSmartAmmo.cs`, `Weapon.Ordnance.cs` (`Titanfall2WeaponOrdnanceController` + `Titanfall2OrdnanceInventory`) / `WeaponOrdnance.cs`.
- `Runtime/`:
  - `Weapon.cs` — `Titanfall2Weapon : BaseCombatWeapon` core component.
  - `PilotViewModel.cs` / `PilotViewModel.Motion.cs` — Bone-merged retail POV rig + pilot arms; `idle_weight = 1f - Zoom` fades hipfire idle sway on ADS. Per-weapon sway/bob comes from `Data/WeaponData.Sway.cs`; the separate parkour pose retains ADS suppression.
  - `WeaponAudio.cs`, `WeaponEffects.cs`, `Weapon.Prewarm.cs`.
- `Ui/`:
  - `WeaponAmmoDisplay.cs`, `WeaponAmmoDisplay.Panel.cs` — Diegetic world-panel gun screens (Double Take, Cold War `pulse_lmg_panel2`, Softball, EPG, L-STAR, etc.).
  - `WeaponUiLayoutCache.cs`, `Weapon.Hud.cs`, `WeaponScope.cs`.

## Invariants & Pitfalls
- **ADS-gated fire (`Archer` / `Arc Launcher`)**: `AttackPressesAds` (`attack_button_presses_ads`) + `RequireFullAdsToFire` require full ADS blend (`_adsBlend >= 0.98f`, host `zoom >= 0.95f`) before launching.
- **ADS sway suppression**: Keep `idle_weight = 1f - Zoom` in `PilotViewModel` / `PilotAnimationPlayer` so hipfire `idle_seq_autoplay` sway (e.g., CAR `2.92°`) does not shake sights during ADS.
- **Satchel**: The retail `ACT_VM_TOSS` is the equipped item's left-click throw; `ACT_VM_PRIMARYATTACK` is the right-click clacker. Use the same resolved model sequences for presentation and host cue timing. Keep release/detonation authority in the host weapon controller; client animation events are presentation only.
- **Authored motion**: Select the actual `ads_blend` fire cell instead of attenuating the whole skeleton. The 171 sway/bob fields use independent hip/zoomed defaults and preserve signed coefficients; repeated camera placement must not advance state twice. Evidence and acceptance limits: `docs/WEAPON_VIEW_MOTION_2026-11-27.zh-CN.md` at the mount root.
- **Standing crouch presentation**: Retail crouch and stand idle can share the same animation cells. Read the signed `viewmodelDuckOffset` from pilot settings, apply it in camera-up space before the viewmodel anchor inverse, and fade it out through ADS without adding angular cant. See `docs/PILOT_CROUCH_WEAPON_POSE_2026-11-28.zh-CN.md`.
- **`PanelComponent` SCSS path**: `WeaponAmmoDisplay.Panel.cs` is coupled to `Assets/gameplay/weapons/ui/weaponammodisplay.panel.cs.scss`. Do not move one without the other.
- **Diegetic UI layouts**: Retail screen regions live in `Code/Formats/Rpak/RpakWeaponUiLayout.cs`; verify `WeaponBugRegressionTests` and `WeaponHudTests` after touching any display renderer. Hemlok BF-R uses the retail text style’s independent 0.5 horizontal scale for glyphs, advances, and kerning; retain the authored 190 line height and MDL panel dimensions.
- **Crouch and slide actions**: Select per-model retail clips at deploy or reload start; keep the selected stance across segmented reloads. Sliding uses an authored hip grip and recoil cells where available. See `docs/PILOT_STANCE_ACTIONS_2026-11-28.zh-CN.md`.
- **Optic presentation**: Physical RUI art loads `rui_full/` atlas resources so cropped sprites retain their original transparent margins. Convert retail linear colors at the Painter boundary. `WorldPanel` defaults to 2x CSS scale; convert canvas CSS lengths with `ScaleFromScreen` while leaving Painter coordinates in authored pixels. Root sizing/clipping is also set in code so a missing SCSS cannot collapse the panel. See `docs/WEAPON_OPTIC_RENDERING_2026-11-28.zh-CN.md`.

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~Weapon|FullyQualifiedName~PilotViewModelTests"
```
