# BUG-0290: D8 page-pin nits: a header mismatch hides the row diffs, and BUG-0243's regression note overstates it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-09-0125, task D8 |
| System | content tests (data track): `Content/CounterTriangleMarginsTests.PageProblems` / `Compare`; studio/bugs/BUG-0243 |
| Fixed by | D-H1 (data track): `Compare` reports a header mismatch with the columns the page lacks / adds and still compares the rows on the shared columns, matched by name; only a page without a row-key column leaves the rows unchecked, and says so. Regressions `CounterTriangleMarginsTests.BUG0290_AHeaderMismatch_StillReportsTheStaleCells` (failed on the early return), `BUG0290_AHeaderWithoutARowKey_SaysTheRowsAreUnchecked`; BUG-0243's D8 note corrected |

## Repro
1. In a scratch clone of the D8 tree, put the pre-D8 page back:
   `git show 1a9925b:docs/factions/malazan.md > docs/factions/malazan.md`, then
   `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~CounterTriangleMarginsTests.EveryPrintedRow"`.
2. The failure lists one problem only:
   `malazan.md group table: header '| Rule | ... | Winner left | Winner keeps (cost) | Time to last death |' vs printed '| ... | Winner hp left | ... |'`.

## Expected
- BUG-0243's D8 update says `EveryPrintedRow_EqualsItsPageRow(malazan)` "failed on the old page (time column, and the
  missing hp column)". It names only the header; the stale 21.5 s cell is never reported.
- When a table's header changes, the rows still get compared, or the message says that rows were not checked, so a
  re-print does not take two rounds.

## Actual
`Compare` returns as soon as the header differs (`if (page[0] != header) { ...; return; }`), so any row or cell problem
in the same table is hidden until the header is fixed. The criterion's evidence (a cell mutation naming pair, seat and
column) holds through `AMutatedPageCell_FailsNamingPairSeatAndColumn` and QA's scratch mutations, so this is wording
and convenience only.

## Notes
- Also cosmetic: a mutated key cell (the siege unit, "Winner v loser" or the seat) is reported as "0 page rows,
  expected 1" plus "page row ... is not a row the harness prints", not by column. That is reasonable for a key, noted
  only so nobody expects a column name there.

## D-H1 fix (2026-10-09)
- `Compare` maps each printed column to the page's column of the same name. A differing header is one problem
  ("header '...' vs printed '...'; page lacks 'Winner hp left'; rows compared on the shared columns"), and every row is
  still compared on the columns both have, so a stale cell is named by pair, seat and column in the same run. If the page
  lacks a column that names a row ("Winner v loser", "Winner seat", the siege unit), the problem ends "rows not checked
  (the page lacks the row key '...')" and the rows are skipped.
- Scratch run with the real pre-D8 page (`git show 1a9925b:docs/factions/malazan.md`, restored after):
  `EveryPrintedRow_EqualsItsPageRow(malazan)` now lists two problems, the header (page lacks 'Winner hp left') and
  `Wickan Lancer v Desert Archer seat 1, column 'Time to last death': page '21.5 s' vs sim '22.0 s'`.
- The Notes item (a mutated key cell reads as "0 page rows" plus "not a row the harness prints") is unchanged, as noted.
