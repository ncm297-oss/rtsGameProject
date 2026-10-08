# BUG-0156: A unit hitting a building in reach never turns on an enemy unit that is killing it (tier 0 priority skipped while in reach)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-08-0313, task M4-2a re-check round 1 (pre-existing since M4-1; found while checking BUG-0154) |
| System | sim: combat acquisition (`CombatSystem.Acquire`: no scan while "engaged in reach"), sim track |
| Fixed by | |

## Repro
1. Scene (48 x 48 flat): a Malazan Heavy Infantry attack-moves into an enemy Tent (or is ordered to Attack it) and hits
   it; an enemy Raider is then ordered to Attack the Heavy Infantry. No further player orders.
2. QA diagnostic `RetargetDiag.UnderAttackWhileHittingABuilding_AttackMoveOnTheAttacker(reissue: false)` (scratch, not
   in the suite). Same result on `9be1991` and `fea7963`.

## Expected
docs/03 "Target acquisition": "Priority: enemies attacking me > units that can attack > other units > buildings", and
units retaliate when hit. A building can take minutes to fall (a Heavy Infantry does 1 damage a hit to a Tent), so
standing on it while being cut down is not a swing worth keeping.

## Actual
The Heavy Infantry keeps hitting the Tent and dies 360 ticks after the Raider's first hit; the Raider is untouched
(108 / 108 hp). The scan is skipped while "mid-swing, or engaged in reach" (docs/03 "a swing is never thrown away for a
better target"), and that rule doesn't tell a building target from a unit target.

## Notes
- Producer's call whether the in-reach rule should keep a *building* target when a tier-0 attacker exists (e.g. re-pick
  after the current swing when the target is a building and someone is hitting the unit).
- Until BUG-0154 is fixed the player's attack-move can't rescue the unit either, only a Move or an explicit Attack.
