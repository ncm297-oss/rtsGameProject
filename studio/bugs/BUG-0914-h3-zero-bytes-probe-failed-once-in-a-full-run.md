# BUG-0914: `SimHardeningH3QaTests.FiveHundredUnits_OrderedOntoAnUnreachableBuilding_AllGiveUp_ZeroBytes` failed once in a full non-Perf run, green alone

| Field | Value |
| --- | --- |
| Severity | S4 (flake; S3 if it fails a second full run) |
| Status | open |
| Found | 2026-10-17-1624, the data track's D-R31 full run on the merged tree (sim M5-H3 + the re-print); filed by the Producer |
| System | tests: `QA/SimHardeningH3QaTests` (an M4-H3 allocation probe: 500 units ordered onto an unreachable Tent, `GC.GetAllocatedBytesForCurrentThread` summed over ticks 5 on, asserted 0) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf` on the merged 1624 tree (sim d048a21 / c91926f + data ba56348): the row
   failed once on the 0-byte assertion (the data dev's run). It passed 3 / 3 alone and as its class, and QA's full M5-H3 run (6,649
   passed, the 8 rule-31 cells the only red) and the Producer's run (session log 2026-10-17-1624) did not reproduce it.
2. Not reproduced since; no byte count was captured from the failing run (the data dev's report gives the test name only).

## Expected
An allocation probe is deterministic for a given tree: 0 bytes alone means 0 bytes in the full run. Every allocation-measuring
test sits in `SerialCollection` (docs/03 "Testing strategy") so it runs after the parallel batch, alone.

## Actual
One failure in one of three full runs on the same tree. The likely classes: (a) a first-touch allocation inside the measured
window that another test normally pays for (a lazily grown static scratch array or cache sized by an earlier test, a tier-0 JIT
of a path first reached mid-run), (b) something the M5-H3 changes allocate on a rare path (`FaceCell` corner walks, the flank's
`AgainstKeptSide` / doubled stride, `AnyBuilder`): all are allocation-free by inspection, and the dev's and QA's 0 B probes
(`ChangedPaths_MidFlankSpamAndCrowdedMineFace_AllocateNothing`, the 500 v 500 mid-flank rows) are green.

## Notes
- Next sim hardening (or sooner if it fails again): print the byte count and the first allocating tick in the failure message
  (the probe sums; a per-tick first-nonzero report would locate it), run the class after a cold `dotnet test` of the whole suite
  twice, and if it is (a), warm the path in the test's first five ticks like the other probes do.
- A second failure in a full run makes this S3 and the first item of the next sim session (a red test on `main`).
