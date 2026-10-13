# BUG-0574: A loaded worker with no drop-off makes the AI mark a live mine out of sight as gone, for good

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-11-0634, task M5-2 (re-check round 1, BUG-0570's fix) |
| System | AI `EconomyManager.NoteDroppedGather` (BUG-0570's dropped-Gather marks) |
| Fixed by | M5-3 `238f261` (`NoteDroppedGather` judges nothing without a finished drop-off or with a full load of the kind; marks clear when the node is seen alive); `QA/AiGatherMarkQaTests.ALiveMine_IsNotMarked_WhenALoadedWorkerHasNoDropOff` un-skipped, `Ai/AiPlayerTests.ADroppedGatherMark_ClearsWhenTheNodeIsSeenAlive` |

## Repro
1. Remove the `Skip` on `QA/AiGatherMarkQaTests.ALiveMine_IsNotMarked_WhenALoadedWorkerHasNoDropOff`, then
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ALiveMine_IsNotMarked_WhenALoadedWorkerHasNoDropOff"`.
2. Scene: the flat AI scene, player 0 an AI seat (Hard) with a quiet build order. Player 0's Town Hall (its only drop-off) is
   freed before tick 0 and its five workers each carry a full gold load (`WorkerCarry`). Its own two mines are live and out
   of its sight (11 cells above the workers).

## Expected
docs/03 "Implementation (M5-2)", Economy manager (as amended for BUG-0570): a node is marked only when the Gather to it was
dropped because the node is gone. A live node is never a "gone" node; the developer's own caveat names an unreachable
live node as the one exception.

## Actual
`live nodes marked dropped: slot 0 (type 0)` after 10 thinks. The first think sends three workers to the near gold mine.
`StartGather` with a full load of the same kind goes to a drop-off first; `StartReturn` finds none and `EndLoop`s, so the
worker is Idle when the order applies. At the next think `NoteDroppedGather` sees an idle worker whose Gather applied, to a
node out of sight (`Remaining` -1), and marks the live mine. `Blackboard.NodeDropped` is never cleared (only a new generation
in the slot escapes it), so after a hall is rebuilt the AI never sends a worker to that mine again; with every known mine
marked this way the AI gathers wood only.

How it happens in play: a human razes the AI's last hall while its workers carry gold (any time from M5-2: the human can
attack). Every gold mine the AI sends a loaded worker to in that window, out of its sight, is lost to it for the match.

## Notes
- A second way to the same mark, named by the developer as a known caveat: a live, out-of-sight node whose Gather leaves the
  worker Idle because no stand point is reachable.
- Possible directions (the developer's call): don't judge a Gather when the worker carries a full load of the node's kind
  (or has no drop-off), or judge only "the Gather was dropped at apply" (the unit's `GatherNode` stayed default) rather than
  "Idle now", or let a mark expire / clear when the node is next seen alive.

## Verified (2026-10-12-1930, QA)
Both rows green on `238f261`. Mutations in a scratch clone: removing the drop-off / full-load guard fails the QA row; removing
`ClearSeenMarks` fails the clear-on-seen row.
