# BUG-1559: Three Stress rows red after M6-S12 beyond the expected `Bug0982_*` pair: the pinned four-seat Pale Hills FFA (seed 1) no longer ends, and two M6-SH2 rows staged on Raraku seed 1 lost their premise

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (verified by QA re-check round 1, 2026-11-16-2036) |
| Found | 2026-11-16-2036, task M6-12 (M6-S12), QA full (the Stress category, once, after the last behaviour change) |
| System | AI match pins (rule 45) and test staging on matches the M6-S12 AI rules moved |
| Fixed by | 4a4f8be (M6-12 fix round 1: tests and docs only; a rule-45 move and two re-stages) |

## Repro
The Stress category on a fresh clone of efa7b31 (`dotnet test sim/Rts.Sim.Tests --filter Category=Stress`, 4 h 17 m):
837 passed / 5 failed / 9 skipped of 851. Red: the two expected `Bug0982_*` rows and these three.
1. `HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt(seed: 1, teams: False)`, 55 m 56 s: `seed 1: no result
   by 144,000 ticks`. Output: `FFA pale_hills seed 1: over False at 120:00 (144000); 4425 hunting thinks oracle-checked, 8527
   sightings; p0 defeated 89:55 waves 7; p1 waves 10 hunts 17 units 19 prod 3; p2 waves 9 hunts 30 units 22 prod 3; p3
   defeated 46:15 waves 8` (every oracle clean: leaks 0, misses 0). The 2v2 row (seed 1, teams) passes.
2. `M6Sh2DeterminismStressTests.RarakuSeed1_TheRetrain_TwinsSaveAndReplay`, 3 m 50 s: `no seat retrained a worker: the row
   proves nothing`; output `first retrain sent for tick -1; save at none; ran to 24354, over True`.
3. `M6Sh2QaStressTests.RarakuSeed1_ASaveEvery500Ticks_FollowsTheRun`, 29 m 22 s, at `Assert.True(saves >= 60)`; output
   `Raraku seed 1: 48 saves, each followed 300 ticks, run to 24354 (match over True)`.

## Expected
- Criterion 8 / rule 45: every pin in the handoff's list re-measured once; "a pinned criterion row that stops ending: move that
  map's row to the next seed that ends by the criterion's own rule, add the seed to `Known` with a new bug id, and report it as a
  finding at the top of your report". The handoff's list names `HuntStressTests` ("`MidHuntSeed` 2, the four-seat FFA row seed
  1").
- QA's expected result for the Stress run: "`Bug0982_*` x2 only"; a row whose match moved is re-staged with its reason, not
  left red (rules 57 / 70).

## Actual
- (1) The FFA row moved to seed 1 in M6-SH2 (over at 91,995, p2). Under M6-S12 two seats with armies hunt each other for
  47 hunts and neither falls by 144,000. The developer's report lists the re-pinned classes and does not name `HuntStressTests`;
  the row was not re-measured and is not reported as a rule-45 finding.
- (2) and (3) were staged on Raraku seed 1 while it stalled (BUG-1460): one needs a seat that loses every worker and retrains,
  the other 60 saves. The match now ends at 24,354 (seat 0) with no retrain. Neither is a determinism failure: twins, the 48
  saves and every follow were hash-equal to the end. Neither class was in the handoff's list, so the developer did not run them
  (the brief forbade the whole Stress category): the same rule-68 gap as BUG-1552, on the sim's own Stress side.

## Notes
- Test-only cures: (1) rule 45, the next ending seed for the FFA row (seeds 2 and 5 ended on the M6-SH2 tree), and the stall
  named; (2) re-stage on a seat that retrains under M6-S12 (`Ai/RetrainTests`' staging, or the first of seeds 1-8 whose seat
  loses its last worker), keeping the twin, save and replay checks; (3) the save sweep's bound read from the match length, or a
  seed that runs longer.
