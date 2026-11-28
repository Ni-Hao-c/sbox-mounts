# Code/Gameplay/Shared/ — Cross-Domain Gameplay Foundations

Shared combat mechanics, audio emitters/aliases, animation players, and prewarm helpers reused across `Pilot/`, `Weapons/`, `Npc/`, `Titans/`, and `Projectiles/`.

## Subfolders & Key Files
- `Combat/`:
  - `HostDamage.cs` — Verified host-side damage attribution (`Titanfall2HostDamage.ShootBullet`, `Observe`, `Consume`) distinguishing Pilot vs Titan armor damage.
  - `CombatEffects.cs` — Retail impact-table material classification, native spark/smoke/flash particles, bounded decal spawning (max 128 decals, 64 impact bursts).
  - `EmpStatus.cs` — Retail Arc Grenade / EMP status (`50%` move slow, `35%` turn slow via `ShortestAngleDelta`, view jitter, fullscreen scanline/vignette HUD, audio, cleansed by Phase Shift).
  - `PhaseDimension.cs` — Shared Phase Shift / Ronin Phase Dash dimension isolation and safe exit overlap resolution.
  - `GroundWave.cs` — Advancing floor-following waves (Scorch Firewall & Ronin Arc Wave) that stop at walls/ledges.
- `Audio/`:
  - `ImpactAudioAliasCatalog.cs` — Data-driven impact sound lookup (`audio_aliases.json` overlay).
  - `SoundEventEmitter.cs`, `DelayedWeaponSound.cs`, `OrdnanceSounds.cs`.
- `Animation/`:
  - `PilotAnimationPlayer.cs` — 1P weapon/arms sequence player (supports `idle_weight` scaling to suppress hipfire sway in ADS).
  - `ActorAnimationPlayer.cs` — 3P NPC/Titan layered locomotion + aim-grid + fire/reload/flinch sequence player.
- `Prewarm/`:
  - `WeaponParticleCatalog.cs` — Maps named weapon/NPC PCF systems (e.g., `P_sup_spectre_death`, `P_plasma_proj_MD_DLight`) to `.pcf` files.
  - `WeaponPrewarm.cs`, `WeaponPrewarm.Resources.cs`, `WeaponFont.cs`.
  - `AnimationResourceCache.cs` — Mount-scoped immutable interaction Model/Skeleton/Clip cache. Prewarm and playback share successes and failures; reset with the mount. Never retain scene objects or share mutable playback Graph instances.

## Invariants & Pitfalls
- **No upward dependency bloat**: Keep `Shared/` utilities decoupled from specific prefab loaders or UI layouts where possible.
- **AG2 state budget**: A state machine has at most 64 states; the actor selector uses two slots per clip. Keep upper-body Titan actions out of the body selector on rigs where the action overlay already owns them.
- **Bounded scene FX**: Maintain strict caps on decals (`<= 128`) and impact bursts (`<= 64`) in `CombatEffects.cs`.
- **EMP self-hit**: Arc Grenade splash applies `EmpStatus` to self as well as hostile players/NPCs, and must cleanse cleanly on Phase Shift or death.
