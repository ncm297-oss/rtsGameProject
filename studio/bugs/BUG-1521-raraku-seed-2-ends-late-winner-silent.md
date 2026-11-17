# BUG-1521: Raraku seed 2 Normal v Normal ends only at 56,940; its winner sends nothing for 8,799 ticks

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | closed by measurement (2026-11-16-2036: Raraku seed 2 ends at 39,262, longest silence 1,379 ticks, its `Known` entry removed at the row's own request; not cured by cause, reopen if a sweep row shows a silent winner again) |
| Found | 2026-11-15-2136, task M6-SH2 (developer, criteria 3 / 8; confirmed by QA) |
| System | sim: AI end game |
| Fixed by | |

## Repro
`Stress/MatchCriterionSweepQaStressTests` (raraku, 2), or `Stress/MatchRowsM6S10Report` raraku 2.

## Expected
A winning seat with workers keeps acting; the match ends in about the time its neighbours take (25,000-31,000 ticks).

## Actual
Ends at 56,940 (47:27), seat 0; p0, with workers, is silent 40,161..48,960. At M6-S10 it ended at 27,936 and was
`MatchCriterionTests`' Raraku row; that row moved to seed 3 (25,083), the bound unchanged. In `Known` with this id.

## Notes
QA: caused by the drop-off rule (BUG-1520) through match divergence, not by the retrain (item 0 alone: 56,940). The
BUG-1152 family (a seat with workers silent for minutes). Next AI task.

## M6-S12 (2026-11-16-2036)
Raraku seed 2 ends at 39,262 (seat 1), longest silence 1,379 ticks; its `Known` entry removed. Not cured by cause.
