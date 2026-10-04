# BUG-0023: One flow-field build on a 512 or 1024 map blows the 8 ms tick budget (1024: 55 ms, longer than a frame)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-04-0120, task M1-4b (fix round 1 re-check) |
| System | pathfinding (FlowField.Build) / map size bounds |
| Fixed by | |

## Repro
1. Remove the `Skip` from the 512 and 1024 `InlineData` rows of
   `Rts.Sim.Tests.Stress.MovementStressTests.Perf_500MovesToOneCliffCell_ApplyCost`.
2. Run the test in isolation (Debug). Each row times the tick that applies 500 Moves to the
   blocked corner cell, which builds that goal's field once.

## Expected
docs/03 perf budget: p99 tick < 8 ms. `MapGenParams.Validate` accepts maps up to 1024 x 1024.

## Actual
In isolation, one run each:

| Map | Tick (ms) |
| --- | --- |
| 128 | 1.1 |
| 256 | 5.2 |
| 512 | 13.1 |
| 1024 | 55.1 |

The 1024 tick is longer than a whole 50 ms frame, so it's a visible stall. The ring search from
BUG-0019 is not the cost: with the field already cached, the same 500 Moves take 0.93 ms on 512
(the developer's `FiveHundredMovesToABlockedCell_On512Map_WithFieldCached_ApplyUnderEightMilliseconds`).
The cost is `MaxFieldBuildsPerTick = 1` full build of a big field inside one tick.

## Notes
The design map is 128 x 128 (docs/02), so 512 and up are not shipping sizes. That makes this S3.
docs/03 already says "time-sliced builds are future work". Options:
- Time-slice builds across ticks.
- Lower the `MapGenParams` maximum to the largest size that fits the budget.
- Document that maps above about 256 are unsupported.

The 512 and 1024 rows stay skipped under this id. The 128 row now runs.
