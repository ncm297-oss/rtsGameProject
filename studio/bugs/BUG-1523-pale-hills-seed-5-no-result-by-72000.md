# BUG-1523: Pale Hills seed 5 Normal v Normal has no result by 72,000 (a beaten seat's last unit is not found)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (its Stress row is red on `main` by ruling: rule 57) |
| Found | 2026-11-16, task M6-SH2 (developer, the Stress re-run; confirmed by QA) |
| System | sim: AI end game (the hunt of a beaten seat's last unit, BUG-1116's family) |
| Fixed by | |

## Repro
`Stress/MapSlice2StressTests.NormalVersusNormal_PaleHillsSeed5_SlowRaze_EndsWithin72000`.

## Expected
The match ends within 72,000 ticks (it did on the base).

## Actual
No result at 72,000: seat 0 has 1 unit and no hall, seat 1 has 81 units and 2 hunts. The Hard v Normal tally's Pale
Hills seed 5 also ends in none. Not re-bound.

## Notes
QA: caused by the drop-off rule (BUG-1520) through match divergence (base + the retrain alone ends at 40,805). Next AI
task, with BUG-1116.
