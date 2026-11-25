# BUG-1539: `Perf_UnitsConvergingOnOnePoint (500)`'s 8 ms worst-tick bound fails about half the time when run alone

| Field | Value |
| --- | --- |
| Severity | S3 (a flaky-looking Perf row; not caused by M6-SH2) |
| Status | fixed (M7-H3) |
| Found | 2026-11-15-2136, task M6-SH2 (QA re-check 1) |
| System | tests: movement Perf (`LocalMovementTests+Serial.Perf_UnitsConvergingOnOnePoint_AvgAndWorstTick`) |
| Fixed by | M7-H3 |

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

## Fix (M7-H3, 2026-11-25; ruling docs/01 2026-11-25 (2))
`LocalMovementTests+Serial.Perf_UnitsConvergingOnOnePoint_AvgAndWorstTick` (500, enforced) plays its 400 ticks three
times on fresh sims and bounds the least of the three worst ticks against the unchanged 8 ms; the average (< 4 ms) is
the first run's, as before. A slow tick in every run still fails. The judge is `Serial.Judge`, unit-tested on made-up
times (`Bug1539_TheJudge_TakesTheLeastWorstTick_OfThreeRuns`: a 20 ms tick in two of three runs passes, in three of three
fails). The 1,000 and 2,500 rows stay one run, information only.

Verified by M7-H3 QA (2026-11-25-0924): the 500 row alone 5 of 5 (worst ticks 2.51-3.51 ms in every run, averages 1.04-1.26 ms); a scratch mutation padding tick 137 by 20 ms passes with runs {1,2}, {2,3} or {1,3} padded (least worst 2.54 / 3.20 / 3.31 ms) and fails with all three ("the least worst tick of 3 runs 20.57 ms (< 8 ms)").
