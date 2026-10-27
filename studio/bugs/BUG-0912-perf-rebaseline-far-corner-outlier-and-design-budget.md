# BUG-0912: BUG-0764 re-baseline: the far-corner scan gate was set from one head reading, and two gates now sit above the 4 ms design budget

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-17-1624, task M5-H3 (QA, full tier) |
| System | tests: Perf gates (`Stress/CombatScaleQaTests`, `Vision/VisionGatePerfTests`, `CombatPerfTests`), docs/03 "Testing strategy" |
| Fixed by | M5-H4, session 2026-10-26-2108 |

## Repro
1. QA ran the whole `dotnet test sim/Rts.Sim.Tests --filter Category=Perf` four times on an idle PC: base bf8026e, head
   d048a21, base, head. Base was red on 16 rows, then 15. Head was green both times (179 / 179).
2. `Stress/CombatScaleQaTests.TightBlob2500_OneEnemyAtTheFarCorner_ScansNearFree` (median delta, ms). The old bound was 0.25:
   - base: 0.220 and 0.236 in the category runs; 0.226, 0.236 and 0.216 in three fresh paired processes. 0 of 5 over 0.25.
   - head: 0.212 and 0.276 in the category runs; 0.262, 0.191 and 0.247 in the paired processes. 2 of 5 over 0.25.
   - The mean difference is +0.011 ms (noise). The row was not red alone on base in QA's runs.
3. The new bound is 0.64 ms. It comes from the dev's single head reading of 0.515 (+25 %), against base readings of
   0.211-0.312 in the dev's own runs.
4. `VisionGatePerfTests.Brawl250v250_OnAThreeLevelMap_*` is 500 units. It reads 5.8-6.0 ms in the category run and is now
   gated at 7.5 ms. docs/03 l. 1090: "The 4 ms design budget is for 500 units". `CombatPerfTests.Brawl500v500_*` (1,000
   units) reads 5.5 ms in the category run and 3.36-3.45 ms in a fresh short process, and is now gated at 6.9 ms.

## Expected
Brief M5-H3, scope 1: re-baseline only rows red alone on both trees, and each new threshold is the measurement plus at most
25 %. For a delta of two medians whose noise is the size of the bound, one high reading is not a measurement. The dev's
comment says as much: "QA may want more pairs here rather than a wider bound". The design budget in docs/03 (4 ms for
500 units) should still be checked by some row, or docs/03 should say that it isn't any more.

## Actual
- The far-corner row now passes a far-enemy scan that costs about 2.7× today's ~0.23 ms. It no longer checks brief
  criterion 8 ("scanning in a world with no enemy in sight must be near free") at the resolution it was written for.
- No Category=Perf row on this PC now fails when a 500-unit scene goes over 4 ms. On the category run's sustained clock
  the 250 v 250 vision brawl is 45-50 % over the design budget, on base and head alike, and its gate allows 7.5 ms.
  docs/03's new "Re-baselined for this PC" paragraph does not mention that the 4 ms design budget itself is now
  unguarded.

## Notes
- Not a code regression. The paired A/B on head is within noise: 500 moving 1.10-1.13 / 1.10-1.13 ms; tight blob
  7.29-7.34 / 7.32-7.35; brawl ticks 5-205 3.36-3.38 / 3.38-3.45; mixed brawl 3.34-3.59 / 3.35-3.37.
- Suggested: give the far-corner row more pairs (8 alternated, as BUG-0158 did) and a bound of about 0.35 ms, or keep 0.25
  with a best-of-N rule. For the design budget, either a short fresh-process gate row (the fast figure) or a line in
  docs/03 / STATE "Waiting on you" saying the 4 ms budget is unchecked until the PC's sustained clock is fixed (Producer /
  owner call).
- The dev's own remark that the gates "would pass a ~1.6x slower change in a short run" holds. In a short fresh process
  the tight blob reads 4.4 ms against its new 9.3 ms gate (2.1×). Only the category run, the check everyone runs, keeps
  the 25 % margin.

## Fix (M5-H4, session 2026-10-26-2108)
- `Stress/CombatScaleQaTests.TightBlob2500_OneEnemyAtTheFarCorner_ScansNearFree`: eight alternated pairs after the discarded
  warm-up pair (four before), bound 0.64 -> 0.35 ms. Readings: +0.135 and +0.130 ms (blob 4.5-5.1 ms). Under another track's
  full test run on the same PC it read +1.2 to +2.0 ms (blob 8-24 ms): only a quiet machine's reading counts, as for every
  Perf row.
- The design budget: `VisionGatePerfTests.Brawl250v250_DesignBudget_FreshProcessShortRun_Under4Ms` runs three fresh `dotnet
  test` children of the assembly (the row with `RTS_PERF_FRESH_CHILD=1`), each timing the 250 v 250 vision brawl (300 ticks
  after 10 warm-up), and gates the best under 4 ms. Measured 3.73-3.90 ms: green, but with under 10 % margin.
- docs/03 "Re-baselined for this PC" says both.
