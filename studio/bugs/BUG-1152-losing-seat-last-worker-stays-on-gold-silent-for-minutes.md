# BUG-1152: A losing AI seat's last worker stays on gold with no wood, and the seat sends nothing for minutes

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-08-0726, task M5-5 (QA, the extra "silent seat" oracle on the criterion rows) |
| System | AI economy (gather split / rebalance, rebuilding after losses) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MatchCriterionSweepQaStressTests&DisplayName~raraku"`
   (Stress). Rows `raraku` seed 1 and 2 are listed in `MatchCriterionSweepQaStressTests.Known` under this id and print
   `known (BUG-1152): a seat with a worker sent nothing for N ticks`.
2. Or `CriterionMatch.Build("raraku", 1)` (the M5-5 criterion row for Raraku), played with `AiPlayer.SentForTests` on
   seat 1: no command between tick 21,141 and tick 27,120 (5,979 ticks, about 5 minutes).

## Expected
docs/02 "AI opponent": the AI keeps playing while it has anything to play with. A seat that still has a worker and an
income should get to a state where it can spend: move a gatherer to wood when wood is what blocks every build and
train, and rebuild what it lost (the M5-H5 hall rebuild).

## Actual
Raraku seed 1, seat 1 (Whirlwind), from tick 21,141 (probe, every 300 ticks):
- one `whirlwind_camp_follower`, gathering gold the whole time; gold rises 27 -> 117, **wood stays at 7**;
- its Town Hall equivalent is gone; production buildings stand but every unit and the rebuild cost wood;
- the AI thinks every 20 ticks (1,065 -> 1,350 thinks) and sends **no command** until a Move at 27,120; the seat is
  defeated at 28,083 (the row still ends with a winner, inside its bound).
Raraku seed 2, seat 1 is silent from 28,441 to the end at 38,554 (10,113 ticks, gold 150, wood 35 at the end), of which
28,441-33,573 (5,132 ticks) with a live worker.
On generated seeds 3 and 5 the losing seat is silent 3,568-4,379 ticks with 764-1,246 gold and 0-35 wood (not probed
for workers; below the 4,800-tick limit once the oracle counts only time with a live worker).

## Notes
- Not on the literal criterion row (Vathar Crossing seed 1); the Raraku criterion row (seed 1) shows it. Every affected
  match still ends: the winner razes the rest. It weakens the "keeps playing" read of criterion 5 and would matter in a
  match the AI could still turn around.
- Likely cause: the gather split works in shares of the workers it has; with one worker the share rounds to gold and
  the rebalance never moves it, so wood never reaches the cost of anything. Related: BUG-1067 (no wood for minutes after
  the scouts die; another path to the same wood starvation), BUG-1022 (the shift's churn).
- Candidate fix (AI behaviour, own task): when every affordable-by-gold option is blocked on wood, put a gatherer on wood
  whatever the share says.
