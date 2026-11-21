# BUG-1630: A click on the Garrison Keep's towers misses it (up to 19 % of its pixels) and selects an own unit hidden behind it

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-18-0926, task M6-V26 |
| System | building picking (`BuildingPicker.PickRay` via `SelectionController`), building views (`BuildingModels`) |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V26Test.tscn` (add `-- --strict` to make the rows fail).
2. Read the `KNOWN BUG-1630` lines.

The rows spawn a finished Keep and a Barracks alone, focus the camera on each at zoom 20, 40 and 60, sample every 6th
pixel over the model's screen rectangle, keep the pixels whose camera ray meets the fitted model's triangles, and click
each one through `SelectionController._UnhandledInput`. A last row puts an own Laborer 1.2 m behind the Keep's back edge
(north, away from the camera), checks the Keep's model is in front of it on that ray, and clicks there.

## Expected
A click on what the player sees selects it: every pixel of the Keep model selects the Keep (criterion 5 says "as today",
and today the drawn box and the hit box were the same 3 m). SelectionController's remarks (M6-V8): "an own unit or building
in front hides what is behind it, as the player sees it".

## Actual
```
pick malazan_barracks at zoom 20/40/60: 100 % of sampled model pixels select it
pick malazan_garrison_keep at zoom 20: 3172 of 3933 sampled model pixels select it (80.7%)
pick malazan_garrison_keep at zoom 40: 636 of 681 sampled model pixels select it (93.4%)
pick malazan_garrison_keep at zoom 60: 254 of 265 sampled model pixels select it (95.8%)
hidden unit: a click on the Keep's tower where an own Laborer stands hidden behind it (model at 42.3 m, unit at 51.3 m) selected the hidden Laborer
```
The Keep model is 8.09 m tall, but picking still tests a 3 m box (`BuildingViews.BoxHeight`). The towers are at the back of
the footprint, so a ray through their upper part passes over the 3 m box and lands on the ground behind it. That ground
holds whatever is behind the Keep: nothing (the click clears the selection), or a unit the player can't see, which is then
selected. A base's units collect around the Keep, so this will happen in play.

## Notes
- The developer flagged this in docs/03 "Implementation (M6-V26)", Question 4, and suggests that a later task give `PickRay` each
  type's drawn height. BUG-1381 item 2 is the same 3 m box in the other direction (scale below 1).
- The fix is in the view API: pass the drawn top (`BuildingModels.Top(type)` for a model type, `DrawnHeight` for a box) to
  `BuildingPicker.PickRay` / `PickGhostRay`. A tighter fix would be a per-type hit shape, but the box at the model's top is
  probably enough. When it is fixed, make the KNOWN rows in `QaM6V26Test` plain checks.
- The Barracks (3.49 m tall, 3.9 m wide on a 6 m footprint) loses no pixel, because its hit box is wider than the model.
