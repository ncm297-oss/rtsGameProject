# BUG-0137: Chasing a target the unit cannot reach (or only by a detour) never ends: attack-moves stop for good, Idle units dither forever

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (but the fix's `MaxGiveUps` cap causes BUG-0143) |
| Found | 2026-10-07-2014, task M4-1 |
| System | combat acquisition / chase / settle (`CombatSystem.Acquire`, `Chase`, `Settle`) |
| Fixed by | eebb152; regressions `QA/CombatQaTests` (3 rows un-skipped), `CombatTests` give-up rows, `QA/CombatGiveUpQaTests` |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~QA.CombatQaTests"`, three rows:

1. `AttackMovePastAnUnreachableEnemyOnACliff_StillArrives`: a Heavy Infantry attack-moves along the foot of a cliff;
   a Crossbowman stands on the cliff-top 10 m away (inside sight, no ramp). The unit takes it as a target, walks to
   the cliff and stays there: after 1,200 ticks `Idle`, mode `AttackMove`, target the crossbowman, 36.2 m short of
   its destination. It never arrives.
2. `UnreachableEnemyInSight_ChaserDoesNotWalkAndGiveUpForever`: two Idle Heavy Infantry, one on a cliff-top, 12 m
   apart. Both retaliate forever: 2,000 fresh walks in the last 1,000 ticks (a new `Walk` every tick each: Idle with
   the target out of reach -> `Chase` in phase 7 -> movement finds no way -> Idle -> again).
3. `EnemyInSightBehindAWall_IdleUnitSettles_NoChaseAndReturnCycleForever`: an Idle unit sees an enemy holder 12 m
   away behind a wall whose way round is 30+ m. It chases (the path leads away), loses sight, walks home, sees it
   again: Moving on 2,000 of ticks 2,000-4,000, oscillating at y 53.2-53.8 m, about 7 m from its anchor, for good.

Also in the pre-M4 suites (see BUG-0135): 500 units of two owners to 500 random goals, seed 1, traced at tick 8,000:
all 8 units still Moving are Horse Raiders (sight 18) cycling every 8 ticks: target at 17.1 m picked, chase goal
leads away (18.1 m), out of sight, walk home, in sight again (`FiveHundredUnitsTo500RandomGoals_Seeds1To4`: 6-14
units still Moving after 30,000 ticks per seed).

## Expected
Brief criterion 4: an attack-mover with nothing it can fight resumes its leg and arrives; docs/03 leash rule: a
retaliator that cannot catch its target goes home and is Idle. Units must settle (the termination rows that went red
are long-standing liveness guarantees). A unit with a target also never pops its queue, so a stuck engagement also
freezes its shift-queue.

## Actual
See above: a target that cannot be reached within the leash (other plateau, behind a wall, path longer than the
sight radius) holds the unit forever; it never drops it because it stays in sight, and re-acquires it as soon as it
is back in sight.

## Notes
Suspected cause: target choice and chase ignore reachability. `PickTarget` picks by straight-line distance only,
`Chase` re-walks every phase 7 while Idle and out of reach (no give-up memory), and the leash only measures the
distance from the anchor, so a target on another plateau or behind a wall within sight is chased forever. Options for
the developer: skip targets on another nav region / level the unit cannot path to (the flow field's cost at the
unit's cell is infinite), give up on a target after a failed chase (remember it for some ticks), or count a walk away
from the target as leash progress. The scratch "livelock breaker" QA used for classification (drop the target after
12 chase re-walks without planting and stop scanning for a while) turned 8 of the 9 red termination rows green.

## Re-check 2026-10-07-2315 (QA, fix round 1)
All 3 QA rows green; the 9 termination rows green on combat. New attacks green (`QA/CombatGiveUpQaTests`): 2 and 3 unreachable cliff-top enemies taking turns end after 3 engagements, Idle within 1 m of home by tick 364 / 245 and still for 3,000 ticks; at the cap a reachable Raider is fought and killed and the reset leads to at most 3 more engagements, settled 293 ticks after the kill; an attack-move past 5 cliff-top enemies arrives in 581 ticks; a retaliator behind a wall is Idle at its anchor after 76 ticks (bound 300) and stays. But the cap stalls real brawls: BUG-0143 (S2).
