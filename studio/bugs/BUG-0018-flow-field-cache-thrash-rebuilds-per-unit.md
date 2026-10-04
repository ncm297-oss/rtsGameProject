# BUG-0018: More than 32 live move goals makes the flow-field cache rebuild a field for every unit, every tick (~320 ms/tick)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-04-0120, task M1-4b |
| System | pathfinding (FlowFieldCache) / movement |
| Fixed by | |

## Repro
1. Remove the `Skip` from `Stress.MovementStressTests.Perf_500Units_DistinctTargetsInterleavedBySlot_CostPerTick`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Perf_500Units_DistinctTargetsInterleavedBySlot"`

The test spawns 500 units on the default 128 x 128 map and gives unit `i` the target `targets[i % N]`,
so goals interleave in slot order (the normal case: units of many groups are mixed in the store).

## Expected
docs/03 "Testing" perf budget: 500 units, average tick < 4 ms, p99 < 8 ms. docs/03 "Flow fields":
units heading to the same target share one field.

## Actual
(Debug, as tests run)

| Distinct live goal cells | ms per tick | field builds per tick |
| --- | --- | --- |
| 4 | 0.05 | 0 |
| 32 | 0.07 | ~0 |
| 33 | 322.5 | ~464 |
| 64 | 358.7 | 500 |

One goal past the cache size takes the tick from 0.07 ms to 322 ms (80x the 4 ms budget, about
6 ticks of wall time per tick). It stays that way until enough units arrive.

## Notes
`MovementSystem.Run` calls `cache.Get(GoalCell)` once per moving unit in slot order. With more
distinct goals than slots and goals interleaved by slot, LRU evicts exactly the field the next unit
needs (round-robin is LRU's worst case), so every unit pays a full Dijkstra build (~0.7 ms).
33+ goals in flight is easy to reach: two players with a dozen squads each, scouts, workers, and
(M1-4c) per-unit arrival-slot goals if those end up with distinct goal cells.
Possible directions (developer's call): fetch each distinct goal's field once per tick (e.g. group
units by GoalCell before moving), size the cache to the number of live goals, or have units hold a
reference to their field. Even grouping alone leaves 33 builds per tick (~23 ms) when the working
set exceeds the capacity, so the capacity policy also matters.
Allocation stays at 0 bytes in this state (`Tick_With500Units_And64DistinctTargets_AllocatesNothing`).
