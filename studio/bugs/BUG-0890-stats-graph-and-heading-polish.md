# BUG-0890: Stats screen polish: short-match time axis has only "0:00", overlapping zero lines and the 1-sample dot are hard to see, one-line headings sit mid-row

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-17-0554, task M6-V11 |
| System | post-match stats screen (`game/scripts/ArmyValueGraph.cs`, `game/scripts/MatchStats.cs`) |
| Fixed by | |

## Repro
1. `dotnet build RtsGame.sln`
2. `& $env:GODOT --path game res://tests/FreeForAllTest.tscn -- --shots <dir> --size 1280x720`; open `ffa-stats-4.png` and `ffa-stats-4-ai.png`.
3. `& $env:GODOT --path game res://tests/QaM6V11Test.tscn -- --shots <dir> --size 1280x720`; open `qa-v11-ai-end.png`.

## Expected
The graph is easy to read at any match length, and the table's headings line up over their numbers.

## Actual
Four small issues, filed together:
1. **Time axis on a short match.** A match under 1 minute (the 0:19 / 0:20 four-seat screens) labels only `0:00` at the
   origin. Nothing marks the right end, so you can't tell the scale. The labels come at whole-minute steps from 0
   (`MinuteSteps`), and the end of the span never gets one.
2. **Overlapping lines.** Players at the same value draw over each other, and the last-drawn colour wins. On the
   four-seat screens three players at 0 show as one blue line on the baseline. Player 1's default colour `#4B4F55` on
   the `#0F0D0D` backdrop is hard to see even when it is on top (the dev noted this). The lines are drawn (`DrawnLines`
   counts them, and `QaM6V11Test` checks they are on the baseline), but you can't see them.
3. **One sample is a 3 px dot.** On a 10-19 s match each player is a 3 px circle (`DrawCircle(pts[0], 3f, ...)`). It is
   nearly invisible, and the grey seat's dot is invisible.
4. **Heading alignment.** One-line headings ("Units lost", "Age II at", "Player") sit at the middle of the two-line
   heading row, not on the bottom line next to "gathered" / "trained". The code asks for `VerticalAlignment.Bottom`.

## Notes
- Out of range, not a bug: with every counter at `int.MaxValue` (10 digits) the 74 px columns grow to 98 px. The table
  still fits 1280 x 720 headless but runs off a 1152 x 648 window (`qa-v11-digits-2147483647.png`). Six digits fit
  (`QaM6V11Test` Layout row).
- Possible fixes: label the span's end (or use second steps below 1 minute); draw a small offset or a legend for
  equal-value lines; a bigger dot, or a short horizontal tick for a single sample; bottom-align the heading cells in
  the grid (`SizeFlagsVertical = ShrinkEnd`).
