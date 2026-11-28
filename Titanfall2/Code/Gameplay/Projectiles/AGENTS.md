# Code/Gameplay/Projectiles/ — Swept Flights, Ordnance & Guidance

Host-driven swept projectile simulation for Pilot bolts/rockets/ordnance, NPC integrated plasma weapons, and Titan ordnance/clusters.

## Files
- `Projectile.cs` — `Titanfall2Projectile`: swept ray/hull flight, gravity, occluded splash damage, retail `Titanfall2ProjectileBehavior` handlers (`Standard`, `HomingTitan`, `ArcZap`, `FragGrenade`, `ArcGrenade`, `ThermiteBurn`, `GravityPull`, `ElectricSmoke`, `RemoteCharge`), and continuous `FlightSound` looping (e.g., `weapon_epg_projectile_loop` for EPG & Arc Launcher).
- `PlasmaFlight.cs` — Plasma projectile trail/light visuals and fallback glowing streaks when retail uses an empty bolt model.
- `TitanWeaponCluster.cs` — Northstar Cluster Missile sub-munition detonation and area damage.
- `TitanWeaponGuidance.cs` — Homing/tracking missile steering (Archer, Tone Tracker Rockets, Salvo Core).

## Invariants & Pitfalls
- **Snapshot-at-launch**: Projectiles snapshot their `DamageProfile` and `Titanfall2ProjectileSettings` on spawn — holstering, switching, or reconfiguring the source weapon mid-flight must not mutate in-flight projectiles.
- **Occluded splash**: Explosion/splash damage must trace cover via `Titanfall2CollisionQueries.Run` (`Shot` mask) so walls block blast damage.
- **Self-hit EMP**: `ArcGrenade` detonation must apply `EmpStatus` to the throwing player when within radius.
- **Flight audio cleanup**: Always stop looped `FlightSound` emitters on impact, detonation, timeout, or `OnDestroy`.

## Verification
```powershell
dotnet test Titanfall2/Tests/Titanfall2.Tests.csproj -c Release "-p:SboxEngineRoot=$env:FACEPUNCH_ENGINE" --no-restore --filter "FullyQualifiedName~OrdnanceTests|FullyQualifiedName~WeaponBugRegressionTests|FullyQualifiedName~TitanWeaponTests"
```
