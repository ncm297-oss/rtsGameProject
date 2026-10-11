# BUG-0510: `EmptyTick_WithManyUnits_IsCheap` spawns most of its units off the map, so it times a corner-bucket pile-up and a line brawl, not an idle tick

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-11, task M4-H3 (the BUG-0451 profiling pass, sim track) |
| System | perf tests (`Stress/SimCoreStressTests.EmptyTick_WithManyUnits_IsCheap`); spatial hash clamping |
| Fixed by | |

## Repro
1. `Stress/SimCoreStressTests.EmptyTick_WithManyUnits_IsCheap(2500)`: `SpawnUnit(i % 2, 0, new Vector2(i, i))` for i < 2,500
   on the default 128 x 128-cell (256 m) map, type 0 (`malazan_cadre_mage`, a fighter), owners alternating.
2. A phase-by-phase profile of the measured ticks (a scratch copy of `Simulation.Tick`'s phases with a stopwatch each,
   2,000 ticks after the test's 102 warm-up ticks, Debug, not committed): target acquisition 305-327 us of a 410-555 us
   tick (75 %), of which `PickTarget` 244 us for 149 scans a tick, each finding **no** enemy; spatial rebuild 51, vision 34,
   swings 36, movement 15, economy + construction 21, the counter-gated M4 passes (summons, passives, statuses, zones,
   stealth, abilities) 0.5 us together. 6,100 ticks in, 966 of the 2,500 units are alive.

## Expected
A row named "empty tick with many units" times 2,500 standing units on the map: the per-unit bookkeeping cost (docs/03
"Testing strategy").

## Actual
2,244 of the 2,500 spawn points are off the map (x = y > 256 m). Spawns are accepted there, and `SpatialHash.BucketX/Y`
clamp off-map positions to the edge, so all of them share the corner bucket: every scan by one of them walks that whole
bucket (hundreds of entries) to find nothing. And since the type fights and neighbours along the diagonal are 1.4 m apart
and of alternating owners, the line brawls (about 1,500 die over the run), so the measured window is a slow fight, not an
idle tick. The gate has crept with this scene's acquisition cost, not with the M4 passes.

## Notes
- M4-H3 re-baselined the 2,500 row to 550 us (BUG-0451, the Producer's fallback) with the trend in the row's comment; it did
  not change the scene (QA's suite; changing it would lower the number).
- Options for QA / the Producer: keep the row as a "pile-up" stress and add a true idle row (2,500 units on the map, a
  non-fighting type or one owner, spaced beyond sight); or move the spawns on the map. Separately, whether `SpawnUnit` should
  refuse an off-map point (it is a dev command; the replay format accepts it) is a sim-rules question.
