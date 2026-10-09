# BUG-0158: `CombatScaleQaTests.TightBlob2500_OneEnemyAtTheFarCorner_ScansNearFree` reads a sustained-load machine slowdown as the far enemy's cost (fails alone on base and head alike)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-08-0313, Producer ACCEPT reruns (sim track; test-only) |
| System | QA Perf row `sim/Rts.Sim.Tests/Stress/CombatScaleQaTests.cs` (M4-1) |
| Fixed by | M4-H1 (sim track): the row discards a warm-up pair, then runs four pairs in alternating order and compares medians |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~TightBlob2500_OneEnemyAtTheFarCorner_ScansNearFree"`
   alone, on this PC, 2026-10-08 ~08:40, CPU load average 2 % (a long-running `python` process with 60,000 CPU-s is
   present but idle at the time).

## Expected
The row alternates six 300-tick runs of the 2,500 blob (alone / with a far enemy, three times each) in one process and
asserts the enemy costs at most 0.25 ms a tick on average. It passed alone in QA's rounds 0 and 1 (alone 4.42-4.47 ms
on every run) and on the base `007262d` (4.38-4.43).

## Actual
Three runs today, two on the M4-2a head and one on the base sim code (the view worktree, whose `sim/` rules are
`007262d`'s): `alone 4.37 / 7.23 / 7.24, enemy 5.03 / 7.42 / 7.34 ms` (head), `alone 4.35 / 7.23 / 7.14, enemy 5.05 /
7.42 / 7.32` (base), delta +0.32 to +0.77 ms. The first run of each process is at the usual 4.35 ms; every later run in
the same process is at 7.1-7.4 ms, blob alone and with the enemy alike. The dev budget row
`CrowdPerfTests.TightBlob2500_OnePlayer_AverageTickAtMost4_6Ms` (one short run) passes alone three times in a row. So the
machine slows after about two seconds of sustained single-core load (power or thermal management), and the row's
alternation (alone first, enemy second) attributes the slowdown's onset to the enemy.

## Notes
- Not a code regression: identical on base and head. Per the tracks rule a Perf failure counts only alone; this one
  fails alone, but for the machine, so it is not held against M4-2a (Producer, ACCEPT 2026-10-08-0313).
- Fix options (next sim hardening): compare medians of interleaved runs after a warm-up run that is discarded; or
  measure the delta within one process only after both sides have run once; or pin the per-run figure, not the delta.
- Check the machine too: a sustained-load throttle would also explain the full Perf category failing 8-9 wall-clock rows
  on base and head in QA's round 2.
