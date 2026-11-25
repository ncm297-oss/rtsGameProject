# BUG-1612: after D33 the Easy Teblor AI almost never researches Blood-oil Anointing (35 -> 1 of 64) and fields fewer Shamans (51 -> 34); Hard loses some too

| Field | Value |
| --- | --- |
| Severity | S3 (QA's rating, kept: a real loss for Easy and Hard outside D33's criteria, which were Normal and Hard unit counts) |
| Status | open, half fixed (Producer, 2026-11-18-0926 ACCEPT): **the Anointing half is fixed by D34** (Easy 1 -> 28 of 64, Normal 11 -> 17, Hard 18 -> 23; `QA/Content/D34QaTests`, `Content/TeblorOpeningFixtureTests.TheShippedOpening_IsTheD34Fixture`). **What stays open: the unit mix.** Shamans Easy 34 -> 16 (51 before D33), Normal 25 -> 15, Hard 25 -> 22; D34 also cost Easy its Riders (36 -> 4) and War-dogs (51 -> 27). Order alone cannot give both; waits for Request 55 (the sim's AI follow-up), then one data re-measure |
| Found | 2026-11-17-0816, task D33 (the developer; measured against `main` by QA's `D33TeblorRowQaStressTests`; file written by the Producer) |
| System | data: `game/data/factions/teblor/ai.json` `standard` (step order against the reserve) |
| Fixed by | |

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
