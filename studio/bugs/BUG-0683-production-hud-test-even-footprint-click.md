# BUG-0683: ProductionHudTest clicked a 2x2 footprint's centre (a cell corner), which landed in the cell to the west

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-13-0916, task M6-V6 (the dev found it red on base dde71a4) |
| System | view test scene `game/tests/ProductionHudTest.cs` (placement row "a click on another green anchor builds there") |
| Fixed by | M6-V6 (442a3e6): the row clicks the anchor's cursor cell (`G.CellCenter(anchor + footprint / 2)`), as `AnchorScreen` does |

## Repro
1. At dde71a4: `powershell -File tools/qa/scene-loop.ps1 -Filter ProductionHudTest` fails the second-anchor click (per the
   dev's report; the scene staged on main a8ebff7's map).

## Expected
The test clicks where a player would click to place the house on that anchor.

## Actual
For an even footprint the footprint centre is a cell corner; the ray from that pixel resolved to the neighbouring cell, so the
ghost's anchor differed from the one the row wanted. A test staging bug, not a product bug.

## Notes
Recorded at the dev's request. QA proof of the fix: `ProductionHudTest PASS` in the 0916 scene loop on 442a3e6.
