# BUG-1857: The worker floor's population-room test ignores workers queued but not started: it over-queues, and a paid worker waits at the cap

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-11-26-1847, task M6-S14 (QA, tier full) |
| System | AI (`BuildOrderExecutor.Retrain`, the worker floor, BUG-1660) |
| Fixed by | M6-SH3 C1 (`AiPlayer.WaitingWorkerHalfPop` in `BuildOrderExecutor.Retrain`'s room test; QA's row un-skipped; `Ai/M6Sh3AiCureTests.Floor_WithRoomForN_*`) |

## Repro
1. Un-skip `QA/M6S14AiPlayQaTests.Floor_RoomForTwoWorkers_SendsAtMostTwo_NoPaidWorkerWaitsAtTheCap` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Floor_RoomForTwoWorkers"`.
2. The scene: a Normal Malazan AI seat with one Laborer, a finished Keep, 1,000 gold, no wood, and Heavy Infantry placed
   until the population has room for exactly two more Laborers (16 / 20 half-pop).

## Expected
docs/03 "Implementation (M6-S14)" B and the code's own comment: "with a worker alive or queued it also needs population
room now (a Train accepted at the cap waits paid in the queue, BUG-1731's case)". With room for two, at most two worker Trains,
and no paid worker sitting in a queue unable to start.

## Actual
`room 4 half-pop (2 a worker), cap 20; worker Trains before the cap moved (at 4571): 3 at 2001, 2021, 2041; a paid worker
waited at the cap 2091 ticks (first 2480)`. Three Trains with room for two; the third Laborer (50 gold, paid) sat at the head of
the Keep's queue for 2,091 ticks (104 s), until the seat happened to gather wood and build a house. Without a house (no wood
anywhere) it would wait for good, and a paid unit in a queue counts as "can still train", so it also keeps the draw clock
from ever starting (BUG-1731's case, which the room test was written to avoid).

## Notes
- Cause: `Retrain` checks `View.HalfPop + ItemHalfPop > View.HalfPopCap`, but a unit's population is reserved only when its
  item starts (`ProductionSystem.TryStart`). The first floor Train starts at once and reserves; the second goes into the
  queue behind it and reserves nothing, so on the next think the room test reads the same room and sends a third.
- Cure (a few lines): count the queued-but-not-started worker items' half-pop (or every queued unit item's) in the room
  test, i.e. `HalfPop + queuedNotStartedHalfPop + ItemHalfPop > HalfPopCap`.
- At zero workers the floor keeps M6-SH2's no-room-test rule on purpose (docs/03 Q2), which is BUG-1731's existing case;
  this bug is the new case with a worker alive.
