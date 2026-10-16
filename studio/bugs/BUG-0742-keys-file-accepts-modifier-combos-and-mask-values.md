# BUG-0742: `[keys]` accepts "Shift+Q" / "Ctrl+1" (a masked key that never fires) and `TrySet` accepts non-key `Key` values

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-15-2325, task M6-V8 (QA) |
| System | view: keybinds (`KeyMap.Read`, `KeyMap.TrySet`) |
| Fixed by | acdaeef (M6-V8 fix round 1): `KeyMap.IsPlainKey` refuses modifier masks, `Key.Special`, `Key.Unknown` in `TrySet` and in the file. QA re-check 2026-10-15-2325: `QaM6V8Test` rows green; `QaM6V8R1Test`: every action x every `Key` enum value (4,799 accepted bindings) round-trips; 10 masked / flag values refused by `TrySet` for a command, a digit and a grid key; "Shift+Q", "Ctrl+1", "Alt+A", "Shift+Ctrl+K" refused with one problem. Leftovers (harmless, not reachable from the tab: "Meta+K" reads as K, non-enum codes pass `TrySet`) filed as BUG-0744 (S4) |

## Repro
`& $env:GODOT --headless --path game res://tests/QaM6V8Test.tscn`, rows "'Shift+Q' for order_stop", "'Shift+Q' read from the
file leaves order_stop with no key", "'Ctrl+1' for order_stop", "accepted keys don't round-trip".

## Expected
Brief: "a bad or missing key = the default". A value that is not a single plain key is one problem and the action's default.

## Actual
- `order_stop="Shift+Q"`: 0 problems, bound to `0x2000051` (Q | Shift mask). `Apply` puts that in `PhysicalKeycode`; neither Q
  nor Shift+Q nor S then matches `order_stop`: **Stop has no key** and the log says nothing. `"Ctrl+1"` likewise (`0x10000031`).
  The masked value also slips past the clash check (`Shift+A` is not `A`).
- `KeyMap.TrySet(group_9, Key.Special)` (`0x400000`, a flag, not a key) returns Ok; it is written as an invalid string and reads
  back as `Key9`. Not reachable from the tab today (a key press never carries it), only from code.

## Notes
In `Read`, reject a key with any `KeyModifierMask` bit or `Key.Special` bit (any bit outside `KeyModifierMask.CodeMask`, or `Key.Special`), and have `TrySet`
refuse the same. A lowercase name ("k") reads as K with no problem, which is fine.
