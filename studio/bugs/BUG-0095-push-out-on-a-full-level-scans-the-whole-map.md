# BUG-0095: Push-out on a level with too few free cells scans every ring of the map per leftover unit (9 ms at 128, 35 ms at 256) and stacks them on one point

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-07-0800, task M3-H1 |
| System | construction push-out (`ConstructionSystem.PushOut`, BUG-0092 (c)) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningQaTests.PushOutOnAFullSmallPlateau_ScansTheMap_Report" --logger "console;verbosity=detailed"`
   (`QA/SimHardeningQaTests.cs`): a 6 x 6 level-1 plateau (13 free cells once a House stands on it) with a ramp,
   30 own units on the House's footprint, the House built there (`ConstructionSystem.StartBuild`, timed alone, Debug).

## Expected
Brief item 5 (c): "push-out past ring 8 keeps searching outward ring by ring until a free cell (never stacks two
pushed units)"; docs/03 "only a level with no free cell at all falls back to the nearest passable cell". Brief item 5
(d) and docs/03 "Cost" put a push-out at well under 1 ms.

## Actual
```
128 x 128: placed True; 30 units on a plateau with 13 free cells: Build apply 8.862 ms; most units on one cell 18
256 x 256: placed True; 30 units on a plateau with 13 free cells: Build apply 35.352 ms; most units on one cell 18
identical-position pairs: 153 after the push, 153 after 10 s; most units in one cell after 10 s 18
```
Once the level's free cells are taken, each remaining unit restarts at ring 1 and walks every ring up to
`max(Width, Height)` (the M3-H1 change replaced the 8-ring cap with the map size) before the fallback, so the cost is
(leftover units) x (map area): 17 leftovers cost one 9 ms tick at the default map size and 35 ms at the largest
supported one. All 17 then land on the exact same point (`FlowField.NearestPassable` of the anchor) and stay there:
idle units on one exact point never separate (pre-existing, the same happens to 18 units spawned on one point; docs/03
"two coincident units push apart by slot" covers walkers only).

## Notes
- Reachable in play: a player parks a group on a small plateau and drops a House on it.
- Cheap fixes: remember "this level has no free cell left" after the first unit that finds none (later units go
  straight to the fallback); or bound the rings by the level's extent. For the stacking: spread fallback units over the
  nearest passable cells by ring like the main search, or allow other levels for the fallback.
- No correctness problem found: every unit ends on passable ground, never in the footprint, nothing non-finite after
  200 ticks.
