# BUG-1532: M6-SH2 small findings (test cross-check dropped, a relaxed timing row, smoke.ps1 spacing, a BOM)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-15-2136, task M6-SH2 (QA) |
| System | tests, QA tooling |
| Fixed by | |

## Repro / Actual
1. `sim/Rts.Sim.Tests/QA/Content/DR39ReStatementQaTests.cs`: `Assert.True(named)` (the side must be in
   `AiOpeningPairsReportTests.DefeatedBeforeDue`) became `const bool named = true;`. The row's verdict check stays, but the
   cross-check that ties the row to the report's list is gone; nothing now fails if the list and this row disagree again.
   A row that names its own side could say so in its name, or assert `DefeatedBeforeDue` is empty, as
   `AiOpeningPairsVerdictTests` now does.
2. `sim/Rts.Sim.Tests/Ai/AiPlayerTests.cs` `TenStepBuildOrder_*`: a Build / Expand step's "sent at the first think" check
   now skips thinks with no builder free (counting a Laborer standing `Returning` beside a site as free). Reasoned in its
   comment and correct for the new rule, but it is a loosening: a step that waits a think for any other reason while a
   builder is busy also passes now.
3. `tools/qa/smoke.ps1` line 39: `$import =cmd /c ...` (the space after `=` was lost). It parses and smoke runs, but it
   reads like a typo next to the unchanged `$smoke = cmd /c ...`.
4. `sim/Rts.Sim.Tests/Stress/StarvedSaveTwoProcessQaStressTests.cs` gained a UTF-8 BOM in the diff (line 1), a byte-only change.

## Expected
Re-staged rows keep their cross-checks or say in the row which check they gave up; whitespace and encoding unchanged
outside the edit.

## Notes
None affects behaviour. The LFS-pointer gate itself works: on a clone made with `GIT_LFS_SKIP_SMUDGE=1`, smoke stops
before any import with "FAIL: 72 file(s) under game/ are Git LFS pointers ... run 'git lfs pull' first", exit 3.
