# BUG-1041: M5-H4 test-harness nits: the fresh-process Perf row waits on its children with no timeout; the pairing totals pass silently on a partial run

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-26-2108, task M5-H4 (QA, full) |
| System | tests: `Vision/VisionGatePerfTests`, `Stress/ReservePairingsStressTests` |
| Fixed by | |

## Repro
1. `VisionGatePerfTests.Brawl250v250_DesignBudget_FreshProcessShortRun_Under4Ms` (`RunFreshChild`): `p.StandardOutput.ReadToEnd()`
   and `p.WaitForExit()` with no timeout. A child `dotnet test` that hangs (a locked DLL, a vstest discovery stall, a child that
   itself waits) hangs the parent Perf run with no message, and a child that fails to print its line only reports after it exits.
2. `ReservePairingsStressTests.Pairing_Twins_AgeTwoForEverySideAlive` asserts the restated totals (no-wave sides = 3, Malazan
   waves >= 79, defeats <= 8 / 5) only in the row that completes the 27 in one process. Any filtered run of fewer rows (a single
   row, `nth: 3` only, a rerun of a failing row) asserts no total and prints nothing about it, so a green partial run reads as
   "totals held".

## Expected
A Perf row bounds its own wall time and fails with a message; a gate that is skipped says so.

## Actual
As above. Both are green on this tree (Perf alone: fresh rows 3.51 / 3.50 / 3.53 ms; the 27 rows: totals 3 / 81 / 5-0).

## Notes
- Suggested: `WaitForExit(120_000)` and kill + fail with the captured output on timeout; print "totals not asserted: k of 27 rows ran"
  from the row when the set is incomplete (xUnit has no class-teardown hook that can fail a run without a fixture).
- Also noted, not a defect: the brief named the allowance tuples `(1,0,1)` seed 32 Malazan and `(1,0,2)` Whirlwind seed 9; the
  pairing's seeds are the other way round (`pair 1x0 #1` is seed 9, `#2` is seed 32), and the code's `(1,0,2,0)` / `(1,0,1,1)`
  are the correct ones.
