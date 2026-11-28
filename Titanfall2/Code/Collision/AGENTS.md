# Code/Collision/ — Contents Masks, Queries & Diagnostics

Translates Titanfall 2 retail contents flags into s&box collider tags and provides mask-faithful ray/sweep traces.

## Layer Boundary
- **Depends on**: `Formats/` / `Resource/` metadata structs and s&box `Sandbox.Physics` / `SceneTrace`.
- **Must not depend on**: `Gameplay/` or `Components/` (Gameplay calls into `Collision/`, never the reverse).

## Files
- `CollisionContents.cs` — Retail contents bit constants and `Titanfall2TraceMask` (`Shot`, `PlayerSolid`, `ShotHull`, etc.).
- `CollisionFilter.cs` — Maps effective contents (`sprp` solid type + MDL contents - remove mask) to tags (`ttf2_contents`, `ttf2_content_<bit>`, `playerclip`, `titanclip`, `nograpple`, `nowallrun`).
- `CollisionQueries.cs` — `Titanfall2CollisionQueries.Run(...)` and `SelectTrace(...)` for contents-masked traces over imported geometry and standard scene objects.
- `CollisionSource.cs` — Provenance metadata describing whether collision came from rBSP CM, VPHY, hitboxes, or render fallback.
- `CollisionModelResult.cs` — Result record for generated physics collision models/groups.
- `CollisionDiagnostics.cs` — Console command `titanfall2_collision_look [distance] [radius]` for inspecting collision source, contents, shapes, and tags.

## Invariants & Pitfalls
- **7-tag batch cap (`TagsPerTrace = 7`)**: Filling all 8 native s&box tag slots breaks `WithAnyTags` matching in the runtime. Never increase `TagsPerTrace` above `7`; wider masks must continue batching and keeping the nearest hit.
- **Empty mask semantics**: An empty mask with `includeOtherObjects: false` must return `default`, never fall back to hitting everything.
- **Trigger-only clips**: Imported non-player-solid contents groups (e.g., bullet/titan clips, water) are triggers; `CollisionQueries.Run` uses `.HitTriggers().WithAllTags("ttf2_contents")` to evaluate them accurately.
- **Always use `Titanfall2CollisionQueries.Run`**: Gameplay traces (weapons, parkour wallrun/mantle, Titan embark) must go through `CollisionQueries` rather than raw `Scene.Trace` so `nowallrun`, `nograpple`, `playerclip`, and `Shot` masks are respected.
