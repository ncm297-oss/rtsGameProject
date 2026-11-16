# BUG-1537: `MatchCriterionSweepQaStressTests` seed sweep red on Pale Hills seed 2 and Vathar Crossing seed 2, neither named in the M6-SH2 report

| Field | Value |
| --- | --- |
| Severity | S2 (Stress red outside the rows M6-SH2's criterion 11 allows; criterion 3 lets only Raraku rows change in `Known`) |
| Status | open |
| Found | 2026-11-15-2136, task M6-SH2 (QA) |
| System | AI (gather re-send, BUG-1151 shape; a silent seat, BUG-1152 family), moved by the drop-off rule (BUG-1520) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MatchCriterionSweepQaStressTests.NormalVersusNormal_SeedSweep_ChecksAndExtraOracles"`
2. Rows `(pale_hills, 2)` and `(vathar_crossing, 2)` fail.

## Actual
| Row | base `fdc8710` | base + item 0 only | M6-SH2 head |
| --- | --- | --- | --- |
| pale_hills 2 | green: ends 25,964, p1; silent max 545 | red: `tick 26461: player 1 Gather ... refused 9 thinks in a row` (Gather:NoResourceNode 25) | red, same message |
| vathar_crossing 2 | green: ends 30,569, p0; silent max 439 / 939 | red: `a seat with a worker sent nothing for 5699 ticks: p0 silent 40801..46500` (ends 57,039) | red, same message |

## Expected
Every sweep row's checks and oracles clean, or the row in `Known` with an id (criterion 3 changes `Known` only for Raraku).

## Notes
- The Pale Hills row is the BUG-1151 shape the developer reported on `FarExpandProbeQaStressTests` Pale Hills seed 2. The
  same match also fails this sweep, and that row was not named.
- The Vathar row is the BUG-1152 family (a seat with workers silent for minutes). Its match is the one whose end moved
  from 30,569 to 57,039 (`MatchCriterionTests`' Vathar comment lists seed 2 at 57,039).
- The retrain plays no part: item 0 alone gives the same results.
