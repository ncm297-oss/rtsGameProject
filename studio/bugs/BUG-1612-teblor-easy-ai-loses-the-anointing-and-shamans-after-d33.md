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
