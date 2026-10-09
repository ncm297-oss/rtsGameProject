# BUG-0342: M4-V6a ability UI nits (cast bar hard to read early, Shift + click does not stay armed)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-09-1155, task M4-V6a |
| System | view: ability views, targeting |
| Fixed by | |

## Repro
1. Windowed: `& $env:GODOT --path game res://tests/AbilityViewTest.tscn -- --seed 1 --shots <dir>` and open
   `ability-seed1-casting.png`.
2. In a match, select two mages, press Q, then hold Shift and click twice.

## Expected
a) The cast bar reads as a progress bar at the default RTS zoom.
b) Common RTS convention is that Shift keeps the targeting armed, so you can queue several casts in a row.

## Actual
a) Two ticks into the cast, the bar is a thin dark line (1.2 m x 0.12 m). The violet fill is not visible at 1152 x 648,
   so the bar looks like an empty black dash over the mage.
b) `AttackMoveClick` disarms after every click, Shift included (`Targeting = false` before `AbilityOrder`). A second
   queued cast needs Q again. The brief only asks that Shift queue, so this is a design choice for the Producer or the
   owner.

## Notes
Also seen and not filed: the targeting rings are flat tori at a fixed lift, so they may sink into or float above hills
(the developer reported this). Cosmetic until the M6 art pass.
