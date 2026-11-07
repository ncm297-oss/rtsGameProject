# BUG-1107: The hunt's sweep never sees a unit standing at the map's edge or corner, so a hidden last unit keeps the match alive forever

| Field | Value |
| --- | --- |
| Severity | S2 (the feature's purpose, docs/02 "The AI finishes the match ... a beaten side's last unit hiding ... is found", fails for any unit near the border) |
| Status | open |
| Found | 2026-11-06-0926, task M5-4d (QA full) |
| System | AI military manager, the hunt's sweep (`MilitaryManager.HuntPoint` / `NextSweep`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HuntQaTests.AHuntFindsALastUnitAtTheMapEdge"` (fast, flat 64-cell map, combat off):
   rows (1, 1), (62, 62), (32, 1) fail: the unit is never seen in 20,000 ticks (80 sweep targets, every point visited 6 times over);
   the centre control row and the row the army starts beside pass.
2. Real matches: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HuntStressTests.AHumanSeatRazedToHidingWorkers_IsHuntedDown"`
   (a human seat that never plays sends its 5 workers to the 4 corners and the centre at tick 1; a Normal AI of either faction, a seed
   per opening, on generated / Raraku / Pale Hills / Vathar Crossing).

## Expected
docs/02 (M5-4d bullet): with no enemy building known the AI sweeps the map and "a beaten side's last unit hiding in the woods is found".
docs/03 "Implementation (M5-4d)": "with the interior corners ... at most half a cell (25.6 m)" from a sweep point.

## Actual
The sweep points are the scout grid's cell centres and its *interior* corners. Nothing lies on or near the map border: the outer band is
half a cell (25.6 m on a 256 m map / Pale Hills' 512 m map at `scoutGrid` 56) from the nearest point along an edge and half a cell's
diagonal (36 m) at a corner. A unit's sight is 14 m and a point is "reached" as soon as one member is within `rallyReach` (6 m), so a
unit standing within roughly 8-12 m of the border is never seen. The docs' "at most 25.6 m" is true only inside the interior.

Raraku (end state after 72,000 ticks, AI seat 0, `standard`, seed 1): hall razed 7:53, hunt from 7:54, still hunting at 60:00 (1 hunt,
15 / 41 points visited this pass, 58 army units); the 4 corner workers alive at (4.7, 4.7), (252, 4), (4.8, 252.2), (252, 252), each
29.6-30.5 m from the nearest sweep point. The centre worker was killed. Every row of the 24-row matrix observed so far ends the same way
(4 units left, 1 hunt, no result by 60:00) on all four maps; see the QA report for the table.

## Notes
- A human who has lost their base can keep any match against the AI alive forever by walking one worker to a map corner; an AI's
  fleeing worker can reach the border too (the M5-4d stress rows ended because their last units happened to stand inside the interior).
- Suggested direction (the developer's call): add border points to the sweep (the grid lines' intersections with the map's edges and the
  four corners, clamped inside the map/passable ground), or derive the point spacing from unit sight; then fix the docs/03 claim.
- Related: BUG-1068 (the stalls this task targets).

## Re-check round 1 (2026-11-06-0926, fix 6a3c83c): partly fixed, still open
The fix adds a border ring (the four map corners plus, on each edge, a point level with each cell centre) and `SeenPoints`. The corners and
the edge points level with cell centres are now found: `HuntQaTests.AHuntFindsALastUnitAtTheMapEdge` 5 / 5 and the shipped 24-row corner
matrix both pass. But ring points are a whole scout cell apart along each edge (51.2 m), and sight is 14 m. So the stretch of edge more
than 14 m from both neighbouring ring points (about 23 m of every 51 m) is seen only if the army happens to walk along the edge. Nearest-first
never does: from a ring point it turns inward to the cell centre (about 25 m away) rather than to the next ring point (51 m away). The walk order
is the same on every pass, so the gap is permanent.

Repro: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HuntSweepWorstPointQaTests"` (`Stress/HuntSweepQaStressTests.cs`).
The human seat's 5 workers hide at the sweep's worst points: on each edge a point level with a grid line (half a cell from the two ring points beside
it), plus one interior point half a cell from a centre and a corner. All four maps x AI seat 0 / 1 x openings 0 / 2: **11 / 16 rows never end**
(at 53c4a92, before the fix: 16 / 16). Every unit left is an edge worker 24.7-25.4 m from the nearest sweep point, except one interior worker
(generated, AI seat 1, `holy_war` seed 3, at (50.6, 76.2), 25.0 m; also missed before the fix, so not caused by `SeenPoints`). Examples:
- Raraku AI seat 0 `standard` seed 1: hunting from 7:55, the worker at (3.7, 51.5) is still alive at 60:00. The army reached ring points 48
  (1, 25.6) and 50 (1, 76.8) as targets on two full passes, each time turning inward next. Its nearest approach was 26.4 m.
- Pale Hills AI seat 0 `standard` seed 1: (257.0, 3.9) and (508.9, 460.0) alive at 60:00, 64 / 225 marked on the current pass.
- Vathar Crossing AI seat 1 `standard` seed 1: (256.4, 3.6) and (51.6, 508.2) alive at 60:00.

The docs/03 claim "every point of the map is within half a cell of a sweep point" is true, but being within 25.6 m of a point is not being seen
(14 m). Direction (the developer's call): a coverage guarantee in terms of sight, not point spacing. For example, ring points every
`2 x sight` along the edges, or make the sweep walk the edge between neighbouring ring points (the sweep's time budget, 57:36 of 60:00 on
Vathar Crossing, is the constraint the developer measured).
