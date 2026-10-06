# BUG-0043: Re-ordering a walking unit to another point of its goal cell makes it give up mid-route

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-05-1609, task M1-4d-3 (regression from the BUG-0030 change) |
| System | commands / movement (`Simulation.ApplyMove` same-cell retarget, progress estimate) |
| Fixed by | M1-4d-3 fix round 1 (6abd200): a same-cell retarget moves the best estimate by exactly its change at the unit; `QA/CrowdRoutingQaTests.FreeWalker_*` (10 rows) un-skipped, pass |

## Repro
1. Remove the `Skip` from `QA/CrowdRoutingQaTests.FreeWalker_ReorderedOnceToAnotherPointOfItsGoalCell_StillArrives`
   and `FreeWalker_JitterSpamClickedWithinItsGoalCell_StillArrives`, then
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CrowdRoutingQaTests.FreeWalker"`.
2. Scenario: one unit on an open flat map, ordered to a point ~35 m away. 40 ticks later (still walking,
   nothing in its way) it gets one more Move to another point of the same 2 m goal cell, 1.2 m from the first.

## Expected
A short correction of the destination inside one cell moves the destination; the unit walks on and
arrives (it did on base 7f741f1 in every case below). BUG-0030's brief rule was about Idle units.

## Actual
```
radius 0.9 (speed 0.11 m/tick), shift 1.2 m: Idle after 21 more ticks, 35.35 m from the new point, goal cell -1
radius 0.9, shift 1.6 m: gave up 35.63 m short      radius 0.4 (0.16 m/tick), shift 1.6 m: 32.63 m short
radius 0.4, shift 2.4 m: 33.21 m short               radius 0.9, shift 0.8 m: arrives
```
Jitter spam (re-clicking around one spot) is worse: every tick at +-0.05 m, every 2 ticks at
+-0.1 m, every 4 ticks at +-0.25 m: the unit gives up 26-40 m short after 22-142 ticks. Base: all arrive.

## Notes
- Cause: M1-4d-3's `ApplyMove` lowers `BestRemaining` by 2 x the shift on every same-cell retarget of a
  Moving unit. Outside the goal cell the progress estimate is the field cost to the goal *cell* plus
  the distance to the aim (a cell center): it does not depend on the point inside the goal cell at
  all, so the whole 2 x shift is pure deficit. A slow unit (2.2 m/s = 0.11 m/tick) needs
  (2 x shift + margin) / speed ticks without progress to make it up; past `GiveUpTicks` (20) it gives up.
- The point of the rule (a blocked unit jitter-re-ordered must still give up,
  `CrowdRoutingTests.BlockedUnit_JitterReorderedInItsGoalCellEveryTick_StillGivesUp`) can be met
  without this, e.g. only adjust the estimate when the unit is in its goal cell (where the aim is the
  goal), or by the change the retarget makes to the estimate rather than its upper bound.
- docs/03 "Giving up" documents the 2 x shift rule, so a fix also updates the doc.

## Re-check round 1 (2026-10-05-1609, fix commit 6abd200): fixed
Verified: the single re-order rows (shift 0.8-2.4 m, radius 0.4 and 0.9) and the six jitter-spam rows all
arrive (0.66-1.00 m from the point, the same ticks as base). The blocked-unit counterpart
(`CrowdRoutingTests.BlockedUnit_JitterReorderedInItsGoalCellEveryTick_StillGivesUp`) still passes.
