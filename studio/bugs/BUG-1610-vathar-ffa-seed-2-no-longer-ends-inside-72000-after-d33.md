# BUG-1610: the four-seat Vathar Crossing free-for-all on seed 2 no longer ends inside 72,000 ticks after D33's waves

| Field | Value |
| --- | --- |
| Severity | S3 (a pinned AI match stands; the criterion row moved to seed 1 by rule 45; no bound raised) |
| Status | fixed by measurement (M6-S14, session 2026-11-26-1847: the hunt's resume and the worker floor) |
| Found | 2026-11-17-0816, task D33 (the developer; confirmed by QA alone; file written by the Producer at the ACCEPT) |
| System | AI end game (the four-seat stall family: BUG-1068 / 1116 / 1536) |
| Fixed by | M6-S14 (A, B) |

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

## M7-8 trace (2026-11-24-1431, the sim; no cure shipped; QA re-ran it and got the same numbers)
- From 33,951 only seats 2 and 3 stand. Seat 3 (58 units) hunts; seat 2's last Keep stands 7 m from hunt sweep point 31,
  which no hunt reaches until 88,441. Hunt 5 (38,061-67,401) went to the free starts first (M5-H6), then the east and the
  whole border ring, and at 66,921 found the depot seat 2's new worker had built 40 m from the Keep; razing it ended the
  hunt, and hunt 6 began from scratch (marks cleared, free starts first), so the Keep waited a second full lap. Defect
  filed as BUG-1661; the worker half (why seat 2 is only a Keep) is BUG-1660.
- Recommended cure: BUG-1661 (a hunt that turned into a wave on a found building resumes its marks nearest-first from
  that building), measured alone; then BUG-1660 apart. Both wait for the AI-play session (docs/01 2026-11-24 (2)).
## M6-S13 (2026-11-25-1437): measured, unchanged
Never spent under the draw rule: won by p3 at 88,998 (74:09); p3 keeps 8,020 gold and 26 workers with 15,457 gold left
on the map, so "can still train" holds throughout. The Stress row stays skipped with its ids (seat 2's silence and the
hunt's length are AI-play items, BUG-1660 / BUG-1661).

## Fix (M6-S14)
The Vathar Crossing four-seat seed 2 match ends at 47,412 (seat 3); `FarExpandProbeQaStressTests.VatharCrossingSeed2_FourSeats_FarFogOracleAndExtraOracles` un-skipped.
