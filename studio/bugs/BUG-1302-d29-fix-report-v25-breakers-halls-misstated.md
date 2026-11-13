# BUG-1302: D29's fix report says the 8 pop / 600 variants finish 0 Breakers Halls; V25 finishes 4, and beats the hand-over there at the same defeat count

| Field | Value |
| --- | --- |
| Severity | S4 (report accuracy; the Producer's V24 / V14 choice rests on the conclusion's figures) |
| Status | closed (answered at the 2026-11-12-0546 ACCEPT: V25 is named beside V24 in the session log's table and in the hand-over; no code or data to change. Set at the 2026-11-13-1118 PLAN) |
| Found | 2026-11-12-0546, task D29 re-check (fix round 1, QA) |
| System | D29 fix-round-1 report (conclusion); `Content/TeblorOpeningHorizonReportTests.Variants_Seeds1To5_To15Minutes_Report` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TeblorOpeningHorizonReportTests.Variants" --logger "console;verbosity=detailed"`
   (~18 min under load; 960 matches, seeds 1-5, QA's eight pairings, to 15:00).
2. Read the "Per variant, every seed together" table.

## Expected
The conclusion's grouping "8 pop / 600 (V12, V17, V24, V25) is defeated in 5-7 of 40 but meets the target on 1 of 5 seeds
and finishes 0 Breakers Halls" matches the table.

## Actual
| variant (of 40) | seeds met | Age II 15:00 | expanded | every kind | Breakers Hall | Stables | Anointing | defeated | army pop 15:00 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| V12 | 1 of 5 | 24 | 0 | 0 | 0 | 19 | 0 | 7 | 34 |
| V17 | 1 of 5 | 23 | 0 | 0 | 0 | 15 | 2 | 5 | 42 |
| V24 (hand-over) | 1 of 5 | 21 | 3 | 0 | 0 | 14 | 2 | 5 | 32 |
| V25 (7 pop / 525) | 1 of 5 | 23 | 0 | **4** | **4** | 15 | 0 | 5 | 36 |

V25 finishes the Breakers Hall (and every building kind) in 4 of 40, not 0, at the same 5 defeats and the same 10:00 count
as V24. V24's edge is the expansion (3 vs 0) and the Anointing (2 vs 0). The pick of V24 over V25 is a judgement the
report doesn't show; "the best of them" holds only if the expansion counts for more than the Age II halls.

## Notes
- Every other figure in the fix report reproduces exactly (QA re-ran all three reports and cross-checked V0 / V24 / V14
  through its own harness, `D29OpeningQaStressTests.HandOverShippedAndV14_Seeds1To5_To15Minutes_Report`).
- V25 is a 7 / 525 wave, so it sits between the two groups the conclusion describes. The fix is one sentence in the
  report, or naming V25 next to V24 in the Producer's choice.
