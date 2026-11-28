# BUG-1933: The Hard v Normal tally's Pale Hills seed 3 / 6 (Hard seat 0) no longer ends by 72,000 after M6-S15 C

| Field | Value |
| --- | --- |
| Severity | S3 (a measure-and-report row that ended on the base no longer ends; not a pinned criterion row) |
| Status | open (triaged at the 2026-11-27-1857 ACCEPT; Producer ruling in docs/01, the 2026-11-27-1857 ACCEPT row, Q4: traced first at the sim's next AI task, the computer player should end this match, not the draw rule) |
| Found | 2026-11-27-1857, task M6-S15 (criterion 9, the per-item table; the item: C; filed from the developer's patch, confirmed by QA) |
| System | AI production (C) and the draw rule's "can produce" (BUG-1894's family) |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DrawStalledRowsReportTests.ATallyMatchWithoutAHardWin_Measured"`
(Category Stress): the rows `pale_hills, 3, 0` and `pale_hills, 6, 0` (one match).

## Expected
The match ends (the base: the Normal seat 1 wins at 55,532).

## Actual
No end by 72,000 on C's tree (773cecec) and after: seat 0 (Hard) 9 workers, no unit-producing building, 538 gold;
seat 1 (Normal) 4 units, no worker, nothing that produces, 43 gold. Never spent: the draw rule counts seat 0's workers
as producers and its bank pays a unit, though no building of its can train one. QA's probe: seat 0 keeps rebuilding a
Keep and Billets that seat 1's last 4 units raze, never a producer (538 / 50 at 72,000).

## Notes
- The Hard v Normal tally reads 32 / 5 / 0 / 5 with this row (was 32 / 4 / 0 / 6).
- docs/03 "Implementation (M6-S15)" question 4.
