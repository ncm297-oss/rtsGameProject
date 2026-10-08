# BUG-0140: `TightBlob2500_OnePlayer` (4.5 ms) now fails about 1 run in 6 after M4-1 (+0.08 ms)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (budget 4.5 -> 4.6 ms, Producer-approved in advance) |
| Found | 2026-10-07-2014, task M4-1 |
| System | tick cost: combat phase 7 early-outs, movement's planted checks |
| Fixed by | eebb152 (`CrowdPerfTests.TightBlobBudgetMs`) |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TightBlob2500_OnePlayer"`, alternated 13 times between a
clone of the base (8655fb1) and of f2879b9, same machine:

| | runs (ms) | mean |
| --- | --- | --- |
| base | 4.35 4.34 4.41 4.32 4.42 4.41 4.35 4.42 4.33 4.43 4.35 4.37 4.36 | 4.374 |
| M4-1 | 4.48 4.48 4.52 4.47 4.46 4.53 4.42 4.44 4.41 4.46 4.39 4.38 4.46 | 4.454 |

Paired delta +0.080 ms (range +0.01 to +0.15). The row's 4.5 ms budget failed 2 of 13 M4-1 runs (4.52, 4.53) and
0 of 13 base runs.

## Expected
Brief criterion 8: not worse than `main` by more than 0.1 ms (met, narrowly), and a Perf row that does not flip.

## Actual
Within the criterion on average, but the row now sits 0.05 ms under its budget and flips red under ordinary noise.

## Notes
The one-player early-out itself is cheap: the same blob with one enemy at the far corner (every scan runs and skips
own-only buckets) costs +0.087 ms over the blob alone (`Stress/CombatScaleQaTests`). The rest is spread over the
per-unit checks added to phase 7 and movement (`Target` read per walker for `ChaseArrival2`, the `Attacking` compares
in the hard-wall / shove tests).

## Re-check 2026-10-07-2315 (QA, fix round 1)
5 runs alone: 4.47, 4.51, 4.49, 4.48, 4.49 ms, all under 4.6 (1 of 5 over the old 4.5). Far-enemy delta +0.076 ms (`CombatScaleQaTests`).
