# BUG-1681: A right click on an enemy seen behind an own Keep no longer targets it (the 8.1 m pick box covers the gaps)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-11-24-1431, task M6-VH9 (QA) |
| System | building picking (`BuildingPicker.PickRay` per-type heights) via `SelectionController.EnemyAt` / `ContextTarget` |
| Fixed by | |

## Repro
1. `dotnet build game/RtsGame.csproj`
2. `& $env:GODOT --headless --path game res://tests/QaM6VH9Test.tscn` and read the `(E)` rows and the `KNOWN BUG-1681`
   lines (add `-- --strict` to make them fail the scene).

The row spawns an own Garrison Keep and 21 enemy Heavy Infantry 1.5, 4 and 7 m behind it (north, away from the camera),
`--no-fog --no-combat`, focuses the camera on the Keep, and samples every 5th pixel over the Keep model's screen
rectangle. It keeps the pixels where the camera ray meets an enemy unit (`UnitPicker.PickRay`) **before** any of the Keep
model's triangles (`BuildingViews.ModelRayDistance`): pixels where the player sees the enemy, through the gaps between the
towers, over the hall roof or beside the towers. It calls `SelectionController.EnemyAt` (the right click's attack pick)
on each.

## Expected
The task's goal (M6-VH9 brief): "a click on what the player sees selects it". A right click on an enemy the player sees
is an Attack on it, as it was before this change wherever the old 3 m box was not in front.

## Actual
```
(E) zoom 20: 967 pixels show an enemy behind the own Keep (not hidden by its model); a right click there targets it at 0 (0%), means the own Keep / ground at 967; the old 3 m box left 869 of them clear (targeted before M6-VH9)
(E) zoom 40: 173 pixels ... targets it at 0 (0%) ...; the old 3 m box left 157 of them clear
(E) zoom 60:  70 pixels ... targets it at 0 (0%) ...; the old 3 m box left 62 of them clear
```
`EnemyAt` (and `ContextTarget`, which is the same ray order) now tests every building as a full-footprint box up to its
drawn top: 8.09 m for the Keep. The Keep's model is three towers at the back and a hall at the front, so most of that box is
air; an enemy seen in it is behind the box's front face, `buildingT < unitT`, and `UnitPicker.ResolveEnemy` returns the
own Keep (no target: the right click becomes a Move / the Keep's context order). Before M6-VH9, 90 % of these pixels
(869 of 967 at zoom 20) targeted the enemy. Enemies attacking a base gather round its hall, so this lands in base defence.

The left click got a fix for the same box (`HiddenByBuilding` tests the model's triangles before dropping an own unit), but
the right click's enemy pick, the context order's resource-node pick and `PickBuilding` for the ground behind the Keep do
not. The developer measured the box's air (`ViewHardenVh9Test` P3: 99.9 % of the off-model pixels in the Keep's pick box
select the Keep) and asked the Producer to accept it (docs/03 "Implementation (M6-VH9)" question 1), but the report does not
say that a seen enemy behind the Keep can no longer be right-clicked.

## Notes
- A fix in the same spirit as `HiddenByBuilding`: when the box is met first but the unit (or node) is met before the
  model's triangles (`ModelRayDistance`), the unit wins. Or a per-type hit shape (the tighter fix the developer proposes).
- Box types are not affected (their box is what is drawn). The Barracks model (3.49 m, narrower than its footprint) has
  the same air at a smaller scale.
