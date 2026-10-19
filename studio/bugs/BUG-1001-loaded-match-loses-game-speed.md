# BUG-1001: A loaded match runs at 1x whatever speed the match was set up at, and the docs don't say so

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-18-1236, task M6-V14 |
| System | Save / load resume path (view: `Match.StartLoaded`, `Main.LoadSavedGame`) |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V14Test.tscn`: the tick-0 section starts a `--speed 2` match,
   saves, quits, loads from Load Game; the log prints
   `QA NOTE: the match ran at 2x; loaded from Load Game it runs at 1x (the speed is not in the save)` and the load line
   `Save loaded: ..., speed 1x, ...`.
2. By hand: Skirmish setup, game speed Fast, play, Esc, Save, Quit to menu, Load Game, Load: the match resumes at 1x.

## Expected
Game speed is part of the match's setup (docs/02 "Game flow" 2) and the brief's QA focus lists it among what a resume
keeps. Either the loaded match runs at the speed it was saved at, or docs/02 "Game flow" 6 / docs/03 "Implementation
(M6-V14)" say that a load resumes at normal speed (as they already say for the `--colour` picks).

## Actual
`Main.LoadSavedGame(path)` from Load Game passes `LaunchOptions.Parse([])`, so `Match.StartLoaded` sets no speed and
`SimRunner.GameSpeed` stays 1.0. The sim save holds no speed (it's a view setting). docs/03 mentions only that `--speed`
counts for `--load`; docs/02 6 is silent.

## Notes
The save format is frozen (rule 36), so carrying the speed means a view-side sidecar (e.g. a line in the `.replay`
beside it is not available for loaded-match saves) or a later format field; the cheap fix is the doc line. Same family:
`--colour` picks (documented) and `Match.DefeatedAt` (documented known limit).
