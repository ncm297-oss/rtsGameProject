# BUG-0085: The default match (seed 1) spawns player 0's start army inside a forest

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-1503, task M2-3b |
| System | Match start layout (`ViewApi.StartLayout.Block`) vs map generator resource placement |
| Fixed by | M2-H2 (f4e1b8d): `StartLayout.IsOpen` + one same-level clearing per block; `StartLayoutTests`, tightened `PropLayoutQaTests.StartBlocks_OnTheMatchMap_*` |

## Repro
1. `& $env:GODOT --path game -- --seed 1 --screenshot <abs png> --screenshot-after 2` (the defaults: 12 forests, 8 mines, 100 units per player).
2. Look at the screenshot: player 0's grey block is spread between the tree cones of a forest on the plateau west of the centre.
3. Measured: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StartBlocks_OnTheMatchMap" --logger "console;verbosity=detailed"`
   prints the start spots that have a resource node in their 3 x 3 neighbourhood. Seeds 1-20 at 12 / 8, 100 per player:
   `seed 1 p0: 17/100, seed 2 p0: 1/100, seed 3 p0: 9/100, seed 7 p0: 10/100, seed 7 p1: 4/100, seed 11 p0: 17/100, seed 11 p1: 2/100, seed 15 p1: 1/100, seed 20 p1: 5/100`.

## Expected
docs/02 "Map": start locations are open bases (2 mines per start location, forests around the map). Until start
locations exist (M3), the debug start block should at least land on open ground, so the first thing the owner sees
on a default launch is an army standing in a clearing, not threaded through a forest.

## Actual
M2-3b turned forests on in the match (12 by default) without reserving the start blocks: the placer does not know
where `StartLayout.Block` puts the armies, and `Block` just takes the passable cells between trees. On the default
seed 17% of player 0's spots touch a tree; 9 of 40 start blocks over seeds 1-20 touch a forest. Every spot is still
a passable cell (the QA test asserts it), so nothing is stuck or invalid; it looks wrong and splits the army.

## Notes
- Out of the M2-3b brief's scope on purpose ("changes to the sim's start layout" OUT), so this is a triage call for
  the Producer: either a resource-free zone around each start block in the placer (sim track, with M3 start
  locations), or `StartLayout.Block` skipping cells near `NavFlags.Resource` (view track).
- Regression test to un-report when fixed: `QA/ViewApi/PropLayoutQaTests.StartBlocks_OnTheMatchMap_NeverOnAResourceCell_ReportsHowManyTouchAForest`
  (currently asserts only that no spot is on a blocked cell; tighten to "no spot touches a node" with the fix).

## Re-check (2026-10-07-0800, M2-H2 commit f4e1b8d): fixed
Verified: `ViewH2QaTests.StartBlocks_Seeds1To40_*` (independent open / clearing oracle, 100 and 1,000 per side, spacing, determinism, hash unchanged) and the CLI-map row pass; the real Match at `--units 1000` on seeds 1 / 13 / 40 spawns 1,000 per side on open cells, one level each (`QaH2Test`). Screenshots at 100 and 1,000 units looked at: both blocks clear of trees, mines and cliff edges.
