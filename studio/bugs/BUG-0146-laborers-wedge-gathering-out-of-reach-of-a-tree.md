# BUG-0146: Laborers wedge in Gathering out of reach of a tree (and a dev-seen walker deadlock in a two-cell corridor): wood income stops for the rest of the match

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-07-2014, task M3-V4 (QA; found by the M3 Playable scene, not caused by M3-V4) |
| System | sim: gather loop / movement arrival (`Economy/EconomySystem.cs`, `Movement/`), sim track |
| Fixed by | |

## Repro
1. The replay is attached: `studio/bugs/BUG-0146-seed21-wood-wedge.replay`. It is `M3PlayableTest -- --seed 21`'s own
   command stream, shipped data at data hash `702859B867AAC412`, a checkpoint every tick, 16,001 ticks, and it replays
   bit-exact.
2. Regression row (skipped while open): `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs`
   `Seed21PlayableReplay_NoGathererStandsOutOfReachForever`. Remove the `Skip` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherWedgeQaTests"`.
3. Or regenerate it: `& $env:GODOT --headless --path game res://tests/M3PlayableTest.tscn -- --seed 21` gives
   `M3 PLAYABLE TEST FAIL 12. Age II live, W (seed 21, tick 16001): Age II's cost: not by tick 16000 (gold 980, wood 100)`.

## Expected
docs/03 "Economy implementation" (M3-2): a worker standing out of reach on its gather loop walks again every
`RetryTicks` (20) ("the queue at a mine's edge"). A walker that arrives is within `Reach` (1.25 m) of the footprint
(`GoalInset`). A worker with a live, exposed node either works it or reaches it.

## Actual
Player 0's laborers 10-13 gather the tree at cell (57, 45). They stand in a column south of the tree, in state
`Gathering` with velocity 0, re-walking every 20 ticks and ending on the same spot. Unit 10 stays within 1 m of one
spot, out of reach, for 14,575 ticks (about tick 1,240 to 15,815). The regression row fails today with
`unit 10 stood out of reach of its tree for 14575 ticks (to tick 15815)`. Player 0's wood stays at 5 the whole time
(diagnostic replay, every 100 ticks):
```
W t2600 wood 5 | 10:Gathering@(115.0,93.7)v0.00 11:Gathering@(115.0,95.1)v0.00 12:Gathering@(115.0,95.8)v0.00 13:Gathering@(115.0,94.4)v0.00
...
W t4400 wood 5 | 10:Gathering@(115.0,93.7)v0.00 11:Gathering@(115.0,95.1)v0.00 12:Idle@(115.0,95.8)v0.00 13:Gathering@(115.0,94.4)v0.00
```
The front unit (10) is 1.72 m from the tree's footprint edge (y 92), out of `Reach`. No unit stands between it and the
tree. Its 20-tick retry walks end on the same spot. The map around it (x 49-65, y 39-53; T tree, B building = the
Depot the scene placed by the forest, u a laborer):
```
     90123456789012345
   43 ......TTTTT......
   44 ....BBTTTT.......
   45 ....BB.TTT.......      tree (57,45): trees north / west / east, open only to the south
   46 ......u.u........
   47 ........u........
```
The scene's re-task kept sending more idle laborers to the same (nearest) tree, so up to 8 "woodcutters" produced
95 wood in 13,000 ticks.

**Dev-reported, not reproduced by QA (no 800-tick site stall in QA's 8 runs of the scene on the branch):** on seed 6, a builder leaving the mine wedged against two
gatherers standing in the two-cell corridor between the mine's Depot and a cliff (around cell (47, 86)), its velocity
flipping about ±0.19 m/s every tick, in about 1 in 5 two-seed runs. The scene works around it after 800 still ticks by
sending another laborer, and saves `m3playable-seed6-stall-tick<T>.replay`. None was saved on this machine in QA's runs.

## Notes
- Suspected cause (not confirmed): the walk goal for a node open on one side is reached as "arrived" (flow-field cell or
  crowd arrival) at a point that is not within `Reach`. The retry then repeats the same arrival. `GoalInset` assumes the
  walker gets within `ArrivalDistance` of the goal point, which a stopped crowd / pocket may not allow.
- Not a view bug. It makes the M3 Playable scene's budget depend on whether its nearest tree is such a pocket.
- **Replay header vs D4 (QA 2026-10-07-2315).** The attached replay is a 16,087-line text file headered
  `data-hash 702859B867AAC412` (the pre-D4 shipped data). D4 regenerates the golden to `data-hash A863BAF8637CC860`
  (string-only data changes). Once D4 is merged, the replay loader's data-hash check will reject this file, so whoever
  un-skips `GatherWedgeQaTests` next session must either (a) have the row bypass or override the header's `data-hash`
  (the D4 changes are text only, so the command stream still plays the same game), or (b) regenerate the replay on the
  merged data with `M3PlayableTest -- --seed 21` (the scene saves `m3playable-seed21-fail-tick16001.replay` in Godot's
  user data folder) and confirm the wedge is still in it before replacing this file. Don't loosen the row's assertion
  to make it pass. The row used to `return` (pass) on a data-hash mismatch; QA 2026-10-07-2315 changed that to a
  failing assert, so a stale header can no longer let the un-skipped row pass without testing. Hand-editing the header
  is not enough either: the file is checksummed, and QA's scratch edit of only the `data-hash` line made
  `ReplayFormat.TryReadFile` return `ChecksumMismatch`. So option (a) means the row substitutes the hash in code (or
  rewrites the file through `ReplayFormat` so the checksum is recomputed), not a text edit. QA re-ran the un-skipped
  row on this branch's data at 2026-10-07-2315: it still fails with
  `unit 10 stood out of reach of its tree for 14575 ticks (to tick 15815)`.
