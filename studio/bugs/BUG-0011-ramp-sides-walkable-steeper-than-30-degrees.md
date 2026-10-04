# BUG-0011: Ramp sides are walkable: a unit can step 1.6-3.2 m sideways off a ramp (39-58 degrees)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-03-1235, task M1-3 |
| System | terrain / nav grid |
| Fixed by | dd63cca (M1-4a) |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.MapQaTests.RampSides_NoPassableStepSteeperThan30Degrees`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~RampSides_NoPassableStep"`

The test walks every pair of passable 4-neighbours on the seed-1 default map and checks that the
height change over one 2 m cell is at most `MaxRampSlope * CellSize` (1.155 m).

## Expected
docs/02 "Map and terrain": "slopes steeper than 30 deg are impassable, so plateau edges act as cliffs
and ramps as chokepoints". A passable step between neighbouring cells should never exceed 30 deg.

## Actual
```
(83,8)->(84,8) rise 1.6 m
(83,9)->(84,9) rise 2.4 m
(83,10)->(84,10) rise 3.2 m
(107,12)->(107,13) rise 3.2 m
...
```
Ramp cells keep the lower level and the cell beside a ramp is flat ground on that same level, so
`NavGrid` marks both passable and criterion 3 (|dLevel| <= 1, dLevel = 1 only via a ramp) is met
literally. But the top ramp cell sits 3.2 m above its side neighbour: a unit can walk onto the
upper half of the ramp from the side, so the ramp is a cliff-free wedge rather than a corridor.

## Notes
The developer flagged this as a known deviation ("ramp sides are walkable sideways"). The chokepoint
at the mouth is still `RampWidth` wide, so gameplay impact is small today; it will show once
rendering draws a 3.2 m drop that units walk down, and M4 flow fields / steering will happily path
across it. Options: mark ramp side cells (or ramp cells whose side neighbour is more than one slope
step lower) as blocked/cliff; or let the Producer accept it and amend docs/02. Producer's call.

**Producer triage (2026-10-03-1235):** docs/02's 30° rule stands (ramps must be corridors, that
is the chokepoint pillar). Fix per-cell, consistent with the cliffs-as-cells decision: the
lower-level cells flanking a ramp along its length become `Cliff | Blocked` ("ramp walls"); the
generator already keeps that one-cell ring flat and lower. Must keep the connectivity and >= 50%
passable invariants (`MapAssert`, `MapQaChecker`). Scheduled: first commit of the next task
(M1-4a), with BUG-0012/0013. Does not block.

**QA verification (2026-10-03-2220, M1-4a):** `RampSides_NoPassableStepSteeperThan30Degrees` is
un-skipped and green. The new `QA.RampWallQaTests.Sweep_NoSteepPassableStep_InvariantsHold` checks
every passable 4-neighbour pair (<= tan 30 x 2 m) and every open diagonal pair (<= tan 30 x 2.83 m).
It also runs `MapQaChecker` (connectivity, >= 50% passable, diagonal level leaks). It covered 1,740
seeds across 7 param sets: default, 32x32, RampWidth 1, RampWidth 6 x length 9, dense ramps,
160x48, and 256x256 crowded. All clean. A 1,000-seed scan of 3 sets found no blocked ramp cell and
no seed missing a passable level, so the walls don't seal ramps off.
