# BUG-0673: Two raiders taking turns on opposite sides of the hall re-send the whole army every think

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-13-0916, task M5-4b |
| System | AI (DefenseManager) |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DefenseQaTests.TwoRaidersTakingTurns_DoNotReSendTheArmyEveryThink"`:
two player 1 Raiders, 30 m north and 30 m south of player 0's hall, each inside `defendRadius` on alternate thinks (the
other out of it), for 10 thinks; three army units.

## Expected
The M5-4b QA focus: a threat that flickers must not re-issue orders each think. The brief's rule re-sends only when the
point moves past `rallyReach`, which is meant to stop churn.

## Actual
`29 AttackMoves to 3 units over 10 thinks; defense versions 10`. The point (the centroid of the intruders seen this
think) jumps 60 m each think, so every unit is re-sent each think and the army walks back and forth, never reaching
either raider. Two cheap units dancing at the fog edge pin the whole army.

## Notes
A single threat flickering at one spot is fine (`DefenseQaTests.AFlickeringThreat_AtOneSpot_SendsTheArmyOnce`). Options
(Producer's call): keep the point while a remembered intruder is still near it, hold a sent point for a minimum time, or
use the blackboard's enemy memory rather than this think's sightings.
