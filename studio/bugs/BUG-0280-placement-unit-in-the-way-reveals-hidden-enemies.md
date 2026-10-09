# BUG-0280: Placement's "Units in the way" counts enemy units the player can't see, so the build ghost reveals them

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M4-4a) |
| Found | 2026-10-09-0125, task M4-V4 (reported by the developer as out of scope, confirmed by QA) |
| System | economy / building placement (`ConstructionSystem.Check`), shown by the view's build ghost |
| Fixed by | M4-4a: `ConstructionSystem.Check` skips enemy units `Fog.CanSeeUnit` hides for the `CanPlace` query; the Build apply path still counts them. Regression: `PlacementTests.HiddenEnemyUnit_IsNotInTheWayForCanPlace_ButStillRefusesTheBuild` |

## Repro
1. Start a match with fog on (`& $env:GODOT --path game`), seed 1.
2. Walk an enemy unit (or wait for one) into explored but not visible ground; it is hidden (M4-V4 hide rule).
3. Pick a building from the worker's build menu and hover the ghost over the hidden unit's spot.

Code path: `sim/Rts.Sim/Economy/ConstructionSystem.cs` `Check(...)`:
```csharp
for (int i = 0; i < u.Capacity; i++)
{
    if (!u.Alive[i] || i == worker || (u.Owner[i] == player && !u.IsPlanted(i))) continue;
    if (Inside(u.Position[i], x0, y0, def)) return PlacementError.UnitInTheWay;
}
```
Every live enemy unit counts, whatever `Fog.CanSeeUnit(player, i)` says.

## Expected
docs/02 "Vision and fog of war": explored ground shows terrain and last-seen buildings, not units. The player
should learn nothing about hidden enemies from the placement ghost. One fix: hidden enemy units don't refuse the
ghost (the Build may still fail when the worker arrives, as in other RTS games).

## Actual
The ghost turns red with "Units in the way" (`ui.json` `placement.unit_in_the_way`) exactly over a hidden enemy, so
sweeping the ghost across explored fog finds enemy units the screen hides. M4-V4 hid the units, dots and shots, so
this is now the main leak.

## Notes
- Not part of M4-V4's criteria (sim file, outside the view track). Once M4-3b's `PlacementError.Unexplored` lands,
  the same function is the natural place for the fog check.
- Hidden enemy *buildings* still refuse placement as `Blocked`. That's usually acceptable because footprints can't
  overlap, but the Producer may want the same decision there.
