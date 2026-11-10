# BUG-1157: With the far expansion, decided Pale Hills matches take a long time to finish (seeds 2 and 5 end past 72,000)

| Field | Value |
| --- | --- |
| Severity | S3 (two pinned non-criterion rows stop ending inside 72,000; every criterion row ends) |
| Status | fixed (M5-H6, 2026-11-09-1336, ACCEPTED; QA verified: Pale Hills 2 / 5 un-skipped at 49,565 / 55,485, seed 1 50,140, the tally 29 / 11 / 2 with 2 no-results) |
| Found | 2026-11-09-0426, task M5-9b (developer, rule 43 re-measure) |
| System | AI end game: the hunt's sweep (BUG-1116's sweep order) and the losing seat's rebuilding, made longer by the far expansion |
| Fixed by | M5-H6 (the house rule, BUG-1116; the hunt sweeps the free starts first from its second hunt; the leg fixes BUG-1158 / BUG-1159) |

## Repro
1. On the M5-9b branch: `dotnet tools/Rts.Cli/bin/Debug/net8.0/Rts.Cli.dll match --map pale_hills --seed 2 --ticks 144000`
   and the same with `--seed 5`.
2. Rows: `Stress/MapSlice2StressTests.NormalVersusNormal_PaleHillsSeed2_Stalemate_EndsWithin72000` and
   `NormalVersusNormal_PaleHillsSeed5_SlowRaze_EndsWithin72000` (both skipped with this measurement; remove the `Skip`).

## Expected
Both rows ended inside 72,000 on the M5-9a branch: seed 2 at 53,797, seed 5 at 45,358.

## Actual
- **Seed 2:** both seats expand to a free start (gathered gold 14,000-16,000 each, against 9,000 before). The match is
  decided by 40:00 (seat 0: no army, no production building; seat 1: 44 units, 65 by 60:00), but seat 1 needs until
  **79,504 (66:15)** and five hunts to finish seat 0, whose far depot and workers stand at a start the hunt's sweep
  reaches late.
- **Seed 5:** both expand (seat 0 at 27,520, seat 1 at 28,200), seat 1 a second time at 44,560, seat 0 a second depot by
  the 2,000-gold pair near start 2 at 53,080. The war runs on with 25-37 units a side; at 70:00 seat 1 has no army and no
  gold; seat 0's 25 units need until **96,321 (80:16)** and four hunts, while seat 1 rebuilds with 14,000-17,000 wood.
- Pale Hills seed 1 (the criterion row) gets shorter instead (68,664 to 48,858), and seeds 3 / 4 / 6 too (31,891 to 29,514).
- **The Hard v Normal tally** (QA's 42 matches, to 72,000): 26 / 11 / 5 (Hard / Normal / no result; was 27 / 15 / 0). The
  five no-results are all Pale Hills with Hard in seat 0 (seeds 2, 3, 6, 7, 8) and have the same shape: seed 2
  (`Rts.Cli match --map pale_hills --seed 2 --ai hard,normal --ticks 144000`) is decided by 40:00 (Normal: no army, no
  production building; Hard 48-57 units) and ends at **92,963 (77:28)** after ten hunts, Normal rebuilding six producers with
  its wood meanwhile (12,000-13,400 wood held). With Hard in seat 1 Pale Hills is 7 / 0 / 0.

## Notes
- The far expansion works as designed in both matches; what is slow is the finish of a decided match. Two causes, both
  already on the end-of-M5 hardening list: BUG-1116 (the losing seat rebuilds with its wood; the hunt's sweep order walks
  the perimeter leg before the ground where the remnant is), and, new with M5-9b, the loser's remnant can now be a depot
  and workers at a far start rather than at home.
- Candidate levers (each its own task): the hunt looks at the map's start locations first (the start list every seat is
  given, as the far expansion's leg does); a decided wave keeps its target choice on the loser's drop-offs.
- Not a re-bound: rule 43. The two rows are skipped with this measurement, the KnownStalls theory keeps seeds 3 / 4 / 6
  and generated 6.

## QA confirmation (2026-11-09-0426, M5-9b inspection)

- Reproduced exactly: `FarExpandProbeQaStressTests.PaleHillsSeeds2And5_To144000_Measured` gives seed 2 **79,504 (66:15) p1**
  and seed 5 **96,321 (80:16) p0**, the far fog oracle clean on every think; the tally (run once, 6 processes) is
  **26 / 11 / 5**, the five no-results Pale Hills Hard seat 0 seeds 2, 3, 6, 7, 8.
- Severity S3 confirmed under criterion 5 (a `Known` skip under an existing id with the measurement). For the Producer:
  seed 2 is skipped on `main` as well, but **seed 5 is green on `main`** (`KnownStalls`), so this is a pinned row `main` has
  green that the branch skips.

## Fix (M5-H6, 2026-11-09-1336, the developer)

- Shipped: (i) from the match's second hunt on, the sweep's next point is first the free start (no player, or a defeated
  one) nearest it, through the interior sweep point at the start's centre (`MilitaryManager.NextHuntStart` / `StartProxy`);
  (iii) a lost House step is reopened only when the population is within `ai.houseWhenPopWithin` of the cap
  (`BuildOrderExecutor.ReopenLost`); with BUG-1158 / BUG-1159's leg fixes. Not shipped: (ii) the sweep-order deferral (adds
  nothing with (i) and (iii)) and (iv) drop-offs first (sinks Raraku seed 5 and Pale Hills seed 5). The lever x row table
  is in docs/03 "Implementation (M5-H6)".
- Pale Hills Normal v Normal: seed 2 **49,565** (41:18, seat 1), seed 5 **55,485** (46:14, seat 0): both `MapSlice2` rows
  un-skipped. Seed 1 50,140 (bound 72,000); Vathar seed 1 23,322 (bound 24,000); generated seed 2 17,053 (24,000).
- The Hard v Normal tally (42 matches to 72,000, run once): **29 / 11 / 2** (was 26 / 11 / 5): Raraku 10 / 4 / 0, Vathar
  7 / 7 / 0, Pale Hills 12 / 0 / 2. The five Hard-seat-0 no-results: seed 2 ends at 49,301, seeds 7 / 8 at 67,593 (Hard);
  seeds 3 / 6 (one match) are a long war Normal wins at 116,335, still a no-result at 72,000.
- Why (i) only from the second hunt: with the starts first on every hunt QA's `HuntSweepRound2HideQaTests` Pale Hills
  AI-seat-1 row no longer ended (its quarter-cell hide went unseen on the changed route).
