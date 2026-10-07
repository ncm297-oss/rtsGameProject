# BUG-0108: A right click on the visible top of a building's box picks the ground behind it: half of a damaged hall's top gives a Move, not a Repair

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-07-1131, task M3-V2 |
| System | right-click context order (SelectionController.CommandAt / ContextOrder), view |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaV2Test.tscn` (row `RightClickOnBoxTop`; add `-- --strict` to make
   it fail). Seed 1, 3 workers selected, own Town Hall damaged to 3/4 hp, camera at 30 m zoom on the hall.
2. The row samples a 9 x 9 grid on the hall box's top face and keeps the 81 pixels whose camera ray meets the hall's
   box first (`SelectionController.PickBuilding`, the same pick a left click uses).

## Expected
docs/02 "Controls and camera": right-click is the context command (repair on an own damaged building). Criterion 6:
a right-click on an own damaged finished building with workers selected sends Repairs. The player clicks the box
they see.

## Actual
```
box top of the damaged hall: 81 sampled pixels on the box, 42 Repair, 39 Move;
far-edge right-click sent [Move u4 t0 (104,129.2), Move u11 t0 (104,129.2), Move u18 t0 (104,129.2)]
```
`CommandAt` ground-picks the click (`GroundPicker.TryPick`) and `WorkAt` looks up the building by the ground cell
(`BuildingPicker.SlotAt`). A ray through the top of a 3 m box at the 55 degree pitch reaches the ground about
3 / tan 55 = 2.1 m beyond the point on the top, so the far 2.1 m of every box top lands behind the footprint: about half
of a 4 x 4 hall's top, and almost all of a 2 x 2 house's. The workers walk to the ground behind the building. If a
tree, mine or another own building stands there, they get that target's order instead (Gather, or Repair / join of
the wrong building).

## Notes
The brief named `BuildingPicker.SlotAt(buildings, cell)` for this lookup, and the developer followed it. That's why
this is S3 and not a missed criterion. The developer's own `CommandCardTest.GroundScreen` aims at a pixel whose ground
pick is inside the footprint, so it can't see the problem. Fix idea: in `CommandAt`, call `PickBuilding`-style
`BuildingPicker.PickRay` (any owner) first and use the hit building's footprint centre as the context point; fall back
to the ground pick. The same gap exists for resource nodes (trees and mines have height too; M3-V1's Gather uses the
ground cell). When fixed, turn the `Known("BUG-0108", ...)` row in `QaV2Test.cs` into a plain `Check`.
