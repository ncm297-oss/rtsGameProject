# BUG-0044: 2,500-unit tight blob averages 4.7 ms/tick, over the M1-4d-3 target of 4.5 ms (+9%; +52% with two players)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-05-1609, task M1-4d-3 |
| System | movement (per-tick cost: chain-shove queries, detour, widened queuing) |
| Fixed by | |

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
