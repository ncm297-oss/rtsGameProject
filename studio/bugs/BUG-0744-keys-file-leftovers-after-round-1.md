# BUG-0744: `[keys]` leftovers after the BUG-0740/0742 fix: "Meta+K" / "Command+K" read clean as K, non-key codes pass `TrySet`, one bad hand-edited key can reset a whole row

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-15-2325, task M6-V8 (QA, fix round 1 re-check) |
| System | view: keybinds (`game/scripts/KeyMap.cs` `Read`, `IsPlainKey`, `ResolveClashes`) |
| Fixed by | |

## Repro
`& $env:GODOT --headless --path game res://tests/QaM6V8R1Test.tscn` prints the notes below (the scene passes; these are
logged, not asserted).

## Expected
docs/03 "The file": a value that is not one plain key name (`"Shift+Q"`, `"Ctrl+1"`) is one problem and the action's
default; `KeyMap.IsPlainKey` refuses anything that is not one key.

## Actual
1. `order_stop="Meta+K"`, `"Command+K"`, `"Cmd+K"` and `"Kp+K"` all read with **0 problems** as plain K
   (`OS.FindKeycodeFromString` on Windows drops a prefix it does not treat as a mask). The key works (K fires Stop), so
   nothing is dead, but the file said a combo and the log says nothing. `"Shift+Q"`, `"Ctrl+1"`, `"Alt+A"`,
   `"Shift+Ctrl+K"` are refused as documented.
2. `TrySet` accepts codes inside `KeyModifierMask.CodeMask` that are not keys (`0x400999`, `0x40FFFF`, `0x5FFFFF`,
   `0x12345`, `0x7FFFFE`: 5 of 5). Written by name, they come back as `Key.None` (lost) and `OS.GetKeycodeString` prints a
   "Unicode parsing error" for each. Code-only: a key press (physical keycode) and the file never produce them.
3. One bad key in a hand-edited file can reset many good ones. Rotating all 15 grid keys by one in the file
   (`card_0="W"` ... `card_4="A"` ... `card_14="Q"`) without moving attack-move: `card_4="A"` clashes with attack-move
   (top-row rule), goes back to T, which then clashes with `card_3="T"`, and so on round the grid: **15 problems, all 15
   grid keys back to the defaults**. With `order_attack_move="K"` in the same file the rotation reads clean. The tab can't
   build such a map (it refuses card_4 = A while attack-move is A), so only a hand edit or a future clash-rule change
   (an older file read under a stricter rule) hits it.

## Notes
1: compare `OS.GetKeycodeString(key)` with the trimmed input (case-insensitive) and refuse a mismatch, or refuse any value
containing `+` after a non-empty prefix. 2: `IsPlainKey` could require `OS.GetKeycodeString(key)` to be non-empty and
`OS.FindKeycodeFromString` of it to give the key back. 3: a design choice (docs/03 documents the repeat); resetting only
the key that clashes with an unmoved default, or the file key read last, would keep more of a hand edit. None of these is
reachable from the Keybinds tab.
