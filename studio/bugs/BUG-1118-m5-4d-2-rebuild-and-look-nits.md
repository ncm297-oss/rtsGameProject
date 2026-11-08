# BUG-1118: M5-4d-2 rebuild and look nits (grouped)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (item 1 fixed by M5-H5, 2026-11-08; item 2 fixed in fix round 1, 8a1e588: `AiPlayer.Save` reads `TownHallCell` with `ReadInt32In(-(cells + 1), cells - 1)`, proof `SaveRefusalTests.AForgedTownHallCell_OutsideTheMap_IsMalformed` (7 cases, fail-first); item 3 **fixed by D-R34 (d990ade, 2026-11-07-0926)**: the Malazan page's prose round both tables matches the cells (13:50 / 15:03 / 10:44 / 18:27 / 16:54) and the seed 19 / 25 paragraph is rewritten; QA re-ran DH6 and matched every figure) |
| Found | 2026-11-07-0236, task M5-4d-2 |
| System | AI (HallFirst, TownHallCell encoding), docs |
| Fixed by | item 2: 8a1e588; item 3: D-R34 d990ade (docs only); item 1: M5-H5 cd211f2 (fix round 1: 6c3639a), `Ai/RebuildTests.AHallSite_RazedBeforeItsBuildIsJudged_IsRebuiltAtTheSameAnchor` (red before: the next Keep went up off the anchor); fix round 1 (BUG-1148) narrows the excuse to one retry at the anchor, proof `Ai/RebuildTests.AUnitParkedOnTheLostHallsAnchor_CostsOneRetryThere_ThenTheHallGoesUpOffIt` |

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

## Item 1 fix (M5-H5, 2026-11-08)
`BuildOrderExecutor.HallFirst`: a hall Build at the lost hall's own anchor (`LostHallCell`) is not given a retry mark when
it is judged not accepted while `PlayerView.CanPlace` still allows a hall there: it was not refused for its ground (its site
was razed before the judge, or a unit stood there and moved off), so the next rebuild goes back to the old spot. A Build
refused because the ground is taken (a tree, a building) still gets the mark and the ring search moves on
(`RebuildTests.ARazedHall_WhoseAnchorIsTaken_IsRebuiltNearby` unchanged). Builder's call: the brief's "a Keep stood at the
anchor since it was sent" needs a remembered fact across thinks (a new saved field); "the anchor still takes a Keep" needs
none and gives the same answer in the razed-site case.

