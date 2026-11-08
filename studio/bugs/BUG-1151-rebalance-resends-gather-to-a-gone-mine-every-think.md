# BUG-1151: The economy's rebalance re-sends a Gather to a remembered mine that is gone, every think

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-08-0726, task M5-5 (game-dev, the criterion rows' refusal-run check) |
| System | AI economy (`EconomyManager.Rebalance`, BUG-0570's dropped-Gather mark) |
| Fixed by | |

## Repro
1. Generated map, seed 4, Normal v Normal (`CriterionMatch.Build("generated", 4)`), played with the checks on
   (`CriterionMatch.Play(sim, 24_000, checks: true)`), or `Rts.Cli match --map generated --seed 4` and read the
   `refused Gather:NoResourceNode 39` line.
2. From tick 18,221 seat 1 sends one `Gather` a think to (34, 134), the anchor of a gold mine it remembers (out of sight,
   `Remaining` -1) that was emptied (its slot freed, generation 2). Each is dropped at apply (`NoResourceNode`); the run check
   (`CriterionMatch.MaxStreak`, 8) fires at tick 18,421 (the 9th think in a row on that target). Before it, at 18,181, 15
   idle workers were sent to another gone mine (52, 108) in one think (BUG-0570's designed one-think cost).

## Expected
BUG-0570's rule: a Gather to a remembered node that is gone is dropped and the node marked, so no worker is sent there again.

## Actual
The worker the rebalance picks is at work (`Gathering`); the dropped Gather leaves it gathering its old node, so it is never
Idle and `NoteDroppedGather` (which judges idle workers only) never marks the mine. The next think's rebalance picks a
gatherer again and sends it to the same nearest remembered mine: one wasted command a think until the seat sees the ground
(here 24 sends between ticks 18,221 and 19,581, every one dropped). The split stays off `goldShare` meanwhile.

## Notes
- Not on any criterion row (generated seed 1 is the row); the row check would catch it on seed 4.
- Likely fix (an AI change, so out of M5-5): mark the node when a Gather the AI sent to a worker that was not idle applies
  and the worker's gather node is unchanged, or let `Rebalance` skip a node it sent a gatherer to last think when that
  worker still works its old node.
