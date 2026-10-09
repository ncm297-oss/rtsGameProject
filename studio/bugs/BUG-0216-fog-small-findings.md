# BUG-0216: Fog small findings: circle capped by the map's side, the holding Catapult's reach, two docs lines

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-08-1435, task M4-3a |
| System | fog of war / combat targeting / docs |
| Fixed by | |

## Repro
1. **Circle capped at the map's side, not its diagonal.** `FogQaTests.MaxSight64_OnA24CellMap_MatchesTheOracle`
   (skipped with this id): a Laborer with sight 64 m (32 cells, `DataLimits.MaxSight`) in the corner of a 24 x 24 map
   misses 104 cells it should see, e.g. (23, 10) at 25.1 cells. `FogStore` caps every limit at
   `max(width, height)^2` (576 here) instead of `width^2 + height^2`. Shipped sights (<= 24 m = 12 cells) and maps >= 46
   cells are not affected, so only test maps / modded data.
2. **A holding Catapult no longer acquires at its reach on a flat map.** Holding, its scan radius is reach
   (24 + 0.9 + 0.9 = 25.8 m), beyond its sight (18 m); with no spotter it now takes nothing between 18 and 25.8 m
   (`FogQaTests.Catapult_OutrangesItsSight...`, "holding it never acquires"). That follows the fog rule, but docs/03
   says "(a) holds for every scan candidate on a one-level map" and "on its own it acquires at 18 m as before": true
   for a walking Catapult, not for a holding one (or one past `MaxGiveUps`, which also scans at reach).
3. **The view and combat disagree for up to 4 ticks.** `Fog.CanSeeUnit` (what the view will hide by) is the last
   update's fog; combat also counts the unit's own sight at this tick. A unit can start shooting an enemy the player's
   screen still hides (until the next update). Builder's documented choice; noted for M4-V4 so the view isn't
   surprised.
4. `DataLimits.MaxSight` (64) also caps unit `sight` (not in the brief; harmless: shipped 10-18 m).

## Expected
1: the stamp matches the oracle for any sight the loader accepts on any map size. 2: docs/03 states the holding
Catapult case. 3-4: noted in docs/03.

## Actual
As above.

## Notes
Item 1 is a one-line fix (`maxLimit = w*w + h*h`).
