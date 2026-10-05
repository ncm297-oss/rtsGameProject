# BUG-0037: An Idle enemy holding the walker's goal cell counts as its arrived groupmate, so it is no wall (walked 0.12-0.16 m into in one tick)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-05-1234 (re-check round 1), task M1-5 |
| System | movement (`MovementSystem.Plan` groupmate rule, `WallLimit`, `AimCovered`, `RecheckAnchors`) |
| Fixed by | |

## Repro
1. Remove the `Skip` from `QA/HardWallQaTests.IdleEnemyHoldingTheWalkersGoalCell_IsStillAHardWall` and run
   `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~IdleEnemyHoldingTheWalkersGoalCell`.
2. Setup: flat 24 x 24 map. A player-1 radius-0.9 unit is ordered to cell (15, 10)'s center and
   arrives (Idle, GoalCell 255). A player-0 radius-0.4 unit 8 m west is ordered to a point 0.6 m east
   of that center (same cell) and walks straight at the enemy.

## Expected
docs/03 (the BUG-0035 fix): "Other players' units that aren't walking (Idle) are *hard* walls: a step
never goes deeper into one, also when the unit touches several walls at once." The walker stops
touching the enemy.

## Actual
```
walker ended Idle, goal cell 255, overlapping the enemy by 0.120 m; worst per-tick deepening 0.120 m
```
The walker steps 0.12 m into the enemy in one tick and "arrives" there, holding the goal through
the enemy (an enemy counts as the anchor of its blob). The same in the QA open-field fuzz
(`OpenField_TwoArmiesMixingWithIdleEnemyClusters_...`, seeds 1-8, random goals for both players):
whenever a walker's goal shares a cell with an Idle enemy's goal, it walks into that enemy:
seed 3: 2 times, up to 0.155 m in one tick; seed 5: 2 times, 0.110 m; seed 6: 0.042 m; seed 7:
0.029 m. Pairs whose goal cells differ never go deeper (2e-6 m worst over ~43,000 contacts).

## Notes
- Cause: every "groupmate" test in movement checks only `GoalCell`, never `Owner`:
  `Plan` (`groupmate = idle && u.GoalCell[j] == goalCell`, so the enemy gets the soft
  `SeparationShareStill` push and counts for `touchingArrived`), `WallLimit` (skipped as a
  groupmate before `IsHardWall` is ever asked), `AimCovered`, and `RecheckAnchors` (an enemy can
  anchor a player's blob). The queued check in `Plan` is owner-blind too (noted in the round-1 report).
- The developer listed this as "not fixed" in the round-1 fix report, but docs/03 states the
  hard-wall rule without the exception.
- Why S3, not S2: M1 has no combat, the BUG-0035 repro (enemies plugging a gap, walkers ordered past
  them) is fixed, and a walker doesn't pass through: it stops at the enemy and claims arrival. It
  matters from M4: an attack-move or a Move onto the point an enemy army holds puts both armies in one
  "blob", and two players racing to the same ramp top share an anchor. QA's two-owner cross-map rows
  (both players to the same goal) currently arrive as one mixed blob because of this rule; if it
  changes, those rows need re-measuring.
