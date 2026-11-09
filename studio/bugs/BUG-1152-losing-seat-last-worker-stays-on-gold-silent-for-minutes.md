# BUG-1152: A losing AI seat's last worker stays on gold with no wood, and the seat sends nothing for minutes

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (the gather pattern is not fixed; on the held M5-9a branch Raraku seed 5 ends at 52,360 thanks to the starved wave and its `MapSlice2` row is un-skipped there, but it stays in the sweep's `Known` under this id: a seat silent 6,439 ticks) |
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
- **M5-9a (2026-11-08-1316, the developer's re-measure):** Normal's counter shift changes the Raraku matches. Seed 2 no
  longer shows it (ends at 29,190, every sweep oracle clean): its `Known` entry is removed. Seed 1 still does (seat 1
  silent 21,141-27,120, unchanged). **Raraku seed 5 newly shows it, worse:** no result at 72,000, seat 1 silent from 35,081
  to the end (36,919 ticks) with 1,970 gold and 5 wood, seat 0 still from 26,534 with 4,454 gold and 35 wood (both sides
  wood-starved). Left failing on the M5-9a branch in QA's sweep (not added to `Known`) and in
  `MapSlice2StressTests.NormalVersusNormal_OnANewMap_OtherSeeds_EndWithin72000(raraku, 5)`, the Producer's call with BUG-1153.

## Developer notes, M5-9a round 2 (2026-11-08-1556; applied by the Producer at ACCEPT)
- **M5-9a round 2 (2026-11-08-1556, the developer's re-measure with the starved wave, `ai.starvedWaveAfter` 60 s):** Raraku
  seed 5 still has **no result at 72,000** (`MapSlice2StressTests.NormalVersusNormal_OnANewMap_OtherSeeds_EndWithin72000(raraku, 5)`
  and QA's sweep row; the lever does not reach it). Probed every 6,000 ticks: from 30,000 to the end seat 0 (Malazan) holds
  gold 4,454 / wood 35, **5 army, no worker**, 17 buildings, never stamped: 35 wood pays for a Heavy Infantry (54 gold,
  20 wood), so the starved rule's (i) fails, though it trains nothing for 42,000 ticks; seat 1 (Whirlwind) holds gold 1,970
  / wood 5, **no army**, 13 workers (one stamp at 27,460, cleared: (iii) needs a free army unit). Both sides wood-starved
  in effect, neither attacks. The lever's rule (i) ("can pay for no unit") misses a seat that can pay for one entry but
  does not train it (here likely the hall-rebuild reserve holding the wood, or no producer for the pick): the same gap as
  BUG-1068's seed 2. Raraku seed 1 (in `Known` under this id): see the report for the sweep's verdict.
- **M5-9a round 2, fix round 1 (2026-11-08-1556, the developer):** Raraku seed 5 now **ends at 52,360 (43:38), seat 0**
  (BUG-1154's fix: seat 0's pick is a unit its 35 wood can never pay, so it is starved and waves).
  `MapSlice2StressTests.NormalVersusNormal_RarakuSeed5_WoodStarved_EndsWithin72000` is un-skipped and green. QA's sweep row
  `raraku` 5 stays in `Known` under this id: it still fails the silent-seat check (p1 silent 27,461..33,900, 6,439 ticks;
  p0 46,861..52,360). Raraku seed 1 unchanged (in `Known`). The gather pattern itself is not fixed.
