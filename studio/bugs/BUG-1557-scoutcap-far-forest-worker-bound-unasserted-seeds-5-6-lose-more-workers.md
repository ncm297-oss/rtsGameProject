# BUG-1557: `ScoutCapQaTests`' far-forest worker bound (14) no longer asserted on seeds 5 and 6: under M6-S12 those seats lose up to 12 workers in 6,000 ticks (base 2-8)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (needs the Producer's ruling on the re-stated row) |
| Found | 2026-11-16-2036, task M6-12 (M6-S12), QA full |
| System | AI economy on a far-forest start (rebalance / re-scout, BUG-1462's family); QA guard `QA/ScoutCapQaTests.OneScoutKilled_At0_200_2000_OnAFarForestStart_Seeds1To10` |
| Fixed by | |

## Repro
1. On a scratch copy of the M6-S12 tree, put back the base's `ScoutCapQaTests.cs` (`git show 95186a7:sim/Rts.Sim.Tests/QA/ScoutCapQaTests.cs`)
   and run `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ScoutCapQaTests"`: 52 of 55 pass; red are
   `OneScoutKilled_*` seeds 5 ("@0: 11 workers; @200: 12 workers") and 6 ("@2000: 13 workers"), and
   `ASeatPastTheCapBeforeItsFirstScout_ItsFirstScoutIsReplaced_*` seed 3 (BUG-1550's re-read of "capped").
2. The same rows on the base (95186a7) are green.

## Expected
The QA guard's bound: at least 14 workers at 6,000 ticks after one scout is killed at 0, 200 or 2,000 on a far-forest start.
QA rows are not loosened without a ruling (rules 57 / 70).

## Actual
| Seed | Kill at | Base workers (losses) | M6-S12 workers (losses) |
| --- | --- | --- | --- |
| 5 | 0 | 15 (8) | 11 (12) |
| 5 | 200 | 17 (6) | 12 (11) |
| 5 | 2,000 | 17 (6) | 15 (8) |
| 6 | 0 | 17 (3) | 17 (3) |
| 6 | 200 | 17 (3) | 18 (1) |
| 6 | 2,000 | 18 (2) | 13 (7) |

The developer moved seeds 5 and 6 to a `KnownWorkerLosses` list that prints the bound instead of asserting it ("BUG-1462's
family: the woodcutters at the 147-154 m forests die to the enemy there"), and said so in its report. The scout counts are
still asserted and hold. (The developer's base figure for seed 6 at 200 is 18; this run reads 17.)

## Notes
- The cause is traced by the developer only as "the match diverges"; the losses are at the far forest, not scouts. Whether a
  rebalance measured from the drop-off now sends more gold workers to wood at the far forest (the only forest) is worth one
  trace in the next AI task.
- The re-read of "capped" (BUG-1550) is plausible (QA did not trace it further): the base read a released scout that later died as a gatherer as
  "the sweep ended on the first death" (seed 3's `pre-losses` row: capped at 4,259, long after the release).
