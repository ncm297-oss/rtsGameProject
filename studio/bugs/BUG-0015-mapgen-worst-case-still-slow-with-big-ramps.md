# BUG-0015: Worst valid map params still take ~45 s (Debug) / ~7 s (Release): ramp size and map size are not in the time bound

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-03-2220, task M1-4a |
| System | terrain / map generator |
| Fixed by | 558f580 (M1-4a fix round 1) |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.RampWallQaTests.WorstValidParams_1024Map_AtCaps_UnderFiveSeconds`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~WorstValidParams_1024Map"`

Params (all pass `Validate`): 1024 x 1024, `Level1Plateaus = Level2Plateaus = 32`,
`RampsPerPlateau = 16`, `RampTries = 128`, `MaxAttempts = 16`, `MinPassableFraction = 1`, plus
`RampWidth = 50`, `RampLength = 100`, `Level1MinSize = 52`, `Level1MaxSize = 200`,
`Level2MinSize = 52`, `Level2MaxSize = 150` (the `bigRamps: true` case).

## Expected
The M1-4a brief, BUG-0013 item: "worst valid case ~2 s". QA focus: "time the worst valid param set
at the new caps (expect ~2 s, fail above 5 s)". docs/03 now says "the worst valid 512 × 512 case
takes under a second".

## Actual
Measured with a scratch console app (generation + NavGrid) over seeds 1, 11 and 99:

| Params (all at the new caps, 1024 x 1024) | Debug (how the tests run) | Release |
| --- | --- | --- |
| default 3 x 4 ramps, Level1MaxSize 400 | 4.3-4.8 s | 0.36-0.41 s |
| ramps 20 x 300 | 12.8-13.9 s | 2.0-2.4 s |
| ramps 100 x 200 | 23-31 s | 2.2-4.7 s |
| ramps 200 x 100 | 31-36 s | 4.5-5.5 s |
| ramps 50 x 100, Level1 52-200 | 42.6-48.2 s | 6.4-7.4 s |

The worst case is now *longer* than BUG-0013's original 35 s. Even default-size ramps at 1024 x 1024
take about 4.8 s in Debug, well past "~2 s".

## Notes
The fix capped the counts (plateaus, ramps, tries, attempts). Each ramp try still costs
O(RampWidth x RampLength) footprint checks, and the BUG-0012 fix lets `RampWidth`/`RampLength` go
up to the map side (1024). Two possible fixes: cap `RampWidth`/`RampLength` at sensible sizes
(docs/02 ramps are chokepoints a few cells wide), or put an overall cell-check budget on placement.
The docs/03 sentence should state the bound the code actually guarantees.

Impact is low. No shipped config comes close; this only matters if params ever come from data. It
is filed as S2 only because the brief made "~2 s" (fail above 5 s) an acceptance criterion. The
Producer may downgrade it.

**QA verification (2026-10-03-2220, M1-4a fix round 1):** fixed. Ramp tries now use a summed-area
table plus rectangle overlap, so their cost no longer scales with RampWidth x RampLength. Debug
test-run timings on 1024 x 1024 at every cap (MinPassableFraction 1, so all 8 attempts run):
- this bug's two repros: 0.58 s and 0.67 s;
- the developer's 20x300, 100x200 and 200x100 ramps: 0.51-0.57 s;
- eight new QA shapes in `Stress.MapGenWorstCaseHuntTests`: many tiny ramps, huge plateaus with
  tiny ramps, a 1000-wide mouth, a 400-wide mouth, a 1024-long ramp, 1024x16, 16x1024 and 10x40.
  The worst is 1.32 s (400-wide mouth).

The new checks are equivalent to the old ones. QA oracles copy the pre-fix generator and NavGrid
from 4b204f6, and the oracle itself reproduces the developer's 15 pinned hashes. Compared against
them, every case matched on levels, elevation bits, RNG state after generation, and every nav
flag and cost:
- 1,920 fuzzed param/seed maps (tiny and non-square maps, margin 1, ramp width 1, long ramps);
- 280 edge-set maps;
- 3,000 adversarial heightmaps from 1x1 to 40x40, which also cover the bounds-free Flood.

Note for the Producer: `MaxMaxAttempts` went from 16 to 8. That is a tightening the brief didn't
ask for. With 16 attempts the worst case would be roughly 2x (about 2.6 s), still under the 5 s
gate, so the lower cap is a choice, not a necessity. Nothing in data or docs relied on 9-16.
