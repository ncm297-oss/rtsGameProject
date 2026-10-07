# BUG-0107: M3-V1 nits: Malazan site vs finished colour, tiny wood cube at 60 m, minimap resource redraw on every building change

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-07-0925, task M3-V1 |
| System | building views, worker feedback, minimap (view) |
| Fixed by | |

## Repro
1. Windowed: `& $env:GODOT --path game res://tests/QaV1Test.tscn -- --shots <dir> --size 1280x720` (and `--size max`),
   and `res://tests/EconomyViewTest.tscn -- --shots <dir>` (`economy-site.png`).
2. Item 3: clone the branch, drop `"--no-bases"` from `game/tests/PropsViewTest.cs` / `QaM23bTest.cs`, and run them.

## Expected / Actual
1. **Site vs finished building for player 0.** Malazan's `palette.primary` is `#4B4F55` (dark slate) and the site
   colour is slate `(0.42, 0.45, 0.50)`: the same hue, a bit lighter. In `economy-site.png` the site still reads as
   a site, but only through its lower height and the yellow bar. At full progress (100% height, bar about to vanish)
   a Malazan site and a finished Malazan building differ only by lightness. The Whirlwind side does not have this
   problem.
2. **Wood cargo at 60 m zoom.** On a 1280 x 720 window the 0.35 m cube is about 3 to 4 px. The gold cubes read
   clearly; the wood-brown ones on green-tinted workers beside dark trees are barely visible. At 30 m both are clear.
3. **Minimap resource layer redrawn on a building change.** The resource layer (and the props relist) rebuild on
   `NavGrid.Version`, which a building spawn, site or cancel also bumps. Without `--no-bases`, `PropsViewTest` reports
   "minimap resource layer drawn 2 times, expected 1" and `QaM23bTest` "resource layer redrawn 2 times with no change".
   Each extra redraw is cheap (BUG-0105: about 0.3 ms), but M3-V2's placement will trigger one per Build, Cancel and
   completion.

## Notes
Item 1: give sites a different hue (e.g. a light sand or scaffold tone), or a wireframe or striped look. Item 2: scale
the cube with zoom, or give it a darker outline. Item 3: key the resource layer on a resource-only change (e.g.
`ResourceStore` count or generation sum, or a resource version), not on the whole grid version. That is pre-existing
design, but it now fires every match.
