# BUG-0421: hp techs never raise unit hit points, so with Dryjhna's Prophecy the Zealot frenzies at 65 % of its real hp

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-10-1015, task M4-4b-3a |
| System | techs / combat (hp stat), unit passives |
| Fixed by | 11d741c (M4-4b-3b): spawn at the max incl. hp techs; a landing hp tech adds the max change to every live unit of the player (floor 1). Proof: `Abilities/HpTechTests` (dev, fail-first), `QA/PassiveQaTests.ProphecyResearchedMidFight_RaisesItTo61Of83_NotFrenzied_UntilBelowTheNewLine` (re-stated to the rule), `QA/HpTechQaTests` (overlapping techs, either order, negative floor, mid-burn, summoned units, the 41 / 83 line, twins fuzz). Verified by QA 2026-10-10-1314; the view's hp bar still uses the type hp: BUG-0450 |

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

## Producer triage (2026-10-10-1015 ACCEPT)
S3 confirmed; pre-existing since M3-5 but now visible in play (Dryjhna's Prophecy's +20 HP does nothing to a Zealot's hit
points). **First item of the sim's next task (M4-4b-3b)**, ahead of the summons work. Direction (Producer default, owner may
revisit; record in docs/01 with the fix): a unit spawns with `hp = def.Hp + its owner's hp bonus`; when an hp tech lands, every
live unit of the affected types gains the delta to its current hit points at once (the usual RTS rule), so max and current move
together; `PassiveSystem.MaxHp` stays as it is and the 41 / 83 rows become real. Regression test: a Zealot at 63 / 63 researches
Prophecy and reads 83 / 83; one at 41 / 63 reads 61 / 83 and is not Frenzied. M4 criterion 8 is ticked with this fix.
