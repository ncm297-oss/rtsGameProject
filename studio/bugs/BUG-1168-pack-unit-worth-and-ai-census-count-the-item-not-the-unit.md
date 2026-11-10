# BUG-1168: Every per-unit "worth" counts a pack unit at the whole item's cost (x SpawnCount), and the AI's queue census counts a pack as one unit

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-10-0146, task M7-2 |
| System | match stats (`MatchStats.Sample`), AI (`AiPlayer.ArmySize`, `MilitaryManager` wave check, `DefenseManager`, `AiPlayer` intruder worth, `ProductionManager.Pick`, build-order `train` count) |
| Fixed by | |

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
