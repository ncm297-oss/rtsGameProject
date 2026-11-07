# BUG-1107: The hunt's sweep never sees a unit standing at the map's edge or corner, so a hidden last unit keeps the match alive forever

| Field | Value |
| --- | --- |
| Severity | S2 (the feature's purpose, docs/02 "The AI finishes the match ... a beaten side's last unit hiding ... is found", fails for any unit near the border) |
| Status | fixed (re-check round 2, 2026-11-06-0926) |
| Found | 2026-11-06-0926, task M5-4d (QA full) |
| System | AI military manager, the hunt's sweep (`MilitaryManager.HuntPoint` / `NextSweep`) |
| Fixed by | 6a3c83c (border ring, partial) + 1eccd06 (perimeter leg, odd pass); regression: `HuntSweepWorstPointQaTests` x16, `HuntSweepRound2HideQaTests` x8, `HuntTests.ALastUnitOnTheEdge_LevelWithAGridLine_IsSeen` x4 |

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

## Re-check round 2 (2026-11-06-0926, fix 1eccd06): fixed
The fix has three parts:
- **The perimeter leg.** The first ring point reached starts a clockwise walk of every ring point and back. After that,
  every ring point counts as visited for the pass. A sighting mid-leg keeps the leg.
- **The odd pass.** Every second pass sweeps the cell edges' midpoints instead of the centres and interior corners.
- **The tie.** The nearest-point tie is now measured against the true least distance.

QA's evidence, independent of the developer's rows:
- `HuntSweepWorstPointQaTests` (the round-1 repro, 16 rows): all 16 end in the full run (at 6a3c83c, 11 never ended).
- New hide points, `HuntSweepRound2HideQaTests` (`Stress/HuntSweepRound2QaStressTests.cs`): 4 maps x AI seat 0 / 1 on
  opening 1. None of these points is named by the developer's rows:
  - interior points a quarter cell off both lattices;
  - edge points a quarter cell along;
  - Vathar's river band where it meets the west and east borders;
  - Raraku's north terrace corner (level 1, no even-pass point within sight);
  - Pale Hills' level-2 plateau corner (low ground can't see up).

  All 8 rows end: Raraku 17:04 / 20:14, generated 33:21 / 39:36, Vathar 47:52 / 51:53, Pale Hills 47:26 / 54:02. Every
  leg that began finished, and none was dropped. The generated map reached the odd pass (max pass 1 / 2).
- The leg against interruptions, on a flat 128 m map. Every one of the 252 border cells was visible during the leg with
  0 interruptions, and again with 3 nearby ones (a Raider running at the army, then fleeing inland).
- One sighting far across the map leaves one border cell unseen. The leg resumes at the next ring point, approached from
  inland. Filed separately as **BUG-1111** (S3): the stretch is walked on the next pass, so it does not block this fix.
- Save / load mid-leg and mid odd pass:
  - generated: saved mid-leg at tick 14,761 (ring 52, 12 left) and on odd pass 1 at tick 27,801;
  - Pale Hills: saved mid-leg at tick 26,341 (ring 185, 22 left).

  Each load is field-equal at once (the new fields included) and hash-equal every tick to the end. Forged `HuntRing` /
  `HuntRingLeft` / `HuntPass` values are refused as Malformed.
- 0 B over 600 ticks of the leg and over 200 pass flips driven through `NextSweep`.

Status: fixed. Regression tests:
- `HuntTests.ALastUnitOnTheEdge_LevelWithAGridLine_IsSeen` x4 (developer);
- `HuntSweepWorstPointQaTests` x16 and `HuntSweepRound2HideQaTests` x8 (QA).
