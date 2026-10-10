# BUG-0362: `AnUnknownPageRow_Fails_ButAPendingOneDoesNot` fails once Sandstorm has landed (sim + data merge is red)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-10-0215, task M4-4b-2 (cross-track: the defect is in the data track's D10b test file) |
| System | content tests (`sim/Rts.Sim.Tests/Content/AbilityContentTests.cs`, data track) |
| Fixed by | |

## Repro
1. Make a scratch clone of the sim branch at 9febc27 and run `git merge f74a0f2` (data D10b plus its QA). It merges cleanly.
2. Run `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Content"`. Result: 242 passed, 1 skipped, **1 failed**:
   `Content.AbilityContentTests.AnUnknownPageRow_Fails_ButAPendingOneDoesNot`
   (`Assert.Contains() Failure: Filter not matched in collection`, line 127).
3. The full non-Perf suite on the merge gives 4423 passed, 13 skipped, 1 failed (this test only), in 12 m 40 s.

## Expected
D10b's brief says "a landed entry must be reported, not failed, so the integration stays green". The merged sim + data result
must be green.

## Actual
The test asserts that some report line contains "Sandstorm" and a lowercase "pending". With the real Sandstorm loaded (sim
M4-4b-2), D10b's new report line is "'Sandstorm' has landed in abilities.json; drop it from PendingAbilities to pin it". It has
no lowercase "pending", so the assertion fails.

Neither track's run could see this:
- On the data branch alone there is no Sandstorm entry, so the test passes.
- The data QA checked the allowance only with hand-made stand-ins (studio/qa/2026-10-10-0215-D10b.md, criterion 3).

The sim report's claim checks out: `EveryLoadedStatus_HasItsDocs02Row` fails on the sim branch alone and passes on the merge.

## Notes
The fix belongs to the data track and touches only the test file: accept "landed" as well as "pending" for a pending row, or
assert on the problems only. Until it is fixed, the integration gate (the full non-Perf suite on the merged result) is red. The
sim diff does not cause this; the sim branch's own failure is the expected cross-track one.
