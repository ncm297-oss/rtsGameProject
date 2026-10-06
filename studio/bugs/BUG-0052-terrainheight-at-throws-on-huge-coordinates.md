# BUG-0052: TerrainHeight.At throws instead of clamping for coordinates beyond ~4.3e9 m or +Infinity

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-05-1609, task M2-2 |
| System | view: ViewApi TerrainHeight (unit view placement, selection rings) |
| Fixed by | M2-H1 (3dc0568): `TerrainHeight.At` clamps in float before the `(int)` cast; the 5 `QA/ViewApi/TerrainHeightQaTests.At_FarOffTheMapOrInfinite_*` rows un-skipped, pass |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TerrainHeightQaTests.At_FarOffTheMapOrInfinite"`
   after removing the `Skip` on the five rows marked BUG-0052.
2. Or directly: `TerrainHeight.At(map, 1e10f, 5f)` on any map.

## Expected
`TerrainHeight.At`'s doc: "points off the map are clamped onto it" (docs/03 "Implementation (M2-2)" says
the same). Any float input should return an edge cell's height.

## Actual
```
System.ArgumentOutOfRangeException : cell (-2147483648, 2) is outside the 128 x 128 map (Parameter 'x')
   at Rts.Sim.ViewApi.TerrainHeight.InCell(...) TerrainHeight.cs:line 28
   at Rts.Sim.ViewApi.TerrainHeight.At(...) TerrainHeight.cs:line 22
```
Fails for x or y = 1e10, +Infinity, float.MaxValue. Negative huge values, -Infinity and NaN are fine
(they take the `fx >= 0f ? ... : 0` branch).

## Notes
- Cause: `Math.Min((int)fx, map.Width - 1)`: on .NET 8 x64 an out-of-range float-to-int cast gives
  `int.MinValue`, so `Math.Min` picks it. Clamp in float first (`Math.Min(fx, map.Width - 1)`) before the cast.
- Unreachable today: the sim keeps units on the map. But `UnitViews.Sync` and `SelectionRings.Sync`
  call it every frame for every unit, so if a sim bug ever put a unit far off the map this would throw
  every frame instead of drawing the unit at the edge.
- `GroundPicker` is not affected (it clamps cell indices before calling `InCell`).

## Re-check (2026-10-06-0905, M2-H1 commit 3dc0568): fixed
Verified: the 5 un-skipped rows fail on base code (scratch clone, `TerrainHeight.cs` reverted) and pass on 3dc0568. New QA `ViewHardeningQaTests.At_EverySpecialOnBothAxes_IsFiniteAndEqualsTheClampedPoint`: 16 specials (NaN, +/-Inf, +/-MaxValue, +/-1e10, +/-4.3e9, 2.2e9, +/-Epsilon, +/-0, +/-1e-30) on each axis against 22 values on the other, on 4 maps (one with a ramp in the map's edge column): every result is finite, in range, and exactly equal to the height at the clamped point. `InCell` over every cell of the edge-ramp map with all 256 special pairs stays inside the cell's corner range.
