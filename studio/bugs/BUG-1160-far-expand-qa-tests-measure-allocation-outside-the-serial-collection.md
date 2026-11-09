# BUG-1160: `QA/FarExpandQaTests.cs` measures allocation outside the serial collection (the default loop is red on the M5-9b branch)

| Field | Value |
| --- | --- |
| Severity | **S2** (the default test loop is red on the branch: `main` cannot take it; one line to fix) |
| Status | fixed (4ace852; verified by QA re-check round 1, 2026-11-09-0426: guard green, default loop 8,245 / 0 / 18) |
| Found | 2026-11-09-0426, task M5-9b (the Producer's scratch run of the default loop, twice: 8,244 / 1 / 18 in 20 m 14 s and 23 m 56 s) |
| System | Test hygiene: `sim/Rts.Sim.Tests/SerialCollectionTests.EveryFileThatMeasuresAllocation_PutsItsMeasuringClassInTheSerialCollection` (BUG-0024's guard) |
| Fixed by | 4ace852 "M5-9b: fix QA findings (round 1)" (`[Collection(SerialCollection.Name)]` on `FarExpandQaTests`) |

## Repro
1. On `studio/2026-11-09-0426-sim` (f6af427): `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SerialCollectionTests"`.
2. Red: `allocation measured outside the serial collection: FarExpandQaTests.cs`.

## Cause
QA's `QA/FarExpandQaTests.cs` (added in f6af427, "M5-9b: QA inspection") reads the per-thread allocation counter (its
`AnArmedSeatsThink_AllocatesNothing` row and the `Candidate` / `NextStart` 0 B checks) but the class carries no
`[Collection(SerialCollection.Name)]`, which every allocation-measuring file must declare so the measurement never runs beside
the parallel batch (BUG-0024). The sibling `StarvedWaveQaTests` has it. The developer's commit 70dfde7 is green in the default
loop; QA ran the default loop on 70dfde7 and then only the Stress / Soak complement with its own files added, so the guard never
ran over the new file.

## Fix
One line: `[Collection(SerialCollection.Name)]` above `public class FarExpandQaTests` (line 23). Then the default loop
(`Category!=Perf&Category!=Stress&Category!=Soak`) green, 8,245 / 0 / 18. Nothing else on the branch changes.

## Process note (the Producer)
QA's re-check must run the default loop on the commit that includes its own suites (the handoff's "full non-Perf once, at the
re-check, on the fixed commit" meant exactly that). A QA file that measures allocation declares the serial collection.

## Verification (QA re-check round 1, 2026-11-09-0426)
- HEAD 4ace852: `dotnet build RtsGame.sln` 0 warnings / 0 errors.
- `SerialCollectionTests` + `FarExpandQaTests` alone: 13 passed / 0 failed / 2 skipped (the two skips name BUG-1158 / BUG-1159).
- Default loop (`Category!=Perf&Category!=Stress&Category!=Soak`) on HEAD, which includes QA's own suites: **8,245 passed / 0 failed / 18 skipped of 8,263 in 19 m 01 s**.
- `git diff 70dfde7 HEAD` on production paths (`sim/Rts.Sim`, `game/` outside `tests/`, `tools/` outside `qa/`): empty.
