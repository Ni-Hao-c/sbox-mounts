# Titan defense presentation (2026-11-28)

Presentation only: unchanged host collision, capture/refire payloads, damage scaling, meters, timings, NPC choices and deployables. Scope is Ion, Legion, Scorch and Ronin.

## Evidence from the local retail-script mirror

Paths and lines below refer to `D:\northstar`; use `scripts\`, not the modified gameplay overlays under `mods\`, unless explicitly noted.

| Defense | Retail script evidence | Mount implementation |
| --- | --- | --- |
| Ion Vortex | `scripts\vscripts\weapons\mp_titanweapon_vortex_shield.nut:37–63,128–148,217–218,272–306` precaches the world/FP/replay effects, starts/stops the loop and chooses end vs. throw. `scripts\weapons\mp_titanweapon_vortex_shield.txt:42–47` declares tracer and absorb audio. `_vortex.nut:204–227,1365–1445` binds CP1 to the inverse remaining-energy color ramp. | World sustain and generic Ion ping retain their PCFs; CP1 follows synced shared energy. The mounted pilot gets their 1P audio variants. Empty release gets the endcap, captured-round release gets throw, and the existing hitscan refire tracer keeps its endpoints. No suspended bullet/projectile display, per-weapon absorb override, passive or replay variant. |
| Legion Gun Shield | `scripts\vscripts\weapons\mp_titanability_gun_shield.nut:10–12,145–192,252–302` declares world/FP/break PCFs, start/stop and break cues, `gun_shield` + 180° roll and a health CP. `_vortex.nut:1569–1640,1737–1799` distinguishes bullet and default projectile impacts. `_particle_wall.gnut:384–390` in the local `mods\Northstar.CustomServers\mod\scripts\vscripts\weapons\` copy corroborates the remaining-health inversion; it is not an independent retail-file check. | Keep synced gun attachment transform and shade by remaining shield health. Separate bullet and projectile/splash PCFs, and a distinct break cue near animated `PROPGUN` (fallback: shield transform). The retail default `TitanShieldWall.{Light,Heavy,Explosive}.BulletImpact_3P_vs_3P` events did not resolve in the installed `general.mbnk`; normal Legion impacts are FX-only rather than replaying Ion audio. Per-projectile overrides are not implemented. |
| Scorch Thermal Shield | `scripts\vscripts\weapons\mp_titanweapon_heat_shield.nut:20–24,54–65,200–238` gives world/FP sustain, absorb ping and the start/end event names. `scripts\weapons\mp_titanweapon_heat_shield.txt:39–42` declares the loops; `_vortex.nut:880–901` chooses `heat_shield_stop_bullet` vs. `heat_shield_stop_projectile`. | Sustained world PCF with existing synced placement, start/loop/end audio, distinct Thermal Shield ping and interception sounds. The mounted pilot gets 1P lifecycle audio. Target burn decals/particles and victim-side audio are separate, untouched damage presentation. |
| Ronin Sword Block | `scripts\vscripts\weapons\mp_titanability_basic_block.nut:78–109,123–158,277–280,319–344` uses the draw cue, no ordinary world shield surface, optional Sword Core glow on `sword_edge`, and `PROPGUN`-relative `P_impact_xo_sword` with white CP1 and the impact cue. `scripts\vscripts\_utility_shared.nut:1184–1187` verifies `AngleToDot(150)` is **cos(150°)**, not cos(75°). | The integrated presenter removes the synthetic guard ring. Play the draw cue and gate the hit FX/audio to the retail arc and actual blocked damage, without altering the mount's damage rules. Retail +200-unit hit offset and 90° pitch are applied. No new authored block pose, flinch animation, cockpit sword rig or Sword Core glow. |

Tone stays unchanged. Its retail weapon wrapper delegates to `CreateParticleWallFromOwner` (`scripts\vscripts\weapons\mp_titanability_particle_wall.nut:25–50`), and the matching local `_particle_wall.gnut` implementation is in a Northstar server overlay; it is not needed for this handheld-defense pass.

## Integrated call sites

The Kit update calls `UpdateDefensePresentation()` before the host/proxy split; disable/destroy call `ClearDefensePresentation()`. The old visual root and both duplicate Thermal Shield one-shots were removed. The accepted Ronin guard reduction path calls `PresentRoninBlockImpact` without changing the damage scale or applicability.

Persistent local presentation tracks the synced guard state, with one root/emitter per visible kit; death, termination, disable, and observed pilot changes tear it down. An observer joining mid-guard may hear the entry cue once when first observing it. Start/loop binding uses the active local seated pilot for audio perspective, but the world effect is used for both perspectives: no separate unverified local `vortex_center`/FP gun rig. Instant impacts remain host-dispatched, capped to one per 60 ms per kit (break bypasses the cap).

## Asset existence and verification

`D:\Game\steams\steamapps\common\Titanfall2` verified through `Titanfall2.Tests`: the following roots and their full child graphs parse from the named retail VPK PCF; each selected cue has an exact, audible layer definition in `r2/sound/general.mbnk`. This checks data, not final sound/visual output in a live scene.

| Library under `particles/` | Roots used |
| --- | --- |
| `wpn_vortex_titan_cp.pcf` | `wpn_vortex_chargingCP_titan` |
| `wpn_vortex_titan.pcf` | `wpn_vortex_shield_impact_titan` |
| `tracer_fx.pcf` | `weapon_tracers_vortex` |
| `anti_titan_shield_fx.pcf` | `P_titan_gun_shield_3P` |
| `xo_armor.pcf` | `P_xo_armor_break_CP` |
| `impact_fx.pcf` | `P_impact_xo_shield_CP`, `P_impact_xo_sword` |
| `impact_fx_exp_med.pcf` | `P_impact_exp_med_xo_shield_CP` |
| `wpn_vortex_titan_heat.pcf` | `P_wpn_HeatShield`, `P_wpn_HeatShield_impact` |

The loader uses the existing shared catalog. If the catalog is still cold, short-lived unmapped PCFs are not guaranteed on the first shield use; the exact library table above is suitable for a separately owned prewarm mapping. Live first-person/third-person, multiplayer ordering and per-PCF renderer fidelity are not yet accepted. No package was deployed.
