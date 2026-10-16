# BUG-0741: The tab lets a control-group digit share a lower grid key; with a building selected the digit then never recalls its group

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-15-2325, task M6-V8 (QA) |
| System | view: keybinds clash rule (`KeyMap.Clash`), `CommandCard.HandleKey` |
| Fixed by | acdaeef (M6-V8 fix round 1): `KeyMap.Clash` makes a group digit or Tab clash with every grid key; `SettingsTest` refuses 1 for card_5 on the tab. QA re-check 2026-10-15-2325: `QaM6V8Test` "group digit on a lower grid key" green; `QaM6V8R1Test` checks group_1 and Tab refused on all 15 grid keys and every grid key refused 1 and Tab, orders / build keys still share lower grid keys, a freed grid key can be taken, a file `group_1="Z"` / `subgroup_next="G"` / a pre-fix file with `card_5="1"` each read as one problem and the defaults |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V8Test.tscn`, rows "group_1 shares '1' with card_5 / card_10".
2. By hand: Settings → Keybinds, "Command card row 2, column 1" → 1 (accepted). In a match, Ctrl+1 on some units, click the
   Town Hall, press 1.

## Expected
A digit recalls its control group whatever is selected (M6-V7 made "select the hall, press 1 to get the army back" the normal
flow). The tab either refuses a binding that breaks that, or the group key wins.

## Actual
`KeyMap.TrySet(card_5, Key1)` and `TrySet(card_10, Key1)` return Ok (the rule only stops commands sharing a **top-row** grid
key). With the hall selected, 1 goes to the production card (`HandleKey`: "a production card owns the whole grid", empty cells
included): units 0, the hall still selected, no command. Bound to `card_14` (the site's Cancel cell) the same digit would
cancel a selected construction site instead of recalling the group.

## Notes
The "command may share a lower grid key" exception exists for A / S / V / B, which have no meaning on a production card. The
digits (and Tab) do. Simplest fix: treat `group_N` (and probably `subgroup_next`) as clashing with every grid key.
