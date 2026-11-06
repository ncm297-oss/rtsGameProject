# BUG-1047: A start whose nearest forest is past `ai.gatherReach` gets no wood until the scout's full sweep ends

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-11-05-0805, task M6-S5 (QA) |
| System | AI economy / scout (`AiPlayer.InGatherReach`, `ScoutManager.FoundEnough`), M6-S5 BUG-1045 fix |
| Fixed by | M6-S7 (session 2026-11-05-1716): the reach lifts once the head ring is seen (or the sweep ends with the scout alive), not after the whole-map sweep (`AiPlayer.ReachLifted`, `ScoutManager.HeadRingSeen`). The decided fallback (a kind with no node in reach takes the nearest known one) was measured and left out (it re-created BUG-1045 and starved two soak seats; this row gives the same ticks without it): docs/03. Regression: `QA/GatherReachQaTests.NearestForestJustPastTheReach_AtTheShippedReach_WoodByTick5000` un-skipped (first wood at tick 2,931 / 3,649 / 4,293 on seeds 1-3). docs/03 "Implementation (M6-S7)" |

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

## Producer triage (2026-11-05-0805 ACCEPT)
S3 stands (no shipped generated start hits it: the reach table). **Decision:** fix it together with BUG-1048 as one rule, before the
map-file format lands (a hand-made map can put the nearest forest past 64 m, which makes this an S2): lift the reach once the scout has
visited the head ring (home is looked at by then), instead of after the whole-map sweep; and while the reach holds, a kind with no node
within it falls back on the nearest known node of that kind (the "no node" path never idles the seat). First item of the next sim
hardening (M6-SH1, counter 4 / 4), or folded into the map-format task if it comes first. Un-skip `NearestForestJustPastTheReach_AtTheShippedReach_*` with it.
