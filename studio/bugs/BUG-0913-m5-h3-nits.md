# BUG-0913: M5-H3 nits: stand-point comments and docs, the gather row's cost, the seed-21 wedge margin

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-17-1624, task M5-H3 (QA, full tier) |
| System | economy (`EconomySystem.WalkToFootprint` / `FaceCell`), construction docs, docs/03 "Economy implementation", AI pacing constant |
| Fixed by | |

## Repro / items
1. **Orphaned doc comment.** In `sim/Rts.Sim/Economy/EconomySystem.cs` (l. 328-347), `RingCell`'s old `/// <summary>` now
   sits directly above `FaceCell`'s own summary. `FaceCell` has two `<summary>` blocks and `RingCell` has none.
2. **Corner order in the comment.** `FaceCell`'s comment says the corner cells come "SW, SE, NW, NE". The code's k = 0 is
   (x0 - 1, y0 - 1), which `RingCell` calls "the row above", so the order is above-left, above-right, below-left,
   below-right. The comment names them the other way up.
3. **Stale "4-adjacent" docs.** `ConstructionSystem`'s class summary (l. 20) still says a builder stands "4-adjacent to the
   footprint like a gatherer". `WalkToFootprint` also serves builders, repairers and drop-off walks, and since M5-H3 it
   offers the diagonal corner cells too.
4. **Scope of the change understated.** docs/03 (BUG-0790 paragraph) says "Moved: gathering wherever a face is crowded".
   A corner cell's centre is the nearest candidate for any worker that comes in on a diagonal, crowded or not. So every
   footprint walk can move: gather, drop-off, build, repair. That is why the seed-21 replay parts at tick 19, the first
   gather walks. QA's runs show the effect in the seed-21 match: wood at its end goes from 235 to 895.
5. **Undocumented cost on a gated row.** `GatherPerfTests.TwoHundredGatheringWorkersAlone` is 0.709-0.726 ms on base and
   0.857-0.868 ms on head: +20 % in three paired fresh processes and both category runs. The gate is 1 ms. The scene also
   gathers more (gold 1,400 -> 1,570, wood 490 -> 640), as in M4-2a's precedent. docs/03's M5-H3 cost line names only "up
   to 3 `PointCrowd` queries ... and 4 more candidate cells", not the measured row.
6. **Seed-21 wedge margin.** `QA/GatherWedgeQaTests.Seed21PlayableReplay_*`: the longest out-of-reach stand on a wood loop
   is 332 ticks on base and 478 on head, against a bound of 600. A fix meant to cut wedges moved this row 44 % closer to its
   bound. It is still green and documented (docs/03, the test's summary). Logged so the next gather change watches it.
7. **A pacing constant in C#.** `BuildOrderExecutor.NoSiteRetryThinks = 5` changes AI build timing by up to 5 thinks
   (2.5-10 s by difficulty). docs/03 calls it an engine pacing constant like the 16 retry marks. If the owner sees it as
   AI tuning, it belongs in `rules.json` `ai` (CLAUDE.md rule 6). Owner's call. See also BUG-0910.

## Expected
Comments and docs say what the code does. Measured costs on gated rows are named in docs/03.

## Notes
None of these change a test outcome.
