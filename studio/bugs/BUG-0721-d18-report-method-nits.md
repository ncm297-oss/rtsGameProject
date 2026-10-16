# BUG-0721: D18 report method nits: siege ratio vs HI only; spending conclusion counts post-win marks; 6 mines, not the menu's 8

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (D-H5 a6c23f0, all three items, verified by QA 2026-10-16-0847: the Raider-side ratio printed and pinned (BUG-0780 carries the two cells under 2x), the spending conclusion cut at the idle seat's defeat (mutant killed), the menu's 8 mines; set by the Producer at the 0847 ACCEPT) |
| Found | 2026-10-14-0946, task D18 |
| System | content reports (`sim/Rts.Sim.Tests/Content/`) |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~BuildingKillTimeReportTests|FullyQualifiedName~AiSpendingReportTests.GeneratedNormal"`,
then read the output (trx or detailed console).

## Expected
The brief's band and columns as stated: siege does "at least 2x the line units' damage per cost against buildings", the
spending re-measure is of a live match on a menu-like map, and every caveat behind a conclusion is printed with it.

## Actual
1. `BuildingKillTimeReportTests`: the siege check divides by `runs[v, 0, x]`, the 20 Heavy Infantry row only. The Raider
   row, the cheaper and faster line unit, is not checked. Under the proposed (C), Raider razes 1.33-1.40x faster per cost
   than HI, so the catapult's ratio against the Raider is about 2.5x (Town Hall), 3.1x (hall) and 3.9x (House). The
   verdict doesn't change (still ok), but the margin is half of what the table shows.
2. `AiSpendingReportTests.GeneratedNormal_Spending_Report`: the idle seat is defeated at 741 / 769 / 822 / 840 s, so the
   15-min and 20-min marks are after the match is won. The conclusion ("9 of 16 marks") counts them. Using the 5-min and
   10-min marks only gives 4 of 8, the same verdict under the report's rule (2 x bound >= marks). The report prints the
   defeat time, but the conclusion line doesn't mention it.
3. The generated spending rows use `AiScenes.Generated`'s fixed 6 mines, while the menu (and the trees report) uses 8
   (`LaunchOptions.DefaultMines`). This is disclosed in the class summary, but the row header reads like a menu map.

## Notes
None of these changes the proposed lever (C) or the "wood is still the limit" conclusion. They're worth folding into D19
when the reports are rerun on the new numbers. Not a bug, but an owner-relevant fact from report 4: Crossbowmen sit on the
1-damage floor of `DamageCalc` (round(raw) - armor, at least 1) against the hall and Town Hall under every lever, so their
times (131 s / 263 s) don't move even under (C). Only the House time halves, because its armor drops to 1.

**2026-10-16 (D-H5, developer; proposed status: fixed, for QA to verify).**
1. `BuildingKillTimeReportTests` prints the siege ratio against both line rows ("x HI / x Raider per cost"). The band's 12
   cells stay the Heavy Infantry's (12 / 12); against the Raider the Catapults are 2.8 / 1.7 / 1.6x, so two cells fall under
   2x. Pinned exactly in the report (not loosened) and filed as BUG-0780 (S3) for the Producer.
2. `AiSpendingReportTests.GeneratedNormal_Spending_Report` counts only the marks before the idle seat's defeat, prints
   "(after the defeat: not counted)" on the others, and asserts the cut-off (counted marks == marks before the defeat
   tick; mutation: counting every mark fails "Expected: 1, Actual: 4"). The flat report asserts nobody falls (combat off).
3. The generated rows run on the menu's map, 12 forests and 8 mines (a local `MenuMap` built as `AiScenes.Generated` but
   with `GoldMines = 8`; the helper is the sim track's and fixes 6), asserted 8 mines on the map.
Re-run on D19 + the surround rule: the idle base falls at 496-593 s, so only the 5-min marks count: 1 of 4 wood-bound
(the Whirlwind, seed 7): "wood binds at some marks only, not the limit overall" (D18 read "still the limit", 9 of 16).
