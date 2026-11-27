# BUG-1854: The Pale Hills four-seat free-for-all on seed 2 no longer ends by 144,000 (never spent, nobody defeated)

| Field | Value |
| --- | --- |
| Severity | S3 (a pinned Stress row stopped ending; moved by rule 45) |
| Status | open (a finding for the sim's next AI or hardening task; traced below as far as M6-S14 went) |
| Found | 2026-11-26-1847, task M6-S14 (the pin move: `Stress/HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt`) |
| System | AI end game (four seats) |
| Fixed by | |

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
