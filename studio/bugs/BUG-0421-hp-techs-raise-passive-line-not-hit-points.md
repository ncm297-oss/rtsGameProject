# BUG-0421: hp techs never raise unit hit points, so with Dryjhna's Prophecy the Zealot frenzies at 65 % of its real hp

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-1015, task M4-4b-3a |
| System | techs / combat (hp stat), unit passives |
| Fixed by | |

## Repro
1. `Rts.Sim.Tests.QA.PassiveQaTests.ProphecyResearchedMidFight_FrenziesAZealotNowBelowTheNewLine_NextTick`: a Zealot is
   spawned at 63 hp and hit down to 41. Dryjhna's Prophecy is researched. On the next tick the Zealot is Frenzied.
2. `UnitStore.TrySpawn` sets `Hp = def.Hp` (63). Nothing in the sim reads `TechStat.Hp` except `PassiveSystem.MaxHp`
   (`grep -rn "TechStat.Hp" sim/Rts.Sim`).

## Expected
docs/factions/whirlwind.md: Prophecy gives Zealots +20 HP (83 max), and Frenzy of the Apocalypse starts below 50 % of max
hp, which is 41 / 83.

## Actual
The Zealot never has more than 63 hp, but the passive uses 83 as its max. After Prophecy, Frenzy starts at 41 / 63 hp, which
is 65 % of what the unit can actually have. The tech's +20 HP does nothing else.

## Notes
This is a known gap, not something this task introduced: docs/03 "Implementation (M3-5)" says "Combat does not apply it
yet (M4 adds TechBonus to ... hp ...)", and the developer listed it in docs/03 "Known limits (2)". No bug was filed for it,
though, and the new passive now makes it visible in play. The fix belongs where hp techs get applied (at spawn, and to
live units when a tech finishes). After that fix, the passive's line and the real hp agree again.
