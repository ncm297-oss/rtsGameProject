# BUG-1183: D-R35 nits: a 145-character summary line in AiAdaptsContentTests; D21OpeningsQaTests' seed-19 comment quotes pre-M5-H6 figures

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-09-2226, task D-R35 |
| System | Tests: `Content/AiAdaptsContentTests`, `QA/Content/D21OpeningsQaTests` (comments only) |
| Fixed by | |

## Repro
1. `sim/Rts.Sim.Tests/Content/AiAdaptsContentTests.cs` line 19: the re-wrapped `<summary>` line is 145 characters
   ("non-worker enemy slots, and the BUG-1156 sentence's claim (no opening's Age I mix lists a unit that answers an enemy
   Line) holds in the file."). The lines around it were wrapped at 120; the brief asked for lines under 120.
2. `sim/Rts.Sim.Tests/QA/Content/D21OpeningsQaTests.cs` lines 304-305 (the `KnownBeatenAlive` comment): "522 gold, 210
   wood ... the Whirlwind 1 wave, Age II 10:21". Today's run prints gold 502, wood 270, Age II 10:34 (the Whirlwind
   table's re-printed cell). The comment is dated (the 2026-11-07-0236 merge), so it is history, not a wrong claim; the
   developer flagged it.

## Expected
The summary wrapped at 120 like its neighbours; the comment either left as dated history or given a "(today 10:34)" note.

## Actual
As above. Nothing fails.

## Notes
Comment-only. Fold into the next data-track hardening task.
