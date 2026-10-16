# BUG-0732: Surround rule nits: an unreachable open spot is claimed nearest-first; another owner's claims count as held

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-15-2325, task M5-P1 |
| System | combat / surround rule (`Combat/SurroundRule`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SurroundQaTests.ASealedPocketOnTheNearFace_Measured" --logger "console;verbosity=detailed"`:
   the target House's west face cells are sealed in by three of the attackers' own Houses (store seam), 20 Heavy
   Infantry ordered from the west, 600 ticks.
2. `SurroundQaTests.TwoOwnersOnOneHouse_BothStrike` (three seats, players 0 and 2 both attack player 1's House).

## Expected
1. Attackers take the reachable spots (north, south, east faces: 14 of them) and keep the order.
2. Each owner's spots are judged by the ground, not by a rival's claims.

## Actual
1. `rule off: 3 / 20 struck, 16 dropped the order; rule on: 12 / 20 struck, 8 dropped the order`. The 4 pocket spots are
   `Open` (passable cell) and nearest, so they are claimed first; their claimers stall, `QueuedAtRing` returns false (own
   claim kept), `ReachableOnFoot` says no route, and they give up; the next waiter claims the freed pocket spot and does
   the same.
2. `player 0 12 / 12 struck, player 2 8 / 12 struck`: `HeldByOther` doesn't compare owners, so player 2 sees player 0's
   claims as held (and `full`), while `Occupied` only counts own units.

## Notes
- (1) needs a sealed pocket next to a footprint, which the placement rules (NeverSeal, the pocket rule) don't allow in
  play; only store-seam scenes make one. A reachability filter on the pick (the cached field at the unit's cell, as
  `ReachableOnFoot`) would fix it and BUG-0731 together.
- (2) only matters with 3-4 seats; cheap to fix (compare `Owner[h]` in `HeldByOther`).
