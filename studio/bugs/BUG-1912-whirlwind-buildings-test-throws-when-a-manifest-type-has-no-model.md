# BUG-1912: WhirlwindBuildingsTest throws (NullReferenceException) when a manifest type is drawn as a box, skipping its later rows

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-27-0817, task M6-V31 |
| System | view tests: `game/tests/WhirlwindBuildingsTest.cs` (`ScreenRect`, `ModelPixels`) |
| Fixed by | |

## Repro
In a scratch clone (QA's: `<scratchpad>\m6v31-qa-clone`, imported), replace
`game/assets/models/whirlwind/whirlwind_archer_camp/whirlwind_archer_camp.glb` by its Git LFS pointer text (or delete it
and its `.import`), then `& $env:GODOT --headless --path game res://tests/WhirlwindBuildingsTest.tscn`.

## Expected
The scene fails with its FAIL lines for the missing model and still runs its other rows ((S), (F), (3)), so a broken file
shows every row it breaks.

## Actual
```
WARNING: Building model res://assets/models/whirlwind/whirlwind_archer_camp/whirlwind_archer_camp.glb is a Git LFS pointer ...: drawing the placeholder box.
WHIRLWIND BUILDINGS TEST FAIL: (M) whirlwind_archer_camp: no model
WHIRLWIND BUILDINGS TEST FAIL: whirlwind_archer_camp: not drawn as a model
WHIRLWIND BUILDINGS TEST FAIL: (T) whirlwind_archer_camp: not drawn as its model
WHIRLWIND BUILDINGS TEST FAIL: exception: System.NullReferenceException ...
   at Rts.Game.Tests.WhirlwindBuildingsTest.ScreenRect(Int32 slot) ... line 354
   at Rts.Game.Tests.WhirlwindBuildingsTest.ModelPixels(Int32 slot) ... line 333
   at Rts.Game.Tests.WhirlwindBuildingsTest.TerraceAndPickRows(Int32[] types) ... line 272
```
The (P) row calls `ModelPixels` for a type drawn as a box (`_views.ModelOf(slot)!` is null), and the exception ends the
scene: (S), (F) and (3) never run. The game itself is right (one warning, the box, no error line).

## Notes
Test robustness only; the scene is still red, so nothing ships green by it. Skip `ModelPixels` and the bar row when
`_views.ModelOf(slot)` is null, as the (T) row already does.
