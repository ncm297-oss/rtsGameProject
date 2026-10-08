# BUG-0152: Re-issuing the same Attack (or AttackMove) cancels the wind-up every time: a unit ordered every 5 ticks or faster never deals damage

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-08-0313, task M4-2a |
| System | sim: orders / combat (`Orders/OrderSystem.Apply` and `Execute` -> `CombatSystem.ClearForOrder`), sim track |
| Fixed by | `fea7963` (`CombatSystem.ReaffirmAttack`, `KeepFightForAttackMove`, `OrderSystem.OnLegTo`); rows `AttackOrderQaTests.ReissuingTheSameAttack_EveryNTicks_StillLandsHits` (un-skipped, 5 cases), `AttackOrderTests.Reissued*`, `QA/FightReissueQaTests` |

## Repro
1. Un-skip `sim/Rts.Sim.Tests/QA/AttackOrderQaTests.cs` `ReissuingTheSameAttack_EveryNTicks_StillLandsHits` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReissuingTheSameAttack"`.
2. Scene: a Malazan Heavy Infantry in reach of a held enemy Laborer (100,000 hp), `Command.Attack(0, a, t, false)` once,
   then the same command again every N ticks, 600 ticks.

## Expected
Re-issuing the order a unit already has does nothing (the rule Move spam got in BUG-0029: "click spam, an AI refreshing
its orders: it doesn't restart"). docs/03 already says re-ordering never swings sooner; it must not stop the swing
either. Players spam-click attack targets, and the AI uses the same command API.

## Actual
```
damage over 600 ticks (Attack): one order 190, re-ordered every 1 ticks 0
damage over 600 ticks (Attack): one order 190, re-ordered every 3 ticks 0
damage over 600 ticks (Attack): one order 190, re-ordered every 5 ticks 0
damage over 600 ticks (Attack): one order 190, re-ordered every 10 ticks 190
damage over 600 ticks (AttackMove): one order 190, re-ordered every 3 ticks 0
```
Every unqueued Attack runs `StartAttack` -> `ClearForOrder` -> `Disengage`, which zeroes `WindupTicks` (the swing in
progress is thrown away) while the cooldown set at the swing's start keeps counting. The Heavy Infantry's wind-up is
6 ticks (0.3 s), so any re-order at least every 5 ticks cancels every swing: zero damage, forever. The same holds for
AttackMove to the same point (M4-1's path), so it isn't new in M4-2a, but M4-2a's Attack is the order a player will spam
(right-click / F on a target, the view's next task).

## Notes
- Suggested shape (the developer's call): an unqueued Attack on the target the unit already holds under `Ordered`
  (same handle, same building flag) changes nothing, as a same-cell Move does; likewise an AttackMove whose point resolves
  to the unit's current leg.
- With a 10-tick spam the damage is unchanged (the swing lands within 6 ticks of starting), so the threshold is the
  unit's wind-up: the longer the wind-up, the slower a spam that still zeroes its damage.

## Re-check (2026-10-08-0313, round 1, QA)
- `ReissuingTheSameAttack_EveryNTicks_StillLandsHits` (every 1 / 3 / 5 / 10 ticks, Attack; every 3, AttackMove) passes.
- New `QA/FightReissueQaTests` (9 rows pass on `fea7963`; 4 of them fail on the pre-fix `9be1991`): spammed same-leg
  attack-move past a cliff target still gives the chase up and reaches the leg end in 537 ticks (pre-fix: never within
  1,500 for spam every 3 or 10 ticks); a spammed Attack on a cliff target is still given up (tick 136; pre-fix never); a
  Move mid-swing still pulls the unit out with no hit landing after it (also after a kept attack-move fight); a re-issued
  Attack still replaces the queue; a reaffirmed hold target is chased as ordered; two attack-moves elsewhere during a
  fight walk the last leg after the kill.
- **But the "attack-move anywhere keeps an in-reach fight" half of the fix is a regression: BUG-0154 (S2).** An
  attack-move can no longer redirect a fighting unit, e.g. off a building onto the enemy killing it.
