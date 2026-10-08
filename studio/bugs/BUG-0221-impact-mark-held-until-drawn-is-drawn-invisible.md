# BUG-0221: An impact mark held "until drawn once" is first drawn at age 1, fully transparent, when a frame runs more ticks than its life (13 % of marks at 8x / 30 fps)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-08-1435, task M4-V3 |
| System | view: `sim/Rts.Sim/ViewApi/ImpactMarks.cs` (`Age`), `game/scripts/ProjectileViews.cs` (`SyncMarks`) |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaV7Test.tscn` (QA scene): 500 v 500 archer lines, five ticks per
   frame (8x at 30 fps, the developer's own "slow machine" setting in `ProjectileViewTest`). Prints
   `5233 marks first drawn, 694 of them fully transparent on that frame (BUG-0221)`.
2. xUnit (skipped until fixed): `ProjectileViewQaTests.ImpactMark_HeldUntilDrawn_IsDrawnBeforeTheEndOfItsLife`: a flash
   collected at tick 10 and first drawn at tick 15 has `Age` 1.

## Expected
docs/03 "Implementation (M4-V3)": "a mark expires only after a frame has drawn it, so a landing at 8x always shows at
least once" (also the `ImpactMarks` class summary). Criterion 2: marks "appear on the frame after a tick with
`Impacts.Count > 0`".

## Actual
The hold only delays expiry. `Age(slot, tick, alpha)` is measured from the tick the mark was added, so a mark first drawn
`life` or more ticks later is at age 1; `SyncMarks` draws it at full size with alpha `0.85 x (1 - age)` = 0. A Flash
(4 ticks) from the first tick of a 5-tick frame is never visible. `ProjectileViewTest` checks `Drawn` and the transform,
not the colour, so it passes; the headless renderer keeps no instance colours, so the scene can't read them back either
(the QA scene computes the alpha from `Age`).

## Notes
A fix could age a mark from the first frame that draws it (e.g. record the tick of the first draw, or clamp the start
to `tick - 1` when first drawn) so every landing gets a visible first frame. 60 fps at 8x (about 2.7 ticks a frame)
mostly avoids it; 30 fps or lower, or a hitch, does not.
