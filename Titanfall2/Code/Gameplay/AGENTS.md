# Code/Gameplay/ — Modular Gameplay Layer

Implements all Titanfall 2 runtime gameplay systems on top of s&box `BaseCombatWeapon`, `BaseCombatNpc`, `PlayerController`, and `MoveMode`.

## Layer Boundary
- **Depends on**: `Code/`, `Content/`, `Formats/`, `Collision/`, `Resource/`, `Components/`.
- **Must not leak downward**: Lower layers (`Formats`, `Content`, `Collision`, `Rendering`, `Resource`, `Components`) must never depend on `Gameplay/`.
- **Global namespace preserved**: Public gameplay `Component` classes stay in the global namespace to preserve `.scene` / `.prefab` serialization compatibility.

## Cross-Domain Rules
- **Host authority**: Damage, ammo, health/shield, projectile flights, NPC AI, Titan states, and EMP/Phase status mutate on host (`Networking.IsHost && !IsProxy`) and replicate via `[Sync(SyncFlags.FromHost)]` or `[Rpc.Host]`.
- **Collision masks**: Use `Titanfall2CollisionQueries.Run(...)` with retail `Titanfall2TraceMask` constants rather than raw `Scene.Trace`.
- **Data-driven JSON overlays**: Catalogs support optional overrides from `scripts/titanfall2_sbox/*.json` (mod/VPK) or `FileSystem.Data/titanfall2/data/*.json` via `Titanfall2RetailScriptReader.TryReadJsonOverlay<T>`.
- **Pure rule extraction**: Keep math/state-machine rules in stateless `*Rules.cs` / `*Data.cs` types so `Tests/` can cover them without instantiating a full s&box `Scene`.

## Domain Map
| Subfolder | Scope | Guide |
| --- | --- | --- |
| `Catalog/` | Prefab registration, retail script reader, JSON overlays, prefab loaders | [Catalog/AGENTS.md](Catalog/AGENTS.md) |
| `Pilot/` | Jump Kit parkour (wallrun/wallhang/3P poses), combat, input interception, tacticals | [Pilot/AGENTS.md](Pilot/AGENTS.md) |
| `Weapons/` | Pilot/Titan weapon scripts, mechanics, runtime viewmodel, diegetic UI | [Weapons/AGENTS.md](Weapons/AGENTS.md) |
| `Npc/` | `Titanfall2Npc` AI, factions, schedules, tactics, ordnance, melee, voice, animation | [Npc/AGENTS.md](Npc/AGENTS.md) |
| `Titans/` | Drivable chassis, 7 kits, 7 cores, physical defense volumes, cockpit HUD/view | [Titans/AGENTS.md](Titans/AGENTS.md) |
| `Projectiles/` | Swept projectile flights, plasma bolts, cluster missiles, homing guidance | [Projectiles/AGENTS.md](Projectiles/AGENTS.md) |
| `FrontierDefense/` | Harvester objective, Arc/Nuke/Elite Monarch/Mortar Titan AI roles | [FrontierDefense/AGENTS.md](FrontierDefense/AGENTS.md) |
| `Shared/` | Cross-domain combat (`EmpStatus`, `PhaseDimension`, `HostDamage`), audio, animation, prewarm | [Shared/AGENTS.md](Shared/AGENTS.md) |
