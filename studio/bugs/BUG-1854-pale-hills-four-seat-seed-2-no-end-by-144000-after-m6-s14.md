# BUG-1854: The Pale Hills four-seat free-for-all on seed 2 no longer ends by 144,000 (never spent, nobody defeated)

| Field | Value |
| --- | --- |
| Severity | S3 (a pinned Stress row stopped ending; moved by rule 45) |
| Status | fixed (2026-11-27-1857, M6-S15 C: the match draws at 58,341) |
| Found | 2026-11-26-1847, task M6-S14 (the pin move: `Stress/HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt`) |
| System | AI end game (four seats) |
| Fixed by | M6-S15 C (773cecec); `Stress/M6S14FourSeatQaStressTests` FFA seed 2, `Ai/SpendWhatItHasTests` |

## Repro
1. `Stress/DrawStalledRowsReportTests` row (pale_hills, 2, 4, ""): `first spent never, restarts 0, ... no end by 144000
   (120:00); gold left on the map 0`.
2. The session's probe: no seat defeated by 144,000; a seat loses its hall at 50,780; 6 build steps reopened by 42,000.

## Expected
The match ends or, when no seat can train again, draws (docs/02 "Game flow" 4). Before M6-S14 it drew at 58,378.

## Actual
No result by 144,000 and never spent: some seat can always train (gold on hand or carried while the map's gold is gone),
so the draw clock never starts, and no seat razes another's last building. The oracle row moved to seed 3 (over at
86,931, seat 1) under rule 45; `EndGameHardeningTests.PaleHillsSeed2_*` and `RebuildLookMatchQaStressTests.PaleHillsSeed2_*`
moved to seed 3 too (their premises: a defeat, a lost hall).

## Notes
- Not traced further in M6-S14. The draw rule's "can still train" (BUG-1731's family) and the worker floor (a seat with
  gold keeps training workers) are the first suspects: report against BUG-1731.

## M6-SH3 B (2026-11-27-0817)
The draw rule now counts wood and asks for a finished drop-off before counting carried and map resources (docs/03
"Implementation (M6-SH3)" B). Pre-trace at 72,000 (`Stress/DrawReachPreTraceTests`): p0 (40 gold, 13,580 wood) can pay for a
Crossbowman and p2 (50 gold) for a Crossbowman or a Laborer, by the old and the new rule alike, and neither sends a Train from
48,000: production waits for its pick (BUG-1890). After B the match is unchanged (no end by 144,000, same end hash
381F5ED6CBDF4F93). Not a draw-rule fault any more; left open for BUG-1890's cure.

- M6-S15 (2026-11-27-1857): with "spends what it has" (BUG-1890) the two seats that sat on 40-50 gold buy what they can pay for, and the match is spent and drawn at 58,341 (the base before M6-S14 drew at 58,378).
