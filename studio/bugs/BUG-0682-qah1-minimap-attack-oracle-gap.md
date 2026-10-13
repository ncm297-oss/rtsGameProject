# BUG-0682: QaH1Test's oracle wants a Move for every minimap right-click and counts only command kinds 0-7

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-13-0916, task M6-V6 (the dev reported it; red on base dde71a4 too) |
| System | QA test scene `game/tests/QaH1Test.cs` (QA-owned) |
| Fixed by | QA, session 2026-10-13-0916 (this branch): `QaH1Test.MiniEnemyDotAt` + `Issued()` over every `CommandKind` |

## Repro
1. `powershell -File tools/qa/scene-loop.ps1 -Filter QaH1Test` at dde71a4 or 442a3e6:
```
QA M2-H1 TEST FAIL: seed 2 step 652 (H -> miniRight): Move x0, expected 5
```

## Expected
Since M4-V4 a minimap right-click on a live, shown enemy unit's dot is one Attack per selected unit (`Minimap.CommandAt`,
docs/03 "FogViewTest" row); the QA oracle should expect that.

## Actual
The fuzz oracle still expected a Move for every minimap right-click (the M2-H1 rule), and `Issued()` counted kinds 0-7 only, so
the Attack (kind 16) was invisible and read as "Move x0". The product was right; the test was stale. It only fires when the
random click lands on a shown enemy dot (seed 2 step 652 since the M5-4a start geometry).

## Notes
Fixed in the test: `Issued()` now covers every `CommandKind` (so `ExpectOnly` / `ExpectNoCommands` also see Attack, UseAbility,
Build and the rest), and the minimap right-click predicts the Attack case before the click with the minimap's own rule
(`Raster.DrawnEnemyDotAt` on the last drawn dots, the unit alive and `FogView.ShowsUnit`). Proof: `QaH1Test PASS` (135 s, seeds
as before) with the change; FAIL at step 652 without it.
