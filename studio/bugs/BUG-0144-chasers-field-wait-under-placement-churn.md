# BUG-0144: Chasers wait up to 1.7 s for a flow field when buildings are being placed (BUG-0080 threshold 1 s)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (M4-2); the original row now runs combat off (eace378), this skipped row keeps the combat-on scene |
| Found | 2026-10-07-2315 (re-check of 2026-10-07-2014), task M4-1 fix round 1 |
| System | combat chase x flow-field cache (build cap, placement invalidation) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ChasersUnderPlacementChurn_LongestFieldWait_AtMostOneSecond"`
   (`Stress/CombatScaleQaTests`, skipped with this id). It is `ConstructionScaleStressTests.
   APlacementEveryTwoSeconds_32MarchingGroups_LongestFieldWait_Report`'s scene (512 units of two owners in 32 mixed goal
   groups march across the default map; player 0's worker places a House every 2 s) on combat.
2. The metric is a tick count (`GridChangeOracle.WaitingForField`), so it is deterministic, not wall clock.

## Expected
The BUG-0080 bound: no unit waits more than 20 ticks (1 s) for a usable field.

## Actual
`30 placements, combat on: longest field wait 34 ticks (1.70 s, unit 101, chasing True); 219 dead`. Same scene: combat
off 16 ticks (equal to base 8655fb1: 16 ticks, 403 arrived, 27 still moving); combat on with no placements 14 ticks.
The developer traced every wait over 16 ticks to a chaser re-aiming its goal cell about every 4 ticks while the
placements stale the cache.

## Notes
The original row (`APlacementEveryTwoSeconds_...`) is the suite's only red row after fix round 1. QA's recommendation:
run that row with combat off (`MoveScenario.Spawn(..., combat: false)`, a config-only change that measures what it was
written for, BUG-0080 walkers, and gives exactly the base numbers), and track the chaser waits here for M4-2 (with the
chase / give-up rework of BUG-0143). Fix ideas: let a chaser keep walking its old field while a new one is pending, or re-aim only when the target leaves the goal cell's neighbourhood.
