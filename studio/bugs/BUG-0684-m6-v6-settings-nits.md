# BUG-0684: M6-V6 nits: fixed shared test paths under user://, the screen shows the file's VSync under --vsync, Reclaim's scan per call, the bench orders half of seed 6's army

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-13-0916, task M6-V6 (QA) |
| System | view: `game/tests/SettingsTest.cs`, `game/tests/ShellTest.cs`, `game/scripts/SettingsScreen.cs`, `game/scripts/Main.cs`, `game/scripts/GroundDiscPool.cs`, the bench |
| Fixed by | |

## Repro / items
1. **Shared test paths.** `user://` is one folder for every worktree and run (`%APPDATA%\Godot\app_userdata\RtsGame`).
   `SettingsTest` writes and deletes `user://settings-test/settings.cfg` and `ShellTest` `user://shell-test-settings/settings.cfg`,
   fixed names: two scene loops at once (two worktrees, or a loop beside a single-scene rerun) race on the same file and can
   fail each other's `Committed` rows. A per-process folder (`OS.GetProcessId()`, as `QaM6V6Test` does) avoids it.
2. **VSync shown vs in effect.** With `--vsync off` and a file saying on, the Settings screen shows VSync "On" while the run
   has it off (`ShowSettings(Settings, ...)` shows the file; `EffectiveVsync` is off). Correct for the file, confusing on
   screen; a dev flag only.
3. **`CommitSettings(picks, stay: false)` with a running, unpaused match calls `ShowMenu()`**, drawing the main menu over
   the live match (found by calling it directly; no UI path reaches it today, since the screen opens only from the menu or
   the pause menu). A trap for M6-V4 or later callers; `else if (CurrentMatch == null) ShowMenu();` would make it safe.
4. **`GroundDiscPool.Reclaim` scans every node per call** (BUG-0662's fix; it was one pass a frame): a frame where every key
   is new and no node is free costs O(nodes x keys). Allocation-free and small at today's counts (tens of discs), noted in
   case a pool ever holds thousands.
5. **The bench orders only part of seed 6's army** (48 of 100: the block is a tall column only half on the bench's screen;
   BUG-0661's analysis). The re-derived march rows account for it, but a seed-6 bench drives half the load a seed-1 bench does.
   The documented bench is seed 1, so this matters only for cross-seed perf comparisons.

## Expected / Actual
See each item.

## Notes
Line count: `game/` net +1,310 over the diff base (dev code and the dev's tests), against the brief's 1,200; the dev reported
+1,324 and named the 355-line `SettingsTest`.
