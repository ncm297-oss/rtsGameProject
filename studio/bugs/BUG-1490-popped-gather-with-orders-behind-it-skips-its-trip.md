# BUG-1490: A Shift-queued Gather with more orders behind it does no trip when it starts walking, one full trip when it starts in reach

| Field | Value |
| --- | --- |
| Severity | S4 (filed S3 by QA; rare input, no crash, no determinism or AI effect) |
| Status | open (Producer decision at the 2026-11-15-1536 ACCEPT, docs/01 2026-11-15 (c) item 2: the intended rule is exactly one trip for a queued Gather with orders behind it, built by the first sim task that changes the save format; until then the behaviour stands and docs/02 says so) |
| Found | 2026-11-15-1536, task M6-S11 |
| System | economy / orders: the gather loop with a Shift queue (`EconomySystem.EndsForQueue`, `OrderSystem.Execute` Gather) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherQueueTests.APoppedGather_WithOrdersBehindIt_EndsWhileWalkingOutEmpty"`
   (the developer's pin): a worker queued "Gather node B, then Move" delivers its load at node A, pops the Gather, walks
   toward B with nothing carried, and on the next tick `EndsForQueue` ends the loop (cargo 0, not in reach) and the Move
   starts. The Gather did nothing.
2. The same queue on a worker that happens to pop the Gather **in reach** of the new node (e.g. it delivered at a
   drop-off right beside node B, or the queued Gather is the node it already stands at) runs one whole trip at B first:
   `EndsForQueue` returns false for "cargo 0 and in reach", so it works, fills up, walks back, delivers, then pops the Move.
3. Likewise for a worker still walking out empty but already within `EconomyConstants.Reach` (1.25 m) of the node's
   footprint on the tick the order applies: it is "in reach", so it runs a full trip (QA's sweep
   `QA/GatherQueueQaTests.AQueuedMove_OnEveryTickOfATrip_*` counts these; docs/03 states the rule by reach, so that part is
   as documented).

## Expected
docs/01 2026-11-15 (b) item 5: "the worker finishes its current trip (delivers its load) and then starts the queue".
A player who Shift-queues "gather there, then go here" expects either one trip at "there" or (at least) the same result
wherever the worker stands. docs/02's new sentence says "a queued gather with more orders behind it ends as soon as it
starts with nothing carried", which is only true when it starts out of reach (case 2 contradicts it).

## Actual
The outcome of a queued Gather in the middle of a queue depends on where the worker stands when it pops: nothing (walking)
or one full trip (in reach). Not a crash or a determinism problem; the rule as the developer wrote it, applied literally.

## Notes
- The developer's reason not to give the popped Gather one trip: nothing in the saved state records that the queue was there
  when the loop began, so "one trip, then go" needs a new saved bit (a format bump, out of M6-S11's scope).
- Options for the Producer: (a) accept and fix docs/02's parenthesis to say "with nothing carried and out of reach of the
  node"; (b) a per-unit "loop started from the queue" flag (save format bump) so a popped Gather does exactly one trip;
  (c) the view refuses to queue a Gather anywhere but last.
- No AI match can see this (the AI never queues).

## Producer ruling (2026-11-15-1536)
Option (b): a saved per-unit "this loop was started from the queue" flag, so a popped Gather with orders behind it makes exactly one trip wherever the worker stands. Not worth a save-format bump of its own: it rides on the first sim task that changes the save format anyway (M8-2's stand-still stealth is the likely one). docs/02 "Queued orders (Shift)" was corrected at the ACCEPT to say what happens today. QA's pin `QA/GatherQueueQaTests.Bug1490_APoppedGatherWithOrdersBehind_RunsATripOnlyWhenItPopsInReach` and the developer's `GatherQueueTests.APoppedGather_WithOrdersBehindIt_*` are re-stated by that task.
