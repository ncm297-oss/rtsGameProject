# BUG-1027: After the M5-4g merge, nine rows are known-red on `main` until the data re-print and the stress-row restatement

| Field | Value |
| --- | --- |
| Severity | S2 (red rows on `main`; the cause is known and both fixes are the next session's first items) |
| Status | open (the sim half, rows 6-9, fixed in M5-H4; rows 1-5 are the data track's D-R32) |
| Found | 2026-10-18-1814, Producer at the ACCEPT (M5-4g merged with criterion 3 waived, BUG-1024) |
| System | Data-owned pinned rows (rule 31) and QA's `Stress/ReservePairingsStressTests` |
| Fixed by | rows 6-9: M5-H4, session 2026-10-26-2108 |

## The rows

Data-owned (rule 31: the data track re-prints the faction pages' cells and the pairs tables from the rows' output; no C# outside
`Content/` and `QA/Content/`):

1. `Content/AiSpendingReportTests.GeneratedNormal_PagesAndDocs03_QuoteTheReport`
2. `QA/Content/D20PinQaTests.HouseNote_*` (malazan)
3. `QA/Content/D20PinQaTests.HouseNote_*` (whirlwind)
4. `Content/AiOpeningPairsReportTests.Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable` (re-read the keep-18 rule from the new
   output; a changed number is a data change → golden regen + a For your review table)
5. `QA/Content/DH6ThresholdQaTests.CrossbowPushVersusRaiderRush_FourthAndFifthSeeds`: seed 25 `crossbow_push` is now alive at
   20:00 with no wave and 1 pop (defeated at 11:26 on the base). Name it as an allowance like `KnownBeatenAlive` (BUG-1010's case), or
   restate the row to what the measurement shows.

Sim-owned (the sim's hardening restates them to the waived criterion, BUG-1024):

6. `Stress/ReservePairingsStressTests.Pairing_Twins_AgeTwoForEverySideAlive(1, 1, 3)` (`crossbow_push` v `raider_rush` seed 19)
7. `...(1, 0, 1)` (`crossbow_push` v `standard` seed 32, Malazan)
8. `...(2, 0, 1)` (`cadre_rush` v `standard`, Whirlwind seed 13)
9. `...(1, 0, 2)` (`crossbow_push` v `standard`, Whirlwind seed 9)

   The restated assertion: every side alive at 20:00 has Age II **except a named allowance list** (the four above, each with its
   base state quoted), and the totals printed (no-wave sides 3, Malazan waves ≥ the base's 79, defeats ≤ 8 / 5) hold. Keep the
   twin / hash / command-count checks as they are.

## Why they are red

M5-4g's reserve moves the AI's spending, so every pinned "Age II at", spending cell and wave line moved (expected: rule 31). The four
stress rows were written to the brief's criterion 3 ("every side alive at 20:00 has reached Age II"), which the Producer waived at the
ACCEPT because no gate form keeps both every Age II and every wave (docs/03 "Implementation (M5-4g)", the variants table).

## Notes
- Filed by the Producer so the state of `main` is on record: nine known-red rows, nothing else (BUG-0914 is a separate flake).
- Not a regression in the game: every number moved in the direction BUG-0981 asked for or stayed (BUG-1024's table).
- Closes when both the data re-print and the sim restatement are on `main` and the full non-Perf suite is green.

## Sim half (M5-H4, session 2026-10-26-2108)
`Stress/ReservePairingsStressTests.Pairing_Twins_AgeTwoForEverySideAlive` asserts the restated criterion: every side alive at
20:00 has Age II except `KnownNoAgeTwo` (rows 6-9's four sides, each with its 20:00 state quoted in a comment); a listed side
that passes prints "take it out" (`TheAllowance_SetToAPassingSide_PrintsTakeItOut`, and seen live in the BUG-1022 variant runs).
The row that completes the 27 asserts the totals: no-wave sides 3, Malazan waves 81 (>= 79), defeats 5 / 0 (<= 8 / 5). The
twin, hash-every-50-ticks and command-count checks are unchanged. 27 / 27 green.
