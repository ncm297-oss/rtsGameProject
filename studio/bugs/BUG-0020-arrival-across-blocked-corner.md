# BUG-0020: A unit "arrives" across a blocked corner: arrival is a straight-line check, not a path check

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-04-0120, task M1-4b |
| System | movement (MovementSystem arrival) |
| Fixed by | a5f81af (M1-4b fix round 1) |

## Repro
1. Remove the `Skip` from `QA.MoveQaTests.DiagonalNeighborAcrossBlockedCorner_UnitDoesNotArriveThroughTheCorner`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DiagonalNeighborAcrossBlockedCorner"`

Seed 0 default map: cells (63,61) and (64,62) are passable, (64,61) and (63,62) are blocked, so the
two cells touch only at the corner (128, 124) m and no diagonal step joins them. Spawn a unit at
(127.7, 123.7), order a Move to (128.3, 124.3).

## Expected
The unit walks the long way round (the flow field gives 25.9 cells of path) and stops at the goal,
or at least stays Moving. docs/03: no corner cutting past blocked cells.

## Actual
```
seed 0 corner (63,61): path around costs 25.89947 cells; unit state Idle at <127.7, 123.7>, goal <128.3, 124.3>
```
The unit never moves: it is 0.85 m from the goal, under `ArrivalDistance` (1 m), so it is declared
arrived on the first movement tick, on the wrong side of a cliff corner.

## Notes
`MovementSystem.Run` tests `DistanceSquared(pos, goal) <= ArrivalDistance^2` before anything else.
Only diagonally-touching cells are affected (4-adjacent passable cells are always one straight step
apart). A fix could require the unit to be in the goal cell, or in a cell with an allowed step into
it, before the distance check counts. Matters more once attack-move and gather orders use arrival.

**QA verification (2026-10-04-0120, M1-4b fix round 1):** fixed. Arrival is now checked only
inside the goal cell. The un-skipped `DiagonalNeighborAcrossBlockedCorner_UnitDoesNotArriveThroughTheCorner`
passes. Inside the goal cell the unit steps straight at a goal in the same convex cell, so the new
rule can't strand it. `BuildCap_500UnitsWith500DistinctGoals_EveryUnitArrives_NoDeadlock` ends with
all 500 units within ArrivalDistance of their goals.
