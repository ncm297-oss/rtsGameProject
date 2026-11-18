# BUG-1611: `crossbow_push` against `holy_war`, seed 71: the Malazan side now falls at 17:49 without reaching Age II

| Field | Value |
| --- | --- |
| Severity | S4 (balance; one seed of nine; the side waves and is beaten, not a stall) |
| Status | open |
| Found | 2026-11-17-0816, task D33 (the developer; reproduced by QA's 19 / 17 / 15 / 14 sweep; file written by the Producer) |
| System | AI openings (content): `whirlwind/ai.json` `holy_war` wave 14 pop against `malazan/ai.json` `crossbow_push` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiOpeningPairsReportTests"` (the Age II table prints it), or
   `D33BalanceReportTests.HolyWar_WaveThreshold` for the sweep.

## Expected
Under M6-S12 the side reached Age II at 9:28 and fell at 16:14.

## Notes
- At `holy_war` 17 and 15 pop it falls sooner (12:51, 14:54); 14 is the least bad of the values that cure BUG-1553.
- For the next balance step (D34 or later), not a reason to move `holy_war` back.
