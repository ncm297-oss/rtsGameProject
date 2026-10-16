# BUG-0731: Attack-movers (the AI's waves) stalled on the way to a free claimed spot still give the building up

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-15-2325, task M5-P1 |
| System | combat / surround rule (`Combat/SurroundRule`, `CombatSystem.QueuedAtRing`) |
| Fixed by | 620ac47 (M5-P1 fix round 1; verified by QA 2026-10-16) |

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

## Verification (QA, 2026-10-16, round 1)
Fix: `CombatSystem` keeps a stalled chase when `Mode == Ordered || SurroundRule.WalksToSpot(i)`, and `ReachableOnFoot(i)`.
`WalksToSpot` requires `SurroundRule.Applies` (rule on, live building, melee reach), so ranged attackers and unit chases
take the old path. Evidence:
- QA's repro row (`walled: False`): rule on 18 / 20 struck, **0 give-ups** (was 16 / 20, 4); rule off still 9 / 20, 11.
- Dev row `AttackMove_StalledOnTheWayToItsClaimedSpot_KeepsTheBuilding`: HI and Raiders 20 / 20 on the House, no give-up.
- QA `SurroundRound1QaTests.AttackMove_FromEverySide_NoGiveUpWhileTheBuildingStands` (8 rows: 4 sides, House and 3x3
  hall, HI and Raiders, 20-40 attackers): no give-up in 600 ticks, 17-26 striking at once.
- Unreachable spots still end the chase: `AttackMove_ClaimedSpotInASealedPocket_StillGivesUp_NoPacing`: units holding a
  pocket spot stand Idle where their walk ended and give up after 40 ticks (10 scans), 9 give-ups, no pacing.
- Ranged / unit chases unchanged: `RangedAndUnitChases_RuleOnAndOff_HashEqualEveryTick` (crossbowmen attack-moving past a
  House; HI vs Raiders attack-move fight) hash equal rule on vs off on all 800 ticks.
