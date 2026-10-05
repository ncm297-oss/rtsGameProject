# BUG-0031: A unit overlapping a standing unit with a cliff behind it can't walk away in any direction

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-05-0742 (re-check round 1), task M1-4d-1 |
| System | movement (MovementSystem.Constrain) |
| Fixed by | 5d854cb (M1-4d-2): `Constrain` limits a step to `max(gap, 0)` along the normal, never pushing out of an existing overlap. Regression: the QA theory above un-skipped (3 rows) + `LocalMovementTests.UnitOverlappingAnEnemyStandingUnit_WithACliffBehind_WalksAway` (fails when the cap is reverted; verified by QA mutation) |

## Repro
1. Remove the `Skip` from `QA/LocalMovementRecheckQaTests.UnitOverlappingAStandingUnit_WithACliffBehind_CanStillWalkAway` (3 rows) and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~UnitOverlappingAStandingUnit_WithACliffBehind" --logger "console;verbosity=detailed"`.
2. Scenario (same map as the BUG-0027 regression test): cliff column x = 4. Unit A at (10.05, 11), hugging
   the cliff. Unit B (no orders, Idle) at (10.15, 11), 0.1 m east, so they overlap by 0.7 m. A is
   ordered north along the wall (10.5, 25), south along the wall (10.5, 3), or east (25, 11).

## Expected
A walks away. Moving along the wall, or anywhere that does not go deeper into B, is legal.

## Actual
```
stopped 14.01 m from <10.5, 25> after moving 0.00 m (goal cell -1)
stopped 8.01 m from <10.5, 3> after moving 0.00 m (goal cell -1)
stopped 14.95 m from <25, 11> after moving 0.00 m (goal cell -1)
```
A never moves and gives up after 20 ticks, whatever the order. It stays pinned until B leaves.

## Notes
- Cause (reading the code): `Constrain` treats a standing unit as a wall and subtracts
  `normal * (into - gap)`. When the units already overlap, `gap` is negative (-0.7 m), so every
  candidate step (the full step, both slide axes, the bare flow step) gets a shove of up to 0.7 m
  away from B (clamped to speed), straight into the cliff, and `CanStep` refuses it. Only steps
  that would go deeper into B need removing; an already-overlapping unit should be allowed to keep
  or reduce the overlap.
- How it happens in play: units stopped at the back-off limit (the BUG-0027 fix) can overlap a
  neighbor by more than 40% (documented known limit), and give-ups and spawns also leave overlaps.
  Next to a cliff, such a unit can't be ordered anywhere.
- Shoving (M1-4d-2) may hide it when B can be pushed, but not when B is an enemy or immovable.
