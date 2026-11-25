# BUG-1680: On a level-2 terrace at the closest zoom, the bar ceiling lowers a Keep's bar into its own model (hidden)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-24-1431, task M6-VH9 (QA) |
| System | building views (`BuildingViews.BarCeiling` / `PlaceBar`, BUG-1631's fix) |
| Fixed by | |

## Repro
1. `dotnet build game/RtsGame.csproj`
2. `& $env:GODOT --headless --path game res://tests/QaM6VH9Test.tscn` and read the `(T)` rows and `KNOWN BUG-1680`
   (`-- --strict` fails on it).

The row finds, on seeds 1-16, a flat open anchor for a Garrison Keep on each terrace level (level 0 and 1 on seed 1,
level 2 on seed 2), spawns it, focuses the camera on its centre at zoom 20 / 30 / 40 / 60, and traces the camera ray to 9
points along the bar: a point is hidden when the Keep model's triangles are met first.

## Expected
BUG-1631 / criterion 7: with the camera centred on a Keep at the closest zoom, its hp bar is on screen (and, by the
intent, visible: the player can read the building's health).

## Actual
```
(T) Keep on level 0 (ground 0.00 m) at zoom 20: bar 8.69 m over its ground (look's 8.69), on screen, 0 of 9 points behind its own model
(T) Keep on level 1 (ground 4.00 m) at zoom 20: bar 7.31 m over its ground (look's 8.69), on screen, 2 of 9 points behind its own model
(T) Keep on level 2 (ground 8.00 m) at zoom 20: bar 3.31 m over its ground (look's 8.69), on screen, 9 of 9 points behind its own model
```
The camera sits at an absolute height (`RtsCamera`: Y = zoom), so `BarCeiling` subtracts the building's ground height:
on a level-2 terrace (8 m) at zoom 20 the ceiling is 3.31 m, inside the Keep's hall, and the depth-tested bar is drawn
inside the model: no point of it shows. Before the fix the bar was off the top of the screen there; now it is on screen but
hidden, so the player still cannot see it. Zoom 30 and up are unchanged on every level (bar 8.69 m, nothing hidden).

## Notes
- The same happens to BUG-1381 item 1, which the developer reports as fixed by the ceiling: `QaM7V4Test`'s forged 3.0 Keep
  (a 9 m box) at zoom 20 now prints `bars @20: HitPoints 0.5 at y 7.315 m, screen (493, 26), drawn top centre screen (576, -52)`:
  the bar is on screen, but 1.7 m under the box's top at the footprint's centre, so the box's front face is in front of it.
  The scene only checks the screen point when the bar is lowered, not that it is visible.
- The criterion's row (the developer's `ViewHardenVh9Test` K, a Keep on level 1) passes; on level 1 the lowered bar is 2 of 9
  points behind the 8.09 m towers (the developer's Producer question 2).
- Options: never lower a bar below the model's top's screen point less a margin (accept it off screen on high ground), draw
  the bar without depth test when lowered, or follow the focus ground in `BarCeiling` (the camera does not, so the
  building really is nearer to it on high ground).
