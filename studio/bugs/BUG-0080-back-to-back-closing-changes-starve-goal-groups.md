# BUG-0080: A closing change on every tick starves all but 2 goal groups for as long as it lasts

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-06-1744, task M3-2b |
| System | movement build pass / flow-field cache (closing grid changes) |
| Fixed by | Not fixed; documented as a known limit in M3-H1 (4abbf37, session 2026-10-07-0800, Producer decision): docs/03 "Known limits" states the measured bound (a closing every p ticks lets only the 2p oldest goal groups walk; `SimHardeningTests.ClosingsEveryPeriodTicks_OnlyThe2xPeriodOldestGroupsWalk` pins periods 1 / 2 / 4) and why the usable-stale change was left (walkers pressed against a new building on an old field would count stuck ticks and give up in 20 ticks; the closing semantics are pinned by overlay, `PeekCached` and cache tests on both tracks). Stays open at S3; revisit when the M5 AI's placement rate is known |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FourGroups_AClosingChangeEveryTick_StarvesTwoGroupsWhileItLasts_Report" --logger "console;verbosity=detailed"`
   (`QA/GridChangeQaTests.cs`): 300 units, 4 goal groups ordered on the same tick (`CrowdRows.ToFourPoints`
   seed 1), and a closing change (the `BumpVersionForTests` seam) after each of the first 300 ticks.

## Expected
docs/03 "Known limits" (M3-2b): "A closing change still makes every field unusable at once: with more than 2
goal groups the younger ones wait up to ceil(groups / 2) ticks." With 4 groups that's 2 ticks.

## Actual
```
closing every 1 tick(s) for 300 ticks, 4 goal groups: longest field wait 300 ticks; Arrived = 116, GaveUp = 184, Ticks = 965
closing every 2 tick(s) for 300 ticks, 4 goal groups: longest field wait 1 ticks; Arrived = 130, GaveUp = 170, Ticks = 828
closing every 3 tick(s) for 300 ticks, 4 goal groups: longest field wait 1 ticks; Arrived = 143, GaveUp = 157, Ticks = 750
```
With a closing change every tick, every field is unusable every tick, the cap builds the 2 oldest goals
(ties: the lower goal cells, BUG-0026), and the same 2 groups stand still for all 300 ticks. The bound
holds only while closings are at least ceil(groups / 2) ticks apart (32 groups: 16 ticks).

## Notes
- This is BUG-0073's mechanism, kept for closing changes: M3-2b split opening from closing changes and fixed
  the opening half (felling) only. Not a regression (before M3-2b any change did this), but the docs sentence
  is wrong as written.
- No current game path closes cells every tick (only `SpawnBuilding`, dev / tests). It matters from M3-3:
  several players (and the AI) placing buildings, and any later wall-segment placement, can make closings
  back-to-back.
- Options: fix the docs sentence now; at M3-3 consider keeping a closed field usable when none of its
  directed cells were blocked (a per-change bounding box against the field), region versions, or letting a
  group keep walking its old field until the cells it would step into are actually blocked.
