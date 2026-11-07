# BUG-1117: DH7MapTextQaTests.AChangedPinnedDescription_IsReported_ById is red once D25's text ships

| Field | Value |
| --- | --- |
| Severity | S1 (red build; a one-constant fix) |
| Status | fixed (fix round 1, 8a1e588: the row writes "A changed sentence that no pin holds." instead of the now-shipped D25 sentence; only line 112 of `DH7MapTextQaTests.cs` changed, so it merges beside the data track's comment edits at lines 74 / 92; green on the Producer's merge rehearsal of sim → view → data) |
| Found | 2026-11-07-0236, task M5-4d-2 |
| System | data QA tests (map text pin) |
| Fixed by | 8a1e588, proof `DH7MapTextQaTests.AChangedPinnedDescription_IsReported_ById` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DH7MapTextQaTests.AChangedPinnedDescription_IsReported_ById"`

## Expected
Green (criterion 1). The row writes a description that differs from the pin into `pale_hills.json` and expects
`PinProblems` to report `pale_hills: description`.

## Actual
The row writes `ProposedPale`, the D25 sentence. M5-4d-2 shipped that sentence and re-pinned `MapTextContentTests.Pinned`
to it, so the edit no longer changes anything, `PinProblems` is empty, and `Assert.Single` fails.

## Notes
- Fix: make the row write a sentence that is neither shipped nor pinned (for example `ProposedPale + " Changed."`, or a
  fixed decoy sentence), and update the `Proposed*` constants' comments to say D25 has shipped. This does not loosen the
  row: it still proves a changed description is reported by id.
- The file is data-track QA (`QA/Content/`), and the data track edited its comments this session. Whichever branch merges
  second should make the swap, to avoid a conflict. The sim track's rider (D25) caused the break.
