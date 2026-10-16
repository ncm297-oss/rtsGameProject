# BUG-0770: test scenes that boot `Main` without a `SettingsPath` read the player's `settings.cfg`, so a rebind on the desk can fail them

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-16-0847, task M6-VH2 (QA) |
| System | view tests: `game/tests/ShellTest.cs` (`Flow`, `SkirmishBoot`), `AiSeatTest` (dev-owned; QA's `QaM6V2Test`, `QaM6V3Test`, `QaM6V7Test`, `QaShellTest`, `QaVh1Test` fixed by QA in M6-VH2) |
| Fixed by | |

## Repro
1. `grep -ln "Main.tscn" game/tests/*.cs` and check each for `SettingsPath`: `AiSeatTest`, `QaM6V2Test`, `QaM6V3Test`,
   `QaM6V7Test`, `QaShellTest`, `QaVh1Test` never set it, and `ShellTest` sets it only in `VsyncOverride` (`Flow` and
   `SkirmishBoot` don't).
2. `Main.SettingsPath` defaults to `GameSettings.DefaultPath` (`user://settings.cfg`, the player's file). `Main._Ready` loads
   it and `SettingsApplier.Apply` calls `s.Keys.Apply()`, so the player's `[keys]` become the `InputMap` for that scene.
3. `QaM6V7Test` presses `Key.Key5` / `Key.Key6` for control groups and `QaShellTest` / `ShellTest` press keys too. Once the
   owner rebinds `group_5` on the Keybinds tab (the watch-out says he hasn't tried it yet), those presses no longer fire the
   action and the scenes fail on his machine only. His file has no `[keys]` section today, so every scene passes now.

Not run against a changed player file on purpose: QA doesn't edit the owner's `settings.cfg`. The evidence is the code path
above.

## Expected
docs/03 "M6-V6": "Tests point `Main.SettingsPath` at their own file and never touch the player's", which since BUG-0684 item 1
means a per-process folder. The player's settings shouldn't be able to change a test's outcome.

## Actual
Seven scenes read the player's file. Nothing writes it: QA checked that `settings.cfg`'s md5 was unchanged across two
parallel `SettingsTest` / `ShellTest` pairs and two scene loops.

## Notes
The fix is one line per scene: `main.SettingsPath = $"user://<scene>-{OS.GetProcessId()}/settings.cfg"` (or a missing file,
which reads as the defaults) before `AddChild`. The `Qa*` scenes are QA's own files, fixed below. Also left in `%APPDATA%/Godot/app_userdata/RtsGame/`: the old shared folders `settings-test/` and
`shell-test-settings/` from before BUG-0684 item 1, which no test uses any more and which are harmless to delete.

## QA fix of its own scenes (2026-10-16-0847)
QA pointed `QaM6V2Test`, `QaM6V3Test`, `QaM6V7Test`, `QaShellTest` and `QaVh1Test` at a missing per-process file
(`user://qa-<scene>-<pid>/settings.cfg`, which reads as the defaults and is never written). All five PASS, the log says
"Settings: no user://qa-m6v7-16388/settings.cfg yet; using the defaults.", and no folder is left behind. Also QA's
`QaM6V8Test` now uses a per-process file (`user://qa_m6v8_settings-<pid>.cfg`). Still to fix (the view track's files):
`ShellTest.Flow` / `SkirmishBoot` and `AiSeatTest`.
