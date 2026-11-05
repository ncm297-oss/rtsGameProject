# BUG-1047: A start whose nearest forest is past `ai.gatherReach` gets no wood until the scout's full sweep ends

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-05-0805, task M6-S5 (QA) |
| System | AI economy / scout (`AiPlayer.InGatherReach`, `ScoutManager.FoundEnough`), M6-S5 BUG-1045 fix |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherReachQaTests.NearestForestJustPastTheReach"`
   with the `Skip` removed from `NearestForestJustPastTheReach_AtTheShippedReach_WoodByTick5000`.
2. The scene: a generated 128 map, two Normal AI seats (free-for-all), every tree within 66 m of player 0's hall taken
   once the hall stands. Player 0's nearest forest is then 66.6 m (seed 1), 90.0 m (seed 2) or 77.0 m (seed 3) away:
   just past the shipped 64 m.

## Expected
The reach rule is meant to keep gatherers near home while the scout looks around (docs/02 "AI opponent"). It should not
cost a start its wood when there is no forest within the reach: the seat should get its first wood about as soon as with
every node in reach. The control, `NearestForestJustPastTheReach_AtReach512_WoodByTick5000` (the pre-M6-S5 choice),
gathers its first wood at tick 2,931 / 3,658 / 3,912.

## Actual
At the shipped 64 m, `FoundEnough` never holds (no forest within 64 m of the hall), so the scout walks every waypoint of
the map; until then `InGatherReach` hides every forest, so every gatherer goes to gold:

| Seed (nearest forest) | Scout done, 64 m | First wood, 64 m | First wood, 512 m |
| --- | --- | --- | --- |
| 1 (66.6 m) | tick 9,261 | tick 10,651 | tick 2,931 |
| 2 (90.0 m) | tick 8,741 | none by tick 30,000 | tick 3,658 |
| 3 (77.0 m) | tick 9,641 | none by tick 30,000 | tick 3,912 |

Seed 2 per 2,000 ticks at 64 m: 17 of 18 workers on gold and 0 on wood through tick 6,000, no army ever (wood 0), the
enemy's first wave wipes the workers by tick 8,000-10,000 (18 → 2). At 512 m the same start fields an army from
tick ~6,000.

## Notes
- The reach table in docs/03 puts every generated start's nearest forest at 14.8-21.2 m (menu map and 4-start 256 map,
  seeds 1-10), so no shipped generated map hits this today: S3. It becomes an S2 the moment a start has no forest within
  64 m: the next sim candidates include a hand-made map format (M6 criterion 7), and a menu option with fewer forests
  would do it too.
- A possible fix (Producer's / dev's call): fall back to the nearest known forest when none is known within the reach
  (the per-kind "no node" path), or release the reach once the scout has visited the head ring (the scout already
  covers home by then), rather than waiting for the whole-map sweep.
- Related: BUG-1045 (the cause of the rule), BUG-1048 (its other fallback).
