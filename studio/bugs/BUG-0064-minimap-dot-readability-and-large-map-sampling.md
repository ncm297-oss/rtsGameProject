# BUG-0064: Minimap dots are hard to read against ramps and cliffs, and maps wider than 220 cells drop dots

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-05-2330, task M2-4 |
| System | HUD / minimap (view) |
| Fixed by | M2-H1 (3dc0568): 3 x 3-cell dots (owner cell plus a luma-chosen rim); `MinimapRasterTests` dot/rim/edge/contrast rows; docs/03 note for maps over 220 cells (no code change for big maps) |

These are small related readability findings, grouped into one bug. None of them affects the
acceptance criteria on the shipped 128 x 128 map.

## Repro
1. `& $env:GODOT --path game -- --units 100 --screenshot <png> --screenshot-after 3`, then crop
   the minimap (x 8-228, y 420-640) and enlarge it 4x with nearest filtering.
2. For the sampling issue, work it out on paper (the game can't launch a 256 map yet): the
   control is 220 px and the dot texture uses nearest filtering (`texture_filter = 1`), so a
   256-cell axis has 220 sampled texels.

## Expected
docs/02 "Minimap": "units as dots in player colors". A one-unit dot should be visible and
readable as a unit, and you should be able to tell it from terrain anywhere on the map.

## Actual
- **Whirlwind dots look like ramps.** Whirlwind's primary `#C8892E` (200,137,46) sits next to the
  ramp tint `RampColor` (204,153,87). On the minimap, ramps are 1-2 px orange-tan ticks around
  every plateau, the same size and nearly the same colour as a lone Whirlwind unit's dot.
- **Malazan dots vanish on cliffs and the border.** Malazan's primary `#4B4F55` (75,79,85) is
  close to the cliff tint (82,69,59) and the darkened border ring (86,76,55). A Malazan unit
  standing on a cliff-lip cell or next to the border reads as terrain.
- **Larger maps drop dots.** On a map wider than 220 cells, nearest filtering skips texels. On a
  256 map, 36 rows and 36 columns are never sampled, so about 26% of cells (1 - (220/256)^2) are
  never drawn, and a unit standing in one of them has no dot. The brief's QA focus mentions
  256 maps, and the generator allows up to 1024 (docs/03), but `Match` only runs 128 today.
- (Note, not worth fixing on its own) `MinimapTransform.TryToMap` compares against `MapSize`
  in float, so on a letterboxed map the exact far-border pixel can round about 1e-5 px outside
  and be rejected (for example a 48 x 160 px control with a 160 x 48 cell map: corner
  (48, 87.2) maps to y = 96.00003 > 96). A real click never lands exactly there.

## Notes
Possible fixes, for the developer to choose from: outline the dots or draw them 2x2 px; draw
ramps in a less saturated tint on the minimap; use a min-filter or one dot per screen pixel
(draw the dots in screen space) when the map has more cells than the control has pixels.
Faction colours come from data, so the dot style is the right place to fix it, not the palette.

## Re-check (2026-10-06-0905, M2-H1 commit 3dc0568): fixed
Verified, readability part: in windowed shots at 100 and 990 units per player (QA `game/tests/QaH1DotsShot.tscn`), lone units of both factions beside the border, beside a cliff lip, on a ramp, and on the top plateau are all visible as 5 x 5 px squares. Big maps: docs-only note, as the brief allowed (Match runs 128 only). Follow-ups: the dot now reads as its rim colour, not the player colour (BUG-0069), and the docs note's range is off (BUG-0070).
