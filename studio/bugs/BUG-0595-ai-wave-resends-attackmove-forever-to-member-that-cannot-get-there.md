# BUG-0595: A wave member that can't get to the target gets `AttackMove` every think, and the wave never ends

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-12-1930, task M5-3 (re-check round 1) |
| System | AI `MilitaryManager` (wave orders); contributing: `OrderSystem` goal snapping (pre-existing) |
| Fixed by | |

## Repro
1. Remove the `Skip` on `QA/AiMilitaryQaTests.AWaveAtATargetItCantReach_IsNotSentAttackMoveEveryThinkForever`, then
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AWaveAtATargetItCantReach"`.
2. Scene: the flat AI scene; player 0 (AI, Normal) has a 1-pop wave threshold and sees player 1's hall through a billet.
   Player 1's hall stands inside a closed ring of its own tents (2 x 2) with a one-to-two-cell gap. Four Heavy Infantry at
   home; 60 thinks. Both rows: combat off and on.

## Expected
BUG-0591's own expectation: a member that the AI keeps re-ordering without effect is given up on (sent home, dropped from the
wave, or the wave re-targeted or ended) after a bounded number of tries.

## Actual
The wave's target is the nearest tent. `target ... at <100, 62>; wave active True, waves 1; over 60 thinks to 4 members:
AttackMove 240; members at 37.0,57.0 Idle d=63.2 ...`: every member gets an `AttackMove` at every think (4 per think, 240 in
60) and none moves a step. The same happens with combat on. The wave has no exit while no member dies and the target stands,
so no new wave starts: the AI's offence is frozen for the rest of the match.

## Notes
- Why nobody moves (pre-existing, outside M5-3): an `AttackMove` / `Move` to a point inside a footprint goes to
  `FlowField.NearestPassable`, the passable cell nearest by distance. Here that cell is in the gap inside the ring, which no unit
  outside can reach, so the order leads nowhere and the unit is Idle again at once. A player's own order to that point does the
  same. That is reasonable movement behaviour; the AI's re-send loop is the bug.
- The round 1 fix bounds only the "Idle near the target" branch (`AttackSent`). The "Idle elsewhere: AttackMove again" branch
  has no count. The dev's report names it ("bounded per think, not overall"). A member stuck while walking (blocked path,
  ring of buildings, a unit spawned in a blocked cell) loops the same way.
- A per-member try count (the `WaveFlag` byte has room), or "Idle at the same place as last think, so give up", would bound it.
