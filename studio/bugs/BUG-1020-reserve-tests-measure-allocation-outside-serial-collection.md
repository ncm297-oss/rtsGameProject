# BUG-1020: `Ai/ReserveTests` measures allocation outside the serial collection: `SerialCollectionTests` is red on the branch

| Field | Value |
| --- | --- |
| Severity | S1 |
| Status | open |
| Found | 2026-10-18-1814, task M5-4g (QA) |
| System | test suite (sim-owned): allocation-measuring tests |
| Fixed by | |

## Repro
1. Fresh clone of `studio/2026-10-18-1814-sim` at 0650492, `dotnet build RtsGame.sln` (0 warnings).
2. `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~SerialCollectionTests"`
   ```
   allocation measured outside the serial collection: ReserveTests.cs
   Failed!  - Failed: 1, Passed: 1, Skipped: 0, Total: 2
   ```
3. The same filter on the diff base f531dd9: `Passed: 2`.

## Expected
Brief criterion 7: everything outside the listed data-owned rows green. `SerialCollectionTests` requires every file that
calls `AllocationProbe.` (or reads `GC.GetAllocatedBytesForCurrentThread`) to put its measuring class in
`[Collection(SerialCollection.Name)]` (BUG-0017: an allocation count taken while other test classes run on the thread pool
can be charged someone else's JIT or type loading and flake).

## Actual
`ReserveTests.AThinkWithAReserve_AllocatesNothing` calls `AllocationProbe.AssertZero` and `ReserveTests` has no
`[Collection(SerialCollection.Name)]` (neither on the class nor on a nested `Serial` class). The meta-test is red on every
run, and the 0 B row itself runs in parallel with the rest of the suite.

The developer's report lists 9 red rows, all data-owned / `Content`; this one is sim-owned and not on criterion 7's list.

## Notes
- Fix pattern used elsewhere (e.g. `QA/AiSaveQaTests.Serial`, `QA/ReserveQaTests.Serial`): move the allocation row into a
  nested `[Collection(SerialCollection.Name)] public class Serial` or put the attribute on the class.
- S1 by the severity table (a red build); trivial to fix.
