# BUG-0073: A tree falling every tick leaves all but the 2 oldest goal groups without a flow field

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-06-1255, task M3-1 |
| System | pathfinding / economy (depletion vs the flow-field cache) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~OneTreeFallsEveryTick_Report" --logger "console;verbosity=detailed"`
   (`QA/ResourceQaTests.cs`): 32 walkers with 32 distinct goals on a 120 x 72 flat map, all 32
   fields cached, then one tree is felled (`ResourceStore.Take` to 0) before each of 100 ticks.
2. The assertion version is `OneTreeFallsEveryTick_NoWalkerStandsWaitingForItsFieldMoreThan40Ticks`
   (skipped with this bug's id).

## Expected
Felling is M3-2's normal state: with workers chopping, some tree falls every few ticks. Walkers
should keep walking (or wait a bounded number of ticks) while trees fall elsewhere on the map.

## Actual
```
one tree per tick for 100 ticks, 32 goal groups: longest wait 100 ticks; 30 of 32 walkers waited
>= 90 of the 100 ticks; 2 waited <= 2; avg tick 1.820 ms vs 0.014 ms with nothing felled
```
Every fall bumps `NavGrid.Version`, which makes every cached field stale at once. Only
`MaxFieldBuildsPerTick` (2) rebuild per tick, always the two oldest orders, and those are stale again
the next tick. Every other group gets `ActWait` (stand still, no stuck count) for as long as felling
goes on. Each fall also costs a full `ComputeSteps` pass plus 2 field builds, so the tick goes from
0.014 ms to 1.8 ms on this small map, and it grows with map size.

A single burst is fine: 100 trees falling between two ticks with 32 cached fields gives a longest
wait of 15 ticks, no give-ups, and 32 of 32 arrive (`HundredTreesFallInOneTick_With32CachedFields_WalkersWaitThenAllArrive`).

Not reachable in M3-1 (nothing calls `Take` inside a tick); it becomes real with M3-2 gathering.

## Producer triage (2026-10-06-1255 ACCEPT)
S3 agreed; a design question for the M3-2 brief, not a hardening item: the recommended route is the
"open-only changes keep fields usable, rebuild lazily under the cap" option below (closing changes,
M3-3 buildings, still invalidate at once). The `FlowFieldCache` surface is pinned by QA; the brief
must say what changes.

## Notes
- Depletion only ever *opens* cells, so a field that is stale only because of depletions still
  never points into a blocked cell. It is just not the shortest path any more. One option is
  per-field "still safe" versioning: keep using a field after open-only changes and rebuild it
  lazily within the cap, and invalidate everything only for closing changes (buildings, M3-3).
  Others are batching depletions per N ticks, or incremental repair of the affected region.
- Plan this into M3-2's design (the brief says the gather system calls `Take` in tick phase 4).
