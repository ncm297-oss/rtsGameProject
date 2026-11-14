# BUG-1323: `QaM6V20Test` row 8 ("the object count climbs on every restart") fails on garbage-collection timing: red 5 of 6 runs on the merged tree, with nothing leaking

| Field | Value |
| --- | --- |
| Severity | S3 (a test defect in a QA regression scene; the product does not leak) |
| Status | open |
| Found | 2026-11-13-1118, task M6-V20 (the Producer's verification at ACCEPT, on the three-way tree sim 3f67a6f + view 19cdacf + data 302c225) |
| System | view tests (`game/tests/QaM6V20Test.cs`, `FourPlayersAndRestarts`) |
| Fixed by | |

## Repro
1. A clone of the merged tree, built and imported (`tools/qa/smoke.ps1`).
2. `& $env:GODOT --headless --path game res://tests/QaM6V20Test.tscn`, five times, alone.

## Expected
`QA M6-V20 TEST PASS` every run: four start / quit cycles of a four-player skirmish leave the counts flat.

## Actual
Five of six runs (one in the scene loop, three of four alone, one on a second clone) end:

```
restart 0: nodes 555, resources 97, objects 3638, orphans 0
restart 1: nodes 555, resources 97, objects 3648, orphans 0
restart 2: nodes 555, resources 97, objects 3688, orphans 0
restart 3: nodes 555, resources 97, objects 3708, orphans 0
QA M6-V20 TEST FAIL: (8) the object count climbs on every restart: 3638, 3648, 3688, 3708
```

The sixth read 3638, 3648, 3633, 3671 and passed. Nodes, resources and orphans are exactly flat every time.

## Why it is the test and not a leak
The object count includes C# wrapper objects that wait for the .NET garbage collector. The row fails when the count
rises three times in a row over only four samples, which is what happens whenever no collection falls inside the first
four cycles. The same scene run for **16 cycles** on a scratch copy (the two object-count checks switched off, nothing
else changed) saws between 3,623 and 3,708 and never trends:

```
3637 3648 3688 3708 3625 3648 3648 3623 3671 3707 3635 3648 3633 3671 3671 3633
```

QA's own runs on the view branch alone happened to catch a collection ("3,631-3,688, wandering without climbing").

## Notes
- **Ruling (Producer, rule 57):** M6-V20 lands with this row red; the scene loop's expected result is **100 of 101, red:
  `QaM6V20Test` alone**, until the fix, which is the first item of the view's next brief (M6-VH7).
- Fix options: force a collection before each sample (`GC.Collect()`, `GC.WaitForPendingFinalizers()`, `GC.Collect()`,
  then two frames) and require the count flat within a small band; or run 12+ cycles and compare the minimum of the
  first half with the minimum of the second. Keep the exact checks on nodes, resources and orphans.
- The scene's other rows (the fallbacks, slot reuse, the fuzz, fog, four colours, save / replay) passed in every run.
