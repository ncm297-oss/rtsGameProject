# BUG-0681: The boot's settings window size overrides Godot's `--resolution`, so the bench always runs at the stored size

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-13-0916, task M6-V6 (QA) |
| System | view: `game/scripts/Main.cs` (`_Ready` boot order), `game/scripts/SettingsApplier.cs` (`ApplyWindow`); the bench (docs/03 "Implementation (M2-7)") |
| Fixed by | |

## Repro
1. No `user://settings.cfg` (or one with the default 1152 x 648).
2. `& $env:GODOT --path game --resolution 1600x900 -- --bench 2 --units 0 --mute`
```
Settings: no user://settings.cfg yet; using the defaults.
Bench running for 2 s (16 steps per 10 s loop), viewport 1152 x 648, vsync Enabled at 120 Hz, display Windows
```

## Expected
Before M6-V6 the window took the `--resolution` size (docs/03 "Implementation (M2-7)" records its bench figures with
`--resolution 1920x1080`, the window clamped to 1920 x 1061 by the taskbar). Dev and test flags win over the file for the run
(the brief says so for `--vsync` and `--mute`; the same reasoning holds for the engine's own window flags).

## Actual
`SettingsApplier.ApplyWindow` runs at boot and, in Windowed mode, sets the window to the stored size whenever it differs
(`DisplayServer.WindowGetSize() != size`), so `--resolution` is undone: the bench viewport is 1152 x 648 whatever the flag.
The same applies to Godot's `--fullscreen` / `--maximized` and the window position flags (the window is re-centred), and the
bench now also depends on the player's quality preset (Low renders 3D at 75 %), so the documented bench figures can't be
reproduced from the documented command line, and a perf comparison across machines depends on each one's settings file.

## Notes
Fix ideas: skip the window part of the boot apply when the engine was given a window flag (`OS.GetCmdlineArgs()` holds
`--resolution`, `--fullscreen`, `-f`, `--maximized`, `--position`, `-w`), or let a dev / bench run (`LaunchOptions.HasMatchFlag`)
ignore the file's window and quality entries, as `--vsync` already overrides the file. Document whichever rule in docs/03
"Implementation (M6-V6)" beside the boot order.
