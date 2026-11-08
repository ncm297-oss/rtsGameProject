# BUG-1126: TooltipWrap still makes a blank line in the middle: a space beside a data line break, or a double space after a full line

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-07-0236, task M6-VH6 (QA, standard) |
| System | view: `game/scripts/TooltipWrap.cs` |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaVh6Test.tscn` prints
   `e60 + " \n" wraps to 'eee...e\n\n'; "\n " + w61 to '\n\nwww...w'` and
   `wrap edge fuzz: 20000 strings, 0 failures, 3282 with a blank line the wrap made (not at an edge space)`.
2. Three shapes, each a blank tooltip line the wrap added (not one the data had):
   - a space right before a data `\n`, at the width: `Wrap(new string('e', 60) + " \n")` = 60 e's + `"\n\n"`;
   - a space right after a data `\n`, before a long word: `Wrap("\n " + new string('w', 61))` = `"\n\n"` + 61 w's;
   - a double space after a line of exactly the width, before a long word: `Wrap(e60 + "  " + w61)` = `e60 + "\n\n" + w61`
     (the first space breaks, the second becomes the next line's only character and then breaks too).

## Expected
BUG-1115's expectation for item 3: "Text unchanged and no empty lines." M6-VH6 closed the text's own edges only (the
brief's scope); the same rule applies at each data line's edges and at a break.

## Actual
As above. No shipped description has a double space, an edge space or a line break, and the data loader doesn't reject
them, so nothing shows today.

## Notes
- Possible fix: apply the edge rule per data line (skip spaces right after a `\n` or `lineStart`, and right before a `\n`),
  and never break at a space that would leave the new line blank. Or the data checker rejects double / edge spaces in
  `description`.
- The dev's report named the first shape ("a data space before a mid-text \n").

## Producer ruling (2026-11-07-0926 ACCEPT, for the view's next hardening)
M5-V2's developer found the fix conflicts with `QaVh5Test`'s wrap fuzz (`e60 + "  " + w61`: every output breaks one rule or the other). Ruling: **at a line break the wrap consumes every adjacent space** (the wrap never makes a blank line; a data `
` keeps its own line), and **a line may exceed the width only when it is a single word longer than the width**. Amend `QaVh5Test`'s fuzz to that rule (a line over the width must contain no space), keep `QaVh6Test`'s blank-line count at 0, and leave the data checker alone (double spaces in text are the author's).
