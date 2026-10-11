# BUG-0570: The AI re-sends idle workers every think to a node it remembers but is gone; they never gather again

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-11-0634, task M5-2 |
| System | AI `EconomyManager` (with BUG-0540's node memory) |
| Fixed by | |

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
