# BUG-0154: Since the BUG-0152 fix, an attack-move anywhere by a unit fighting in reach keeps that target: the player can't redirect a unit off a building onto the enemy killing it

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-08-0313, task M4-2a re-check round 1 (introduced by the round-1 fix `fea7963`) |
| System | sim: orders / combat (`Orders/OrderSystem.Apply` AttackMove branch, `CombatSystem.FightsInReach` / `KeepFightForAttackMove`), sim track |
| Fixed by | |

## Repro
1. Un-skip `sim/Rts.Sim.Tests/QA/FightReissueQaTests.cs` `AttackMoveOntoAnAttacker_WhileHittingABuilding_TakesTheAttacker`
   and run `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AttackMoveOntoAnAttacker"`.
2. Scene (48 x 48 flat): a Malazan Heavy Infantry attack-moves into an enemy Tent and starts hitting it (1 damage a hit).
   An enemy Raider is then ordered to attack the Heavy Infantry. After the Raider's first hit the player attack-moves
   the Heavy Infantry onto the Raider's position.

## Expected
docs/03 "Target acquisition": priority is enemies attacking me > units that can attack > other units > buildings, and
an attack-move is the player's "fight what you meet" order. Before the round-1 fix an unqueued AttackMove cleared the
engagement (`ClearForOrder`), the next scan re-picked by priority, and the player could pull units off a building onto
an army with an A-click. The BUG-0152 fix was meant to stop spam from cancelling swings, not to take that control away.

## Actual
| Build | Took the Raider | Heavy Infantry |
| --- | --- | --- |
| pre-fix (`9be1991`) | 5 ticks after the attack-move | alive, 120 / 130 hp |
| fix (`fea7963`) | never: still on the Tent | dead 360 ticks later; Raider untouched (108 hp) |

Without an attacker (enemy Raider held 10.7 m away in sight, the player attack-moves onto it): pre-fix it takes the
Raider in 4-5 ticks; on the fix it keeps hitting the Tent for 6,000+ ticks (Tent 499 -> 299 hp, so about 15,000 ticks
to fall), whether the Tent was first taken by an Attack order or by attack-move.

Cause: `OrderSystem.Apply` keeps the fight for an unqueued AttackMove to *any* point when `CombatSystem.FightsInReach`
(mid-swing or in reach), and the scan never re-picks while in reach ("a swing is never thrown away for a better
target"). So an attack-move can no longer change a fighting unit's target at all. Only a Move or an explicit Attack on
the Raider still works.

## Notes
- The developer flagged this design choice for QA ("an AttackMove ANYWHERE by a unit fighting in reach ... keeps that
  target"). It's what the scan would do with no order, but the order is the player asking for a re-pick.
- The spam case (BUG-0152) needs only the same-leg rule, or keeping just the swing in progress (let the wind-up land,
  then re-scan by priority). The "anywhere" half is what breaks redirecting. `AttackOrderTests.AttackMove_ElsewhereWhileFightingInReach_KeepsTheTarget_ThenWalksTheLeg`
  pins the current behaviour and would need to change with the rule.
- Related pre-existing gap (M4-1, not caused by this fix): BUG-0156. A unit hitting a building never turns on the
  enemy hitting it without an order, so the attack-move redirect was the player's only quick rescue.
