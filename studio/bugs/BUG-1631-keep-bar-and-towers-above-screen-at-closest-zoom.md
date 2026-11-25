# BUG-1631: At the closest zoom a centred Garrison Keep's bar and tower tops are above the top of the screen

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed on level 0 and 1 (M6-VH9); level 2 open as BUG-1680 |
| Found | 2026-11-18-0926, task M6-V26 |
| System | building views (`BuildingViews` bar over `BuildingModels.Top`), camera limits |
| Fixed by | M6-VH9: `BuildingViews.BarCeiling` / `PlaceBar`; rows `ViewHardenVh9Test` (K), `QaM6V26Test` bar rows, `QaM6VH9Test` (T) |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V26Test.tscn`. The `KNOWN BUG-1631` row focuses the camera on the
   footprint centre of a finished Keep at zoom 20 (`CameraLimits.MinZoom`), 1600 x 900.
2. Windowed: `& $env:GODOT --path game --resolution 1600x900 res://tests/QaM6V26Test.tscn -- --shots <dir>` and look at
   `qa-selected-keep-20m.png`.

## Expected
The hit-point bar of a building the camera is centred on stays on screen at every zoom, as it did for every type's box at
scale 1.

## Actual
```
bar malazan_garrison_keep at zoom 20, focus on its centre: (552, -51) - (1048, -51) on a (1600, 900) screen   <- off the top
bar malazan_garrison_keep at zoom 60: y 338 (on screen); Barracks at 20 and 60: on screen
```
The model's top is 8.09 m and the bar sits 0.6 m above it (8.69 m). At 20 m zoom and 55 degrees pitch, that point
projects 51 px above the top edge when the camera is centred on the Keep. The tower tops are also cut off (the screenshot
shows the crenellations at the top edge). The player has to pan down to see a centred Keep's health at the closest zoom.

## Notes
- This is the same as BUG-1381 item 1 (a 3.0-scale box at zoom 20), which is still open. Now it happens with shipped
  content at scale 1: the 8.1 m Keep is the developer's Question 3 in docs/03 "Implementation (M6-V26)" (a height cap is
  one constant).
- Possible fixes: cap the fitted height (Question 3), or clamp the bar into view. Either way it is a taste call for the
  Producer and the owner.

## QA verification (2026-11-24-1431, M6-VH9)
The criterion's row: `(K) bar malazan_garrison_keep at zoom 20, focus on its centre (ground 4.00 m): 7.31 m (its look's 8.69), top-left 569 28, bottom-right 1028 45, on screen`; zoom 40 / 60 and the Barracks unchanged. QA's (T) row: on level 0 the bar is not lowered (8.69 m, on screen, visible); on level 1 lowered to 7.31 m (2 of 9 bar points behind the towers); on a level-2 terrace (seed 2) it is lowered to 3.31 m, inside the model and hidden: BUG-1680.
