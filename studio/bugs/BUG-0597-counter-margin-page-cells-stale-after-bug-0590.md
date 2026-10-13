# BUG-0597: Red suite: the counter-triangle margin tables on two faction pages are stale after BUG-0590's combat fix

| Field | Value |
| --- | --- |
| Severity | S1 (red build) |
| Status | open |
| Found | 2026-10-12-1930, task M5-3 (re-check round 1) |
| System | `docs/factions/malazan.md`, `docs/factions/whirlwind.md` "Balance baseline" tables vs `QA/Content/CounterTriangleMargins*` |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf"` on 786c9e4: 7 failures, all in the counter-triangle margin rows that
compare the printed tables with a fresh run.

## Expected
The non-Perf suite is green at the branch head (the definition of done; criterion 11).

## Actual
BUG-0590's fix (an overlapping body is in reach) changes the "Raider v Wickan Lancer" (Line beats Shock) rows. On
docs/factions/malazan.md lines 212-213 and whirlwind.md lines 207-208:
- seat 0: winner hp left 1546 → 1502, time 23.5 → 23.0 s.
- seat 1: winner hp left 1451 → 1429, time 23.0 → 23.2 s.

The winners, winners left and cost kept are unchanged. The tests do what they're for: a sim change that moves a number fails
the row until the table is re-printed.

## Notes
- The pages are data-track files and the dev correctly did not edit them. This session's data track (D15) adds only test files
  under `QA/Content/` and does not touch either page, so there is no file overlap.
- QA recommendation for the Producer: allow these four cells to be re-printed on this branch, in the fix round or by the
  conductor's commit. The numbers are mechanical (printed by the test), the change is caused by this branch's rule fix, and
  leaving them means main goes red at merge. The other option is a data-track task next session, with main red until then.
