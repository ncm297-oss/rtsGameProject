# BUG-0731: Attack-movers (the AI's waves) stalled on the way to a free claimed spot still give the building up

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-15-2325, task M5-P1 |
| System | combat / surround rule (`Combat/SurroundRule`, `CombatSystem.QueuedAtRing`) |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SurroundQaTests.AttackMove_SurroundsTheHouse_OneOpenSideNoGiveUp" --logger "console;verbosity=detailed"`
(row `walled: False`): 20 Heavy Infantry in a block south of a lone Whirlwind Tent (2x2, 20 spots) attack-move to a
point past it; hp topped up; 400 ticks.

## Expected
Brief goal: every unit working, and "every path that chases a building (ordered Attack, attack-move, retaliation, the AI's
waves) goes through the same rule". 20 attackers, 20 spots: nobody should give the House up while a spot is free for them.

## Actual
```
rule off: 9 / 20 struck in 400 ticks; give-ups 11
rule on:  16 / 20 struck in 400 ticks; give-ups 4
  rule 1 tick 177: unit 20 gave up (state Moving, mode AttackMove, gap 1.73, held spot 6, ...)
  rule 1 tick 182: unit 19 gave up (state Moving, mode AttackMove, gap 1.85, held spot 5, ...)
  rule 1 tick 187: unit 18 gave up (state Moving, mode AttackMove, gap 1.79, held spot 4, ...)
  rule 1 tick 216: unit 9 gave up (state Moving, mode AttackMove, gap 0.80, held spot 5, ...)
```
Each gave up while holding a free spot on the north face, jammed behind the strikers round the NE corner. The rule is a
large improvement (11 to 4) but a fifth of an attack-moving army still walks off.

## Notes
- Cause: `QueuedAtRing` keeps a stalled chase only when the ring is full or a new spot was just claimed; one stalled on
  the way to a spot it already holds falls to the old rules, and for a non-Ordered chase that is the BUG-0137 give-up
  (`ReachableOnFoot` is only consulted for `CombatMode.Ordered`). docs/03 states this choice ("a spot past a cliff is no
  reason to pace"), but for a spot on open ground a field-reachability check (as M4-H3 does for Ordered) would keep them.
- Ordered attackers are not affected (the dev's (d) and QA's rows hold the order).
- Related: the owner's playtest note 5; BUG-0028 / 0032 / 0046 (crowd routing, slice 2).
