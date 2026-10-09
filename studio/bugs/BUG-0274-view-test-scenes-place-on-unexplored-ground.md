# BUG-0274: Five view test scenes pick building spots the player hasn't explored, refused since M4-3b

| Field | Value |
| --- | --- |
| Severity | S2 (the scene loop is red on the merged M4-3b tree) |
| Status | fixed |
| Found | 2026-10-09-0125, task M4-3b (scratch merge of the sim branch onto studio/2026-10-09-0125-view 38cf064) |
| System | view test scenes (`game/tests/`), placement on explored ground |
| Fixed by | view 25a9670 (`game/tests/ExploredGround.cs`; QaV3 / QaV3b / QaV4 / ProductionHud `FreeAnchor` take `Unexplored` for dev spawns and scout real-Build / ghost-click spots first; M3Playable spot search keeps to explored footprints, Yard search to 60 m); regression = the scene loop itself |

## Repro
1. Scratch merge of the M4-3b sim changes onto the view branch; `powershell -File tools/qa/smoke.ps1` (PASS), then
   `powershell -File tools/qa/scene-loop.ps1`.

## Expected
34 / 34 (the view branch's 34 scenes, FogViewTest included).

## Actual
29 / 34. All five failures pick building spots with `World.CanPlace`, which since M4-3b answers
`PlacementError.Unexplored` for a footprint the player hasn't explored (docs/02 "Buildings"):
- `QaV3Test`: "spawn failed / no buildings" (its `FreeAnchor` at 10-45 m from the hall accepts only `None` / `CannotAfford`).
- `QaV3bTest`, `QaV4Test`: "no anchor for malazan_barracks" (the same helper shape).
- `ProductionHudTest`: "BUG-0109: a click on green anchor 8358 (drawn 9030): []".
- `M3PlayableTest`: "16. V, W: Engineers' Yard (seed 6, tick 8375): no visible spot for Engineers' Yard in 0 tries".
The scenes the brief names for the towers (`AttackOrderViewTest`, `QaV6Test`, `QaV7Test`) pass.

## Notes
View-track fixes (the sim track doesn't edit `game/`): helpers that place with the dev `SpawnBuilding` (which checks
no player rule) can accept `Unexplored` as they accept `CannotAfford`; the ghost-and-click scenes should pick spots the
player has explored (`World.Fog.IsExplored(player, cell)` over the footprint), or walk a worker there first. The sim's
xUnit scenes that build far from their workers start explored through `TestSim.Explored` (internal seam); a Godot
scene can't call it.

## QA (2026-10-09-0125, M4-3b inspection)
Confirmed on a fresh scratch merge of the sim branch (a86fd01) onto the view head **0d02db7** (M4-V4 QA'd; the dev
merged onto 38cf064): smoke PASS, scene loop **30 / 35**, the same five failures with the same messages
(`ProductionHudTest`, `QaV3bTest`, `QaV3Test`, `QaV4Test`, `M3PlayableTest` at step 16, seed 6, tick 8274).
`AttackOrderViewTest`, `QaV6Test`, `QaV7Test`, `FogViewTest`, `QaFogViewTest`, `BenchTest` pass.

Fix size, tried in the scratch clone only (reverted): adding `r != PlacementError.Unexplored` to the `FreeAnchor`
acceptance line turns `QaV3Test` green (1 line). `QaV3bTest` / `QaV4Test` then fail later ("forge site not placed",
"barracks site not placed"): they place those sites with real worker Builds, so those calls need an explored anchor
(filter `FreeAnchor` on `World.Fog.IsExplored(0, cell)` over the footprint for the Build-path calls, or walk or spawn an own
unit there first). `ProductionHudTest` needs the same for its clicked green anchor, and `M3PlayableTest`'s spot search
needs to keep to explored cells. That is roughly 5-40 lines across five `game/tests/` files. It is view-track work (the
sim can't edit `game/`), and a Godot scene can't call the internal `ExploreAllForTests` seam. No product code changes:
the real placement ghost already refuses `Unexplored` with the view's `placement.unexplored` text.

## QA re-check (2026-10-09-0125, round 1)
Verified on a fresh scratch merge (`qa-inspector-recheck`: sim 1ab51c5 + view 25a9670): build 0 warnings, smoke PASS,
`tools/qa/scene-loop.ps1` **35 / 35** (QaV3 15.3 s, QaV3b 3.8 s, QaV4 4.9 s, ProductionHud 15.9 s, M3Playable 140.3 s with
seed 1's Yard at anchor 9007 and seed 6's at 11704, the seed that failed before). The diff changes no assertion: dev
`SpawnBuilding` spots accept `Unexplored` (the dev command checks no player rule), and every real Build or ghost-click spot
is scouted by the worker that will build it, with a failing `Check` if it is still unexplored after 2,000 ticks, so the
scenes still drive the real player Build path on legal ground. Gap left (not a bug): no Godot scene hovers the ghost over
unexplored ground and checks it reads red with the `placement.unexplored` text; only the text's loading is tested
(`FogViewTest`).
