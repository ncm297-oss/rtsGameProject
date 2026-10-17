# BUG-0884: The 200 v 200 generated-map brawl now ends in an out-of-sight stalemate (not decided by tick 3,500)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-17-0554, task M5-P2 |
| System | combat (brawl termination) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Brawl_IsFoughtToAFinish_NoUnitStandsIdleInSightOfAnEnemy"`
2. Row `(perSide: 200, ranks: 0, limit: 3500)` (`CombatScenes.MapBrawl(1, 200)`) is red at 3ec22ee, green with
   `World.FlankRuleOn` defaulted off.

## Expected
QA's BUG-0143 row: the brawl is fought to a finish (one side wiped) within 3,500 ticks, and no unit stands Idle with an
enemy in sight for 40 ticks.

## Actual
`200 a side, ranks 0: decided on tick -1; longest Idle in sight of an enemy 3 ticks`. Per the dev: 16 Wickan Lancers and
one enemy end 14.5 m apart, all Idle, each outside the other's 14 m sight. The "no Idle in sight" half holds.

## Notes
- QA's judgement: a chaotic outcome change, not a broken rule (units out of each other's sight don't fight, by design), so
  S3 and not a blocker. The bound is QA's and QA doesn't loosen it in the same round as the change that moved it: the
  Producer either accepts a re-baseline (e.g. "decided, or every survivor Idle out of sight of every enemy") or asks for a
  look at why the last enemy stops where it does (on its attack-move goal, or a give-up?).
