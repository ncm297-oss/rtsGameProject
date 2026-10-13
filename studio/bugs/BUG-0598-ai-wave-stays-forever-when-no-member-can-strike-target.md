# BUG-0598: A wave whose members all stand by a target none of them can strike stays on forever, unordered

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-12-1930, task M5-3 (re-check round 2; introduced by the BUG-0596 fix, 1f46ca9) |
| System | AI `MilitaryManager` (wave orders, the `MaxTries` "crowded off the target" branch) |
| Fixed by | |

## Repro
1. Remove the `Skip` on `QA/AiMilitaryQaTests.AWaveWhoseMembersAllStandByATargetNoneCanStrike_DoesNotStayForever`, then
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AWaveWhoseMembersAllStand"`.
2. Scene: the flat AI scene, combat on. Player 0 (AI, Normal) has a 1-pop wave threshold and sees player 1's hall through a
   billet. The hall (cells 52-55, 30-33) is ringed two cells deep by trees, so no passable cell touches its footprint. 4 (or 10)
   Heavy Infantry (melee) at home; 3,000 ticks.

## Expected
The wave is over within a bounded time (ended, or every member left and went home), as it was at round 1 (646e421, where the
same test passes): an AI wave must not be able to stand still for the rest of the match.

## Actual
`4 members; target = hall, hp 2400 / 2400; wave active True, waves 1, still members 4; last order at tick 501; commands:
AttackMove 4, Attack 12; at Idle d=9.2 Idle d=9.5 Idle d=8.9 Idle d=8.9`. With 10 members, 9 stand there (one could not get
near and left), last order at tick 861. Each member walks to the tree line (inside `near` = rallyReach 6 m + half the hall's
8 m side), gets 3 `Attack`s it can't carry out, then hits `if (atTarget) continue;` ("crowded off it, friends fight it") and
gets no more orders. Since every member is in that state, nobody fights the target, no member dies or leaves, the target
stands, so neither the retreat nor the re-target fires, and `TryStart` never runs again: the AI's offence is frozen.

## Notes
- The "crowded" branch assumes friends are fighting the target. Here none is. A guard like "if no member has been ordered or
  made progress for N thinks, end the wave" or "a crowded member stays only while some member is attacking the target" would
  close it; the dev noted the related limitation ("a crowded member waits with no orders unless pushed a cell nearer").
- Same outcome class as BUG-0591 / 0595 (the AI's offence frozen), which were S3. It needs a target with no reachable footprint
  edge but a reachable spot within `near`: a building wedged into a forest or against cliffs; enclosing buildings would be
  attacked instead. With combat off (test scenes only) any target does it.
- Round 1's rule (one `Attack` per target, then home) passed this test; round 2 traded it for BUG-0596's fix.
