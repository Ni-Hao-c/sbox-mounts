# Code/Gameplay/FrontierDefense/ — Harvester & FD AI Titan Roles

Implements Frontier Defense objective mechanics (`Titanfall2Harvester`) and specialized Enemy-only AI Titan behaviors.

## Files
- `Npc.FrontierDefense.cs` — `Titanfall2Harvester` objective component and standalone `Titanfall2NpcFrontierController`: `ArcTitan` (arc aura + arc cannon using Ronin gun world model), `NukeTitan` (harvester rush + nuclear ejection), `EliteMonarch` (tight `0.3x` spread + defensive cadence).
- `Npc.Mortar.cs` — `Titanfall2MortarPosition`, `Titanfall2MortarMissile`, and standalone `Titanfall2NpcMortarController` state machine (stand-to-kneel, mortar spot occupancy, 7-rocket cycle synced to `AE_NPC_WEAPON_FIRE` animation events, break-out recovery when damaged or flanked by enemy Pilot).
- `MortarWeaponData.cs` — Retail mortar rocketstream weapon profile loader (`mp_titanweapon_rocketeer_rocketstream`, cached via `GameplayDataCache`).

## Invariants & Pitfalls
- **Enemy-only mount**: Frontier Defense Titans (`arc_titan`, `nuke_titan`, `elite_monarch`, `mortar_titan`) are registered under `entities/titans/enemy/` with `DualFaction = false` and have NO `Titanfall2TitanSeat` (non-drivable).
- **Custom loop instead of engine schedules**: Actors with `Role != FrontierRole.None` intentionally skip `NavigationLayer` / `UsesEngineSchedules` and tick their specialized FD state machines directly in `Titanfall2Npc.OnUpdate`.
- **Mortar event fidelity**: `Validation/TitanFidelity.cs` verifies 7 `AE_NPC_WEAPON_FIRE` events and `MortarShotsFired == 7` per volley — preserve animation-event-driven launch timing.
