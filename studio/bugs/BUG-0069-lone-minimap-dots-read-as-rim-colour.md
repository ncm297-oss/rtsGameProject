# BUG-0069: A lone minimap dot reads as its rim colour (black or white), not its player colour

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-0905, task M2-H1 |
| System | HUD / minimap (view): `ViewApi/MinimapRaster` dot style |
| Fixed by | M2-H2 (f4e1b8d): `MinimapRaster` draws a 2 x 2 owner centre (`CentreOf`) in a one-cell rim (4 x 4); `MinimapRasterTests`, `ViewHardeningQaTests` reference renderer |

## Repro
1. `dotnet build RtsGame.sln`, then (windowed)
   `& $env:GODOT --path game res://tests/QaH1DotsShot.tscn -- --units 100 --out <dir>/qa100.png`.
   It adds seven lone units (both factions beside the border, beside a cliff lip, on a ramp, and
   a Malazan unit on the top plateau) and prints each one's screen pixel.
2. Read the 1x frame `<dir>/qa100.png` around those pixels (no enlargement). Lone Whirlwind dot on
   a ramp, at screen (36, 564), 9 x 9 px around it (`K` = rim 0x141414, `O` = owner #C8892E,
   `.` = terrain):
   ```
   ..KKKKK..
   ..KKKKK..
   ..KKOKK..
   ..KKKKK..
   ..KKKKK..
   ```
   Lone Malazan dot beside a cliff lip, at (83, 495) (`W` = rim 0xE6E6E6, `M` = owner #4B4F55):
   ```
   .WWWWW...
   .WWWWW...
   .WWMMW...
   .WWMMW...
   .WWWWW...
   ```

## Expected
docs/02 "Minimap": "units as dots in player colors". The M2-H1 brief asked for "2x2 px dots or
1 px dark outline" so a lone dot can be told apart from a ramp tick or a cliff lip. The fix
should still keep the dot's player colour readable.

## Actual
The dot is 3 x 3 map cells: one owner-coloured cell inside a one-cell rim. At the shipped
220 px / 128 cells (1.72 px per cell), that is a 5 x 5 px square with only 1 to 4 px of owner
colour, so at normal size a lone Whirlwind unit is a black dot and a lone Malazan unit is a
near-white dot. BUG-0064's problem is fixed: every lone dot I placed (7 worst-case cells, at 100
and 990 units per player) is clearly visible. But the colour the player sees is the rim, which
`RimFor` picks by luma alone:
- Every faction colour with Rec. 601 luma >= 0.35 gives a black dot, and every one below gives a
  near-white dot. docs/01 plans five factions, so two light factions' lone units will look
  the same.
- The near-white Malazan dots are close to the white camera outline. In the 1,000-unit shot,
  the Malazan army's light rim runs along and across the trapezoid, which makes the outline
  harder to follow (the developer flagged this as a matter of taste).

Inside crowds the owner colour still dominates, because all rims are drawn before any centre.
Empty cells inside a formation (for example around a cliff) do show rim-coloured lines through
the army, black through Whirlwind and white through Malazan, which can look like terrain edges.

## Notes
- Options for the developer or owner: a 2 x 2-cell owner centre with a one-cell rim (4 x 4
  cells, at least 3 px of owner colour per axis at 128 cells); a rim only on the outside of a
  crowd's footprint; or draw the dots in screen space (a fixed 3-4 px owner square with a 1 px
  outline), which also fixes the over-220-cell sampling note in docs/03.
- Not a blocker for M2-H1: the acceptance criteria ask for visibility, and that is met.
- The owner should judge the screenshots: `qa100-minimap.png` / `qa990-minimap.png` (4x crops)
  and the 1x frames that `QaH1DotsShot` writes.

## Re-check (2026-10-07-0800, M2-H2 commit f4e1b8d): fixed
Verified: `ViewH2QaTests.Dots_*` (property oracle at 100 / 990 units with border, ramp and cliff-lip units; four map corners) pass. Windowed `QaH1DotsShot` at 100 and 990 units: every lone dot (border, cliff lip, ramp, level 2, both factions) shows a 3 x 3 or 4 x 4 block of screen pixels in the exact owner colour (#4B4F55 / #C8892E) at 220 px / 128 cells. Perf rows pass alone (see BUG-0105 for the margin).
