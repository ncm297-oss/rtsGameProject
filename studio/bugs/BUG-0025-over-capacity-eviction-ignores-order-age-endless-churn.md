# BUG-0025: With more live goals than cache slots, the build pass evicts by goal cell instead of order age: older groups stall while newer ones walk, and every build is churn

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-04-2056, task M1-4c |
| System | movement / pathfinding (MovementSystem build pass, FlowFieldCache LRU) |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.FieldBuildFairnessQaTests.LiveGoalsOneOverCapacity_OlderOrdersNeverWaitWhileNewerWalk_NoEndlessChurn`,
   or run `LiveGoalsOneOverCapacity_Measure` for the numbers.
2. Setup: 72 unit slots, so the cache holds 32 fields. 34 units of player 0 start near row 10, and
   each gets its own goal near row 118 (far enough that nobody arrives during the run).
   Units 0-31 are ordered on one tick. After 30 ticks all 32 fields are built. Then units 32-33
   are ordered (newer orders). The test watches the next 100 ticks.

## Expected
docs/03 "Build cap" says an order never waits behind a newer one. With 34 fixed goals on 32 slots,
some churn is unavoidable. But the groups that wait should be the newest orders, and the cache
should not rebuild a field on every tick forever.

## Actual
```
34 live goals on 32 slots, 100 ticks: 200 builds, older groups waited 200 group-ticks, newer 0
```
The cap is spent on every tick (2 builds/tick, about 1.4 ms in Debug), and every build re-serves a
field the cache just evicted. On every tick, two of the *older* groups stand still (velocity 0, no
field) while the two *newer* groups walk on every tick.

Cause: the build pass touches every cached field a live group needs, in goal-cell order. When all
slots are live, LRU evicts the field touched first that tick, which is the lowest goal cell. That
choice ignores order age. The evicted group becomes next tick's oldest miss, so it is rebuilt
first, which evicts the next-lowest cell. So the low-cell groups keep trading fields (expected
from the code, not measured per group), while the newest orders keep theirs.

## Notes
- The scale probe `Starvation_300GroupsCyclingEvery40Ticks_Cache64_MeasuresFrozenAndChurn` is less
  severe: 18% of builds are rebuilds after eviction (210 of 1,200), and 78% of group-windows are
  frozen. That freeze rate is inherent: two builds/tick can serve at most 80 goals per 40-tick
  window (about 66 distinct goals get one), and there are 300 goals. M1-4b's probe was 483/960
  frozen.
- docs/03 already says "builds keep running at the cap until enough groups arrive". The claim
  "an order never waits behind a newer one" is still false in this regime.
- Possible directions (Producer's call): when the cache is full of live fields, don't build for a
  group newer than every cached group's order (wait instead of evicting), or evict the live field
  whose group has the newest order rather than the lowest cell.
- Related: BUG-0022 (fixed for the within-capacity case), BUG-0021.
- **Producer triage (2026-10-04-2056):** S3, does not block M1-4c or M1-4d. Only bites with more
  live goals than cache slots (> 64 at 512 units, > 128 at 1024+), which no current scenario
  reaches. Fix as its own small task after M1-4d (preferred direction: when every slot holds a
  live field, evict the one whose group has the newest order; fold BUG-0026's tie-break in). docs/03
  "Build cap" now states the limitation instead of the false sentence.
