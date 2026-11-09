# BUG-1156: The counter shift cannot add units of the countering kind when its one producer already takes one every time its queue empties (a Hard seat that watched 20 Horse Raiders for 3 minutes trains no more Line)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-08-1556, task M5-9a (round 1's QA focus, "the behavioural sanity row") |
| System | AI (counter-composition: `ProductionManager.TrainOne`'s shifted weights) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaidersForThreeMinutes_TrainsVisiblyMoreLine"`
   with the row's `Skip` removed (skipped under this id).
2. The scene: the flat map, a Hard Malazan seat on its `standard` opening (the smallest seed that picks it), shipped data;
   once its barracks and crossbow range stand, 20 Horse Raiders (Whirlwind Shock) are placed far from its base and revealed
   to it on every think for 180 s; the twin run places them unrevealed.
3. Output:
   ```
   seen False: memory [0,0,0,0,0,0,0], max Line share 0, 360 thinks; Train orders in the 3 min: Heavy Infantry 10, Crossbowmen 9
   seen True:  memory [0,0,0,20,0,0,0], max Line share 1, 360 thinks; Train orders in the 3 min: Heavy Infantry 10, Crossbowmen 8
   seen: line 11, ranged 8; unseen: line 11, ranged 9
   ```

## Expected
Round 1's QA focus: "a Hard seat that has seen 20 Horse Raiders for 3 minutes trains visibly more Line than one that saw
none". docs/02 "AI opponent": "A Hard AI that has seen only cavalry gives its Line twice the usual weight".

## Actual
The memory and the share are right (20 Shock remembered, Line's share 1 on every think: the weight is doubled), but the
seat trains the same number of Heavy Infantry (10 Train orders in both runs). `ProductionManager` sends one Train a think,
only to a producer whose queue is empty (`emptyQueueOnly` outside spend-down), and weighs only the units whose producer has
that room (`CanTrainSomewhere`). Heavy Infantry has one producer (the barracks), and in the unshifted play it already gets
a Heavy Infantry every time its queue empties and the wood (20) is there: 10 in 180 s against a ceiling of 12.9 at 14 s
each, the shortfall being wood (both runs end at 10-15 wood). The doubled weight only decides the thinks on which both the
barracks and the crossbow range are free at once, so it can only take a Crossbowman away (9 -> 8). In this phase the
visible effect of "Line weighs twice as much" is one unit of the other kind fewer, not more Line.

The developer's `CounterCompositionTests.ANormalSeat_ThatSawShock_TrainsMoreLine_*` / `AHardSeat_ShiftsItsLineShareFurther_*`
pass because they measure the pick in isolation (a fixed army, one Train) or a share difference; they do not measure the
trained totals with saturated producers.

## Notes
- Not a code slip: the shift does what docs/03 "Implementation (M5-9a)" says. The gap is between that rule and the visible
  outcome the design and the QA focus expect. Options for the Producer / D26: a shifted unit also gets a queue preference
  (fill its producer's queue to `spendDownQueue` first), a build-order step that adds a producer for a strongly shifted
  slot, or accepting that the shift shows only when the bank, not the producers, is the limit (and saying so in docs/02).
- The edges of the formula itself are clean (`CounterMemoryQaTests.TheShare_AtTheFormulasEdges_*`,
  `ASeenSlotWithNoCounter_ShiftsNothing`), and the fog oracle holds on every think of the pinned matches
  (`Stress/StarvedWaveProbeQaStressTests`).
