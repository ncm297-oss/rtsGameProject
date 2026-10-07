# BUG-0084: M2-5 debug overlay nits: cliff tint reads olive, arrow tips dip into steep ramps, a build warning, a blind allocation probe

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-06-1255, task M2-5 |
| System | debug overlay (view), its tests |
| Fixed by | M2-H2 (f4e1b8d): crimson `CliffColor` (hue test over every level), `FlowArrowLayout.ArrowGround`, CS8602 fixed, allocation probe asserts a live relist (>100 arrows) |

## Repro
1. **Cliff tint.** `& $env:GODOT --path game res://tests/DebugOverlayShot.tscn -- --out <png> --units 100`.
   The cliff-lip cells around the green level-1 plateau (`CliffColor` 0.45, 0.02, 0.02 at alpha 0.60)
   blend with the green terrain to an olive brown, not the "dark red" in the brief and docs/03. They still
   stand out from open ground, but you can't tell they're red without knowing.
2. **Arrow tips under ramps.** A one-off measurement over generated maps for seeds 1 to 5 (every passable
   cell, all 8 directions, both arrow ends at +/- 0.65 m with `FlowArrowsView.Lift` = 0.3 m) found
   840 of 1,230,864 arrow-end samples below the terrain, the worst 9.0 cm. On a ramp cell the drawn
   arrow points along the slope, which is exactly this case. The `FlowArrowsView.Lift` summary says it
   "clears a ramp's slope under the arrow", which isn't true for the steepest ramp cells. (Max slope 0.577
   x 0.65 m = 0.375 m > 0.3 m.)
3. **Build warning.** `dotnet build RtsGame.sln` gives `game\tests\DebugOverlayTest.cs(173,133): warning CS8602:
   Dereference of a possibly null reference` (`_nav.Builder.Builds` inside the message string after `_nav.Builder!`).
4. **Blind allocation probe.** `ViewApiAllocationTests.DebugOverlay_NavRefill_Arrows_Counts_Ring_2000Units_AllocateZeroBytes`
   runs `grid.BumpVersionForTests` as its setup. After the bump `PeekCached` returns null, so the measured relist
   lists 0 arrows and never runs the per-cell loop. QA's
   `DebugOverlayQaTests.Relist_WithAField_Panning_AllocatesZeroBytes` now covers a relist with a live field (0 bytes),
   so this only needs a comment or a cheaper setup.

## Expected
Cliff cells read as dark red; arrows sit wholly above the surface, or the comment states the limit; a build
with no new warnings; probes that measure what they say they measure.

## Actual
As above. None of these affects gameplay or correctness.

## Notes
Possible fixes: raise the cliff alpha or saturation (or use the `BlockedColor` hue with a darker value);
lift arrows by `0.65 * MaxRampSlope + margin` (about 0.4 m), or tilt them to the cell plane like the nav
quads; add `!` or a local in the test's message.

## Re-check (2026-10-07-0800, M2-H2 commit f4e1b8d): fixed
Verified: `NavOverlayBuilderTests.CliffTint_*`, `FlowArrowLayoutTests.ArrowGround_*`, `ViewApiAllocationTests` green; `dotnet build` 0 warnings in `game/`.
