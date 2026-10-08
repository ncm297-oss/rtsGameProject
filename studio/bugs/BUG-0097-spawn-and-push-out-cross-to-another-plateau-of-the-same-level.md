# BUG-0097: A spawn (or push-out) on a full plateau lands on another plateau of the same level, 30+ m away

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M3-H2) |
| Found | 2026-10-07-0925, task M3-4 |
| System | production spawn / construction push-out (`FreeCellSearch.Nearest`, `World.LevelBounds`) |
| Fixed by | 3f494c0 (M3-H2): `Map/Plateaus` (connected same-level ground at load), spawn and push-out search bounded by the plateau, per-plateau full memo per tick. Tests: `QA/ProductionQaTests.ASpawnOnAFullPlateau_Waits_NeverLandsOnAnotherPlateauOfTheSameLevel_Bug0097`, `APushOutOnAFullPlateau_NeverLandsOnAnotherPlateauOfTheSameLevel_Bug0097`, `TwentyHallsAllWaitingForACell_EachTick_Under0point3Ms` (0.093 ms), `PlateauTests.*`; QA 2026-10-07-1715 `QA/PlateauSealMemoQaTests.ThirtySixSameLevelIslands_256Map_*` (49 islands), `GeneratedMaps256_ManyLevel1Plateaus_PartitionMatchesAnIndependentFlood` (seeds 1-10, 10-13 level-1 plateaus each) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ProductionQaTests.ASpawnOnAFullPlateau_LandsOnAnotherPlateauOfTheSameLevel_Bug0097Pin" --logger "console;verbosity=detailed"`
   (`QA/ProductionQaTests.cs`): a 64 x 64 map with two 8 x 8 level-1 plateaus, A at (10-17, 10-17) and B at
   (40-47, 40-47), each with its own ramp. A Keep on A, every free cell of A taken by player 1's units, one Laborer
   trained.

## Expected
Brief M3-4: "if no cell is free, the item stays complete and retries next tick (never stack)"; spawn "on the nearest
free cell outside the footprint" on "the building's level". A unit should appear next to the building that trained it,
or wait.

## Actual
```
Keep at (12, 12) on plateau A (full); the trained Laborer stands at (41, 41), level 1, ring 26
```
The ring walk is bounded by the bounding box of **every** cell of the level (`World.LevelBounds`), and the only cell
test is "same level index, passable, no unit center". Map generation makes 5 level-1 plateaus by default
(`MapGenParams.Level1Plateaus`), so on a generated map the box of level 1 spans most of the map. When the building's own
plateau is full, the unit is set down on whichever other level-1 plateau has the nearest free cell: possibly beside
the enemy's base, with the only way back down a ramp. The construction push-out shares `FreeCellSearch`, so units pushed
off a site on a full plateau teleport the same way (this predates M3-4: before it the push-out walked rings up to the
map size, also level-index only).

## Notes
- The developer flagged it as a possible issue in the M3-4 report.
- Cheap fix: bound the search by the building's connected region rather than the level index, e.g. a per-cell
  "plateau id" (connected same-level component) computed with the level boxes at load, and compare that instead of
  `LevelAt`; the box would then be the component's box, which also keeps the BUG-0095 cost bound tight.
- Related cost note (not separately filed): a complete head waiting for a cell repeats its full ring walk to the
  level's box every tick. 20 halls waiting on a fully packed 120 x 24 level-0 map cost 1.7-1.9 ms a tick
  (`ProductionQaTests.TwentyHallsAllWaitingForACell_EachTick_Report`, Debug). A fully packed level is far outside
  play, but a per-level "no free cell this tick" memo would make it one walk per level per tick.
- BUG-0095 re-measured on this build: the push-out cost part is fixed (Build apply 0.08 ms at 128, 0.19 ms at 256,
  was 9 / 35 ms); the fallback stacking (18 units on one cell) is still open.
