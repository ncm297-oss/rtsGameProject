# BUG-0038: The hard-wall fallback drops the clips of the walker's own standing units; a walker beside an enemy slides into an anchored friendly

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-05-1234 (re-check round 1), task M1-5 |
| System | movement (`MovementSystem.Constrain` / `ClosestAllowed`) |
| Fixed by | |

## Repro
1. Remove the `Skip` from `QA/HardWallQaTests.Constrain_EnemyBelowAndAnchoredFriendAbove_FallbackStillRespectsTheFriend`
   and run `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~FallbackStillRespectsTheFriend`.
2. Setup (calls the private `Constrain` by reflection, neighbors in ascending slot order): walker W
   (player 1, Moving) touches an Idle enemy E (player 0, slot 0) straight below it (normal (0, -1))
   and an anchored friendly F (player 1, slot 1, Idle on its own point, a goal cell other than W's, so
   not shovable) up and to the right (normal (0.6, 0.8)). Desired step (0.114, -0.057): right and
   slightly down.

## Expected
docs/03 and the `Constrain` remarks: hard walls are re-checked after the clips; "Its own army's
standing units and units mid-move keep the single clip". The single clip leaves F untouched
(the all-friendly twin `Constrain_TwoAnchoredFriends_SingleClipKeepsTheLastOne` passes: step
(0.073, -0.055), 0.000 into F).

## Actual
```
desired <0.11448669, -0.057243343>, step <0.11448669, 0>: 0.0000 m into the enemy, 0.0687 m into the anchored friendly
```
The clips (E, then F) slide the step back into E, so the fallback runs. `ClosestAllowed` starts again
from the *desired* step and looks at hard walls only, so its answer (the slide along E) ignores F
entirely: W goes 0.069 m (60% of its step) into F. Before the fix (8012852) the same call entered E
by 0.055 m and F by 0.

In the QA open-field fuzz (`OpenField_TwoArmiesMixingWithIdleEnemyClusters_...`, report line),
walkers went more than 1 cm deeper into a friendly unit standing on its own point 0-13 times per
1,500-tick seed, up to 0.14 m in one tick (seeds 2, 4, 5, 6, 8). Not compared against the old code
(the old code fails the enemy invariant of that test first).

## Notes
- Every time the fallback fires, all non-hard walls lose their clip, not only in this corner: the
  candidates (desired, projections onto hard-wall lines, their corners) are checked against hard
  walls only. A fix could take the friendly walls along as constraints when the result allows it,
  or start from the clipped step; QA's `ClosestAllowed_Fuzz_AllowedAndOptimal` pins the hard-wall
  part either way.
- S3: it trades the BUG-0035 enemy overlap for a friendly one in exactly the battle-line situation
  (a unit squeezed between its own line and the enemy's), contrary to the documented behavior. It
  does not let anyone through an enemy plug.

## Producer triage (2026-10-05-1234, ACCEPT)
S3 confirmed, not blocking M1 (no combat yet; nobody passes a plug). Scheduled for the M1 hardening session together with M1-4d-3 crowd routing (same code: `Plan`/`WallLimit`/`Constrain`). docs/03 now lists it as a known gap.
