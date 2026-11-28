# BUG-1892: QA's four-seat table calls its 0 / 1 / 0 / 1 rows "2v2", but team 0 is no team (a 1v1v2)

| Field | Value |
| --- | --- |
| Severity | S4 (a test's naming; numbers are right for the match it plays) |
| Status | fixed (2026-11-27-1857, M6-S15 F) |
| Found | 2026-11-27-0817, task M6-SH3 (D, the Vathar Crossing seed 6 look) |
| System | tests (`Stress/M6S14FourSeatQaStressTests`) |
| Fixed by | M6-S15 F (e55178fe): the rows print "1v1v2", the parameter is `oneVsOneVsTwo`; no row deleted |

## Repro
`M6S14FourSeatQaStressBase.FourSeats_Report(map, teams: true)` passes teams `{ 0, 1, 0, 1 }`. Team 0 means "no team"
(docs/02 "Game flow" 2), so seats 0 and 2 are each a side of their own: the match is seat 0 v seat 2 v the team of seats 1 and
3. Its rows print "2v2".

## Expected
A 2v2 row passes two non-zero teams (1 / 2 / 1 / 2), as `M5H5HuntAfterDefeatQaTests` and `DrawStalledRowsReportTests` do.

## Actual
Vathar Crossing seed 6: the "2v2" row ends at 23,820 (seats 1 + 3); the real 2v2 ends at 79,351 (seat 0's team), past
`M5H5HuntAfterDefeatQaTests`' 72,000 (docs/03 "Implementation (M6-SH3)" D).

## Notes
- QA's own file: renaming the row or passing 1 / 2 / 1 / 2 is QA's call (both tables would then need re-measuring).
