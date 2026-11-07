# BUG-1118: M5-4d-2 rebuild and look nits (grouped)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 2 fixed in fix round 1, 8a1e588: `AiPlayer.Save` reads `TownHallCell` with `ReadInt32In(-(cells + 1), cells - 1)`, proof `SaveRefusalTests.AForgedTownHallCell_OutsideTheMap_IsMalformed` (7 cases, fail-first); item 1 open, the sim's hardening; item 3 open, the data track's next task: the Malazan page's prose round the re-printed tables, plus the seed 19 paragraph (the beaten side now stands at 20:00 again by rebuilding, see BUG-1116's notes)) |
| Found | 2026-11-07-0236, task M5-4d-2 |
| System | AI (HallFirst, TownHallCell encoding), docs |
| Fixed by | item 2: 8a1e588 |

## Repro / Actual
1. **A hall site razed before its Build is judged moves the rebuild off the old anchor.** On the flat scene, raze the
   Keep, wait for the rebuilt Keep site to appear (tick 202), and raze it in the same think. The next think judges the
   pending hall Build as refused, because no Keep stands at its anchor, and gives the anchor (8, 30) a retry mark. The next
   rebuild goes to (7, 29) by the ring search, and every later rebuild treats (7, 29) as "the old spot". This follows the
   existing judge rule (a house Build behaves the same way), and the hall still goes up nearby. It is noted because
   criterion 2 b says "at the old anchor when free". QA's `RebuildLookQaTests.AHallRazedTwice_AndItsSiteRazed_...` waits
   two thinks before razing the site so it tests the rule itself.
2. **`TownHallCell` is read from `AIST` with no range check** (`r.ReadInt32()`, unlike the `ReadInt32In` fields near it),
   and every value <= -2 now means an anchor. QA set it to -3, -1,000,000 and `int.MinValue` on a seat with no buildings and
   workers alive: no exception over 200 ticks (the out-of-range anchor fails `CanPlace` and the search guards hold). So
   this is harmless today. A load check that accepts only -1, a valid cell, or -(valid cell + 2) would match the
   loader's refusal rows for the fields around it.
3. **docs/factions/malazan.md prose** near the "Age II against the Whirlwind" and BUG-0982 tables still quotes the old
   defeat times (13:25, 12:33, "defeated 10:24"), while the table cells now read 13:50, 15:03 and "never". The prose is the
   data track's to reword (rule 40). The dev's report says so as well.

## Expected
1. The rebuild at the remembered anchor when it is free. 2. A range-checked load. 3. Prose agrees with the tables.

## Notes
All three are cheap. Item 1 could be fixed by judging the hall Build against "a Keep stood at the anchor at any point
since it was sent", which is more than a few lines, or it can be accepted as is.
