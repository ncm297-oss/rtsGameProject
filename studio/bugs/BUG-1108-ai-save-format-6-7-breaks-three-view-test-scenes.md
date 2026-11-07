# BUG-1108: Save formats 6 / 7 (M5-4d) turn three view test scenes red: they expect an AI-seat menu save to be format 4 / 5

| Field | Value |
| --- | --- |
| Severity | S2 (cross-track merge gate: the Godot scene loop goes red on `main` at the merge; test-only, the game's behaviour is right) |
| Status | fixed (at the 2026-11-06-0926 merge: the Producer's patch `studio/qa/2026-11-06-0926-bug-1108-view-tests.patch` applied by the conductor after the view branch; rehearsed on the merged tree, the three scenes PASS) |
| Found | 2026-11-06-0926, task M5-4d (QA full; the conductor asked for this check) |
| System | `game/tests/` (view track's files): `QaM6V18Test.cs`, `QaM6V18bTest.cs`, `StartingResourcesTest.cs` |
| Fixed by | the patch: `QaM6V18Test` line 98 / `QaM6V18bTest` MixedFormats rows / `StartingResourcesTest` line 286 compare to `SaveFormat.HuntMaplessVersion` (6, a generated AI match) and `SaveFormat.CurrentVersion` (7, a map-file AI match) |

## Repro
1. On `studio/2026-11-06-0926-sim` (b7a3f83): `dotnet build RtsGame.sln`, then
   `powershell -File tools/qa/scene-loop.ps1 -Filter QaM6V18Test` (and `-Filter QaM6V18bTest`, `-Filter StartingResourcesTest`).
2. The same three scenes at the diff base 1e9e1e8: `QaM6V18bTest` and `StartingResourcesTest` PASS; `QaM6V18Test` fails only on the two
   pre-existing BUG-1109 lines.

## Expected
Every scene green after the merge (the scenes check the save format the menu writes; M5-4d moved it on purpose).

## Actual
Menu matches seat an AI, so M5-4d saves them as format 6 (generated map) / 7 (map file), not 4 / 5:
- `QaM6V18Test` line 98: `the generated save is format 6 (want 4) with map id ''` (compares to `SaveFormat.MaplessVersion`). Its map-file
  check (line 118, `SaveFormat.CurrentVersion`) passes only because `CurrentVersion` moved to 7 too.
- `QaM6V18bTest`: `generated ffa: save format 6 (want 4)`, `generated teamed: save format 6 (want 4)`, `generated high: save format 6 (want 4)`,
  `raraku standard: save format 7 (want 5)`, `raraku high teamed: save format 7 (want 5)` (5 lines).
- `StartingResourcesTest` line 286: `the High save's summary reads 'high' (format 6)` (hard-coded `FormatVersion == 4`).
`SaveLoadTest`, `ShellTest`, `QaM6V16Test`, `TeamFilesTest` (the other scenes that read save/replay formats) pass on the branch.

## Fix
View-side expectation updates only (the sim track may not edit `game/tests/`): compare to
`SaveHeader.VersionFor(mapFile, hasAiSeat)` semantics, i.e. `SaveFormat.HuntMaplessVersion` (6) for a generated AI match and
`SaveFormat.CurrentVersion` (7) for a map-file AI match; `QaM6V18bTest`'s mixed-format Load Game rows should gain a format 6 / 7 save.
The conductor applies it at the merge or the view track takes it next session; until then the loop is red on these 7 lines.

## Notes
- Replay formats are unchanged (6 / 8 / 9 lines in these scenes pass).
