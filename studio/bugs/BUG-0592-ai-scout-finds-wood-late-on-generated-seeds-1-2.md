# BUG-0592: M5-3 criterion 1 not met: on generated seeds 1 and 2 the AI knows wood at tick ~2,400 and gathers it at ~3,800-4,400 (criterion: 1,200 / 2,000)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-12-1930, task M5-3 |
| System | AI `ScoutManager` (the sweep's waypoint list), `Ai/ScoutManagerTests` |
| Fixed by | |

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
