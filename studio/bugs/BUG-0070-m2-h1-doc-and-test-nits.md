# BUG-0070: M2-H1 nits: docs/01 minimap decision row is stale, docs/03 big-map range, twin double-tap constants, dev wall test blind at the far edges

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-06-0905, task M2-H1 |
| System | docs (01, 03), ViewApi ControlGroups, TerrainMeshBuilderTests |
| Fixed by | |

## Repro
1. **docs/01 decision table is stale.** `docs/01-vision.md`, the 2026-10-05 Minimap row: "unit dots
   are one map cell in the player's (= faction's) colour". Since M2-H1, a dot is 3 x 3 cells: the
   owner's cell inside a black or light-grey rim (`MinimapRaster.RimFor`). CLAUDE.md: "When a
   settled decision changes, update the table in docs/01-vision.md with the date." Only docs/03 was
   updated.
2. **docs/03 big-map note gives the wrong range.** docs/03 "Implementation (M2-4)", "Maps wider than
   the control": "from 221 to 440 cells a unit can show only its rim colour, not its owner colour".
   With nearest sampling, the centre cell can be skipped on any axis over 220 cells, so this also
   happens from 441 to 660 cells (and beyond, where the whole dot can be skipped). It should read
   "over 220 cells".
3. **Two constants for one rule.** `ControlGroups.DoubleTapSeconds = 0.3` (docs, summaries) and
   `ControlGroups.DoubleTapMs = 300` (what `Tap` actually compares) are separate literals. If
   someone changes one, the docs and the behaviour drift apart. Derive one from the other, or keep
   only the ms constant.
4. **The dev wall test can't see the far edges.** In a scratch clone, mutating
   `TerrainMeshBuilder.Build` to `if (x + 1 < w - 1)` (no walls on the last column) or
   `if (y + 1 < h - 1)` (none on the last row) leaves every `TerrainMeshBuilderTests` test green.
   The new `DefaultMap_HasAWallOnEveryEdgeWhereTheTwoCellsDiffer_AndNowhereElse` checks seeds 1 and
   42, where the border ring is flat. The developer said so in their report. QA's
   `TerrainMeshBuilderQaTests.AdversarialMaps_AreWellFormedAndClosed` kills both mutants, so the
   whole suite is covered. Dropping half of the x or z walls is caught by the new dev test (verified).

## Expected
Docs that match the code, one source for each constant, and dev tests that can fail on the property
they name.

## Actual
As above. None of these changes behaviour in the shipped 128 x 128 game.

## Notes
For item 4, a hand map with a level step on the last row and column (or the QA adversarial set)
would make the dev test cover the edges.
