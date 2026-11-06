# BUG-1069: M6-S8 paperwork nits (Stress trait wording, docs/02 expansion count, size-budget reading)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (Producer, at the 2026-11-06-0326 ACCEPT) |
| Found | 2026-11-06-0326, task M6-S8 (QA) |
| System | docs/03 "Implementation (M6-S8)", docs/02 "Map and terrain", the brief's size budget |
| Fixed by | Producer at the ACCEPT: item 1 docs/03's sentence now says the two matches carry the `Stress` trait; item 2 docs/02's Gold row reads "1-3 expansions per player (Pale Hills gives one per camp ...)"; item 3 ruled: the budget counts the developer's hand-written lines (production, tests, docs), not generated map content (terrain and forest rows, the tool's source quarters) nor QA's suites, so M6-S8 is ~1,300 lines and inside 1,500; future briefs say "excluding generated map content" |

## Repro / items
1. **The Stress trait.** docs/03 line ~9267 ("the two matches, about 20 s each, so no `Stress` trait") and the developer's
   report say `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks` has no `Stress` trait. The code has
   `[Trait("Category", "Stress")]` on it (`sim/Rts.Sim.Tests/Map/MapFileTests.cs`, the theory above `PlayToTheEnd`). The
   code is fine; the docs sentence is wrong.
2. **docs/02's gold row.** The "Map and terrain" table says "Gold: 2 mines per start location, 2 per expansion, 2-3
   expansions per player". In a 4-player match Pale Hills has one expansion pair per player (16 mines: 2 per start plus
   one 2-mine expansion per quarter). The brief asked for exactly that, so the map is right, but docs/02 wasn't updated to
   match (e.g. "Pale Hills: one per player, gold is scarce"). Vathar Crossing gives 2 per player, in range.
3. **Size budget.** The brief's budget is 1,500 changed lines "excluding the map files' terrain rows". The diff is 4,624
   lines. Without the 512 shipped terrain rows it is 4,112. The developer's ~1,300 also leaves out the map files' forest
   lists (about 2,000 lines) and the two source files (725). Those are generated or tool input, so the developer's reading
   is reasonable, but it isn't the brief's wording. The Producer should rule on it.

## Expected
Docs say what the code does; docs/02 matches the shipped maps; the budget reading is agreed.

## Notes
None of these change behaviour.
