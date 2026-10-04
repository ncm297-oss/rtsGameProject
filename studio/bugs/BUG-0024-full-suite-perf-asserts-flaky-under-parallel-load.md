# BUG-0024: The full `dotnet test` run fails 11 times in 15 on this workstation: wall-clock Perf asserts flake under parallel load

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-04-0120, task M1-4b (fix round 1 re-check) |
| System | test suite (Perf category) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests` (Debug, full suite), repeated. While this ran, the owner's
   desktop apps (Chrome, Spotify, Discord, game launchers) were open, at about 27% CPU on 24
   logical cores.
2. HEAD a5f81af, plus QA's new tests in the later runs: 11 of 15 full runs had 1-3 failures, each
   time a timing or allocation assert. These failures were seen:
   - `FlowFieldTests.Build_OnDefaultMap_AveragesUnderTwoMilliseconds`: 2.04-2.70 ms against a
     2 ms limit; 0.85 ms in isolation. This was the most frequent failure.
   - `SimCoreStressTests.Flood_InterleavedCommands_UnderTickBudget(1000)`: 10.5 ms against an
     8 ms limit; 0.95 ms in isolation.
   - `MovementStressTests.Perf_500Units_DistinctTargetsInterleavedBySlot_CostPerTick(33)`:
     4.49 ms against a 4 ms limit; 0.88 ms in isolation.
   - `QA.FlowFieldQaTests.Cache_Thrash_64TargetsRoundRobin_AllocatesNothing_AndMeasuresBuildCost`:
     an `Assert.Equal` failure (values not captured, most likely the 0-byte allocation check). It
     passed in isolation. Compare BUG-0017.
3. Pre-fix commit ef69c9e, exported to a temp folder: 2 of 3 full runs failed on
   `Build_OnDefaultMap_AveragesUnderTwoMilliseconds`. So the problem predates this fix round.
4. A fresh clone of the branch with `--filter Category!=Perf` passes: 783 passed, 5 skipped.

## Expected
The full suite is green, or red only for real regressions, on the owner's normal machine.

## Actual
Wall-clock asserts tuned in isolation run while xUnit runs other test classes, including the
heavy fuzz and Soak rows, in parallel. Under that load, timings rise 2-10x.

## Notes
No threshold has been loosened, and none should be. Options for the Producer:
- Put every `Category=Perf` test, and the allocation-measuring tests, in one xUnit collection with
  `DisableParallelization = true`. They then run alone, after the parallel batch.
- Or make the definition of done two commands: `dotnet test --filter "Category!=Perf"`, then
  `dotnet test --filter Category=Perf` with parallelism off.

The developer's report says its run was green (832/0/7). That fits a run under less load. It is
not a sign of misreporting.
