# BUG-1537: `MatchCriterionSweepQaStressTests` seed sweep red on Pale Hills seed 2 and Vathar Crossing seed 2, neither named in the M6-SH2 report

| Field | Value |
| --- | --- |
| Severity | S4 (re-rated at the 2026-11-16-2036 ACCEPT; was S3) |
| Status | open, re-rated S4 at the 2026-11-16-2036 ACCEPT (Vathar 2 ends at 30,540 with every oracle clean after M6-S12, its `Known` entry removed; no pinned row shows the shape now; the design gap, what a seat buys while it banks for a hall, is not cured) |
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

## M6-SH2 fix round 1 (the developer)
- Pale Hills 2: BUG-1151's re-send, fixed (see BUG-1151); the row is green.
- Vathar Crossing 2: p0 lost its Keep (about 34,000) and is left with one Laborer, 276 gold, 20-70 wood and 10 soldiers
  (under its opening's wave size, 16 pop); the hall rebuild holds its gold for the Keep (275 / 275) while the one Laborer
  hauls wood 10 at a time, so nothing is sent for 5,699 ticks; the match ends at 57,039. Not a wedge. Left red, named;
  adding it to `Known` is the Producer's call (criterion 3).

## QA re-check 1
- Pale Hills 2: fixed by cause. With BUG-1151's fix taken out the row fails again at 26,461 (the 9-think refusal streak);
  with it the row is green in QA's Stress run.
- Vathar 2: still red ("a seat with a worker sent nothing for 5699 ticks: p0 silent 40801..46500; p1 silent 38361..42260"),
  ends 57,039, p0 wins. The developer's diagnosis fits that line: a seat that holds its gold for a Town Hall while one
  Laborer hauls the wood. QA did not re-trace it. Criterion 3 lets `Known` change only for Raraku, so extending it to Vathar
  is the Producer's call. A ruling is needed: the alternative is an AI change, such as spending the 10 soldiers while banking
  for the hall.

## QA re-check 2 (fix round 2, 039d6eb)
`Known` gains `("vathar_crossing", 2) = "BUG-1537"` with the diagnosis as its comment; no bound raised. The class whole:
24 / 24 green; the Vathar 2 row prints `known (BUG-1537): a seat with a worker sent nothing for 5699 ticks: p0 silent
40801..46500; p1 silent 38361..42260`, the same failure as re-check 1. Note: like the Raraku entries, `Known` exempts
the row from any oracle failure, not only the silent-seat one, so a new failure on that seed would be hidden until the
entry is removed (the class's existing form; it does assert the row still fails). Stays open for the cure.

## M6-S12 (2026-11-16-2036)
Vathar Crossing seed 2 ends at 30,540 (p0) with every sweep oracle clean; its `Known` entry removed at the row's own
request. Not cured by cause (the hall-rebuild bank was not touched): the match moved.
