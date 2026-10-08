# BUG-0110: ui.json whose root is not an object (`[]`, `null`, a number or a string) loads with no error and blank labels

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-07-1131, task M3-V2 |
| System | UiText loader (game/scripts/UiText.cs), view data |
| Fixed by | f1ef79f (M3-V3, root check in `UiText.Parse`); dev row `ProductionHudTest.UiTextRows`, QA rows `QaV2Test.UiJsonRoots` (strict) and `QaV3Test.UiJsonRows` (8 non-object roots, one error each), verified by QA 2026-10-07-1415 |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaV2Test.tscn` (row `UiJsonRoots`; `-- --strict` fails it), or
   call `UiText.Parse("[]", errors)` directly.

## Expected
Brief M3-V2 scope 1 and docs/03 "Implementation (M3-V2)": the loader is fail-fast. Every missing key is one named
error, and with any error there is no `UiText`, so the match logs `GD.PushError` (the smoke gate fails) and hides the
card.

## Actual
```
ui.json '[]' parsed to a UiText with 0 errors (Stop label '')
ui.json 'null' parsed to a UiText with 0 errors (Stop label '')
ui.json '42' parsed to a UiText with 0 errors (Stop label '')
ui.json '"ui"' parsed to a UiText with 0 errors (Stop label '')
```
`Object(root, "commands", ...)` adds an error only when the parent is an object ("a missing parent was already
reported"). The root has no parent check, so for a non-object root no error is ever added. `Parse` returns a
`UiText` with every label, hint and placement text empty and both menus empty. The card would then show blank
buttons and empty build menus with no error anywhere: the silent default the data rules forbid. Wrong-kind sections
inside an object root (`"commands": []`) are reported correctly.

## Notes
One line fixes it: in `Parse`, if `root.ValueKind != JsonValueKind.Object`, add "ui.json: the root must be an object"
and return null. When fixed, turn the `Known("BUG-0110", ...)` rows into plain `Check`s.
