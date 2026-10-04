# BUG-0019: Every Move to a blocked cell runs a full-map nearest-passable scan (500 Moves = 63 ms on 128, ~1 s on 512)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-04-0120, task M1-4b |
| System | movement (Simulation.ApplyMove) / pathfinding |
| Fixed by | a5f81af (M1-4b fix round 1) |

## Repro
1. Remove the `Skip` from `Stress.MovementStressTests.Perf_500MovesToOneCliffCell_ApplyCost`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Perf_500MovesToOneCliffCell"`

500 units, one group Move (500 commands) to the map corner (a ring cell), time the tick that applies it.

## Expected
docs/03 perf budget: 500 units, p99 tick < 8 ms. A group move is one command per unit, so a click
on a cliff with a big selection is a normal player action.

## Actual
(Debug)
- 128 x 128: the tick applying 500 Moves takes 63.4 ms.
- 512 x 512: 969 ms.

## Notes
`Simulation.ApplyMove` calls `FlowField.NearestPassable(grid, cell)` for each command whose target
cell is blocked; it scans every cell of the map (O(cells)), so the cost is commands x cells.
A 30-unit selection on the default map is ~4 ms; the problem grows with selection size and map size.
Options: search outward in rings from the target and stop at the first ring that can't beat the
best distance, and/or remember the last resolved (target cell -> goal cell) within the tick.

**QA verification (2026-10-04-0120, M1-4b fix round 1):** fixed. `NearestPassable` now searches
square rings outward and stops early. The exit test `r*r <= bestD2` is correct: it uses `<=`
because a ring-r cell at distance exactly r^2 can still tie and win on index. Out-of-grid sides
drop out through `IsPassable`'s bounds check.
- Oracle checks: QA's independent full-scan oracle matches on every cell of 400 adversarial grids
  (1x1 to 48x48, including 1xN, Nx1 and all-blocked; salt-and-pepper, k x k checkerboards with
  sealed pockets, sparse walls, flat and stripes), on 1024x6 / 6x1024 / 300x17 strips, and on
  every blocked cell of 3 generated maps. There were 0 mismatches.
- Perf on the 128 map: 500 Moves to the corner now take 0.8-1.1 ms per tick, down from 63 ms. The
  128 row of `Perf_500MovesToOneCliffCell_ApplyCost` is un-skipped.
- The 512 and 1024 rows still miss the 8 ms budget, at 13.1 and 55.1 ms. That cost is the single
  flow-field build in the timed tick, not this scan. Those rows are now skipped under BUG-0023.
