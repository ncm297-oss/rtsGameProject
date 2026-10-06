# BUG-0077: A building placed in front of a moving column makes the units behind it give up after 20 ticks

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-06-1503, task M3-2 |
| System | movement (stuck / give-up rule) vs closing grid changes (buildings) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ABuildingDroppedOnAMarchingColumnsPath_NoUnitCenterEverEntersItsCells_AndTheColumnStillArrives"`
   (`QA/EconomyQaTests.cs`, skipped with this id; remove the `Skip` to run it). Control row with no
   building: `AColumnWithNoBuildingDropped_Arrives_ControlRow` (passes).
2. Setup: 48 x 24 flat map, 16 infantry in a 4 x 4 block at cells 3-6 x 8-11, all ordered to cell
   (42, 10). After 30 ticks (the column is at x 11-17 m, the building's site at x 40-48 m is empty) a
   `SpawnBuilding` puts a Keep at cells 20-23 x 8-11, straight across their path.

## Expected
The column routes round the new building (docs/03 "Implementation (M3-2)": to movement a building is
a wall like a mine; criterion 2's "a Move through the footprint routes round it") and arrives, as it
does when the building was there before the order.

## Actual
```
building on the path False: all Idle at tick 472; within 6 m of the goal 16/16
building on the path True:  all Idle at tick 484; within 6 m of the goal 8/16;
  the 8 units of the two middle rows stand Idle at x 14-20 m, near where they were when the building appeared
```
Trace of one of them (slot 5): the building lands at tick 64 (`NavGrid.Version` 0 -> 1); the field
is rebuilt the same tick and the unit keeps walking at full speed, but `StuckTicks` climbs 1, 2, ...
20 while `BestRemaining` stays at 72.3, and at tick 83 it gives up (Idle, goal cleared).

## Notes
- Cause (suspected): `BestRemaining` is the best remaining field cost the unit has seen. Until M3-2 the
  grid only ever *opened* (depletion), so a rebuilt field's costs could only fall. A building *closes*
  cells: the new field's cost from the unit's cell is the detour, higher than the old best, so every
  step "makes no progress" and the give-up rule fires after `StuckTicks` reaches its threshold. Resetting
  the progress mark when the field's version changes (or when the grid closes) would fix it.
- Today only the dev / test command `SpawnBuilding` closes cells, so no player can hit this yet; with
  construction (M3-3) it is a normal event (a player drops a House in front of their own army), and
  it would be S2 then. It belongs with M3-2b, which splits opening from closing grid changes
  (`BlockVersion`) anyway.
- Gathering workers recover by themselves (their loop walks again every 20 ticks), so this is about
  plain Moves.
- Safety holds: no unit's centre ever enters the building's cells
  (`ABuildingDroppedOnAMarchingColumnsPath_NoUnitCenterEverEntersItsCells`, passes).
