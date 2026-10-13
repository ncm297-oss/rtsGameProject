# BUG-0602: M6-V2 nits: an escaped `{{0}}` map size format loads clean and shows "{0}"; `--skirmish` is no longer the menu's default match

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-12-1930, task M6-V2 (QA) |
| System | view: `UiText` (`setup.map_size_item` check), `LaunchOptions` `--skirmish` vs `SkirmishSetup.MatchArgs` |
| Fixed by | Item 1: 9ec4a56 (`UiText.IsSizeFormat` compares two sizes), `QaM6V2Test.SizeFormatEscapes` (a Check), `QaM6V3Test.SizeFormatVariants`; QA 2026-10-13-0146 verified. Item 2: only the army half fixed (`--skirmish` defaults `--units 0`); the AI seat half is BUG-0630 (S2). Stays open until BUG-0630 closes. |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V2Test.tscn` prints (probes, not failures, until the fix)
   ```
   KNOWN BUG-0602 probe: map_size_item '{{0}}' accepted: items would read '{0}' / '{0}'
   KNOWN BUG-0602 probe: map_size_item '{{0}} x {{0}}' accepted: items would read '{0} x {0}' / '{0} x {0}'
   ```
2. `& $env:GODOT --path game -- --skirmish`: the `Match started:` line ends `seats malazan human, whirlwind human`; the menu's
   Skirmish -> Start with nothing changed ends `seats malazan human, whirlwind ai normal`.

## Expected
1. docs/03 "Implementation (M6-V2)": `UiText` refuses a `map_size_item` "without `{0}`" as one error naming the key. An
   escaped `{{0}}` has no placeholder: every list item would read the same literal "{0}".
2. docs/03 / `LaunchOptions` remarks: `--skirmish` "start[s] the default match at once, skipping the main menu".

## Actual
1. `UiText.IsSizeFormat` checks `format.Contains("{0}")` on the raw text, which `{{0}}` passes, and `string.Format` doesn't
   throw on it. Comparing two formatted sizes (e.g. `Format(f, 128) != Format(f, 256)`) would catch it.
2. Since M6-V2 the menu's default match has an AI seat (the setup always passes `--ai`); `--skirmish` keeps slot 2 human (the
   brief's "absent flags = today's seats" rule), so the smoke's skirmish pass and the menu's default are now different matches.
   Either is defensible; the doc line should say which.

## Notes
Both are data-entry / wording issues; nothing crashes. Row 1 is `QaM6V2Test.SizeFormatEscapes`: when fixed, flip its probe to `Check(refused, ...)`.
