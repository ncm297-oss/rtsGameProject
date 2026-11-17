# BUG-1523: Pale Hills seed 5 Normal v Normal has no result by 72,000 (a beaten seat's last unit is not found)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | closed by measurement (2026-11-16-2036: Pale Hills seed 5 ends at 57,755, `MapSlice2StressTests` Pale Hills 5 green; not cured by cause, the lone-last-unit hunt stays under BUG-1116) |
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

## M6-S12 (2026-11-16-2036)
Pale Hills seed 5 ends at 57,755 (seat 0); `MapSlice2StressTests` Pale Hills 5 green. Not cured by cause: the match moved.
