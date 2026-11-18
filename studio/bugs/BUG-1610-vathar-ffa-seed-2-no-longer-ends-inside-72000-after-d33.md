# BUG-1610: the four-seat Vathar Crossing free-for-all on seed 2 no longer ends inside 72,000 ticks after D33's waves

| Field | Value |
| --- | --- |
| Severity | S3 (a pinned AI match stands; the criterion row moved to seed 1 by rule 45; no bound raised) |
| Status | open |
| Found | 2026-11-17-0816, task D33 (the developer; confirmed by QA alone; file written by the Producer at the ACCEPT) |
| System | AI end game (the four-seat stall family: BUG-1068 / 1116 / 1536) |
| Fixed by | |

## Repro
1. `D33_ROWS=vathar_crossing:2:4 dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D33BalanceReportTests.CriterionRows_Report"`
2. At 72,000: seats 0 and 1 defeated at 33,951 / 24,932, seats 2 and 3 standing, no error.

## Expected
The match ends inside the 60-minute bound, as it did before D33 (38,690 after M6-S12).

## Notes
- D33 changed only three AI openings (`cadre_rush` 16 pop, `holy_war` 14 pop, the Teblor `standard` order).
- `MatchCriterionTests`' FFA row plays seed 1 now (31,991, seat 2). Seeds 1 / 3 / 4 / 5 / 6 end (31,991 / 41,051 /
  66,919 / 62,482 / 59,255).
- Same shape as the Pale Hills four-seat stalls: two survivors that never finish each other. The sim's AI follow-up
  reads one trace of seats 2 and 3 after 34,000 (who has an army, who has gold) before anything is tuned.
