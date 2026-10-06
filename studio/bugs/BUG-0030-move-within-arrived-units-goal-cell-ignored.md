# BUG-0030: A Move to another point of an arrived unit's goal cell is ignored

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-05-0742 (re-check of the BUG-0029 fix), task M1-4d-1 |
| System | commands / movement (Simulation.ApplyMove "same order" rule) |
| Fixed by | M1-4d-3 (0a71412): `QA/LocalMovementRecheckQaTests.ArrivedLoneUnit_OrderedToTheOppositeCornerOfItsGoalCell_MovesThere` (un-skipped, passes), `CrowdRoutingTests.ArrivedUnit_ReorderedWithinItsGoalCell_MovesOnlyForAPointFartherThanArrivalDistance` |

## Repro
1. Remove the `Skip` from `QA/LocalMovementRecheckQaTests.ArrivedLoneUnit_OrderedToTheOppositeCornerOfItsGoalCell_MovesThere` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ArrivedLoneUnit_OrderedToTheOppositeCorner" --logger "console;verbosity=detailed"`.
2. Scenario: one unit on a flat map is ordered to (30.1, 30.1), the south-west corner of cell (15, 15). It
   arrives. It is then ordered to (31.9, 31.9), the north-east corner of the same 2 m cell.

## Expected
A player's short reposition order moves the unit (docs/03: a Move sends the unit to the target
point; arrival is within ArrivalDistance = 1 m of the point). It should end within 1 m of (31.9, 31.9).

## Actual
```
stayed 2.55 m from the new point (moved 0.00 m)
after the second order: Idle at <30.092644, 30.103214> (was <30.092644, 30.103214>), 2.55 m from b, 1 ticks
```
The fix for BUG-0029 treats any Move whose target resolves to the unit's current `GoalCell` as "the
same order". An Idle unit that kept its goal cell (arrived, or stopped at the back-off limit)
ignores it completely; it does not even take the new point. Cells are 2 m, so a unit can ignore an
order up to about 2.8 m away (corner to corner), and a lone arrived unit can never be moved within
its cell except by first ordering it to another cell. For a Moving unit the new point is taken, and
that path works (`MovingUnit_RetargetedInsideItsGoalCell_ToTheOppositeCorner_Arrives_EveryType`).

## Notes
- The cell is coarser than "same order". Possible direction: keep the no-op only when the new point
  is within ArrivalDistance of the stored `Goal` (or when the unit would already count as arrived
  for the new point). Otherwise restart it, which still leaves click-spam to one point harmless.
- Forward risk for M1-4d-2 (shoving): once Idle units can be pushed, a shoved arrived unit with a
  kept goal cell cannot be sent back to its point by re-issuing the same order.

## Fix (QA verified at 2026-10-05-1609 (M1-4d-3, commit 0a71412))
An Idle unit re-ordered to a point of its goal cell farther than `ArrivalDistance` from its stored goal
takes a new order and walks there; click spam to one point stays a no-op
(`QA/CrowdRoutingQaTests.ArrivedBlob_ClickSpammedAtTheSamePoint_NoUnitRestartsOrMoves`, the BUG-0029 tests).
The same change also lowers a *Moving* unit's best estimate by 2 x the shift on a same-cell retarget,
which makes a freely walking unit give up mid-route after one 1.2 m correction: filed as BUG-0043 (S2).
