# BUG-1045: With shared vision, an AI seat that knows a forest only through its ally never scouts and sends its wood gatherers 300-450 m across the map

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-27-0658, task M6-S5 |
| System | AI (ScoutManager `FoundEnough`, EconomyManager `NearestNode`) with shared vision |
| Fixed by | |

## Repro
1. BUG-1042's repro: four AI seats, teams **1, 1, 2, 2**, seed 1, 256 map, 4 starts, 24 forests, 16 mines, one raider each
   (`AiTeamTests.MirrorAllies(1)`). Seat 3 (Normal, Whirlwind) starts south; its ally seat 2 starts north (the mirror).
2. Run 4,000 ticks; read seat 3's known forests (`ai.View.NextResource`), its scout (`Board.ScoutPhase`, `ScoutVisited`)
   and `World.GatheredWood`.
3. Permanent test (skipped until the fix): `QA/SharedVisionQaTests.MirrorAllies_EverySeat_GathersWoodNearHome`.

## Expected
Shared vision gives a seat its ally's eyes; it should not cost it its own economy. At e5c4265 (before M6-S5) seat 3 scouted
its own surroundings, knew a forest 17 m from its hall by tick 1,000, gathered 370 wood by tick 4,000, built an army of
54 half-pop and started a wave. Criterion 1 of M6-S5 asks every non-starved seat on this repro to wave.

## Actual
Seat 3, M6-S5 head (d6f597d) vs e5c4265, same scene:

| Tick | head: scout | head: forests known, nearest | head: wood | base: scout | base: forests known, nearest |
| --- | --- | --- | --- | --- | --- |
| 250 | phase 0 | 9 known, nearest 453 m | 150 (start) | phase 0 | 0 |
| 500 | **phase 2, 0 points visited** | 9, 453 m | 0 | phase 1, 3 visited | 0 |
| 1,000 | phase 2, 0 visited | 20, 453 m | 0 | phase 2, 6 visited | 25, **17 m** |
| 2,000 | phase 2, 0 visited | 31, 298 m | 0 | phase 2 | 28, 17 m |

- From the first think seat 3 knows a forest (its ally's, north), a gold mine and an enemy building (through its ally's
  raider), so `ScoutManager.FoundEnough` holds and the scout is released without visiting a single waypoint. Its own forest
  17 m from the hall is never seen.
- `EconomyManager.NearestNode` sends its wood gatherers to the nearest *known* forest: 298-453 m away. At tick 4,000 its
  wood workers are spread along the walk (`Moving` at 40, 120, 180, ... 440 m from the hall), stored wood 0, gathered wood
  **0** (base: 370).
- With no wood the build order stalls after the first Tent and the Raider Camp (3 Builds by tick 8,000; base: 10, a Supply
  Cache, Smithy, Archer Camp, Horse Lines), gold piles up (1,512 at tick 8,000), the army never passes 6 half-pop (need
  28), and seat 3 **never waves** (base: 1 wave). It is not raided (0 ticks defending, 0 losses).
- `TeamAiSoakStressTests` passes this row only because its strict wave check exempts seats whose army never reached wave
  size ("starved"); the developer's report and BUG-1042's fix note attribute every such seat to raids. Seat 3 here is
  starved by this change, not by a raid.
- Wood gathered by tick 4,000, head vs base, same scenes: seed 1 teams 1,1,2,2: 50, 250, 330, **0** vs 0, 260, 310, 370;
  seed 2 teams 1,1,2,2: 180, 150, **0**, 510 vs 70, 120, 60, 480.

## Notes
- The pathology predates M6-S5 in free-for-all too, more rarely: at e5c4265 seed 3 seat 3 (Easy, FFA) and seed 1 seat 0
  (FFA) also skip the scout and know only a forest 278-324 m away (a raider's sighting), and seed 3 seat 3 gathers 0 wood.
  Shared vision makes it reach any seat whose ally's start view holds a forest its own doesn't.
- Possible directions (Producer's call): `FoundEnough` counts only a forest (and mine) within some radius of the own hall,
  or never releases the scout before its head (own base ring) is walked; and/or `NearestNode` prefers nodes near an own
  drop-off and sends a worker on a long walk only when none is known near home.
- Measured with the QA scratch diagnostics `Diag1042Tests` / `Diag1042cTests` (not committed): per seat waves, army
  half-pop peak, ticks defending, workers, gathered gold / wood, scout phase and nearest known forest at tick 2,000.
