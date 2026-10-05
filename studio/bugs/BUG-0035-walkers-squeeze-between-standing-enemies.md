# BUG-0035: Walkers squeeze between two standing enemy units (0.6 m deep); 40% of a crowd walks through a 3-unit enemy plug

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-05-1234, task M1-5 |
| System | movement (MovementSystem.Constrain / WalkStep) |
| Fixed by | |

## Repro
1. Remove the `Skip` from `QA/QueuedGiveUpQaTests.CrowdAtAThreeCellGapPluggedByEnemies_NobodyPassesThrough` (5 rows) and run
   `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~CrowdAtAThreeCellGapPluggedByEnemies`.
2. Setup: 48 x 24 flat map, cliff column at x = 20 open only at rows 10-12 (a 3-cell gap, a ramp's
   width). Three player-1 units of radius 0.9 (catapult) stand Idle at the centers of the three gap
   cells (2 m apart: slits of 0.2 m between them). 60 player-0 units of every type spawn west of the
   gap and are ordered to a point east of it.

## Expected
docs/03 "Local movement": standing units that aren't groupmates are walls; `Constrain` removes the
part of a step that would go deeper into one ("an existing overlap is never pushed out here, only
not deepened"); enemies are never shoved. No unit (narrowest is 0.8 m wide) fits through a 0.2 m
slit, so every walker stops at the plug and gives up. Nobody ends east of x = 42 m.

## Actual
The enemies never move (checked: end positions equal start positions), yet per seed 20-27 of the
60 walkers end east of the plug (seeds 1-5: 25, 20, 27, 27, 23 at HEAD a3d59d2). A diagnostic probe
tracked each passer's deepest overlap with an enemy: 0.60-0.65 m of a 1.3 m radii sum, for radius-0.4
units and one radius-0.7 lancer; the deepest overlap of any friendly with an enemy was 0.79 m (seed 1)
and 1.17 m (seed 3).

Under the pre-M1-5 give-up rule (queued rule reverted) the same rows let 21 / 1 / 7 / 10 / 13 through:
the leak is older than M1-5, but the queued rule keeps blocked walkers trying for longer, so about
2.3x as many squeeze through (122 vs 52 over 5 seeds).

A 1-cell gap plugged by one radius-0.9 enemy holds (0 through, all 60 give up; pinned by
`CrowdAtAOneCellGapPluggedByAnEnemy_AllGiveUp_InBoundedTime`), so the leak needs two standing units
side by side.

## Notes
Matches the developer's out-of-scope finding (a) in the M1-5 report: when a unit is squeezed between
two standing units, `Constrain` limits the step against each in turn, and the slide along the second
pushes it into the first (about 0.5 m per squeeze). Repeated over ticks the unit works its way through.
The developer tried a fix and reverted it because it broke 3 crowd tests.

Why S2: it breaks the documented "standing units are walls" rule for enemies, and from M4 a line of
units holding a ramp (docs/02: ramps are the chokepoints) is a core tactic; walking through it is a
wrong game rule. It does not affect the M1-5 acceptance criteria (one owner, no plug).
