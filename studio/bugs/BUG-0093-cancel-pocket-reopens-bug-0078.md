# BUG-0093: A cancelled walled-in site leaves a pocket; BUG-0078's stuck worker is reachable with player commands

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-2114, task M3-3 |
| System | construction (cancel / destruction) + gather exposure rule |
| Fixed by | M3-H1 (4abbf37, session 2026-10-07-0800): the pocket rule in `NavGrid.ClearFootprint` with `NavFlags.Pocket` (32): freed cells reopen only if the union of the footprint and the pocket cells 4-connected to it touches open ground, else they stay `Blocked \| Pocket` with no `Version` bump; a later opening beside them reopens the whole chain. QA row `ConstructionQaTests.ACancelledSiteBesideATree_LeavesThePocketBug0078Described_Report` un-skipped (cells `Blocked \| Pocket`, versions unchanged, tree unexposed, worker retargets another tree); `PocketRuleTests` (chain reopen, cell opened into a pocket alone, 4 x 300 fuzz + twins); QA `PocketRuleQaTests` and `Stress/PocketRuleFuzzStressTests` (30 runs x 1,000 steps against an independent reachability oracle, 0 violations). docs/03 "Navigation grid" pocket rule, docs/01 row |

## Repro
1. `QA/ConstructionQaTests.ACancelledSiteBesideATree_LeavesThePocketBug0078Described_Report` (skipped with this id;
   remove the Skip to run it). Player commands only, flat 40 x 30 map:
   - a tree at (20, 15) with trees on its west, north and south sides (its only open side is east);
   - `Build` a House site at (21, 15) (the tree's open side), then three Houses at (21, 13), (21, 17), (23, 15):
     each is legal because the site is already a wall, and together they wall the site in;
   - `Cancel` the site at (21, 15): its four cells reopen as a pocket nobody can reach;
   - `Gather` the tree with a worker outside.

## Expected
The M3-3 brief: "BUG-0078 closed by the never-seal rule". BUG-0078's symptom (a worker sent to a tree exposed only to
a sealed pocket retries for ever) should not be reachable in play.

## Actual
```
pocket after the cancel: True; after 60 s on the tree: state Gathering, node 0 (tree 0), cargo 0, wood 850, at <51.585777, 43.585777>
```
The worker stays on the gather loop for 60 s with nothing gathered. The same works with a destroyed building
(`BuildingStore.Damage`, combat in M4) instead of the cancel.

## Notes
- docs/03 "Implementation (M3-3)" lists this pocket as an accepted exception ("a destroyed or cancelled building that
  other buildings enclosed leaves a pocket ... allowed"), so the never-seal rule itself (criterion 9) holds. What is
  not closed is BUG-0078's player-visible symptom: Cancel is a player command today, destruction arrives in M4.
- Options: when a building is freed, mark a reopened cell that reaches no other passable region as blocked (a pocket,
  like the nav grid does at load), or make the exposure rule count only cells in the node's reachable region. Producer's
  call whether BUG-0078 is closed with this residual.
