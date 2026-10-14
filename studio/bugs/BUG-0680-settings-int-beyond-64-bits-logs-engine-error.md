# BUG-0680: A settings file with an integer beyond 64 bits logs an engine ERROR at boot

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-13-0916, task M6-V6 (QA) |
| System | view: `game/scripts/GameSettings.cs` (`Read`, the pre-parse `LineShape` check) |
| Fixed by | ae578c2 (M6-V4): `GameSettings.LineShape` caps a whole number at 18 digits. QA 2026-10-13-1616: both `QaM6V6Test` 64-bit rows are Checks and pass in a child Godot with no ERROR line, now also in process; 9 more boundary rows (19 digits that fit 64 bits, 20 digits + fraction / exponent, 400 digits + fraction, 1e-999 ...) pass. |

## Repro
1. Put `[display]\nwidth=99999999999999999999\n` (or `[audio]\nmaster=-99999999999999999999\n`) in `user://settings.cfg`.
2. Boot the game (or the smoke gate, which reads the real `user://settings.cfg`).
3. Automated: `& $env:GODOT --headless --path game res://tests/QaM6V6Test.tscn` prints
```
QA M6-V6 TEST FAIL: boot read 'int beyond 64 bits' (BUG-0680 for the 64-bit rows): exit 0, 1 settings warnings, errors [ERROR: Cannot represent 99999999999999999999 as a 64-bit signed integer, since the value is too large.]
QA M6-V6 TEST FAIL: boot read 'negative int beyond 64 bits' (BUG-0680 for the 64-bit rows): exit 0, 1 settings warnings, errors [ERROR: Cannot represent -99999999999999999999 as a 64-bit signed integer, since the value is too small.]
```

## Expected
The M6-V6 brief: "a missing or bad file = defaults, logged, never an error"; `GameSettings`' own remark: the text is
checked against the shape `Save` writes *because* Godot logs an engine ERROR for a value it can't parse. The bad key takes
its default with one warning and no ERROR line.

## Actual
The values are right (the key falls back to its default, one `WARNING: Settings:` line), but Godot's `String::to_int`
(`core/string/ustring.cpp:2661`) logs `ERROR: Cannot represent ... as a 64-bit signed integer` while `ConfigFile.Parse` reads
the number. The `LineShape` regex accepts any run of digits (`-?\d+`), so the pre-parse check doesn't catch it. A player file
like this turns the smoke gate red (it fails on any ERROR line), and every log scan.

## Notes
A floating overflow (`edge_pan=1e999`) only logs a Godot `WARNING: Exponent too high`, not an ERROR (also from the parser;
harmless). Fix idea: cap the digit count in `LineShape` (e.g. `-?\d{1,18}` for integers, a longer run only with a fraction or
exponent), or reject long digit runs in the shape check before `Parse`. The two `QaM6V6Test` child rows are the regression test.
