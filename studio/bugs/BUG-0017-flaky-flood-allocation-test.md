# BUG-0017: Flood_10000Commands_OneTick_AllocatesNothing failed once in 11 full suite runs

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-03-2220, task M1-4a (fix round 1 re-check) |
| System | sim core / QA test suite |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests` (full suite, Debug), repeated.
2. In 11 isolated full runs on 558f580 plus the QA additions, run 1 had one failure:
   `Rts.Sim.Tests.Stress.SimCoreStressTests.Flood_10000Commands_OneTick_AllocatesNothing [206 ms]`.
   The other 10 passed. No other test process was running.

## Expected
The test measures `GC.GetAllocatedBytesForCurrentThread()` around two ticks that apply 10,000
spawn commands and expects 0 bytes. It should pass every time, or fail every time if the tick
allocates.

## Actual
It failed once. The output filter dropped the assertion text, so the byte count is unknown.
The test passed in these other runs:
- alone, 3 times;
- in 6 runs alongside the heavy map and spatial suites;
- with OSR and tiering forced (`DOTNET_TC_OnStackReplacement_InitialCounter=1`, `DOTNET_OSR_HitLimit=1`;
  `DOTNET_TC_CallCountThreshold=1`).

## Notes
The code under measurement shows no managed allocation: CommandQueue.Sort, Apply/SpawnUnit, and
SpatialHash.Rebuild (new in M1-4a) have no `new` on the tick path. The suspected cause is the
runtime, not the sim. A per-thread charge from JIT or type loading on the first 10,000-command
tick, while the suite is under load, would fit. That is a hypothesis, not a confirmed cause.

This is QA's own test (M1-2). It may also be a product allocation that appears only on some code
path, so it is filed rather than quietly hardened. A next step that doesn't loosen the test: on
failure, rerun the measured block once and report both deltas, so the message tells a one-time
runtime charge apart from a steady allocation. A related separate observation: running two test
processes at once once pushed `SpatialHashStressTests.WorstCase_AllUnitsInOneBucket_Report` over
its 50 ms guard. That was self-inflicted load during QA, not a bug.

**Producer triage (2026-10-03-2220):** S3, does not block (1 failure in 11 QA full runs; 0 in the
Producer's 1 full run + 8 isolated reruns on f282aaf). Not planned as its own task. Rule: if it
fails again on `main`, the next session's first commit hardens it as proposed above (re-measure
the block once on failure and report both deltas); never loosen the 0-byte assert. If the second
measurement also allocates, that is a product bug and this becomes S2.

**Producer (2026-10-04-0120, M1-4b ACCEPT):** failed again, in 2 of 3 Producer runs of
`--filter "Category!=Perf&Category!=Soak"` on b707114: `Expected: 0, Actual: 8112` bytes. The
count is a one-time charge (10,000 spawns of a per-spawn leak would be megabytes), and the new
spawn path reads an `ImmutableArray<UnitDef>` element, which allocates nothing. The rule above now
applies: **the next session's first commit hardens this test** (re-measure once on failure, report
both deltas; never loosen the 0-byte assert), together with BUG-0024's non-parallel collection.
Still S3; it does not block M1-4b.
