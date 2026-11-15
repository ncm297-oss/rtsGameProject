# BUG-1412: QA's new `QaM6V21Test` gives a build warning (CS8600), so the tree that lands builds 0 errors / 1 warning

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-14-1246, at ACCEPT (Producer, scratch clone of the three-way tree) |
| System | view tests (`game/tests/QaM6V21Test.cs`, QA's scene), build hygiene |
| Fixed by | |

## Repro
1. The view branch `studio/2026-11-14-1246-view` at 35cceb9 (the QA commit), or the merged tree.
2. `dotnet build RtsGame.sln --no-incremental`

## Expected
0 warnings / 0 errors (rule 64; M6-V21 criterion 8).

## Actual
```
game\tests\QaM6V21Test.cs(257,30): warning CS8600: Converting null literal or possible null value to non-nullable type.
    1 Warning(s)
    0 Error(s)
```
Line 257: `string why = SlotProblem(views, w, i, type, rgb);` where `SlotProblem` returns `string?`.

## Notes
- The developer's commit 497e151 builds 0 / 0 (QA's baseline); the warning arrives with QA's own file in 35cceb9. QA's
  report states "0 / 0" for the worktree before its files and smoke "with QA's new files in"; smoke builds incrementally
  and quietly, which hides it (the BUG-1380 pattern, this time in QA's file).
- Fix: `string? why = ...`. One token, no behaviour change.
- Ruling at ACCEPT (Producer): BUG-1380 was S2 because the developer's own commit missed a stated criterion and a fix
  round was open. Here the developer's work met criterion 8 and nothing is red (`RtsGame` does not treat warnings as
  errors), so M6-V21 is accepted and this is S3, **first item of the next view brief**. New rule 65: QA's re-check
  builds `--no-incremental` on the commit that holds QA's own files.
