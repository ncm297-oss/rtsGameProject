# BUG-1682: A left click on an enemy building drawn in front of an own building selects the own building behind it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-24-1431, task M6-VH9 re-check round 1 (QA) |
| System | building picking for the left click (`SelectionController.PickBuilding`, own buildings only) |
| Fixed by | |

## Repro
1. `dotnet build game/RtsGame.csproj`
2. `& $env:GODOT --headless --path game res://tests/QaM6VH9FixTest.tscn` and read the `(O)` rows' last count,
   "N select an own building behind a drawn enemy building".

The row builds seeded random layouts of own and enemy Keeps, Barracks, a Watchtower and a Teblor Longhouse and samples the
screen every 10 px. For each pixel it finds the nearest drawn thing slot by slot and left-clicks there.

## Expected
The M6-VH9 goal: a click on what the player sees means that thing. Where an enemy building is the nearest thing drawn, a left
click should not select a different building hidden behind it. It should select nothing (enemy buildings are not selectable
today) or, later, show the enemy building.

## Actual
```
(O) seed 1 zoom 20: ... left click 14084 / 0 (...; 274 select an own building behind a drawn enemy building)
(O) seed 1 zoom 40: ... 86 select an own building behind a drawn enemy building
(O) seed 2 zoom 20: ... 124 ...    (O) seed 4 zoom 40: ... 82 ...
```
`PickBuilding` walks only the local player's buildings (`BuildingOnRay(world, null, LocalPlayer, ...)`), so an enemy
building in front is invisible to it, and the own building behind is selected. The same thing happens through an enemy unit:
the left click ignores enemy units. This was already true before M6-VH9 (the old `PickRay` had the same owner filter), so it
is not a regression. The oracle counts these pixels as what the code does today (they pass) and reports them. They only show
up when an enemy building stands directly in front of an own one (base fights, or a layout like this one).

## Notes
- A possible fix is the BUG-1630 pattern: after `PickBuilding`, drop the own building when `BuildingOnRay(world, fog, -1, ...)`
  gives a different building nearer on the ray.
- A small nit in the same code: `FinishSelect` calls `PickResource(release)` and then `PickBuilding(release)`, which calls
  `PickResource` again. That is one extra ray walk per click, no behaviour change.
