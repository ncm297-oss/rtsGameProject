# BUG-0082: With a tree felled every tick the 32-group scene averages 0.81 ms a tick, over M3-2b's 0.5 ms criterion; the perf row only bounds it relatively

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-06-1744, task M3-2b |
| System | movement build pass / flow fields (perf) |
| Fixed by | eec51e2 (M3-2b fix round 1), verified 2026-10-06-1744 re-check |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GridChangeMovementTests.OneTreeFallsEveryTick_AverageTick_Perf" --logger "console;verbosity=detailed"`
   (run alone): 32 walkers on 32 cached goals, 120 x 72 flat map, one far tree felled before each of 90 timed ticks.

## Expected
M3-2b criterion 3: "avg tick on that scene under 0.5 ms (report the before / after)".

## Actual
QA, Debug, quiet machine, three rounds each, same scene at both commits:
```
a80c143 (before): felling 1.759 / 1.730 / 1.735 ms (rerun 1.792 / 1.805 / 1.779), calm 0.016-0.019 ms
28d78f8 (after):  felling 0.807 / 0.806 / 0.835 ms (rerun 0.827 / 0.802 / 0.812), calm 0.016-0.017 ms
dev Perf row:     avg tick with a tree felled every tick: 0.810 ms; calm 0.016 ms; 2 builds + 1 step pass alone 0.745 ms
```
A 2.2x improvement, but 60% over the criterion. The cost is the cap itself: while felling goes on, the 2
builds a tick are spent on refreshes (2 x ~0.33 ms on 120 x 72) plus one step-mask pass.

The row the dev added asserts `felling < calm + 1.5 x (2 builds + 1 step pass)` = about 1.14 ms: 0.33 ms of
slack, about one more build. It catches a step-mask regression (the old pass was 1.5 ms) but not a third
build a tick, and nothing asserts the 0.5 ms target.

## Notes
- The developer reported this miss. It is far inside the docs/03 design budget (4 ms average for 500 units),
  so the Producer may well amend the criterion instead; if so, pin the accepted number as an absolute bound
  (e.g. < 1.2 ms in Debug) beside the relative one.
- Ways under 0.5 ms if wanted: refresh at most 1 field a tick when there are no misses, refresh only fields
  whose route the opened cells can shorten (a cell opened next to a cell the field reaches), or time-slice
  builds (BUG-0023).
- Also noted (no separate bug): `DeterminismTests.ChoppingMarchingAndBuildingDrops_...` fells only 2 trees in
  3,000 ticks and asserts `felled >= 2`, so its replay round trip covers few opening changes.

## Verification (QA re-check, 2026-10-06-1744, eec51e2)
- Fix: `CellQueue` is now storage for a three-bucket ring with lazy deletion, `FlowField.Build` drives it
  inline with the eight directions unrolled and directions picked at relax time, `ComputeSteps` slides a
  3 x 3 window, `NavGrid.Flags` returns the array. The Perf row now asserts an absolute `< 0.5 ms`.
- Perf row run alone, six times: 0.399 / 0.394 / 0.396 / 0.406 ms (calm 0.014-0.015 ms; 2 builds + step pass
  0.350-0.364 ms, step pass 0.045-0.048 ms). A third build a tick (about 0.15 ms) would take it over 0.5 ms.
- Run while the full non-Perf suite was running on the same machine: 0.86 / 1.18 / 0.88 ms, FAIL (calm doubled
  too). That's CPU contention, and Perf rows run alone, but the 20% headroom is thinner than most Perf rows have.
- Results are unchanged: `QA/FlowFieldBuildEquivalenceQaTests` compares the new build bit for bit (cost bits,
  direction, resolved target, step masks) against a verbatim copy of the a80c143 build
  (`QA/LegacyFlowFieldA80c143.cs`) on about 44,000 fields over 17 grid sizes from 1x1 to 257x131, random
  plateaus, blocked targets, pockets walled off by placed nodes and 1-4 sided buildings, node churn, a reused
  field and queue, a 1024 x 1024 open map (costs up to 1,444) and a serpentine. All identical. A tie-break
  mutation in the reference (`<` to `<=`) fails 11 of 19 rows.
- Trajectories: per-tick unit digests and chained StateHash identical at c7a1d1a and eec51e2 for
  CrossMapScenario seeds 1-8 (1,500 ticks), MoreGoalsThanCacheSlots and ToFourPoints (500) seeds 1-3, the
  32-group felling scene (600 ticks, 262 builds) and the grid-change fuzz seeds 1-3 (2,500 ticks, 2,646-2,844
  builds). The replay golden isn't touched by eec51e2.
