# BUG-0622: M5-4a small findings: an edge start location wraps the hall's anchor, short resource counts favour player 0, two doc claims

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-13-0146, task M5-4a |
| System | `ViewApi/StartBase.Plan`, `ResourcePlacer` (start resources), docs/03 "Implementation (M5-4a)" |
| Fixed by | |

## Repro and findings

1. **Hall anchor wraps for a start location by the west edge** (hand-built maps; the generator keeps a 10-cell margin). A hand-built
   40 x 40 `Heightmap` with `new StartLocation(20 * 40 + 1, 0)` (legal: the ctor checks only the cell and level) plans the hall at
   **(34, 17)**, the far side of the map. `StartBase.Plan` computes `anchor = sy * Width + sx` with `sx = -2`, which wraps to the
   previous row's east end, then uses that anchor's centre as the "nearest spot" target. Test:
   `StartLocationQaTests.HandBuiltStartByTheWestEdge_HallStaysByIt` (skipped against this item). It matters once hand-made maps
   (M6-7) carry start locations.
2. **Short counts aren't shared alike.** docs/03 says the start mines go round-robin "so a short count shorts every base alike".
   Measured (`StartLocationQaTests.ShortCounts_StartResourcesPerBase_Measured`, seed 2, 128): `GoldMines` 1 → start mines 1 / 0;
   `GoldMines` 3 → 2 / 1; `Forests` 1 → near forest 22 / 0 trees. Player 0 always gets the odd one. The launcher accepts
   `--mines` / `--forests` 0-64, so a dev run with `--forests 1` has one base without wood. Either share alike (skip the odd
   remainder) or reword the doc.
3. **"their tick doubles" (docs/03 and the comment in `ProductionPerfTests`)** holds for that scene's seed 7 only. Measured over 2,000
   ticks, the same scene on both maps (`StartLocationPerfQaTests`): seed 7 0.54 → 1.19 ms, but seed 1 1.12 → 1.03, seed 3 0.91 →
   0.83, seeds 2 / 4 / 6 within ±5%, seed 5 1.19 → 1.26. The old map's own seeds range 0.41-1.19 ms. So it is scene variance, not a
   cost of start locations; pinning the gate to the old map is fine, but the docs should say "on seed 7".
4. `MapGenTests.GeneratedMap_HasTwoMirroredStartLocations...` checks the mirror as `|x1 - (size - x0)| <= 1`; the rule is exact
   (`x1 = size - 1 - x0`), and QA's row asserts it exactly (green).

## Expected / Actual
See each item.

## Notes
None of these is reachable with the shipped launcher defaults (12 forests, 8 mines, generated starts).

## Producer triage (2026-10-13-0146 ACCEPT)
S4 confirmed; items 1, 2 and 4 go to the sim's hardening if under budget (item 1: clamp the anchor to the map before computing it, keep the hall on the location's side; item 2: round-robin that skips the odd remainder, or the docs reworded; item 4: the dev row asserts the exact mirror). Item 3 (the docs claim) fixed in docs/03 at this ACCEPT ("2.2x on seed 7, within 10 % on seeds 1-6").

## QA re-check (2026-10-13-0527, M5-H1)
Items 2 and 4 **fixed** in M5-H1 1dfdef3: whole-round start counts (`Map/MapGenTests.ShortStartCounts_ShortEveryBaseAlike`, 3 rows; QA's `ShortCounts_StartResourcesPerBase_Measured` re-run green) and the exact mirror assert. Item 1 (the hall anchor wrap in `ViewApi/StartBase.Plan`) is still open (the row stays skipped), so the bug stays open.
