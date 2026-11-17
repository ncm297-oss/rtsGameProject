# BUG-1539: `Perf_UnitsConvergingOnOnePoint (500)`'s 8 ms worst-tick bound fails about half the time when run alone

| Field | Value |
| --- | --- |
| Severity | S3 (a flaky-looking Perf row; not caused by M6-SH2) |
| Status | open |
| Found | 2026-11-15-2136, task M6-SH2 (QA re-check 1) |
| System | tests: movement Perf (`LocalMovementTests+Serial.Perf_UnitsConvergingOnOnePoint_AvgAndWorstTick`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Perf_UnitsConvergingOnOnePoint_AvgAndWorstTick"`, alone, no other
   test host running, several times.

## Expected
A Perf row run alone (rule 42) gives the same verdict run after run.

## Actual
Six runs of the 500-unit row alone on the M6-SH2 fix-round head (5aa9ac6): worst tick 8.81, 7.51, 13.74, 8.07, 9.52,
8.86 ms (fail, pass, fail, fail, fail, fail) against "< 8 ms"; the average stays 1.7-2.0 ms against its own bound. The
previous head (500a13b, QA's commit) alternated with it: 9.74, 6.62, 9.76 ms. So the spread is the same on both trees, and
nothing the fix round changed runs in that scene (no buildings, no AI).

## Notes
- One worst tick out of 400 is what fails, which looks like a GC or OS scheduling pause rather than sim work. Options for
  the Producer: measure the worst tick as the least of N runs, or bound a high percentile instead of the single maximum.
  QA did not change the row (no loosening without a ruling).
- Round 0 noted the same kind of single-worst-tick noise on `Perf_BuildCap_EveryUnitItsOwnGoal(2500)`.
