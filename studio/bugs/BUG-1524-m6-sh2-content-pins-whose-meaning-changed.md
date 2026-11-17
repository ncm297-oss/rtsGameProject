# BUG-1524: three faction-page prose pins went red when M6-SH2 re-printed their cells (sentences made false)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (two fixed in M6-SH2 fix round 2 by the Producer's ruling; one skipped for D33; one S4 comment left) |
| Found | 2026-11-15-2136, task M6-SH2 (developer listed them for ruling; id given by the Producer at the ACCEPT) |
| System | docs/factions pages and their content pins (`QA/Content/**`) |
| Fixed by | |

## Items
1. **Fixed (round 2):** `TeblorOpeningQaTests.TheOpponentSentence_*` and teblor.md's sentence: each row's outcome
   clause is built from the row; both matches stand at 20:00 now. QA: the test goes red when a row's outcome is flipped.
2. **Fixed (round 2):** `DR35ProsePinQaTests.AgeTwoProse_*` and whirlwind.md: seed 13 read from its cell (never reaches
   Age II, defeated at 15:19). whirlwind.md's "What it says" under the holy war's table re-worded to the re-printed
   table (`holy_war` falls on none of nine runs).
3. **Open, D33's first item:** `DR35ProsePinQaTests.Malazan_Bug0982Prose_MatchesTheThresholdTable` is skipped with this
   id. malazan.md's BUG-0982 threshold table (18 / 16 / 14 pop, seeds 7 / 8 / 14) predates M6-SH2 and disagrees with
   the re-printed Age II table (seed 14 now stands at 18 pop). D33 re-measures the table on the landed sim, rewrites
   the prose, and un-skips the row.
4. **Open, S4 (QA re-check 2):** the `<summary>` at lines 25-29 of `Content/AiOpeningPairsReportTests.cs` still says
   seed 3's `holy_war` side falls at 12:29 (comment only). D33.
