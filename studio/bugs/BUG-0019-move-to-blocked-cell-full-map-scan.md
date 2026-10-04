# BUG-0019: Every Move to a blocked cell runs a full-map nearest-passable scan (500 Moves = 63 ms on 128, ~1 s on 512)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-04-0120, task M1-4b |
| System | movement (Simulation.ApplyMove) / pathfinding |
| Fixed by | |

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
