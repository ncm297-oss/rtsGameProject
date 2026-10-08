# BUG-0151: After the BUG-0146 fix the 200-worker gather Perf row costs about 40 % more per tick (M4-2a criterion 3 allows 10 %)

| Field | Value |
| --- | --- |
| Severity | S3 (Producer re-triage from S2 at the 2026-10-08-0313 ACCEPT; see the decision below) |
| Status | open (S3: a crowd-cost item for the next sim hardening session or the post-M4 crowd-cost work; the criterion was re-stated, see below) |
| Found | 2026-10-08-0313, task M4-2a (declared by the developer, confirmed by QA) |
| System | sim: gather walks (`Economy/EconomySystem.WalkToFootprint`, `Movement/MovementSystem` stand arrival), sim track |
| Fixed by |  |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TwoHundredGatheringWorkersAlone"` alone, three runs, on
   `007262d` (base) and on M4-2a's head (`9be1991`), Debug, nothing else running on the sim track's tests.

## Expected
M4-2a acceptance criterion 3: "the gather efficiency Perf rows (200 workers alone) move by at most 10 %".

## Actual
| Run | Base (ms / tick) | Head (ms / tick) |
| --- | --- | --- |
| 1 | 0.226 | 0.321 |
| 2 | 0.239 | 0.324 |
| 3 | 0.231 | 0.330 |

+38 % to +41 %. The row's own budget is 1 ms, so it still passes. The same scene gathers more on the head: gold
1,120 -> 1,310 (+17 %), wood 440 -> 500 (+14 %) by the end of the row, so per resource delivered the tick costs about
20 % more. The other rows the brief named are inside their bounds: 500 marching + 50 gathering 0.90-0.94 ms (base
0.90-0.94), `TightBlob2500` alone 4.42-4.47 ms (base 4.38-4.43, bound 4.6), the 500 v 500 brawl 3.15-3.17 ms (base
3.17-3.64, bound 4).

## Notes
- QA's income report (`Stress/GatherPocketStressTests.IncomePerWorkerPerMinute_Report`, generated maps seeds 1-8, gold +
  wood per worker per minute after a 400-tick warm-up): 1 worker 18.8 -> 18.8, 5 workers 23.8 -> 23.2, 10 workers
  23.2 -> 24.4, 20 workers 20.6 -> 23.6. The fix changes little for light loads and helps crowds (+15 % at 20 workers),
  so it's the crowded walks (stand points filling, more walkers per tick) that cost the time.
- Producer's call: accept the cost (re-state the criterion against the 1 ms budget) or have the sim cut it (e.g. the
  three `PointCrowd` spatial queries per walk, or the extra walk ticks of the stand arrival). It's filed S2 only because
  it's a stated acceptance criterion, not because a budget is blown.
- The +15 % crowd income is a balance change for M3's economy numbers; worth a line in the M4 balance pass.

## Re-check (2026-10-08-0313, round 1, QA)
Not changed by `fea7963` (the developer profiled it and kept no variant). Perf alone on `fea7963`: 0.323 ms. QA
instrumented the row's scene (`Mixed(0, 200)`, 400 warm-up ticks, 400 measured, 5 runs x 2, Debug) on `007262d` and
`fea7963`, counting walkers (`Moving`) and walker-neighbour pairs within 1.5 m each tick outside the timed region:

| | base `007262d` | fix `fea7963` |
| --- | --- | --- |
| ms / tick | 0.225-0.250 | 0.319-0.348 |
| walkers / tick | 40.8 | 56.8 (+39 %) |
| neighbour pairs / tick | 228 | 465 (x2.04) |
| us per walker-tick | 5.51-6.14 | 5.62-6.12 |
| resources in the window | 910 | 1,040 (+14 %) |
| us per resource | 99-110 | 123-134 (+24-30 %) |

At *matched* crowd density the tick costs the same on both builds (median ms by pairs/tick bucket, base vs fix: 0-150:
0.120-0.129 vs 0.132-0.136; 150-250: 0.181-0.191 vs 0.166-0.174; 250-350: 0.202-0.214 vs 0.206-0.213; 350-450:
0.223-0.245 vs 0.238-0.248). The fix spends 496 of 1,600 sampled ticks above 600 pairs, base only 20. So the extra cost is the
denser crowd (more workers walking near their stand points), not slower code. The developer's explanation holds. The
criterion as worded (≤ 10 %) is still not met, so this stays S2 until the Producer re-states it (e.g. against the 1 ms
budget, or per resource delivered) or asks for a cheaper crowd.

## Producer decision (2026-10-08-0313 ACCEPT)
Criterion 3's "at most 10 %" was written as a guard against wasteful code in the arrival fix. QA's profile shows the
per-walker cost is unchanged (5.5-6.1 us per walker-tick on both builds; equal cost at matched neighbour density) and the
extra 0.09 ms is the crowd the fix creates: 39 % more workers walking (they used to stand wedged, which was the bug) and
twice the neighbour pairs round the nodes, in a scene of 100 workers on one tree and one mine per base. The row sits at
0.32 ms of its 1 ms budget, the 500 + 50 row and `TightBlob2500` are unchanged, and income rose (+14-17 % in the row,
+15 % per worker at 20 workers). **Re-stated criterion:** per-walker cost unchanged and the row inside its own budget;
met. **Re-triaged S3** (crowd cost, not a defect in the fix): revisit with the crowd-cost work after the M4 sandbox (the
same item as BUG-0028 / 0032 / 0046: a crowd cost in the flow fields or cheaper neighbour handling at stand points), or
earlier if a hardening session finds a cheap cut (the dev tried four variants; each broke a wedge row or cut little).
Also noted for the data track's balance pass: +15 % crowd income at 20 workers is a balance change to M3's numbers.
