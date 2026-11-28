# Code/Gameplay/Catalog/ — Registries, Overlays & Prefab Loaders

Builds and registers all spawnable gameplay `PrefabFile`s (`weapons`, `items`, `npcs`, `titans`, `titanweapons`) from retail scripts and optional JSON overlays.

## Files
- `GameplayCatalog.cs` — `Titanfall2Mount.AddGameplayRegistrations`: registers weapons, Jump Kit (`entities/items/pilot_jump_kit`), NPCs, Titans, and Titan armaments into the mount catalog.
- `GameplayCatalogData.cs` — `Titanfall2GameplayCatalogData` (`DefaultWeapons`, `DefaultActors`), JSON overlay merging (`weapons.catalog.json`, `actors.catalog.json`), `DualFaction` expansion, `ActorSideBucket`, `ActorPrefabPath`, `FormatActorTitle`.
- `GameplayDataCache.cs` — Session-scoped cache for parsed retail scripts (`NpcGrenadeData`, `MortarWeapon`, etc.), reset on `Titanfall2Mount.Shutdown()`.
- `RetailScriptReader.cs` — Reads snapshot retail text scripts and JSON overlays (`scripts/titanfall2_sbox/*.json` -> `FileSystem.Data/titanfall2/data/*.json`).
- `NpcAiProfile.cs`, `NpcAiProfileCatalog.cs` — Mode-aware NPC/Titan AISettings and skill weapon profiles, with typed use checks, selector declarations, source identities and explicit missing/unsupported-data diagnostics. The bounded `Formats/Scripts/NpcAiKeyValues.cs` reader preserves `#base` inheritance and conditional SP/MP blocks; weapon `Mods` apply only when explicitly requested.
- `ActorPrefabLoader.cs` — `Titanfall2ActorPrefabLoader`: builds NPC, Frontier Defense, and drivable Titan prefabs.
- `WeaponPrefabLoader.cs` — `Titanfall2WeaponPrefabLoader` & `Titanfall2TitanArmamentPrefabLoader`.
- `JumpKitPrefabLoader.cs` — `Titanfall2JumpKitPrefabLoader` (`entities/items/pilot_jump_kit`, tagged `item`, `equipment`, `pilot`).
- `PilotTacticalPrefabLoader.cs` — Four consumable tactical pickups (`entities/items/pilot_tactical_{stim,grapple,phase,pulse}`), registered when the respective retail weapon script and a visible world model exist. Empty-handed retail models are not usable pickup visuals; Stim/Grapple/Phase use a retail pack, Pulse uses the kunai.

## Invariants & Pitfalls
- **Ally / Enemy / Neutral mount layout**:
  - NPCs register under `entities/npcs/{ally,enemy,neutral}/<id>` (15 ally, 15 enemy, 2 neutral MRVNs).
  - Titans register under `entities/titans/{ally,enemy}/<id>` (8 ally drivable, 8 enemy drivable, 4 enemy Frontier Defense).
  - Titles are prefixed `[Ally] `, `[Enemy] `, `[Neutral] ` for clean grouping in s&box's alphabetically sorted NPCs menu; loaders carry the side tag (`ally`/`enemy`/`neutral`) alongside `npc`/`titan`/`frontier-defense`/`humanoid`.
  - Shared models register only one `.infantry` and one `.preview` `ModelLoader` per distinct `.mdl`.
- **Archetype matching in `ActorPrefabLoader`**: Match Reapers/Drones/Turrets/MRVNs by model path or `Id.StartsWith(...)` (never exact `Id == "npc_reaper"`), so `*_ally` / `*_enemy` variants inherit exact `SmartAmmoLockType`, `MeleeDamage`, and `MeleeRange`.
- **AttackPlayers**: Derive from `!IsAllyFaction(faction) && !IsNeutralFaction(faction)`.
- **AI data lifetime**: AI profile caches are partitioned by immutable `Titanfall2ContentSnapshot` through weak keys. Never reuse a profile from another snapshot or describe a mod-winning `SourceId` as retail; each profile records actual selected paths, source IDs and identities.
- **AI modes and execution**: `EnemyMp`, `EnemySp`, ordinary Auto-Titan and Enhanced Auto-Titan are distinct policies. A friendly faction alone is not proof of a player-owned Auto-Titan. Selector lists describe authored candidates, not a complete native scheduler; unsupported selectors/conditions must remain observable. `EvaluateUseCheck` distinguishes denial, direct qualification and the need for further geometry; effect commitment must recheck live eligibility without spending twice.

## Verification
```powershell
$env:TF2_GAMEPLAY_TEST_GAME = "D:\Game\steams\steamapps\common\Titanfall2"
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~GameplayTests|FullyQualifiedName~DataDrivenCatalogTests"
```
