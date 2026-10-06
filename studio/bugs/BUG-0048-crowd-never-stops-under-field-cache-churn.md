# BUG-0048: Livelock: 94 of 128 units stay Moving forever, with 2 field builds every tick (64-goal row, seed 51)

| Field | Value |
| --- | --- |
| Severity | S1 |
| Status | fixed |
| Found | 2026-10-05-1609, task M1-4d-3 (regression: base 7f741f1 stops at tick 238 on the same map) |
| System | movement (queued rule widened to field-waiting units) + flow-field cache under over-capacity load |
| Fixed by | M1-4d-3 fix round 1 (6abd200): behind a field-waiting unit a no-progress tick counts one tick in `QueueOnWaitStride` (4); `QA/CrowdRoutingQaTests.MoreGoalsThanCacheSlots_Seed51_Terminates` un-skipped, passes |

## Repro
1. Remove the `Skip` from `QA/CrowdRoutingQaTests.MoreGoalsThanCacheSlots_Seed51_Terminates` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MoreGoalsThanCacheSlots_Seed51"`.
   The sweep `Stress/CrowdRowSweepStressTests.MoreGoalsThanCacheSlots_Seeds41To80_BoundHolds` shows it too.
2. Scenario: the docs/03 64-goal row, `CrowdRows.MoreGoalsThanCacheSlots(51)`: 128 units (one player per
   goal) to the 64 cells nearest the map center, more live goals than the 32 cache slots.

## Expected
docs/03 (M1-5, restated at M1-4d-3): every hold traces back to a real progress tick, so a jammed group
still gives up; everything stops (the row terminates within ~600 ticks on every other map tried).

## Actual
```
seed 51 t500:   moving 94, standing 56, field not cached 50, builds this tick 2, total builds 1000
seed 51 t3000:  moving 94, standing 55, field not cached 50, builds this tick 2, total builds 6000
seed 51 t20000: moving 94, standing 56, field not cached 50, builds this tick 2, total builds 40000
```
Nothing changes for 20,000 ticks (17 game-minutes): the same 94 units Moving, 50 of them waiting for a
field that gets built and evicted again, 2 builds every tick (the cap) forever. Units 0-12 sit 1.7-24 m
from their goals. Base 7f741f1, same map and orders: everything stops at tick 238.

In the game this is a group of units that never stops "moving" (never gives up, never takes a new
idle state) and a permanent per-tick cost of two field builds (about 1.4 ms on the default map).

## Notes
- Suspected cause: M1-4d-3 made "a unit ahead is waiting for its field" a queued tick (the stuck count
  holds), and waiting itself counts as neither progress nor stuck. With more live goals than cache
  slots the fields keep being built and evicted, so some unit is always waiting and its neighbors hold
  forever. Progress ticks are finite per order (the best estimate only falls), but field waits are not,
  so the docs' termination argument no longer holds. Live goals then never drop to what the cache holds.
- How often: 1 map of 160 fresh ones (seeds 41-200 of this row); the developer's sweep (seeds 1-40)
  doesn't contain one. 500 units to 500 random goals (seeds 1-10) all stop. Rare, but once it happens
  it never ends, and a deterministic sim replays it every time.
- Related: BUG-0025 (eviction ignoring order age, endless churn; fixed at M1-4c).

## Re-check round 1 (2026-10-05-1609, fix commit 6abd200): fixed
Verified: seed 51 stops at tick 389 (53 arrived, 75 gave up; base 92). Termination hunts: 64-goal row
seeds 41-200 all stop; 500 units to 500 random goals seeds 1-10 all stop (6,767-7,918 ticks).
Side effect: random-goal give-ups rose (BUG-0050).
