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
