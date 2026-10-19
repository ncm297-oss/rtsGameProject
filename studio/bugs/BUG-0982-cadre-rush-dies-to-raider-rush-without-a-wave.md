# BUG-0982: Malazan `cadre_rush` can fall to Whirlwind `raider_rush` without sending a wave (seed 8)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | closed (decided by measurement: keep 18; the behaviour is tracked by BUG-1010) |
| Found | 2026-10-18-0516, task D21 |
| System | AI content: `game/data/factions/malazan/ai.json` opening `cadre_rush` (`attack`, Age I mix) |
| Fixed by | D-H6 (52e82c9, session 2026-10-18-1236): measured `cadre_rush` v `raider_rush` at 18 / 16 / 14 pop on seeds 7, 8, 14 (QA: 18, 23 too): seed 8 never waves at any of them (the army peaks at 11 pop), Age II is never delayed, so the Producer's rule keeps 18; `ai.json` and the golden unchanged; the malazan page's "This opening can fall to a rush without ever attacking" paragraph + the "Fast Age II against the raider rush" table (pinned by `AiOpeningPairsReportTests.Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable`). QA's `EveryPairing_SecondSeed…` row stays an allowance (printed). The sim-side lever ("send what you have when the base is being lost") is **BUG-1010** (S3) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D21OpeningsQaTests.EveryPairing_SecondSeed_BothSidesWaveIn20Minutes" --logger "console;verbosity=detailed"`
2. Row `(malazan: 2, whirlwind: 1)` prints:
   `seed 8: malazan cadre_rush first wave never (0 waves), whirlwind raider_rush first wave 4:58 (2 waves); defeated at 13:14 / never; peak army pop 11 / 75`

## Expected
D21 criterion 2: in each Malazan × Whirlwind pairing, both sides send a wave. `cadre_rush`'s description: "The first wave
waits for 18 army pop."

## Actual
On the pairing's smallest seed (7, the developer's report), `cadre_rush` waves at 8:42. On its second seed (8), the
Malazan army never goes above 11 pop against the raider rush, so it never meets the 18-pop / 1,332 threshold. It is defeated
at 13:14 without sending a wave. This is what D21 fixed for `standard` (20 -> 16 after seed 2 peaked at 15 pop), here on
another opening. Two more second-seed rows sit near the edge: `crossbow_push v whirlwind standard` seed 32 peaked at 14 pop
(its threshold) and sent 1 wave before falling at 15:30; `standard v raider_rush` seed 10 peaked at 19 and sent 1 wave.

## Notes
- The criterion holds on the seeds the report measured, so this is S3, not S2. It shows the 9-pairing check depends on the
  seed.
- A greedy fast-Age-II opening that loses to a rush may be a fair design outcome. The owner or Producer decides whether
  `cadre_rush` should keep 18 pop (and the description's promise) or wave lower under pressure. A generic option is the
  sim-side "send what you have when the base is being lost". Either way, the faction page should say that this opening can
  die without attacking.
- The QA row allows "defeated" as a pass, so it stays green and prints the case. Make it strict (`first[p] >= 0`) when this
  is fixed.
