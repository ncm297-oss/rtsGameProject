# BUG-1921: shadow.md's D-H11 paragraph on the limit reader contradicts its own example and leaves out a rule

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-27-0817, task D-H11 |
| System | docs (`docs/factions/shadow.md`, paragraph before the Owner review table, about line 413) |
| Fixed by | |

## Repro
1. Read the sentence added by D-H11 in `docs/factions/shadow.md`: "the number alone, "of them" or the unit's own name,
   said to be alive ("At most 7 War-dogs at once" also reads)".

## Expected
The page describes what `UnitLimitText.Stated` does: the unit's name or "of them" plus an alive word **or "at once"**,
and a third path where any other noun followed by an alive word also counts ("No more than 7 Hounds live at once").

## Actual
- The example has no alive word, so it breaks the "said to be alive" rule written just before it. The code accepts it
  because "at once" is enough after the unit's name or "of them", and the page doesn't say so.
- The "another noun then said to live" path isn't mentioned. That path is where BUG-1920's false readings come from.
- The example names the Teblor War-dog on the Shadow page. It's harmless but reads oddly next to the Hound's sentence.

## Notes
`UnitLimitText`'s class `<summary>` and the `Stated` `<remarks>` do describe all three paths correctly; only the page
paragraph is short.
