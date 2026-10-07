# BUG-0074: The forest placer assumes 1 x 1 trees; a larger tree footprint (valid data) seals pockets

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-1255, task M3-1 |
| System | resource placement / data |
| Fixed by | 28d78f8 (M3-2b): the loader refuses a wood type whose footprint isn't 1 x 1; `QA/ResourceQaTests.TwoByTwoTrees_KeepTheOracle` un-skipped (refusal counts as passing) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TwoByTwoTrees_Report" --logger "console;verbosity=detailed"`
   (`QA/ResourceQaTests.cs`): a copy of the shipped data with the `tree` footprint changed to
   `{ "width": 2, "height": 2 }` (the loader accepts 1-4), `Forests = 12, GoldMines = 4`, seeds 1-20,
   checked by QA's independent `ResourceOracle`.
2. The assertion version `TwoByTwoTrees_KeepTheOracle` is skipped with this bug's id.

## Expected
docs/03 "Implementation (M3-1)": footprints come from data (1-4 cells per side), nodes only cover
open ground, nodes never overlap, and "every passable cell still reaches every other" after
placement. CLAUDE.md rule 6: no hard-coded stats, so a data-only footprint change must keep these rules.

## Actual
```
2x2 trees: 20 of 20 seeds break the oracle, 20 with a sealed pocket
seed 1: slot 67 cell (33, 49) next to Blocked, Cliff at (34, 48)
        (report ResourcePlacement { Forests = 11, Trees = 238, Mines = 4 }, live nodes 81)
```
`ResourcePlacer.TryForest` grows a blob of *cells*, checks those cells (open ground, not touching a
node, ring + flood connectivity), then calls `store.Spawn(type, cell, ...)` with the tree type's
footprint at each cell, ignoring the result. With a 2 x 2 tree:
- Each spawn covers 3 cells the checks never saw. Those cells can be next to cliffs, and they are
  outside the connectivity check, so pockets get sealed (all 20 seeds).
- Later candidates overlap earlier trees, so `Spawn` refuses them silently, and the reported `Trees`
  (238) doesn't match the live store (81 nodes).

Shipped data uses 1 x 1 trees, so nothing ships broken today.

## Notes
Either make the forest grower footprint-aware (grow in footprint-sized steps and check the full
footprint plus its ring), or have the loader / `DataLimits` require a 1 x 1 footprint for the type
the placer uses as trees and document it. Also stop ignoring `Spawn`'s return value in `TryForest`.

## Verification (QA, 2026-10-06-1744)
QA `QA/GridChangeQaTests.AWoodTypeWithANon1x1Footprint_IsOneErrorOnItsFootprint` (1x2, 2x1, 4x4, 1x4: exactly one error on `.footprint` naming the size) and `AWoodTypeWithABadFootprintSide_IsOneError_NotTwo` (0 / 5 sides, missing side, null footprint: one error, no extra wood error); a 1 x 1 gold type still loads.
