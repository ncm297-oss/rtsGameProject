# BUG-0092: M3-3 nits: tiny repair factor rounds to 0, a holding worker can't build on its spot, push-out fallback stacks units

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-06-2114, task M3-3 |
| System | construction / repair / data loader |
| Fixed by | M3-H1 (4abbf37, session 2026-10-07-0800), items (a)-(f): (a) `DataLoader.Factor` rejects repair factors below 2^-16 (QA `RepairDataQaTests` tiny-factor row un-skipped); (b) the issuing worker's own Hold never puts it in the way of its Build (`Check(..., worker)`; QA `HoldingWorkerInsideTheFootprint_TheOnlyCanPlaceBuildDivergence` pins it as the one documented CanPlace / Build divergence); (c) push-out rings go on outward until a free cell (90 units round a 2 x 2 site on distinct cells; QA 400 stacked in a Keep, ring 9, 2.46 ms); (d) occupants through the spatial hash with per-cell answers in the build scratch (16 pushed in a blob of 400: 0.29 ms); (e) `PrevPosition` set with `Position`; (f) docs/03 "Cost" note that `CanPlace` writes flow-field scratch. `SimHardeningTests` rows, `AllocationTests.PocketDecisions_RefusedBuilds_AndAPushOut_AllocateNothing`. Residual: a level with fewer free cells than pushed units scans the whole map and stacks the rest (BUG-0095, S3) |

## Repro
1. **Tiny repair factor.** `QA/RepairDataQaTests.ATinyAcceptedRateFactor_Report` (skipped with this id): `rules.json`
   `"repair": { "rateFactor": 0.000001, ... }` loads with no error (the loader accepts any value above 0 and at most
   1), but `MathF.Round(1e-6 x 65536)` is 0, so a repair restores nothing and costs nothing:
   `rateFactor 1e-6: after 100 s hp 1400 / 2400, worker state Building`. The worker "repairs" for ever. Any factor
   below 2^-17 does this (the same holds for `costFactor`: repair becomes free).
2. **A holding worker can't Build on its own spot.** `QA/ConstructionQaTests.AWorkerOnHoldOrderedToBuildOnItsOwnSpot_Report`:
   a worker on HoldPosition standing in the footprint it is ordered to build: `CanPlace UnitInTheWay; after its own
   Build: 0 sites, hold True`. The worker's own Hold blocks its own order (any other order clears Hold first).
3. **Push-out fallback stacks units.** `QA/ConstructionQaTests.PushOut_RingsOneToThreeFull_LandsOnRingFour_AllEightFull_Report`
   and `Stress/ConstructionScaleStressTests.PushOut_InADenseBlob_ApplyTickCost_Report`: with rings 1-8 full, every
   pushed unit is set down on `FlowField.NearestPassable(anchor)`, the same cell for all of them and already
   occupied (here by an enemy holding unit): `builder set down on (18, 17), ring 1, sharing its cell with 1 unit(s)`.
4. **Push-out cost.** `Occupied` scans the whole unit store for every candidate ring cell: a Keep placed on 16 own
   units costs its apply tick 1.3 ms with nobody around, 7.3 ms (Debug) with 400 own units ringing it.

## Expected
1. Data the loader accepts plays as written (or the loader rejects factors that round to 0).
2. A Build clears the issuing worker's Hold like every other order before the "unit in the way" rule looks at it.
3. Pushed units get distinct cells (or the documented rule says they may stack).
4. Push-out cost bounded by the units near the footprint (the spatial hash), not the store size.

## Notes
- Item 1: a floor of 2^-16 in the loader, or keep the factors as fractions with a minimum, fixes it.
- Item 3 is documented ("past 8 rings the nearest passable cell"); stacking is the part docs/03 doesn't mention.
- Also seen, notes only: `World.CanPlace` is documented "read-only" but writes the flow-field cache's build scratch
  (`FlowFieldCache.BuildScratch`), so the view must call it on the sim thread between ticks, never during one; a
  contested anchor in one tick always goes to the lower player number (application order), like BUG-0026; push-out
  sets `Position` but not `PrevPosition`, so the view interpolates the unit through the new building for one tick.
