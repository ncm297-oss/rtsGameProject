# BUG-0027: Back-off never counts toward giving up; crowded units stay Moving forever

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-05-0742, task M1-4d-1 |
| System | movement (MovementSystem crowded arrival / give-up) |
| Fixed by | M1-4d-1 fix round 1, commit 878fb62 (QA re-check verified: 2,500 units to one point all Idle at tick 802); LocalMovementTests.CrowdedAtGoal_BackOffRefusedByACliff_StopsAfterGiveUpTicks_KeepingItsGoal, QA/LocalMovementQaTests (2 un-skipped), Stress/LocalMovementStressTests 2,500 rows un-skipped |

## Repro
1. Un-skip and run (each fails today):
   - `QA/LocalMovementQaTests.UnitAtGoal_OverlappedByIdleStranger_AgainstAWall_StillGoesIdle`
     (a unit at its goal hugging a cliff, an idle unit with no goal 0.1 m east of it; it is still
     Moving after 200 ticks, position unchanged, stuck counter 0).
   - `QA/LocalMovementQaTests.FiftyUnitsSpawnedOnOnePoint_OrderedToThatPoint_AllTerminate`
     (open ground: 14 of 50 still Moving after 600 ticks).
   - `Stress/LocalMovementStressTests.Crowd_ToOneOrFourClosePoints_InvariantsEveryTick_AllSettle(2500, 1, 6000)`
     (2,500 units to one point on the default map: 57 still Moving after 6,000 ticks = 5 minutes)
     and `(2500, 4, 6000)` (one unit never stops).
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~LocalMovementQaTests|FullyQualifiedName~LocalMovementStressTests"`

## Expected
Brief M1-4d-1 / QA focus "give-up abuse": every unit terminates (goes Idle) within
`GiveUpTicks` + a small slack and never loops; "all go Idle" for 500 and 2,500 crowds.

## Actual
A unit that is at its goal (or touching an arrived groupmate) but "crowded" (a neighbor center
closer than `ArrivalSpacing` x radii) takes `ActBackOff`. Back-off ticks are, by design (docs/03
"Giving up"), "neither progress nor stuck", so `StuckTicks` never advances. Two ways it never ends:

- **Refused back-off:** the push points into a cliff (or the collide-and-slide pushes out of a
  standing stranger toward the wall); `CanStep` refuses; the step is zero every tick, forever.
- **Period-2 oscillation** at a blob's edge (2,500-unit run, straggler 16, 45-55 m from the point):

```
t0: pos <180.17422, 107.06693> v <0.10336723, 0.17121688> stuck 3 touchingArrived 1 crowdedBy 2 Moving
t1: pos <180.07086, 106.89571> v <-0.10336092, -0.17122068> stuck 3 touchingArrived 3 crowdedBy 3 Moving
t2: pos <180.17422, 107.06693> ... (identical to t0, repeats for good)
```

The unit backs off, walks back in, touches the blob again and is crowded again; its stuck counter
is frozen at 3. Visible as permanent jitter, and these units stay in the Moving set (tick cost)
forever. Spawning at one point (production buildings later) triggers it easily.

## Notes
Suggested direction (developer's call): count back-off ticks toward giving up (or a separate
cap), or let a crowded unit that cannot back off stop anyway. Keep the BUG-0020 corner rule.

## QA verification (2026-10-05-0742 re-check round 1)
Verified at 878fb62. The 4 formerly skipped tests pass: wall-hugging unit Idle after 20 ticks; 50
coincident units to their own point all Idle at tick 39 (11 arrived, 39 gave up); 2,500 to 1 point
all Idle at tick 802 (2,488 arrived, 12 gave up); 2,500 to 4 points all Idle at 1,120. Reverting
the fix (back-off not counted) fails 9 tests. No stray anchors from units keeping their goal at the
limit in 7 crowd scenarios (`QA/LocalMovementRecheckQaTests`). Side finding: BUG-0031.
