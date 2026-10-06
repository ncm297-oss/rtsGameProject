# BUG-0045: Shoves press friendly units into, and through, a standing enemy that plugs a corridor

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-05-1609, task M1-4d-3 (pre-existing on base 7f741f1; more seeds pass the plug on M1-4d-3) |
| System | movement (shoves: `SqueezeLimit` / `KeepOffWalls` / chain shove vs other players' units) |
| Fixed by | |

## Repro
1. Remove the `Skip` from `QA/CrowdRoutingQaTests.CorridorPluggedByAnEnemy_FriendlyLinesAhead_ChainShovesNeverSqueezeAnyonePast`
   and run `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CorridorPluggedByAnEnemy"`.
2. Scenario: a 1-cell (2 m) corridor plugged at its middle by one Idle radius-0.9 unit of player 1. On
   player 0's side, ahead of the plug: two goal-less Idle units and a pair parked on one point. A crowd
   of 6-16 player-0 units in the room behind is ordered to the far room every 100 ticks for 1,000 ticks.

## Expected
An Idle enemy is a hard wall (docs/03 M1-5): nobody of player 0 gets past it, and nobody is shoved
deeper into it than the shove pack limit (`ShoveSpacing` = 0.5 x the radii's sum).

## Actual
| Seed, crowd | M1-4d-3 | Base 7f741f1 |
| --- | --- | --- |
| 1, 6 | pressed to 0.433 x the radii's sum, nobody through | pressed to 0.096 x, nobody through |
| 2, 10 | tick 664: unit 11 (Moving, r 0.7) through, at (34.03, 7.77), plug at (33, 7) | pressed to 0.057 x (nearly coincident), nobody through |
| 3, 16 | tick 666: unit 0 (Idle, goal-less, r 0.4) shoved through to (33.94, 6.43) | tick 969: unit 6 (Moving, r 0.4) through |

The per-tick walker check (a walker never steps deeper into an Idle enemy) holds throughout: the
walkers that get through had first been shoved deep into the plug while Idle (given up), then were
re-ordered and walked out the far side (leaving an overlap is allowed). In open-field scenarios the
same shoves push given-up units 0.15-0.32 m deeper into standing enemies in one tick (ring of enemies,
two armies through one gap, two-player crowd), on base and M1-4d-3 alike.

## Notes
- Suspected cause: `ApplyShoves` trims with `SqueezeLimit` first and `KeepOffWalls` / `KeepLinks` after.
  In a corridor `KeepOffWalls` removes the step's sideways part, which can raise the part along the
  enemy's normal back above the room `SqueezeLimit` left, a centimeter or so per tick. The M1-4d-3
  chain shove skips members touching an enemy, but not the first shoved unit.
- `HardWallQaTests.PluggedGap_*` doesn't catch it: walkers only, no goal-less units ahead of the plug,
  no re-orders.
- The brief's QA focus: "enemy plugs still let nobody through (including against chain shoves)".

## Re-check round 1 (2026-10-05-1609, fix commit 6abd200): fixed in 1-cell passages only
Verified fixed where the round-1 rules apply (a plug standing in a cell walled on two opposite sides):
the original repro (3 seeds) and two more seeds pass; closest approach of a friendly to the plug
0.85-0.90 x the radii's sum (base: 0.06-0.10, first M1-4d-3 version 0.43).
Still open elsewhere: new QA test `CorridorOfWidthPluggedByEnemies_FriendlyLinesAhead_NobodyThrough`, a
2- or 3-cell-wide corridor plugged by one wide enemy per row (0.2 m slits): units get through on 4 of 4
seeds (ticks 264-365; e.g. a radius-0.4 walker at (33.9, 10.6) past plugs at x 33), on base too (all 4,
plus a friendly pressed to 0.056 x the radii's sum). The walker per-tick check stays green: they are
shoved in while Idle (given up) and walk out the far side. Rows skipped under this bug.
