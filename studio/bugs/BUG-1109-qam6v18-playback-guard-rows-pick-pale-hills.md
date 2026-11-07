# BUG-1109: `QaM6V18Test.PlaybackGuards` is red on `main` since Pale Hills shipped: its "other size" forgery is Pale Hills' real size

| Field | Value |
| --- | --- |
| Severity | S3 (test-only, pre-existing on `main` at 1e9e1e8; the scene loop is red regardless of M5-4d) |
| Status | fixed (M6-VH5, 2026-11-06-0926: `QaM6V18Test.OtherSide` forges the side the first map file doesn't have; verified by the Producer on the merged tree, `QaM6V18Test` PASS) |
| Found | 2026-11-06-0926, task M5-4d (QA full, while checking BUG-1108 at the diff base) |
| System | `game/tests/QaM6V18Test.cs` (view track) |
| Fixed by | M6-VH5 ca99057 (`QaM6V18Test.cs` `OtherSide(_map.Size)`) |

## Repro
1. Clone 1e9e1e8 (the M5-4d diff base, i.e. `main` with M6-S8's maps), build, `powershell -File tools/qa/scene-loop.ps1 -Filter QaM6V18Test`.

## Expected
`StartPlayback` refuses a map recording forged to another size and one forged with too small a resource capacity (`InvalidHeader`, no sim).

## Actual
```
QA M6-V18 TEST FAIL: StartPlayback of a recording with an other size: None, sim built
QA M6-V18 TEST FAIL: StartPlayback of a recording with an small capacity: InvalidHeader, sim built
```
`_map = _data.Maps.FirstOrDefault()` (line 59) was Raraku (128) when written; with M6-S8 the first map by id is `pale_hills` (256), and the
"other size" forgery sets 256 x 256: Pale Hills' own size, so `SimRunner.StartPlayback` correctly accepts it and builds a sim. The next row's
refusal is right but `runner.Simulation` is still the sim the previous row built, so "sim built" fails it too. `SimRunner`'s guard is fine.

## Fix
Pick the map by id (`raraku`) or forge a size that is not `_map.Size` (e.g. `_map.Size == 256 ? 128 : 256`), and reset the runner between rows.
Same pattern as BUG-1103 (a test counting on one shipped map). M6-VH5 ("the loop on the merged `main` with three maps") is the natural home.
