# BUG-0592: M5-3 criterion 1 not met: on generated seeds 1 and 2 the AI knows wood at tick ~2,400 and gathers it at ~3,800-4,400 (criterion: 1,200 / 2,000)

| Field | Value |
| --- | --- |
| Severity | S3 (filed S2; re-triaged by the Producer at the 2026-10-12-1930 ACCEPT, see below) |
| Status | fixed |
| Found | 2026-10-12-1930, task M5-3 |
| System | AI `ScoutManager` (the sweep's waypoint list), `Ai/ScoutManagerTests` |
| Fixed by | 70479d6 (M5-4a: the generator's start locations put each base beside two mines and a forest within 24 m; `AiScenes.Generated` builds the bases on them). Regression: `ScoutManagerTests.GeneratedMap_TheAiFindsWood_AndGathersIt` now asserts known wood <= 1,200 and first wood in <= 2,000 (measured 386 / 2 / 2 and 902 / 720 / 472 on seeds 1-3), `AiVersusAiTests` first wood <= 3,000 both seats (468-902), QA's `StartLocationAiQaTests` seeds 4-6 (knew wood by 558, first in by 1,169). Producer set at the 2026-10-13-0146 ACCEPT. |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GeneratedMap_TheAiFindsWood_AndGathersIt" --logger "console;verbosity=detailed"`
   (the shipped Malazan order, Normal, `AiScenes.Generated`, 6,000 ticks).

## Expected
Criterion 1: an AI seat with the shipped Malazan order on a generated 128 map, seeds 1-3, knows a wood node by tick 1,200 and
has wood income above 0 by tick 2,000.

## Actual
| Seed | Knows wood (tick) | First wood in (tick) |
| --- | --- | --- |
| 1 | 2,374 | 4,394 |
| 2 | 2,406 | 3,820 |
| 3 | 674 | 1,361 |

Seeds 1 and 2 miss both marks by about 2x. The test asserts `<= 3,000` and `<= 6,000` instead of the criterion's 1,200 and
2,000 (the developer's report says so). The nearest forest on those seeds is 73-75 m from the hall: outside the 40 m ring plus
14 m of sight, and the sweep reaches it only on the legs after the ring (8 ring points first, then the mirror, then the
quadrant centres).

## Notes
- The developer flagged this as NOT MET. The fix may be the sweep (e.g. leave the ring sooner, a larger `scoutRadius`, or put
  the quadrant centres or a spiral before the far ring points), the scene, or the Producer moving the criterion. Whichever it
  is, the test should assert the agreed numbers.
- In AI-vs-AI (`AiVersusAiTests`, seeds 1-2) the Malazan seat's first wood comes at 3,220 / 3,825, so brief item 7's "wood by
  tick 3,000" also fails (BUG-0593).

## Re-check (round 1, 786c9e4)
- Not fixed; the dev reports NOT MET and leaves it to the Producer. QA measured it independently (see the re-check report).
- It now also carries criterion 9's wood part (from BUG-0593): the Malazan seat's first wood comes in after tick 3,000 on seeds
  1 and 2.
- The dev's argument: the seed 1 / 2 forest is 71-72 m north, outside the 40 m ring. A worker sent straight there brings wood in
  at 1,028 / 1,090, so meeting 1,200 / 2,000 needs a sweep that heads for wood. About 60 direction-blind variants were measured
  and none met both seeds. This is a scope / criterion call for the Producer: change the criterion, or give the scout a
  wood-seeking rule (for example toward unexplored ground nearest the hall first, or a cheaper ring with a larger radius).

## Producer ruling (2026-10-12-1930 ACCEPT): S2 → S3, criterion 1's timing re-stated
- The criterion's 1,200 / 2,000 ticks were written unmeasured. The dev's measurement settles it: a worker sent straight to the
  seed 1 / 2 forest (knowing where it is) brings wood in at 1,028 / 1,090, so 2,000 is reachable only by a sweep that heads
  almost straight for a forest it has no way of knowing about. The criterion's purpose (the AI no longer starves of wood on a
  generated map: before M5-3 it never gathered wood at all) is met on every seed; the rest of the criterion holds.
- **Root cause is the map, not the scout:** the generator has no start locations (docs/03 "Start armies (debug until real
  start locations)"; docs/02 "Map": 2 gold mines per start location, open bases). On seeds 1 and 2 the nearest forest is 71-75 m
  from the hall and BUG-0604 shows start bases that see no node at all. The fix is the generator's start locations (a hall
  site with 2 mines and a forest within reach of its sight, bases mirrored), the next sim task (M5-4a); then this bug's
  measured target is **first wood in by tick 2,000 on seeds 1-3** (the generated test map), asserted by
  `ScoutManagerTests.GeneratedMap_TheAiFindsWood_AndGathersIt` and `AiVersusAiTests` (wood by 3,000).
- Until then the test asserts the measured 3,000 / 6,000; docs/03 "Measured on the generated test map" records the numbers.
