# BUG-0050: 500 units to 500 random goals: give-ups rose to 4.7% after M1-4d-3 fix round 1 (target 3%)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-05-1609, task M1-4d-3, re-check round 1 |
| System | movement (give-up rules under field-cache churn) |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CrowdRowSweepStressTests.FiveHundredUnitsTo500RandomGoals"`
(prints per-seed give-ups; the committed row runs seeds 1-4, QA ran 1-10 in a scratch copy).

## Expected
Brief target for this row: at most 3% give up. Before fix round 1 the row met it (QA seeds 1-10 mean
2.3%; the dev's seed-5018 row 2.2%).

## Actual
| | Base 7f741f1 | M1-4d-3 first version | After fix round 1 (6abd200) |
| --- | --- | --- | --- |
| QA seeds 1-10, gave up of 500 (mean / max) | 39.6 / 111 | 11.3 / 33 | 23.7 / 40 |
| Dev row, seed 5018 (docs/03) | 13 | 11 (2.2%) | 16 (3.2%) |

Per seed after the fix: 23, 20, 40, 39, 10, 16, 18, 37, 17, 17. Still better than "now" (6%) and base,
so the miss rule may apply, but docs/03 lists the new 3.2% without saying why it rose.

## Notes
Suspected: the BUG-0048 fix (behind a field-waiting unit, one no-progress tick in 4 now counts), since
this row has a goal per unit and constant field churn; possibly the walk-back no-push rule. Not traced.

## Re-check round 2 (2026-10-05-1609, fix commit 57cc55c)
Unchanged: QA seeds 1-10 gave up 23.7 mean (10-40) of 500, identical per seed to round 1.
