# BUG-0133: Push-out leftovers share a point once a cell takes more than 24 (offsets repeat); docs/03 says no two share a point

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-07-1715, task M3-H2 |
| System | construction push-out (`ConstructionSystem.PushOut`, `LeftoverOffset`) |
| Fixed by | M4-H1 (sim track): passes past 24 take turned directions at van der Corput radii (no repeat); `QA/PlateauSealMemoQaTests.FourHundredPushedOntoATinyPlateau_NoTwoOnOnePoint` un-skipped, the pin replaced by `OneHundredFiftyPushed_NoTwoOnOnePoint`; docs/03 push-out line |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PlateauSealMemoQaTests.Bug0133Pin"`.
2. The scene: a 10-cell level-1 plateau (5 x 5 block, ramp west), 400 own Laborers inside a House footprint at
   (22, 22); `StartBuild` places the House, 6 plateau cells are left.

## Expected
docs/03 "Implementation (M3-3)" push-out paragraph (M3-H2 text): leftovers spread "each pass set off the cell's center in
its own direction ... so no two units share a point". The dev's report says the same ("share cells (never points)").

## Actual
394 leftovers over 6 cells = 66 passes per cell. `LeftoverOffset(pass)` has 24 distinct values (8 directions x 3 radii,
`k / 8 % 3`), so passes k and k + 24 in one cell land on the same point: 356 identical-position pairs. With 150 units
(24 passes) there are 0, so the pin holds both sides. The `NearestPassable` fallback uses `LeftoverOffset(p + 1)` on
one cell for every unit, so it repeats after 24 units too.

## Notes
- Harmless in play so far: local movement separates them, no NaN or blocked-cell position in 200 ticks
  (`FourHundredPushedOntoATinyPlateau_AllStayOnIt_Evenly_FiniteAfterwards`). Needs 25 x (free plateau cells) units in one
  footprint, far outside play.
- Fix either the docs ("no two share a point up to 24 per cell") or the offset (e.g. a radius that keeps growing, or a
  spiral by pass index clamped inside the cell). Then flip the pin to the skipped
  `FourHundredPushedOntoATinyPlateau_NoTwoOnOnePoint`.
