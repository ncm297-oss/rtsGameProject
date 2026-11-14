# BUG-1381: M7-V4 building scale at the data limits: bar off screen at nearest zoom, hit box above a short box, a stale doc line

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-14-0616, task M7-V4 (QA) |
| System | building views (`BuildingViews`), building picking (`BuildingPicker` via `SelectionController`), docs/03 |
| Fixed by | |

Nothing here touches shipped data today: the largest shipped building scale is the Teblor's 1.25 (D31), and none is below 1.
These are what the allowed range (`DataLimits` 0.5-3.0) does once someone uses its ends.

## Repro
`& $env:GODOT --headless --path game res://tests/QaM7V4Test.tscn` (QA scene; forged data: Garrison Keep 4x4 at 3.0,
Barracks 3x3 at 0.5, Watchtower and Depot 1x1 at 0.5 / 3.0, Billet at 3.0). It passes, and prints the numbers below as
`note:` lines. Windowed with `-- --shots <dir> --placeholder-art` it saves `qa-keep3-bar-zoom20.png` and `qa-keep3-bar-zoom60.png`.

## Items
1. **Bar off the top of the screen at the nearest zoom for scale above about 2.4.** A damaged 3.0 Keep centred on screen at
   zoom 20 (`CameraLimits.MinZoom`): its hit-point bar (9.6 m) projects to screen y = -84 px and its drawn top to -52 px in a
   648 px view; the keep fills the screen and neither the bar nor the top is visible (screenshot `qa-keep3-bar-zoom20.png`).
   At zoom 60 the bar is on screen (y 237). The bar's centre leaves the screen at about 7.75 m over the ground at zoom 20 with
   the footprint centred, so at about scale 2.4 and up. The shipped 1.25 (bar 4.35 m) is on screen at both zooms (checked in
   the scene). Options when a big building lands: cap the bar's height, or accept it (the HUD shows the selected building's
   hit points).
2. **Scale below 1: the 3 m hit box reaches above the drawn box.** By the Producer's decision the hit test keeps the 3 m box.
   docs/03 "Implementation (M7-V4)" explains the taller case to the owner ("a tall building is clicked on its lower 3 m") but
   not the shorter one: at the default zoom, 437 pixels of empty air above a 0.5 Barracks (drawn 1.5 m tall) select it. With
   a unit or a building behind it, those pixels would select the short building instead (units pick first, so only the
   building case). Worth a sentence in the docs, or `PickRay` taking the drawn height, when anything ships below 1.
3. **A docs/03 sentence is no longer true at a non-1 scale.** "Remembered sites (BUG-0275 item 1)": "Picking matches the
   drawing: `BuildingPicker.PickGhostRay` has an ..." still describes a 0.45 m foundation pick box (`BoxHeight x
   SiteMinHeight`), while M7-V4 draws a remembered site at 15 % of the type's drawn height (1.35 m for a 3.0 type, 0.56 m for
   the Teblor's 1.25). The same section was edited for the drawing but not for the picking.

## Notes
- Not a finding, recorded so nobody chases it again: the developer's open sim question ("a Laborer spawned 6 m from a
  Barracks site's centre stood in `Building` for 4000 ticks", seed 1, forged row). Reproduced and ruled out with
  `game/tests/QaBuildReachRepro.tscn` (not in the scene loop, 48 matches): the stuck Laborers (5.6, 6 and 7 m south of the
  site at anchor 8360, seed 1) were dev-spawned **inside impassable cells** (a 2x2 blocked patch two rows south of the
  footprint); `SpawnUnit` (a dev / test command) places a unit at any position, and a unit inside a blocked cell can't path
  out, so `ConstructionSystem.Step` retries its walk every 20 ticks forever. Every Laborer spawned on passable ground, in all
  four directions at 2.6, 3.0 and 4.0 m from the edge, on seeds 1 and 6 and on both anchors, finishes the site (811-818
  ticks). No build-reach defect.
