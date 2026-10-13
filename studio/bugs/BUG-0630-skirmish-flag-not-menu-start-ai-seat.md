# BUG-0630: `--skirmish` is still not the menu's Start: slot 2 is a human seat, the menu's default is an AI (Normal)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-13-0146, task M6-V3 (QA) |
| System | view: `LaunchOptions.Parse` (`--skirmish`) vs `SkirmishSetup.MatchArgs` |
| Fixed by | |

## Repro
1. `powershell -File tools/qa/smoke.ps1`: the second pass (`-- --skirmish`) prints
   `Match started: seed 1, map 128 x 128, speed 1x, 0 units per player, ... seats malazan human, whirlwind human`.
2. The menu's Skirmish -> Start with nothing changed (`SkirmishSetup.MatchArgs()` with the defaults) passes
   `--seed 1 --map-size 128 --units 0 --colour 1 1 --faction 1 malazan --colour 2 2 --faction 2 whirlwind --ai normal`:
   seats `malazan human, whirlwind ai normal`.
3. `& $env:GODOT --headless --path game res://tests/QaM6V3Test.tscn` prints
   `KNOWN BUG-0630 probe: --skirmish slot 2 is human, the menu's Start AI Normal (...)`.

## Expected
Brief M6-V3 item 1 / criterion 2: "`--skirmish` (the smoke's second pass) does the same as the menu's Start (BUG-0602 item 2)".
BUG-0602 item 2, the bug this item closes, is exactly this difference ("the menu's default match has an AI seat ...;
`--skirmish` keeps slot 2 human"). docs/03 "Implementation (M6-V3)" now says "so the smoke's second pass is the menu's Start".

## Actual
M6-V3 made `--skirmish` default `--units` to 0 (the army half) but left the seat half: `LaunchOptions.Ai` stays null under
`--skirmish`, so the smoke's second pass still never boots an AI seat, and the doc line claiming equality is wrong.
`ShellTest.SkirmishBoot` ("a `--skirmish` boot equal to the defaults") checks units, workers, halls and colours, not seats.

## Notes
Fix sketch: under `--skirmish` without an explicit `--ai`, set `Ai = AiDifficulty.Normal` (the setup's default), like the
`--units` default; or build the `--skirmish` options from `SkirmishSetup`'s defaults. Then flip the QaM6V3Test probe
(`SkirmishEqualsMenuDefaults`) to `Check(menu.Ai == skirmish.Ai, ...)` and add a seats row to `ShellTest.SkirmishBoot`.
The smoke gate would then exercise the AI seat on every run (it takes the 86-tick match it already runs).
