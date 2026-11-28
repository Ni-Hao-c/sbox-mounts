# Code/Gameplay/Npc/ — Combat NPCs, Factions & AI

Implements `Titanfall2Npc : BaseCombatNpc, Component.IPressable` supporting ground infantry/robots, stationary turrets, hover drones, melee wildlife, Frontier Defense units, and Titan bases.

## Files
- `Npc.cs` — Core actor component, `Faction`/`Friendly`/`AttackPlayers` relationship matrix, `IsAllyFaction`/`IsNeutralFaction`/`IsHostileToFaction`, follow-leader (`E` key for friendly ground NPCs), target acquisition, hover movement, `Fire()`.
- `Npc.Subsystems.cs` — Thin `Titanfall2Npc` shell bridge owning `[Property]`/`[Sync]`/`[Rpc]` contracts and delegating mutable state/logic to 6 standalone controllers (`_mortar`, `_frontier`, `_ordnance`, `_tactics`, `_melee`, `_animation`).
- `Npc.Schedules.cs` — `Titanfall2EngageSchedule` integrating with s&box `SensesLayer` and `NavigationLayer` (`UsesEngineSchedules` for ground non-FD actors).
- `Npc.Tactics.cs` (`Titanfall2NpcTacticsController`), `NpcTacticsRules.cs` — Cover reload, retreat when too close, squad callouts, last-known search (6s memory), friendly-line-of-sight hold-fire, formation offsets.
- `Npc.Ordnance.cs` (`Titanfall2NpcOrdnanceController` + `Titanfall2NpcGrenadeData`) — Retail NPC frag grenade throwing with friendly-fire recheck and arc solver.
- `Npc.Melee.cs` (`Titanfall2NpcMeleeController`) — Telegraph windup + re-swept melee attacks (Reaper groundslam, Stalker swipe, Prowler, Titan melee).
- `Npc.Animation.cs` (`Titanfall2NpcAnimationController` + `Titanfall2LocomotionSelector`), `Npc.Flinch.cs` — Layered retail sequence playback (`Titanfall2ActorAnimationPlayer`) and additive small-flinch cues.
- `Npc.Voice.cs`, `NpcVoiceCatalog.cs` — Data-driven voice lines (`voices.catalog.json` overlay).
- `Npc.SmartAmmo.cs`, `Npc.DeathEffects.cs`, `Npc.Prewarm.cs` — Smart-ammo lock classification, robot/turret death PCF effects, weapon prewarming.
- `Npc.AiProfile.cs` — Retail mode/perception/primary burst settings. `AiMode` is explicit: faction alone does not assign an Auto-Titan owner or follow/guard mode.
- `Npc.Traversal.cs`, `NpcTraversalRules.cs`, `Titanfall2NpcNavigationLayer.cs` — Typed roof links, capability checks, swept crossing, root-curve presentation, landing and route resumption. Ordinary Titans have no infantry roof-jump capability.

## Invariants & Pitfalls
- **Serialized `Friendly = false` semantics**: Setting `Friendly = false` when `Friendly` is already `false` MUST be a no-op so custom factions (`"wildlife"`, `"neutral"`) are not overwritten with `"imc"`.
- **Faction groups**:
  - Ally (`IsAllyFaction`): `"militia"`, `Factions.Ally`, `Factions.Player`, `Factions.Citizen`.
  - Neutral (`IsNeutralFaction`): `"neutral"` (passive MRVNs; hostile to nobody).
  - Hostile: `"imc"` (allied with `Factions.Enemy`), `"wildlife"` (Prowlers; hostile to both IMC and Militia/Player).
- **Titan `IPressable` dispatch**: When `IsTitan` is true, `Titanfall2Npc`'s `IPressable` must delegate to `Seat` so hover/press always targets the cockpit cleanly.
- **Validation reflection**: Keep `LayeredAnimation`, `MeleePending`, `_gun`, and `_nextShot` contracts intact for `Validation/`.
- **Action ownership**: `AiOwnsMovement` prevents generic tactics/schedules from restarting navigation during traversal, dash, flight or a full-body Titan skill. NPC Sword Core attacks are selected by the Titan director; the generic primary-fire loop must not bypass its range/combo checks.
- **Cancellation**: Normal airborne traversal cancellation retains movement ownership until a swept landing. Death, disable, destruction, termination and pilot takeover use immediate handoff. Always clear pending ordnance/melee and Titan actions on those lifecycle transitions.
- **Perception provenance**: `VisualContactAgeSeconds` is separate from shared contact age. Squad reports must not reset direct sight age or provide a hidden target's live facing/velocity.

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~Npc|FullyQualifiedName~ActorAnimationTests|FullyQualifiedName~ActorFlinchTests|FullyQualifiedName~PilotViewModelTests"
```
