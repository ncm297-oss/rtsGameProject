# BUG-1103: `D24MapPinQaTests.ThreeMaps_*` pins the shipped map count at 1, so it goes red on the merged tree (3 shipped maps)

| Field | Value |
| --- | --- |
| Severity | S2 (test-only; it would turn `main` red at the data merge, so it blocks that merge and nothing else) |
| Status | open |
| Found | 2026-11-06-0326, the Producer's merge rehearsal (sim → view → data) at the ACCEPT |
| System | `sim/Rts.Sim.Tests/QA/Content/D24MapPinQaTests.cs` line 49 (QA's row; data track's folder) |
| Fixed by | |

## Repro
1. Merge `studio/2026-11-06-0326-sim`, then `-view`, then `-data` (studio lists both sides).
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D24MapPinQaTests"`.

## Expected
Every D24 row green with any number of shipped maps: the whole point of D24's id-based pin is that the sim can add maps.

## Actual
`ThreeMaps_WithCleanText_PassThePinAndTheChecker_AndRarakuIsFoundById` fails: `Assert.Equal(3, d.Maps.Length)` — expected 3, actual 5.
The row copies the shipped data dir and adds two maps (`aa_first`, `zz_last`); with Raraku alone on the data branch that is 3, on the
merged tree (Pale Hills, Raraku, Vathar Crossing) it is 5. The other three D24 rows and every dev row pass on the merged tree
(the full non-Perf suite: 8,267 passed / 1 failed / 14 skipped, this row the one failure).

## Fix (one line)
`Assert.Equal(TestSim.Data.Maps.Length + 2, d.Maps.Length)` (or drop the count: the `NotEqual("raraku", d.Maps[0].Key)` line and the
by-id lookup already prove the point). Rerun `--filter "FullyQualifiedName~D24MapPinQaTests|FullyQualifiedName~MapTextContentTests"` on
the merged tree.

## Notes
- The dev's rows (`MapTextContentTests`) were written for N maps as briefed; only QA's count assertion was not.
- Nothing on the sim or view branch is affected: `main` with sim + view alone is green.
