# BUG-1640: malazan.md still counts `crossbow_push` seed 19 as "no wave" in two summary sentences after D34 measured it waving

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-18-0926, task D34 (QA) |
| System | data docs: `docs/factions/malazan.md` "Fast Age II against the raider rush (BUG-0982, D-H6, 2026-10-18)" (the BUG-1010 paragraphs) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D21OpeningsQaTests.EveryPairing_ThirdSeed_BothSidesWaveIn20Minutes" --logger "console;verbosity=detailed"`
   and read pairing (1, 1) seed 19: the `crossbow_push` side sends its first wave at 6:46 (D34 took it out of
   `KnownBeatenAlive` for that reason; the same paragraph now says so).
2. Read `docs/factions/malazan.md` about lines 587-588 and 612-616.

## Expected
The summary counts agree with the measurement the same page prints a few lines later (seed 19 waves), or say they are
dated.

## Actual
- "Against `raider_rush` it sends no wave on 2 of the pairing's 5 smallest seeds (5, 11, 19, 25, 38)." Seed 19 now waves,
  so this is at most 1 of 5 (seed 25 was not re-measured by D34; the page says so in a parenthesis further on).
- "Over the two openings, `raider_rush` beats the Malazans without a fight on 5 of 10 seeds (`cadre_rush` 3 of 5,
  `crossbow_push` 2 of 5 ...)" is likewise one too many at least.
- The paragraph keeps "neither ever attacks, so 'falls to the rush without a wave' holds on both" with only a
  parenthetical "D34: seed 19's side now waves, below", so a reader gets two answers in one paragraph.

## Notes
- The developer named this in the D34 report ("seed 25 not re-measured, the page says so"). No pin covers these counts.
- QA's runs this session (2026-11-18, on the D34 tree = `main`'s sim and data):
  `D21OpeningsQaTests.EveryPairing_ThirdSeed_BothSidesWaveIn20Minutes(1, 1)`: seed 19 `crossbow_push` first wave 6:46
  (6 waves), Age II 11:31, standing at 20:00 (pop 19 / cap 66), as the developer reported.
  `DH6ThresholdQaTests.CrossbowPushVersusRaiderRush_FourthAndFifthSeeds`: **seed 25 now waves too** (first wave 6:50,
  1 wave, defeated 18:15, tick 21,906), and **seed 38 sends no wave** (defeated 13:10, tick 15,812, peak 9). So of the
  five named seeds the no-wave cases are now seed 38 (plus whatever seeds 5 and 11 do; not re-run here), not "19 and 25".
  The fix is a re-count and a re-word, docs only.
