# BUG-0078: Exposure counts an open neighbour nobody can reach; a worker sent to a tree exposed only to a sealed pocket retries for ever

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-1503, task M3-2 |
| System | economy (exposure rule, stand-cell choice) vs buildings sealing pockets |
| Fixed by | b4b423c (M3-3): the never-seal rule `Map/SealCheck` behind `World.CanPlace` / `Command.Build` and the dev `SpawnBuilding`; `NeverSealTests`, QA `ConstructionQaTests` (every anchor vs an independent flood oracle), `EconomyQaTests` row now asserts the sealing Keep is refused `SealsGround`. Residual (a Cancel or destruction of an enclosed building reopens a pocket): BUG-0093, S3, sim hardening |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ATreeExposedOnlyToAPocketSealedByBuildings_IsNotWhereTheWorkerEndsUpStuck"`
   (`QA/EconomyQaTests.cs`, skipped with this id; remove the `Skip` to run it).
2. Setup: 40 x 30 flat map; tree A at (20, 15), tree B at (21, 15); five Keeps placed with
   `SpawnBuilding` at anchors (16, 11), (16, 16), (12, 13), (20, 11), (20, 16) seal the corridor
   x 16-19, y 15 (a pocket nobody can enter) whose east end touches tree A. A's only open 4-neighbour
   is the pocket cell (19, 15); B's east neighbour (22, 15) is open and reachable. A sixth Keep at
   (30, 2) is the drop-off. A worker at (30, 8) is ordered to Gather A.

## Expected
A node is worth walking to only if a worker can stand next to it. A, reachable from nowhere, should
count as unexposed, so the order goes to the nearest exposed node (B, 2 m away) by the depleted-node
rule, and wood comes in.

## Actual
```
sealed-pocket tree: node slot 0 (sealed 0, open 1), state Gathering, at <61, 17>, cargo 0, wood 200
```
After 60 s the worker is still on A's loop, standing where it was spawned: every 20 ticks it walks to
the stand cell (19, 15), which no flow field reaches, gives up at once, and waits again. Nothing is
gathered, for ever, while B stands next to A.

## Notes
- `EconomySystem.IsExposed` is "a passable 4-neighbour", and `WalkToFootprint` picks the nearest
  passable ring cell. Both equal "reachable" only while every passable cell connects, which docs/03
  still states ("Unreachable targets can't happen yet: the nav grid seals every pocket"). Since M3-2
  that sentence is no longer true: `SpawnBuilding` does no connectivity check (the developer's report
  defers it to M3-3), so buildings can seal pockets, and the economy then chases them.
- Only the dev / test command can do this today. With construction (M3-3) a player can wall off a
  pocket on purpose or by accident (AoE-style walling), so M3-3 must either refuse placements that seal
  open cells (and mark nothing else) or make exposure / stand cells mean "reachable" (for example a
  region id per passable cell, updated on closing changes).
- Fix the docs/03 sentence at the same time.
