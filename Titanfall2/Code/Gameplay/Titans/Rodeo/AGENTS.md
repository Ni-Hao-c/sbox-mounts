# External Titan riders

- `TitanRodeo` owns one external rider independently of the cockpit. It supports only the local/listen host, matching `TitanSeat`; never remove authority/owner checks to imply remote driving support.
- Do not parent the rider to the Titan root: `BaseChair` discovers child players as occupants, and recursive Titan destruction must not destroy the pilot.
- `TitanRodeoMoveMode` owns the rider's rigidbody transition and follows the animated `hijack` attachment with `PlayerSolid` hull sweeps. Keep the collider available for damage. Restore the captured body state on all exits.
- Enemy action depends on whether the pilot carries a battery, not a Titan battery counter: empty hands steal, carried battery throws a grenade and retains that battery. Friendly insertion consumes the battery only after a successful host reward and enters an armed idle ride.
- Only the host clock may consume `Titanfall2RodeoCommitGate`. Visual animation callbacks never apply damage or grant batteries. Missing blocking animations cancel the interaction; an available clip without its expected event uses an explicitly diagnosed clip-completion fallback.
- Original `rodeoBatteryRemoval` damage does not declare `DF_BYPASS_SHIELD`; send it through the ordinary damage pipeline. Ordinary batteries heal 1250 in the current health pool, including the doomed reserve, fill shields and grant 20% core without reviving the Titan out of doomed state.
- Reference: `D:/northstar/scripts/vscripts/rodeo/sh_rodeo_titan_anim.nut`, `D:/northstar/mods/Northstar.CustomServers/mod/scripts/vscripts/rodeo/_rodeo_titan.gnut`.
- Verification: focused `TitanRodeoTests`, plus native `--titan-interactions` after building current runtime validation. Pure tests cannot establish physical attachment or camera/animation acceptance.
