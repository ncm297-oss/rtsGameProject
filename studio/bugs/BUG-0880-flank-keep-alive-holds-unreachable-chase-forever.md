# BUG-0880: The slice-2 keep-alive holds a chase forever when no standing point is in reach (sealed cliff, or a front past the block bound)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (cbeb944, verified by QA 2026-10-17-0554) |
| Found | 2026-10-17-0554, task M5-P2 |
| System | combat (chase give-up), `CombatSystem.FriendFightsTarget` / `QueuedBehindOwnLine` |
| Fixed by | cbeb944 (`Flanking` + `StandsInReachAtGoal` in `FriendFightsTarget`'s slice-2 tail, and `StandsInReachAtGoal` on M4-H3's ordered keep / BUG-0731's spot keep); regression: `QA/FlankQaTests.ChaseOfATargetUpASealedCliff_*` (6 rows) and `ChaseBehindAnOwnRowPastTheBlockBound_*` (3 rows), un-skipped |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FlankQaTests.ChaseOfATargetUpASealedCliff|FullyQualifiedName~FlankQaTests.ChaseBehindAnOwnRowPastTheBlockBound"`
2. Scene A (`ChaseOfATargetUpASealedCliff_*`): 40 x 40 map, a sealed level-1 plateau (no ramp) at x 20-35. A holding enemy
   Raider stands on it at cell (22, 20). 10 or 20 own Crossbowmen in a row 1 m apart at the cliff foot (x 38.6 m) are
   `Attacking` it. 5 Heavy Infantry 3 m behind the row get an attack-move to it, or stand Idle and retaliate (their scan
   takes it). 4,000 ticks, hp topped up.
3. Scene B (`ChaseBehindAnOwnRowPastTheBlockBound_*`): flat ground, 33 or 40 own Crossbowmen 1 m apart (past
   `MaxPlugCluster`, so the flank rule doesn't walk round them), each `Attacking` its own holding Raider 10 m in front.
   One Heavy Infantry 3 m behind the middle: attack-move, or Idle and retaliating. 3,000 ticks.

## Expected
docs/03 "Chasing" and the slice-2 bullet: "A chaser stalled for any other reason (another plateau, behind a wall, outrun)
has no such friend beside it, so those chases still end." The brief: the keep-alive needs "a friend fights it from any
range **and** a standing point in reach exists on foot". With the rule off, both scenes give up (BUG-0137 / BUG-0143).

## Actual
Scene A, last 500 of 4,000 ticks:
- AttackMove, row 10: rule off: gave up at ticks 62, 65, 56, 119, 62, then Idle. Rule on: **no give-up; every chaser holds
  the target 500 / 500 ticks and is Moving 495 / 500** (pacing the cliff foot behind its own row, forever).
- AttackMove, row 20, and Retaliate, row 10: the same (off: give-ups at 56-107 ticks; on: held 500 / 500, Moving 494-497).
- (Ordered Attack holds the target forever with the rule off too: M4-H3's ordered keep, pre-existing, see Notes.)

Scene B, last 500 of 3,000 ticks: rule off: 3 give-ups, then Idle, not Moving. Rule on: **0 give-ups, held a target 500 /
500 ticks, Moving 475, and its y position reversed direction 499 times in 500 ticks** (a per-tick jitter behind the front).

## Notes
- Cause: `FriendFightsTarget`'s new tail `FlankRuleOn && QueuedBehindOwnLine(i) && ReachableOnFoot(i)`. `QueuedBehindOwnLine`
  holds whenever the chaser stands within 2 m of an own unit `Attacking` its target, and `ReachableOnFoot` only asks whether
  the chaser's *walk goal* has a finite field cost from where it stands (or, stopped short, the cell the chase walks to
  next). Neither proves that a standing point **in the chaser's reach of the target** exists on foot:
  - A: the chase goal is the cliff foot (the nearest ground), which the walk reaches, but nothing there is within 0.5 m of a
    target up the cliff. Every stall is reset to 0 (`ChaseStall = 0`), so the chase never ends.
  - B: the goal (beside the target) is reachable round the front's end, but the flank rule won't walk a block past 32, so
    the chaser presses into the front forever.
- Suggested direction (developer's call): only keep alive when the chaser actually flanks (a flank step in the scan window,
  as the brief's item 2 put it) or when the target's cell itself is reachable on foot and in reach of the goal, and never
  for a block the flank rule leaves alone.
- Related: the ordered-Attack row of scene A holds forever with the rule off as well (`ReachableOnFoot`'s stopped-short case
  judges the cliff foot reachable): the same hole in M4-H3's keep (BUG-0401 / BUG-0512 "nobody paces a cliff"), not caused
  by this change; the fix here should cover it or a separate bug should be opened when this is fixed.

## QA re-check (2026-10-17-0554, after fix round 1, cbeb944)
- Verified in a full run on a fresh clone of the branch. All 9 previously skipped rows pass. Sealed cliff, rule on, every
  chaser gives up: Ordered 10 / 1 at 62, Ordered 10 / 5 at 55-74, Ordered 20 / 5 at 57-60, AttackMove 10 / 5 at 55-74,
  AttackMove 20 / 5 at 57-60, Retaliate 10 / 5 at 56-69. Nothing is held over the last 500 of 4,000 ticks. The Ordered rows now give up with the rule off too
  (the M4-H3 hole noted below is closed by the same test). Behind a front of 33 / 40 the chaser gives up 3 times and is
  Idle, holding nothing, over the last 500 ticks, the same as with the rule off.
- No give-up on reachable targets (QA's `QA/FlankFixRound1QaTests`, A/B against 3ec22ee and 321d7d6). A unit up a ramped
  plateau, shot at from the cliff foot by an own row of 10 / 20: 5 of 5 ordered chasers walk the ramp and reach it
  (ticks 508-551), with no give-up. A hall ringed by 30 to 42 friends (ordered, and attack-move with queued
  Crossbowmen), and a hall walled by trees on three sides: identical to 3ec22ee, 0 give-ups.
