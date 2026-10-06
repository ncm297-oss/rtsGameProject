# BUG-0034: `Perf_500Units_DistinctTargetsInterleavedBySlot_CostPerTick(32)` failed once in a full suite run (4.45 ms vs < 4 ms)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-05-1013, task M1-4d-2 |
| System | test suite (Stress/MovementStressTests, Serial perf) |
| Fixed by | M1-4d-3 (0a71412): the row averages 100 ticks; budget unchanged |

## Repro
1. `dotnet test sim/Rts.Sim.Tests` (full suite) on the workstation at `5d854cb`. Seen in 1 of 3
   full runs (two in the studio worktree, one in a fresh clone).

## Expected
The full suite is green in one run (M1-4d-2 criterion 10; the BUG-0017/0024 Serial-collection fix).

## Actual
```
Failed Rts.Sim.Tests.Stress.MovementStressTests+Serial.Perf_500Units_DistinctTargetsInterleavedBySlot_CostPerTick(distinctTargets: 32)
500 units, 32 distinct targets: 4.45 ms/tick, ~2 field builds per tick
Failed!  - Failed: 1, Passed: 982, Skipped: 10, Total: 993, Duration: 2 m 37 s
```
Run alone the same row measures 1.53-1.63 ms/tick; the next two full runs were green.

## Notes
- The test averages only 10 ticks, each dominated by 2 flow-field builds, so one slow tick (GC
  or a background process) moves the average by a third. Probably not caused by shoving (no
  Idle units stand in the way in this scenario), but it now blocks "green in one run" now and then.
- Suggest averaging more ticks or reporting a median, without raising the 4 ms budget.

## Fix (QA verified at 2026-10-05-1609 (M1-4d-3, commit 0a71412))
`MovementStressTests+Serial.Perf_500Units_DistinctTargetsInterleavedBySlot_CostPerTick` averages 100
ticks; alone it measured 0.56 ms (32 targets), 0.90 ms (33), 3.05 ms (64) against the 4 ms budget.
