# BUG-1681: A right click on an enemy seen behind an own Keep no longer targets it (the 8.1 m pick box covers the gaps)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (verified by QA re-check round 1, 2026-11-24-1431) |
| Found | 2026-11-24-1431, task M6-VH9 (QA) |
| System | building picking (`BuildingPicker.PickRay` per-type heights) via `SelectionController.EnemyAt` / `ContextTarget` |
| Fixed by | M6-VH9 fix round 1 (`5942d4a`: `SelectionController.BuildingOnRay` / `GhostOnRay`, drawn-first ray walk); regression: `QaM6VH9Test` (E) plain check, `QaM6VH9FixTest` (O) / (GH) oracle, `ViewHardenVh9Test` (E) |

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

## Re-check round 1 (QA, 2026-11-24-1431)
Verified fixed. `QaM6VH9Test` (E), now a plain check with a stricter second check added by QA (every seen pixel with no tree
drawn in front of the enemy must be an Attack on it):
```
(E) zoom 20: 967 pixels show an enemy ...; targets it at 880 (91%), no Attack at 87 (87 of the 967 have a tree drawn in front of the enemy); 0 seen with no tree in front and not targeted; the old 3 m box left 869
(E) zoom 40: 173 ... targets it at 166 (96%), no Attack at 7 (7 ... tree in front); 0 seen with no tree in front and not targeted
(E) zoom 60:  70 ... targets it at 69 (98.6%), no Attack at 1 (1 ... tree in front); 0 seen with no tree in front and not targeted
```
The developer's note holds: every pixel not targeted has a tree drawn in front of the enemy (a click there means the tree).
QA's independent oracle (`QaM6VH9FixTest` (O), 4 seeded random layouts of own and enemy Keeps, Barracks, a Watchtower, a
Teblor Longhouse, own and enemy units and the map's trees, zoom 20 / 40 / 60, 14,400 pixels each, nearest drawn thing found
slot by slot) agrees with `EnemyAt`, `ContextTarget` and the left click on every pixel (172,799 / 172,799; 1 near-tie
skipped); with fog and remembered buildings (GH) 43,200 / 43,200. The picks allocate 0 bytes (4 x 300 calls, with and
without ghosts).
