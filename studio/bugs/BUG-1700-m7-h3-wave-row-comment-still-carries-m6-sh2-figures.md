# BUG-1700: M7-H3 small finding: `AiDeterminismTests`' wave-row comment still carries M6-SH2's first-wave ticks

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (M6-S14 item F, session 2026-11-26-1847) |
| Found | 2026-11-25-0924, task M7-H3 (QA standard) |
| System | tests (comment only): `sim/Rts.Sim.Tests/Ai/AiDeterminismTests.cs`, `TwoAiSeats_Waves_TwoRuns_SameHashEveryTick` |
| Fixed by | M6-S14 (F) |

## Repro
1. The comment above `TwoAiSeats_Waves_TwoRuns_SameHashEveryTick` (lines 61-62 on head b44a987) reads: "the Easy seat's
   first wave on seed 1 comes at tick 7,281, past the run (the Hard seat's at 3,841); on seed 2 they wave at 4,131 and 4,121."
2. QA's probe on head (scratch only, the row's own scene: `AiScenes.Generated`, Hard p0 v Easy p1, `AiScenes.EarlyWaves()`,
   no raiders, 9,000 ticks, the first tick each seat's `Board.WavesStarted` turns positive) printed:
   - seed 1: Hard 3,821, Easy 8,041
   - seed 2: Hard 4,071, Easy 4,041

## Expected
Brief M7-H3 item 7: the stale M6-SH2 figures in `AiDeterminismTests` are brought up to date (comments only).

## Actual
The two hunt-row comments were updated (51,218, 2 hunts); this wave-row comment was not (the developer's report says it
was not re-measured). Every figure in it is off by 20-760 ticks. The premise still holds (seed 1's Easy seat waves after
the 6,000-tick run, seed 2's two seats within it), so the row is right and only the numbers are stale.

## Notes
- Comment only; no rule, pin or threshold involved. One line for the next sim hardening: "seed 1: Hard 3,821, Easy 8,041
  (past the run); seed 2: 4,071 and 4,041 (M7-H3 QA, 2026-11-25)".

## Fix (M6-S14)
The comment re-measured on the M6-S14 tree: seed 1 Hard 3,821, Easy 7,881; seed 2 4,071 / 4,041; seed 7 4,351 / 5,201.
