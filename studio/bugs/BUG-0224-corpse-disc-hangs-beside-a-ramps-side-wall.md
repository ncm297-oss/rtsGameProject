# BUG-0224: After the BUG-0223 fix, a corpse disc on low ground beside a ramp's side wall still hangs up to 2.16 m in the air

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-08-1814, task M4-V3 (QA re-check of fix round 1, e1f3333) |
| System | view: `sim/Rts.Sim/ViewApi/TerrainHeight.cs` (`MaxUnder` / `Rim`), drawn by `game/scripts/CombatViews.cs` |
| Fixed by | M4-V3 fix round 2 (view track): `TerrainHeight.MaxUnder` walks the centre-to-rim line cell edge by cell edge; `CorpseDiscRimQaTests.MaxUnder_BesideARampsSideWall_...` un-skipped |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CorpseDiscRimQaTests"` with the `Skip` removed from
   `MaxUnder_BesideARampsSideWall_NeverHangsAboveTheWallFreeGround_OnGeneratedMaps` (`QA/ViewApi/CorpseDiscRimQaTests.cs`).
   The test compares `MaxUnder` with a reference that walks the straight line from the disc centre to each of the eight
   rim samples in 64 steps and drops a sample if the walk crosses a wall (a height jump no slope can make in one step).
2. Every seed (1, 5, 17, 23, 42) fails the same way, for example:
   `seed 42: hang over the wall-free reference: worst 2.161 m at (139.16667, 27.5) r 1.08 cell (69,13) plateau e0.0; 1540 over 1 m`.
3. Terrain around that point (seed 42): cell (69,13) is level-0 ground, height 0. The cell south of it, (69,14), is a
   ramp that rises **east-west** (corners 2.0 / 2.8 m), so its north edge is a 2.0-2.8 m side wall facing (69,13). The
   south-west rim sample (138.403, 28.264) lands on that ramp at 2.161 m. That is under `maxRise` (1.08 x 4 / 2 x 1.001 +
   1e-4 = 2.162 m), so `Rim` counts it, and a Bridgeburner-sized (0.9 m) corpse disc is drawn 2.16 m above its own ground.
   Seed 1, cell (80,38): the same on a level-1 plateau (4 m) beside a 4-8 m ramp's side wall.

## Expected
docs/03 (BUG-0190 / BUG-0223 sentence): "Only rim samples joined to the centre by a slope count: plateau to plateau, or
a rise steeper than one level per cell, is a cliff and is ignored, so a unit dying against a cliff keeps its disc on its
own ground." A ramp's side wall is a cliff; nothing joins the centre to that rim point by a slope.

## Actual
The rule only measures the rise from the centre to the rim sample (`h - centre <= radius x one level per cell`), not
whether a slope joins them. Next to a ramp's side wall the ramp surface right behind the wall can be anywhere from the
foot to the lip height, and every part of it lower than `radius x 2` above the centre counts. Lift up to 0.96 m for a
0.4 m unit, 1.68 m for 0.7 m, 2.16 m for 0.9 m. 1,320-1,540 sampled (point, radius) pairs per map are over 1 m.
Everything else holds: on ramp cells (including beside a plateau one level up) the disc is exactly at the wall-free
reference, it never sinks below reachable slope (0 of ~620k samples over 5 maps), and a ramp foot gets at most 0.55 m
on generated maps (their ramps are 4 cells long).

## Notes
- Much better than before the fix (the plateau-cliff 4 m case is gone; at a1ccd91 this side-wall spot gave 2.77 m),
  so this is the rest of BUG-0223, not a regression.
- The real-fight probe `ProjectileViewQaTests.CorpseDisc_MaxUnderTheRim_...` (seeds 5 / 17 / 23, 1,500 ticks) stays
  under 1 m, so units seldom stand that close to a ramp's side, but nothing stops them.
- Possible fix: count a rim sample on another cell only if the edge it crosses is not a wall, e.g. compare the two
  cells' `CellCorners` heights on the shared edge (equal = joined), or walk centre to rim in a few steps as the QA
  reference does. A tighter `maxRise` (the ramp's own slope, not one level per cell) would also shrink it on generated maps.

## Fix (M4-V3 fix round 2, view track)
`TerrainHeight.MaxUnder` no longer infers a slope from the rise (`maxRise` and the plateau-to-plateau shortcut are gone):
a rim sample counts only if the straight line from the centre crosses every cell edge at a point where the two cells'
drawn surfaces meet (a step of at most 0.15 m; generated maps have a few ~0.1 m seams between slope cells). A cliff or a
ramp's side wall drops the sample. Regression: the QA row
`MaxUnder_BesideARampsSideWall_NeverHangsAboveTheWallFreeGround_OnGeneratedMaps` failed on all five seeds before (worst
2.16 m) and passes after (worst hang 0.10 m, at one of those seams for the 0.48 m radius, where the 64-step reference
reads the seam as a wall); the sink row stays at 0 sunk.
