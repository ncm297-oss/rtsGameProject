# BUG-0740: `[keys]` read-back rejects bindings the Keybinds tab itself makes (an action taking a later action's default, any swap): the player's keys are lost on the next launch

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-15-2325, task M6-V8 (QA) |
| System | view: keybinds (`game/scripts/KeyMap.cs` `Read`) |
| Fixed by | acdaeef (M6-V8 fix round 1): `KeyMap.Read` reads every key, then `ResolveClashes` checks the map as a whole; `SettingsTest` "two keys swapped" now expects the swap. QA re-check 2026-10-15-2325: `QaM6V8Test` rows green; `QaM6V8R1Test` 3,000 seeded tab walks (61,275 accepted `TrySet` calls) all read back as written with 0 problems, orders / 15 grid keys / 9 digits rotated, Tab and 1 swapped, B and V swapped all read clean |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V8Test.tscn` (QA scene), rows "round trip of attack-move=H, hold=J" and
   "round trip of stop=H, hold=S".
2. By hand: Settings → Keybinds, Hold position → J, then Attack-move → H (both accepted, no conflict line). Apply. Quit, start
   the game again: Attack-move is A again, and the log has a settings warning.

## Expected
Brief criterion 2: "`[keys]` round trip". Any map the tab accepts (each step passes `KeyMap.TrySet`) is a valid map, so writing
it and reading it back gives the same map with no problem.

## Actual
```
round trip of attack-move=H, hold=J (reachable on the tab): read attack A, hold J,
  problems [keys.order_attack_move = "H" is already keys.order_hold]
round trip of stop=H, hold=S (reachable on the tab): read stop S, hold H,
  problems [keys.order_stop = "H" is already keys.order_hold; keys.order_hold = "S" is already keys.order_stop]
```
`KeyMap.Read` checks each key, in `Actions` order, against a map that still holds the **defaults** of the actions not read yet.
An action that took the default key of a later action (which itself moved) clashes with that stale default and is reset. Taking
a freed default (H for attack-move once Hold moved, S / H swapped, Q W E R T moved around the grid, digits swapped between
groups) is the most common rebind there is. The dev row `"two keys swapped"` in `SettingsTest` asserts this loss as intended
(2 problems, the defaults).

## Notes
Fix idea: read every present key first into the map (unchecked), then validate the whole map once (reserved / unknown keys per
action back to its default; a real clash among the read keys → that pair, or the section, back to the defaults). Then flip the
`SettingsTest` "two keys swapped" row to expect 0 problems and the swap. The QA scene's two rows are the regression test.
