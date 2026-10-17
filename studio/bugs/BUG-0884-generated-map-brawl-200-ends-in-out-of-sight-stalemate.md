# BUG-0884: The 200 v 200 generated-map brawl now ends in an out-of-sight stalemate (not decided by tick 3,500)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (incidentally by cbeb944, verified by QA 2026-10-17-0554) |
| Found | 2026-10-17-0554, task M5-P2 |
| System | combat (brawl termination) |
| Fixed by | cbeb944 (no targeted change; fix round 1 moved the fight); QA's row `CombatGiveUpQaTests.Brawl_IsFoughtToAFinish_*(200, 0, 3500)` green |

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

## QA re-check (2026-10-17-0554, after fix round 1, cbeb944)
- The row is green in the full run: decided on tick 2,417, longest Idle in sight of an enemy 4 ticks. Brawls are
  chaotic (docs/03 notes the 4-point row moved by -42 to +31 between the round's variants), so a later timing change can
  bring the stalemate back. If it does, reopen this bug rather than filing a new one.
