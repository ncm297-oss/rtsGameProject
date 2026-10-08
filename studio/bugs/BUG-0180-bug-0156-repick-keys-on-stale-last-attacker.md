# BUG-0180: The BUG-0156 fix keys on any live LastAttacker, so a long-gone attacker makes a building-hitter leave the building for a passer-by

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-08-0913, task M4-2b (sim track, QA full) |
| System | sim: combat acquisition (`CombatSystem.Acquire`, the BUG-0156 condition `!u.TargetIsBuilding[i] \|\| u.LastAttacker[i].Generation == 0`) |
| Fixed by | |

## Repro
`QA/ProjectileQaTests.BuildingHitter_WithAStaleLastAttacker_BehavesLikeANeverHitOne` (skipped for this bug; remove the
`Skip` to run). Flat 64 x 64 map, an enemy Tent at cell (30, 30), a Malazan Heavy Infantry at (28, 30):
1. With the history: an enemy Desert Archer 12 m off is ordered to Attack it, hits it once, then is moved to (8, 8)
   (alive, more than 40 m away). `LastAttacker` still holds the archer.
2. The Heavy Infantry is ordered to Attack the Tent, hits it, then gets an attack-move on its own spot; 10 ticks later it
   is in AttackMove mode hitting the Tent.
3. An enemy Laborer walks past 10 m away (Move, never attacks).
4. Control: the same scene without step 1.

## Expected
The brief (BUG-0156 fold-in): "a unit hitting a *building* in reach re-picks after its current swing **when a unit is
hitting it** (the tier-0 priority)". A unit nobody is hitting should keep hitting the building whether or not it was
hit a minute ago.

## Actual
```
never hit: leaves the Tent = False; hit once long ago (attacker alive, gone): leaves = True
```
With the stale `LastAttacker` the unit re-scans every scan interval while hitting the Tent, and the scan's priority
(units before buildings) takes the passing Laborer. `LastAttacker` is cleared only when that attacker dies
(`Acquire` and `Resolve`) or on a free, not by a new order (`ClearForOrder` keeps it) or by time or distance.

## Notes
- docs/03 "Implementation (M4-2b)" words the rule as "once an enemy unit has hit it", which matches the code but not
  the brief's "when a unit is hitting it".
- A possible fix is to require the remembered attacker to be targeting this unit (or to have hit it within a window,
  or to be within its sight), so the re-pick only happens for a current attacker. Keep
  `AttackOrderTests.AUnitHittingABuilding_TurnsOnTheEnemyUnitHittingIt_AfterItsSwing` green.
