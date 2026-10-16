# BUG-0743: M6-V8 nits: the screen's Defaults now also wipes the keybinds at once; key buttons hard to see; last-seen amount sampled per frame

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-15-2325, task M6-V8 (QA) |
| System | view: `SettingsScreen` / `Main.CommitSettings`, `FogOfWar.Resources` |
| Fixed by | Items 1-2 by acdaeef (M6-V8 fix round 1), verified by QA 2026-10-15-2325: the screen's Defaults keeps the tab's keys (`SettingsTest` row: K stays attack-move on screen, in effect and in the file); windowed `settings-keybinds.png` shows framed key buttons and "Default keys" at the top of the tab, away from the screen's Defaults. Item 3 (last-seen amount per frame) not addressed: still open for the Producer to fix or wontfix |

## Repro
1. Windowed `& $env:GODOT --path game res://tests/SettingsTest.tscn -- --shots <dir>`, look at `settings-keybinds.png`.
2. Code read: `Main.cs` `DefaultsPressed += () => CommitSettings(new GameSettings(), stay: true)`; `FogOfWar.Resources`.

## Expected / Actual
1. **Defaults wipes the keys.** The bottom "Defaults" button commits `new GameSettings()` immediately (saved and applied), which
   since M6-V8 includes the key map. A player on the General tab who presses Defaults to reset the volume also loses every
   rebind, with nothing on screen saying so. Either keep the current keys there (the tab has its own "Default keys") or say it.
2. **Key buttons barely visible.** On the Keybinds tab the key buttons are flat dark on the dark panel; only the hovered one is
   visible as a button. The bound key letters read fine. The tab also has two "Default..." buttons stacked ("Default keys",
   then "Defaults"), easy to confuse.
3. **Last-seen amount per frame, not per tick.** `UpdateAmounts` runs once per rendered frame on the tick the frame shows. A frame
   that runs two ticks (speed 2x, a hitch) can skip the last tick a node was in sight, so "Last seen" can be one gather older
   than what the player's sight actually covered. Harmless at 1x; noted for completeness (`SeenResources.Update` has the same
   frame cadence by design).
