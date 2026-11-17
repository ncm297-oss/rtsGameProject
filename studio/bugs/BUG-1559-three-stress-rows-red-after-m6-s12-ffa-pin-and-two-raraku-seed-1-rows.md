# BUG-1559: Three Stress rows red after M6-S12 beyond the expected `Bug0982_*` pair: the pinned four-seat Pale Hills FFA (seed 1) no longer ends, and two M6-SH2 rows staged on Raraku seed 1 lost their premise

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-11-16-2036, task M6-12 (M6-S12), QA full (the Stress category, once, after the last behaviour change) |
| System | AI match pins (rule 45) and test staging on matches the M6-S12 AI rules moved |
| Fixed by | |

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
