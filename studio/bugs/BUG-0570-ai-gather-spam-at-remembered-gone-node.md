# BUG-0570: The AI re-sends idle workers every think to a node it remembers but is gone; they never gather again

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-11-0634, task M5-2 |
| System | AI `EconomyManager` (with BUG-0540's node memory) |
| Fixed by | M5-2 fix round 1 (3cd37a1): `EconomyManager` marks a node whose Gather left the worker Idle out of its sight (`Blackboard.NodeDropped`, `GatherNode`); `QA/AiSeatQaTests.IdleWorkers_AreNotSentForever_ToARememberedMineThatIsGone` un-skipped |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiSeatQaTests.IdleWorkers_AreNotSentForever_ToARememberedMineThatIsGone"`
2. Scene: the flat AI scene (`AiScenes.Flat`), player 0 an AI seat (Hard) whose only build-order step can't fire for an hour.
   Every gold mine is mined out (`ResourceStore.Take`) at tick 0: player 1's two mines out of player 0's sight, so player 0's
   node memory keeps them (`Remaining` -1, the BUG-0540 rule). Trees are known and in sight.

## Expected
Brief M5-2 QA focus: nothing the AI does may "stall or spam". docs/03 "Implementation (M5-2)", Economy manager: idle workers
go to gold or wood; "with no node known a worker stays idle". A worker sent to gather where nothing is should not be sent to
the same spot every think forever; the 3 workers the gold share wants on gold should end up doing something (wood is known,
or they walk there and see the mine is gone, which drops it from memory).

## Actual
```
120 Gathers over 41 thinks; most to one worker 40; idle workers at the end 3; wood 200
```
The gold share picks gold for 3 of 5 workers; `NearestNode` returns a remembered mine that is gone; `Command.Gather` resolves
no node at that position and is dropped whole (`OrderSystem.Apply`: "No node to work drops the whole command"), so the worker
never moves and stays Idle. The economy manager counts it as idle again next think and sends the same Gather. Each of those
workers gets one dropped command per think (2 a second on Hard), for ever, and never gathers: 3 of 5 workers dead, and the
replay log grows by one line per worker per think for the rest of the match.

In a real match this happens once a mine the AI scouted (an enemy's, an expansion) is mined out out of its sight and its own
mines run dry, or for wood when the nearest remembered tree was felled out of sight.

## Notes
Introduced by the combination of this task's items 1 (BUG-0540 memory: `NextResource` now answers gone nodes) and 5 (the
economy manager reads it as a gather target). Possible fixes (dev / Producer call): send a `Move` toward a remembered node
with `Remaining` -1 instead of a `Gather` (the worker then sees the ground and the memory drops it), or mark a node whose
Gather was dropped (the worker still Idle at the next think) and skip it for the other kind / next node, as the executor's
retry marks do for Builds. The regression test is in `QA/AiSeatQaTests.cs`.

## Verified (QA re-check round 1, 2026-10-11-0634)
Status set by QA (the dev can't edit `studio/**`). The un-skipped row passes: `3 Gathers over 41 thinks; most to one worker 1;
idle workers at the end 0` (was 120 / 40 / 3). QA's `QA/AiGatherMarkQaTests`: every remembered node of both kinds gone (16
nodes, 5 workers): 55 Gathers in the first 40 thinks (bound 80), 0 more in the next 40, all 16 marked; a live out-of-sight mine
is never marked over 200 thinks and is mined (gold 200 -> 350); two runs give equal hashes at every think. Mutation "never
mark" (only the kind flip left) passes the un-skipped row but fails QA's bounded row. Follow-on: BUG-0574 (a live mine can be
marked when a loaded worker has no drop-off; marks never clear).