- No determinism, oracle or crash failure is in any of the three.
- (1) is back in the shape of its own skipped twin `FourAiSeats_OnPaleHills_Seed1_KnownStall` (BUG-1068 / BUG-1116: four
  seats spend the map's gold, the two left cannot finish each other) and of BUG-1536 (the owner's open question on a map
  mined out); the cure is the rule-45 move, not a gameplay change.

## Fix verification (QA re-check round 1, on 4a4f8be)
- Fix: tests and docs only (`git diff --stat efa7b31 4a4f8be -- sim/Rts.Sim game tools` is empty). `HuntStressTests`'
  free-for-all oracle row moved from seed 1 to seed 2 (rule 45); `M6Sh2DeterminismStressTests` re-staged on Raraku seed 3
  and `M6Sh2QaStressTests`' save sweep on Raraku seed 2. Each row's body, oracle and bound is byte-for-byte unchanged
  (only the seed constant and the method name moved); the skipped seed-1 row's reason names this bug.
- QA's own probe (temporary, deleted) confirmed every trace the developer gave:
  - FFA seed 1: gathered gold frozen at 8,870 / 8,850 / 8,820 / 8,890 from tick 30,000; p3 out at 55,508, p0 at 107,906;
    p1 0 soldiers from 84,000, p2 1; no result by 144,000.
  - FFA seed 2 over at 59,951 (p1, 4 hunts); seed 5 over at 73,778.
  - Raraku seed 1: p1 has no worker at 21,920 with 3 gold, no retrain, over at 24,354. Seed 2: nobody loses every worker,
    over at 39,262. Seed 3: p0 has no worker at 24,260 with 1,087 gold, Train sent for 24,261.
- Rows run on 4a4f8be: `HuntStressTests`, `M6Sh2DeterminismStressTests`, `M6Sh2QaStressTests`,
  `StarvedSaveTwoProcessQaStressTests`, `ReservePairingsStressTests`, `AiDeterminismTests.PaleHills_ToTheEndWithAHunt_*`:
  75 passed / 0 failed / 1 skipped (the seed-1 known stall) of 76 in 12.5 min. FFA seed 2: 560 hunting thinks
  oracle-checked, leaks 0. Retrain twin: Train at 24,261, save at that tick, ran to 26,261. Save sweep: 78 saves.
- Deviation accepted: the stall is not added to `MatchCriterionSweepQaStressTests.Known`. That dictionary is keyed by
  two-seat matches, and two-seat Pale Hills seed 1 passes, so an entry there would turn the row red ("no longer fails").

## Fix round 1 (the developer's note, applied by the Producer)
1. FFA seed 1: every seat has its start's gold gathered by 25:00 (8,870 / 8,850 / 8,820 / 8,890, frozen after); p3 out
   at 55,508, p0 at 107,906; from 70:00 p1 has 0 soldiers and p2 one, neither can train. Gold exhaustion (BUG-1068 /
   1116 / 1536's shape), not a fault of the M6-S12 rules. Rule 45: the oracle row moved to seed 2 (over at 59,951, p1,
   4 hunts). The skipped `FourAiSeats_OnPaleHills_Seed1_KnownStall` names BUG-1559 in its Skip reason: **this id stays
   the label of that stall** beside BUG-1068 / 1116 (the sim's id range was used up). Seed 2 is also
   `FourAiSeats_OnPaleHills_Seed2_KnownStall`'s match: the twin row is dropped at the next pin move (Producer, Q9).
2. `M6Sh2DeterminismStressTests` re-staged to Raraku seed 3 (seat 0 retrains for 24,261); green.
3. `M6Sh2QaStressTests`' save sweep re-staged to Raraku seed 2 (78 saves, bound 60 unchanged); green.

## M6-S13 (2026-11-25-1437)
The four-seat Pale Hills seed-1 row ends in a draw at 61,779 since the owner's draw rule (see BUG-1068's table); its
Stress row is un-skipped. `EndGameHardeningTests.ALoneIdleWorker_*` stays skipped (BUG-1116): that scene is never spent
(p0 keeps its hall and 400 gold), so the rule cannot end it; the row is about the sweep's order.
