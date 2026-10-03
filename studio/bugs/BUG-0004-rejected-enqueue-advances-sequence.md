# BUG-0004: A rejected Enqueue (queue full) still advances the player's sequence counter

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-03-0907, task M1-1 |
| System | command queue |
| Fixed by | a54a4b2 (M1-2); regression test `Rts.Sim.Tests.QA.SimCoreQaTests.Enqueue_WhenQueueFull_LeavesStateUnchanged` |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.SimCoreQaTests.Enqueue_WhenQueueFull_LeavesStateUnchanged`.
2. `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~Enqueue_WhenQueueFull`

Equivalent by hand: `CommandCapacity: 2`, enqueue two commands, take `StateHash()`, enqueue a
third (throws `InvalidOperationException`), take `StateHash()` again.

## Expected
The QA focus for M1-1: enqueue beyond capacity must fail explicitly, not corrupt. A call that
throws should leave sim state unchanged.

## Actual
The exception is thrown, but `StateHash()` changes (`182186671885610540` -> `12415314852577360621`)
because `Simulation.Enqueue` increments `_nextSequence[player]` before `CommandQueue.Add` throws,
and the sequence counters are part of the hash.

## Notes
Still deterministic (the same failure in a replay gives the same gap), so not S1. But a replay
recorder that only logs accepted commands would diverge from a live run that hit the limit, and
the sequence numbers get a gap. Fix: check capacity (or call `Add`) before incrementing the
counter. Invalid player ids are checked first and do leave state unchanged (verified by
`Enqueue_InvalidPlayer_LeavesStateUnchanged`).

**Producer triage (2026-10-03-0907):** S3 confirmed, does not block M1-1. Fix in the first commit
of M1-2: stamp the sequence from `_nextSequence[player]` without incrementing, call `Add`, then
increment; un-skip `Enqueue_WhenQueueFull_LeavesStateUnchanged`.

**QA verification (2026-10-03-1151, M1-2):** test un-skipped and green. Mutation check: moving the `++` back into the stamp line (pre-fix order) in a scratch copy makes the test fail; with the fix it passes.
