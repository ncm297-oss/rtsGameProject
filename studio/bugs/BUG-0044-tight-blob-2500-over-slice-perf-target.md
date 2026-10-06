# BUG-0044: 2,500-unit tight blob averages 4.7 ms/tick, over the M1-4d-3 target of 4.5 ms (+9%; +52% with two players)

| Field | Value |
| --- | --- |
| Severity | S3 (was S2; downgraded at re-check round 1) |
| Status | fixed |
| Found | 2026-10-05-1609, task M1-4d-3 |
| System | movement (per-tick cost: chain-shove queries, detour, widened queuing) |
| Fixed by | 6d1cbfd (M1-9); see the M1-9 re-check below |

## Repro
Perf probe (QA scratch, same code on both builds): `MoveScenario.Spawn(seed 99, 2,500 units, maxCost 12,
players 1 or 2)`, `MoveAll` to the central cell, 5 warm-up ticks, a full GC, then the average of 300
timed ticks; one sample per fresh `dotnet test` process, 15 s cool-down between runs, base 7f741f1 and
HEAD alternated, Debug, nothing else running (CPU load 8% idle). The in-repo row is
`Stress/LocalMovementStressTests+Serial.Perf_TightBlob_AvgAndWorstTick(2500, enforce: false)`.

## Expected
Brief M1-4d-3 targets: "Perf (Debug) 2,500 tight blob avg <= 4.5 ms"; miss rule: "perf over budget" is
not acceptable.

## Actual
| Row (ms/tick, 3 rounds) | Base 7f741f1 | M1-4d-3 | Change |
| --- | --- | --- | --- |
| 2,500 tight blob, one player (the row as the dev re-set it) | 4.32 / 4.28 / 4.28 | 4.70 / 4.67 / 4.72 | +9% |
| 2,500 tight blob, two players (the row as it was when "now" = 3.8 ms was measured) | 4.55 / 4.45 / 4.42 | 6.90 / 6.93 / 6.78 | +52% |
| 1,000 walkers crossing a 1,500 same-owner blob, 400 ticks (report row) | 3.11 / 3.06 / 3.10 | 5.49 / 5.42 / 5.40 | +76% |
| 2,500 to 4 points, one player per point, seed 1, 600 ticks | 1.96 / 1.93 / 1.93 | 2.54 / 2.54 / 2.61 | +31% |

The developer measured the same (4.81-5.01 vs base 4.41-4.51) and listed it as a miss.

## Notes
- The machine is about 14% slower today than when docs/03's 3.8 ms was measured (base 4.3 vs 3.8), so
  the base itself had little headroom; the M1-4d-3 code is still over on the same machine, same day.
- The docs/03 design budget (500 units: average < 4 ms, p99 < 8 ms) still holds with a wide margin
  (500 moving 0.3 ms, 500 tight blob 0.53 ms).
- The 2,500 tight blob row has `enforce: false`, so nothing in the suite guards the 4.5 ms target.
- Wall-clock caveat: consecutive runs in one process slow down (4.5 -> 10 ms on the same scenario),
  likely thermal or power throttling; only first-sample, cooled, alternated numbers are quoted above.
- Two players to one point is now a contest of enemies (BUG-0037), which costs much more per tick;
  that is a realistic game situation (two armies converging), so the Producer may want that row too.

## Re-check round 1 (2026-10-05-1609, 6abd200)
Same method (cooled single samples, base and fix alternated, 3 rounds, ms/tick):

| Row | Base 7f741f1 | Fix round 1 | Change |
| --- | --- | --- | --- |
| 2,500 tight blob, one player (target <= 4.5) | 4.30 / 4.32 / 4.31 | 4.50 / 4.50 / 4.52 | +5% |
| 2,500 tight blob, two players (report) | 4.51 / 4.50 / 4.53 | 6.62 / 6.71 / 6.65 | +47% |
| 1,000 walkers crossing a 1,500 blob (report) | 3.09 / 3.11 / 3.09 | 5.43 / 5.43 / 5.43 | +75% |
| 2,500 to 4 points, seed 1 (report) | 1.97 / 1.97 / 1.96 | 2.57 / 2.55 / 2.55 | +30% |

The target row is now at its 4.5 ms limit within noise (mean 4.507), on a machine where the base
itself measures 4.3 (3.8 when the target was set): downgraded to S3. Left open for the report rows
(two players, crossing), which cost much more, and for item 1 of BUG-0047 (the row isn't enforced).

## Re-check round 2 (2026-10-05-1609, fix commit 57cc55c): the two-player rows regressed again
Same method (cooled single samples, base and fix alternated, 3 rounds, ms/tick):

| Row | Base 7f741f1 | Round 1 (6abd200) | Round 2 (57cc55c) |
| --- | --- | --- | --- |
| 2,500 tight blob, one player (target <= 4.5) | 4.38 / 4.30 / 4.31 | 4.50 / 4.50 / 4.52 | 4.61 / 4.46 / 4.50 |
| 2,500 tight blob, two players (report) | 4.47 / 4.49 / 4.47 | 6.62 / 6.71 / 6.65 | 10.40 / 10.50 / 10.46 |
| 2,500 to 4 points, one player per point, seed 1 (report) | 1.97 / 1.99 / 1.95 | 2.57 / 2.55 / 2.55 | 3.72 / 3.71 / 3.73 |
| 1,000 walkers crossing a 1,500 blob (report) | 3.10 / 3.09 / 3.12 | 5.43 / 5.43 / 5.43 | 5.48 / 5.38 / 5.40 |

The developer's note "not regressed (the plug test only runs on enemy overlap)" holds for the
one-player rows only. Where two players' units overlap all the time (a contested point, crowds of
both players next to each other) the round-2 `IsPlug` search (a breadth-first search with a spatial
query per member, for every overlapped Idle enemy, in every `Constrain` call) adds +57% / +46% over
round 1: 2.3x / 1.9x base. Still report rows (500 units stay far under the 4 ms design budget), so S3;
the Producer may want a two-player row in the perf targets.

## Re-check M1-9 (2026-10-06-0905, commit 6d1cbfd): fixed
Plug answers cached per unit / walker owner / radius class (`World.PlugAnswers`, epoch-stamped, preallocated)
and `Constrain` skips non-walls inline. `CrowdPerfTests` (Perf, run alone, Debug), the same tests on base
1f533aa and on 6d1cbfd: one-player 2,500 tight blob 4.52 -> 4.33 ms (enforced <= 4.5; fails on base);
two-player contested blob 10.61 -> 6.90 ms (guard < 10.5; fails on base); 2,500 to 4 points 3.76 -> 3.11
ms (guard < 3.7; fails on base); 500 moving 0.63 ms. The cache was checked against a fresh search for
every query in 3 shuffled orders on 5 random worlds (`QA/HardeningQaTests.PlugCache_AnyQueryOrder_EqualsAFreshSearch`:
0 mismatches); one stale-hash corner in the shove pass is BUG-0071. Left as report rows: the two-player
blob is still about 1.5x the one-player blob (docs/03 Known limits), and the tight-blob row has under 4%
headroom on this machine (a slower day may trip it: re-run alone first).
