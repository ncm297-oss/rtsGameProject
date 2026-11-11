# BUG-1168: Every per-unit "worth" counts a pack unit at the whole item's cost (x SpawnCount), and the AI's queue census counts a pack as one unit

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M7-3; QA verified 2026-11-10-1236) |
| Found | 2026-11-10-0146, task M7-2 |
| System | match stats (`MatchStats.Sample`), AI (`AiPlayer.ArmySize`, `MilitaryManager` wave check, `DefenseManager`, `AiPlayer` intruder worth, `ProductionManager.Pick`, build-order `train` count) |
| Fixed by | `M7-3` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PackQaTests.APacksArmyValue"`.
   - `APacksArmyValue_IsThreeTimesTheItemsCost_Bug1168Symptom` (passes, the symptom): on the pack copy (Malazan Heavy
     Infantry `spawnCount` 3, 54 / 20), one trained pack's army-value sample is **222**.
   - `APacksArmyValue_IsTheItemsCost_NotSpawnCountTimesIt` (skipped, `Skip = "BUG-1168 ..."`): expects **74**, what was paid.
2. Read `AiPlayer.ArmySize` (`cost += Live[t] * (def.CostGold + def.CostWood)`), `MilitaryManager` line ~129
   (`cost += def.CostGold + def.CostWood` per free army unit, against `attack.minArmyCost`), `DefenseManager` lines
   155-209 and `AiPlayer` line 577 (`IntruderWorth`): each adds the type's cost per live unit.
3. `AiPlayer` line 832: `Queued[type]++` per queue item; `ProductionManager.Pick` shares `(Live + Queued) * HalfPop / weight`.
   `BuildOrderExecutor.Judge`: a `train` step's `count` counts accepted queue items.

## Expected
docs/03 "Implementation (M7-2)": "The item's `cost` and `trainTime` are the pack's ... `pop` is each unit's." A unit's worth
should then be the item's cost / `SpawnCount` (three dogs of a 120 / 0 pack are worth 120, not 360), and a queued pack is
`SpawnCount` units of its type for the composition share. A build-order `train` step's `count` should say whether it
counts items or units (docs/03 "For the data track (M7-2)" does not).

## Actual
- The army-value graph shows a pack army at `SpawnCount` x what it cost.
- The AI's wave launches on `minArmyCost` with a pack army counted x3, so a Teblor wave with dogs starts smaller than the
  opening's numbers say; the defense and reserve judgements overvalue dogs the same way.
- While a pack is queued, the composition share sees one dog, not three (short-lived: once it spawns `Live` is right).
- docs/03's "The AI" bullet says "`attack.minArmyPop` count[s] a pack from its start, before a dog stands"; it does not:
  `minArmyPop` is the live army's half-pop (`MilitaryManager` / `ArmySize`), which counts a pack only once it stands
  (correctly x3 then). Only the `pop` step trigger reads `World.HalfPop` (proven by `PackProductionTests`).

## Notes
- Latent: no shipped unit has `spawnCount`, so no shipped number moves; it bites the day the data track ships the war-dog
  row and the Teblor AI (M7 bullet 4). The developer flagged the composition half as "possible BUG-1168".
- No AI stall: `Stress/PackQaStressTests.TwoAis_TrainingPacksOf3/Of6_For10000Ticks_*` (two Normal AIs, packs of 3 and 6)
  never had a pack head wait more than 279 ticks unstarted, and reached more pop steps than the shipped-data run.
- Fix sketch: a derived `UnitDef.UnitCostGold/Wood` (or a worth helper) = item cost / `SpawnCount`, used by every worth
  sum; `Queued[type] += SpawnCount`; one docs line on the build-order `count` meaning; correct docs/03's `minArmyPop` sentence.
- M7-3: `UnitDef.UnitCostGold` / `UnitCostWood` = the item's cost / `SpawnCount` (integer division, remainder dropped) and
  `UnitWorth` their sum, read by `AiPlayer.ArmySize`, `MilitaryManager.TryStart`'s `minArmyCost` sum, `DefenseManager.Pick`'s
  six worths, `AiPlayer.IntruderWorth` and `MatchStats.Sample`. The census: `Queued[type] += SpawnCount`. A build-order
  `train` step's `count` counts items (unchanged; docs/03 "For the data track (M7-2)" now says so). docs/03's "The AI"
  bullet in "Implementation (M7-2)" replaced by the fixed statement (`minArmyPop` counts a pack once its dogs stand).
- QA's row `APacksArmyValue_IsTheItemsCost_NotSpawnCountTimesIt` is un-skipped and green, but asserts **72**, not the 74
  it was written with: the brief's rule (integer division per resource) gives 54 / 3 + 20 / 3 = 18 + 6 = 24 a unit, three
  units 72 (the 2 wood of the remainder dropped). The symptom row (222) is removed. For the war-dog's planned 120 / 0 the
  rule is exact (40 a dog). If the Producer wants a whole pack to sum to exactly what was paid, that needs fractional
  worth sums (e.g. in 840ths: cost x 840 / SpawnCount is exact for 1-8) and is a follow-up, not done here.
- Rows: `Ai/PackWorthTests.ANormalSeat_LaunchesAtMinArmyCost_CountedPerDogAtTheItemOverThree` (4 units: 96 < 100 stays
  home, where the old x3 count was 296 and launched; 5 units: 120 goes), `AQueuedPack_CountsThree_AndATrainStepsCount_CountsItems`
  (Queued = 3 x queued packs at a think; `count 2` = two Trains, six units), `APackUnitsWorth_IsTheItemsCostOverSpawnCount_RemainderDropped`.
- No shipped unit is a pack: every pin row, the golden and the NonHard / StandardPreset digests are unchanged.
- QA 2026-11-10-1236 (M7-3): verified independently. `QA/FactionOrderQaTests.APackOfEight_WorthIsIntegerDivision` (7 / 0
  over 8 = 0 a unit; 8 / 7 = 1 / 0; 800 / 16 = 100 / 2), `ZeroWorthPackUnits_InTheArmy_AndUnderAttack_CountZero_AndNothingBreaks`
  (army cost 0, sample 0, the defense runs, 0 B a think), `AThink_WithPacksQueued_AllocatesNothing` (Queued a multiple of 3, 0 B).
