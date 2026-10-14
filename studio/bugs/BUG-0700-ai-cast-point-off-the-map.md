# BUG-0700: The AI's cast point follows a group standing off the map, and the door refuses it again and again

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-13-1616, task M5-4c (by the developer, in `Stress/CommandDoorFuzzStressTests`, seed 4) |
| System | AI (`Ai/AbilityManager`) |
| Fixed by | 416879f (`AbilityManager.Aim` keeps the point half a cell inside the map through `AiPlayer.ClampToMap`, and drops the cast when that leaves it out of range); regression test `Ai/AbilityManagerTests.AGroupOffTheMap_IsCastAtThePointOnTheMapNearestIt` |

## Repro
1. Before the fix: a Cadre Mage of an AI seat by the west edge, three enemy units placed 2 m past the edge (a dev
   `SpawnUnit` can put a unit there), `castMinTargets` 1.
2. Run three thinks.

## Expected
Every `UseAbility` the AI sends names a point on the map (`AbilitySystem.CanUse` refuses any other).

## Actual
The group's centroid was off the map; the AI sent a `UseAbility` there at every think and the door refused each one.
The door fuzz's AI oracle flagged it ("UseAbility off the map").

## Notes
QA re-check (2026-10-13-1616): the regression test passes on 416879f. In a scratch clone, with the clamp taken out of
`Aim`, the test goes red (see the QA report for the mutation result). The fuzz in
`Stress/AbilityManagerFuzzStressTests` also checks that every cast point is on the map, over 320 random scenes.
