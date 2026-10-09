# BUG-0276: Two Perf-category rows fail on the M4-3b branch: the explored rule breaks their setup (full `dotnet test` red)

| Field | Value |
| --- | --- |
| Severity | S2 (the full `dotnet test sim/Rts.Sim.Tests` is red; CLAUDE.md "Definition of done" 1) |
| Status | open |
| Found | 2026-10-09-0125, task M4-3b (QA full) |
| System | tests (placement on explored ground) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HundredUnaffordableBuildsAtALongDetourAnchor|FullyQualifiedName~ProductionScene_At1x2x5x_Report"`
   on a86fd01. Both fail in under 60 ms, deterministically, with no load involved.

## Expected
Green, as at the diff base 1a9925b: both are setup assertions, not timings.

## Actual
- `SimHardeningTests.HundredUnaffordableBuildsAtALongDetourAnchor_OneTick_Under2Ms` (`[Trait("Category","Perf")]`),
  line 43: `Assert.Equal() Failure: Expected: CannotAfford, Actual: Unexplored`. The House anchor at (64, 1) is 44+ cells
  from the workers, so player 0 hasn't explored it.
- `Stress/ProductionScaleStressTests.ProductionScene_At1x2x5x_Report`, line 41: `Expected: 20, Actual: 5`. The scene finds
  only 5 `CanPlace`-legal Holy Camp anchors for player 1, because the rest are unexplored.

Full run on the branch (Perf included): `Failed: 2, Passed: 4288, Skipped: 14, Total: 4304` (with QA's new tests).

## Notes
The developer ran only `Category!=Perf`, so these two rows never ran. Other Perf rows were adapted (`ConstructionPerfTests`,
`ProductionPerfTests`, `RequirementPerfQaTests`). Each fix is one line: wrap the scene's `new Simulation(...)` in
`TestSim.Explored(...)` (`SimHardeningTests.LongWall`, and `ProductionScaleStressTests.Scene` around `MoveScenario.Spawn`'s
sim), the same seam the other scenes about another placement rule use.
