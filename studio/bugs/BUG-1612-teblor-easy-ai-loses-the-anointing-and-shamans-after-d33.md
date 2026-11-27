# BUG-1612: after D33 the Easy Teblor AI almost never researches Blood-oil Anointing (35 -> 1 of 64) and fields fewer Shamans (51 -> 34); Hard loses some too

| Field | Value |
| --- | --- |
| Severity | S3 (QA's rating, kept: a real loss for Easy and Hard outside D33's criteria, which were Normal and Hard unit counts) |
| Status | fixed (D38, 2026-11-26-0717): the three Teblor orders D37 measured ship through M7-9's `buildOrders` (Normal M1RS as `buildOrder`, Easy K9KC10S and Hard QRD1 as entries); measured on the shipped file, every difficulty meets every figure of the bar (docs/01 2026-11-24 ACCEPT (3)(c)) and equals D37's rows cell for cell. Before: open, half fixed (Producer, 2026-11-18-0926 ACCEPT): the Anointing half fixed by D34; the unit mix open through D-H10, D36 and D37 |
| Found | 2026-11-17-0816, task D33 (the developer; measured against `main` by QA's `D33TeblorRowQaStressTests`; file written by the Producer) |
| System | data: `game/data/factions/teblor/ai.json` `standard` (step order against the reserve) |
| Fixed by | D38 (data track, 2026-11-26-0717): `game/data/factions/teblor/ai.json` `standard` (`buildOrder` = M1RS, `buildOrders` = { easy: K9KC10S, hard: QRD1 }); `Content/D38ShippedOrdersTests`, `Content/D38ShippedOrdersReportTests` (Stress) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D33TeblorRowQaStressTests"` (64 seats, four maps, seeds 1-8, to 15:00).

## Measured (`main` -> D33; defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing)
- Easy: 1 / 0 / 51 / 0 / 0 / 0 / 35 -> 1 / 0 / 34 / 27 / 36 / 51 / 1
- Normal: 12 / 25 / 26 / 0 / 4 / 0 / 8 -> 10 / 23 / 25 / 12 / 22 / 39 / 11
- Hard: 11 / 28 / 35 / 6 / 12 / 5 / 27 -> 11 / 23 / 25 / 35 / 43 / 48 / 18

## Expected
The new train steps should not cost Easy and Hard the faction's one upgrade.

## Notes
- Cause as measured for BUG-1420: steps later in the file spend only above the waiting expand step's reserve
  (Request 55). The three new train steps and two buildings now come before the Shaman's Circle and the Anointing.
- D34 first tries order only (the Shaman's Circle and the Anointing ahead of the three train steps, or the Anointing's
  time earlier) and prints all three difficulties. If no order keeps both, it waits for Request 55 in the sim.

## QA re-check (2026-11-18-0926, D34 fix round 1; status left to the Producer)
- D34 shipped QA's order Q2. On the shipped data (QA's loop, `TestSim.Data`, 64 matches a row), main(D33) -> D34:
  Easy 1/0/34/27/36/51/1 -> 1/0/16/30/4/27/28; Normal 10/23/25/12/22/39/11 -> 9/23/15/14/22/37/17;
  Hard 11/23/25/35/43/48/18 -> 11/23/22/39/33/46/23.
- **The Anointing half is fixed** to the brief's bar (Easy 1 -> 28 of 64, pre-D33 35; Normal 11 -> 17; Hard 18 -> 23).
- **The Shaman half is worse, not better**: Easy 34 -> 16 (pre-D33 51), Hard 25 -> 22, Normal 25 -> 15 (bar 14). The
  brief's bar does not cover Easy's or Hard's Shamans, and the trade is printed in teblor.md's D34 questions. Suggest the
  Producer either closes the Anointing half and moves the Shaman loss under Request 55, or keeps this bug open for it.

## D-H10 (2026-11-25): the switches against the bar; none meets it, nothing ships
- Baseline: the shipped order's full rows (`Content/DH10TeblorSwitchReportTests`, D34's row) give D34's 21 figures exactly.
- Eleven candidates (the brief's A, S, RS, AR, AN; the developer's AT, AB, ANS, DS, ASE, ASES), screened on seeds 1-2,
  full rows (64 a difficulty) for A, RS, AN, AT, AB. None meets every figure of docs/01 2026-11-24 ACCEPT (3)(c). Nearest:
  A (`throughReserve` on the Rider, War-dog and Shaman steps), six misses: Easy Breaker 11 (20) and Anointing 9 (20),
  Normal Breaker 11 (12) and Anointing 13 (17), Hard Rider 35 (40) and War-dog 42 (46). The table and the reading are in
  teblor.md "The unit mix (BUG-1612, D-H10)"; questions for the Producer under it. `git diff -- game/` empty; the data
  hash did not move.
- Status stays open (the unit-mix half).

## QA (D-H10, 2026-11-25-0924)
- M's and A's full rows re-run in QA's own class (`QA/Content/DH10TeblorOrderQaStressTests`): equal to the developer's
  figures on every map of every difficulty. "None meets the bar" confirmed for QA's own orders too (seven on full rows,
  four more screened). The nearest is not A: one-Shaman orders miss 4 figures (QRD1 16 seats short, every Hard figure
  met); see BUG-1721. Stays open.

## D36 (2026-11-25-1437): the second search, around the one-Shaman orders; none meets the bar, nothing ships
- `Content/D36TeblorOrderSearchReportTests` (Stress), D34's row. Baseline re-measured: the shipped order's full rows equal
  D-H10's (Easy 1/0/16/30/4/27/28, Normal 9/23/15/14/22/37/17, Hard 11/23/22/39/33/46/23).
- Five candidates on full rows: QRD1 (QA's; 4 misses, 16 short: Easy Shaman 17, Anointing 18; Normal Shaman 19,
  Anointing 12), RSC (5, 23), RSX (6, 21), K9KC10S (7, 47; meets every Easy figure), K9KC930 (8, 38). 57 orders screened.
  Nearest: QRD1. No fixture; `git diff -- game/` empty.
- For the Producer (teblor.md "Questions for the Producer (D36)" 1): one order meets every Easy figure (K9KC10S), one
  every Hard figure (QRD1), one every Normal figure but the Anointing (RSC). Recommendation: an opening per difficulty
  (sim schema request). Status stays open.

## D38 (2026-11-26-0717): the three orders ship; fixed
- `game/data/factions/teblor/ai.json`, the `standard` opening only: `buildOrder` = M1RS (the D37 Normal fixture's), new
  `buildOrders` = { "easy": K9KC10S, "hard": QRD1 } (the D37 Easy and Hard fixtures'), written from the fixtures, step for
  step, switches included; description, compositions, wave, difficulties and retreat unchanged. Data hash moved once
  (header of the golden regenerated; no literal in a test).
- Measured on the shipped file itself (`Content/D38ShippedOrdersReportTests`, Stress; D34's row, 64 seats a difficulty,
  192 matches in 318 s), defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing, D34's order -> D38:
  - Easy (K9KC10S): 1/0/16/30/4/27/28 -> 1/0/38/28/34/42/22
  - Normal (M1RS): 9/23/15/14/22/37/17 -> 9/23/23/18/30/40/18
  - Hard (QRD1): 11/23/22/39/33/46/23 -> 11/23/23/40/42/47/25
- Every cell equals D37's row for that order; every figure of the bar is met on all three difficulties (0 misses).
  teblor.md "The unit mix, shipped (BUG-1612, D38)".
