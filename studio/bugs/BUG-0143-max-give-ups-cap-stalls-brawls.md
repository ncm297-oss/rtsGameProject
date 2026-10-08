# BUG-0143: The `MaxGiveUps` cap stalls every brawl: units that gave up three crowd-blocked chases stand Idle beside reachable enemies

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-07-2315 (re-check of 2026-10-07-2014), task M4-1 fix round 1 |
| System | combat (BUG-0137 give-up memory: `ChaseStall`, `Ignored`, `GiveUps`, `CombatConstants.MaxGiveUps`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Brawl_IsFoughtToAFinish_NoUnitStandsIdleInSightOfAnEnemy"`
   (`QA/CombatGiveUpQaTests`, skipped with this id; remove the `Skip` to run). Four rows: `CombatScenes.FlatBrawl` 40 v 40
   (10 and 5 ranks) and 100 v 100 on open flat ground, and `CombatScenes.MapBrawl(1, 200)`.
2. The row fails if a unit stands Idle with no target for 40 ticks in a row (2 s, ten scans) while an enemy is inside its
   sight, or if one side isn't wiped out within 2,500-3,500 ticks.

## Expected
docs/03 "Target acquisition": "idle, attack-moving, holding, and patrolling units scan for enemies within sight"; a
battle of two attack-moved armies is fought to a finish. The pre-fix build (f2879b9, same scenes, same row) passes
every row: longest Idle-in-sight 1-4 ticks, decided on ticks 979, 1,285, 1,934 and 2,370.

## Actual
HEAD (eebb152), same rows: never decided; one unit stands Idle in sight of an enemy for 2,279 / 2,302 / 2,768 / 3,280
ticks.

Scratch report (state every checkpoint, f2879b9 -> eebb152), 500 v 500 `FlatBrawl` 10 ranks deep (the
`CombatPerfTests` scene), run to 6,000 ticks (5 min):

| tick | f2879b9 alive (P0 v P1) | eebb152 alive (P0 v P1) | eebb152 Idle, untargeted, enemy in sight | of them at `MaxGiveUps` | mean nearest enemy |
| --- | --- | --- | --- | --- | --- |
| 600 | 375 v 381 | 382 v 385 | 289 | 285 | 3.8 m |
| 1,200 | 235 v 209 | 299 v 282 | 510 | 509 | 4.1 m |
| 2,400 | 84 v 0 (last death 2,373) | 249 v 230 | 455 | 455 | 4.7 m |
| 6,000 | 84 v 0 | 213 v 190 | 387 | 387 | 6.6 m |

Same shape on 25-rank columns (f2879b9 83 v 0 by 3,406; eebb152 264 v 239 at 6,000, 500 standing) and the 200 v 200
map brawls on seeds 1 and 2 (f2879b9 decided by 2,371 / 2,008; eebb152 45 v 41 and 58 v 37 at 6,000). Every stalled unit
is at the cap and has `Ignored` set. Both armies end up interleaved on open ground, 2-9 m apart, each side waiting for
the other to step into reach.

## Notes
Cause: in a melee crowd a back-rank chaser's gap doesn't shrink for 10 scans (its own front rank is in the way), so it
gives up (the code meant for unreachable targets). Three of those and `Scan` treats it as holding (`hold = u.Hold[i] ||
u.GiveUps[i] >= MaxGiveUps`): reach-only until an order or a landed hit. An attack-mover at its leg's end goes Idle at
the cap; an Idle unit is reach-only too. A unit only gets out by being hit (the retaliation path ignores the cap), and in
a stalled line nobody hits. Not a livelock (nothing moves), a deadlock.

The BUG-0137 rows still need: a target on another plateau, behind a wall, or one that outruns the chaser must not
hold the unit forever. Options for the developer: make the cap expire (a number of scans, or the target's position
changing by more than X), count progress by path cost (flow field at the unit's cell) rather than straight-line gap so a
crowd-blocked chaser on a reachable target never stalls, or tell "blocked by friends" (`StuckTicks` / speed) from
"no way". Re-measure `CombatPerfTests` (500 v 500 < 4 ms) after the fix: the stalled brawl does less work than a real one
(ticks 205-605 average 3.76 ms now).
