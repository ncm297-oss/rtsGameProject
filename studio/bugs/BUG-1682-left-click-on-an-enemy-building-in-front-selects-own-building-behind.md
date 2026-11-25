# BUG-1682: A left click on an enemy building drawn in front of an own building selects the own building behind it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (M6-V27, verified by QA 2026-11-25-0924) |
| Found | 2026-11-24-1431, task M6-VH9 re-check round 1 (QA) |
| System | building picking for the left click (`SelectionController.PickBuilding`, own buildings only) |
| Fixed by | M6-V27 (view, 2026-11-25-0924) |

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

## Fix (M6-V27)
`SelectionController.PickBuilding` (through its new private core `OwnBuildingAt`) drops the own building when another
player's building (`BuildingOnRay` over every owner, fog-filtered) or a remembered one (`GhostOnRay`) is drawn in front of
it on the ray; a building met only in its model's air is in front of nothing. Enemy units in front: unchanged (out of scope).
`QaM6VH9FixTest`'s (O) oracle now wants nothing selected there and counts the own buildings still selected:

```
(O) seed 1 zoom 20: ... left click 14084 / 0 (316 skipped near own units; 0 select an own building behind a drawn enemy building, of 274 with one there)
(O) seed 1 zoom 40: ... 0 ..., of 86      (O) seed 1 zoom 60: ... 0 ..., of 32
(O) seed 2 zoom 20: ... 0 ..., of 124     (O) seed 2 zoom 40: ... 0 ..., of 14     (O) seed 2 zoom 60: ... 0 ..., of 3
(O) seed 3: 0 of 0 at every zoom          (O) seed 4 zoom 40: ... 0 ..., of 82     (O) seed 4 zoom 60: ... 0 ..., of 23
```
Every other count of the twelve rows is the base's (pixels, first drawn, EnemyAt, ContextTarget, left click ok / skipped).
The nit in the notes is fixed too: `FinishSelect` walks `PickResource` once a click (`OrderNoticeTest` row P counts it;
the old path gave 2 on every own-building click).

## QA verification (2026-11-25-0924, M6-V27)
- `QaM6VH9FixTest` (O) on new layout seeds 5-12 (`-- --seeds 5 12`, a QA switch; the default run is unchanged), zoom 20 /
  40 / 60: 0 wrong for `EnemyAt`, `ContextTarget` and the left click on all 24 rows, and 0 own buildings selected behind a
  drawn enemy building on every row (see the QA report for the per-row "of N").
- `game/tests/QaM6V27Test` with fog on, an independent per-slot oracle: (CL) a live enemy Barracks against an own House's
  south side, (CS) the same as a 40 % site, (CG) an enemy Keep on a higher terrace remembered as a ghost in front of an own
  House on low ground (the `GhostOnRay` path the developer's rows did not reach): every front pixel selects nothing and
  `EnemyAt` gives the enemy building / ghost; every own-first pixel selects the own House; a real right click on the front
  building / ghost with a soldier selected sends one Attack on it.
- The developer's edit of QA's (O) oracle was judged honest: the oracle encodes the new rule, the `leftBad == 0` check is
  unchanged, and the counter now counts what the code selects (with the "of N" denominator) instead of the oracle's own
  expectation.
