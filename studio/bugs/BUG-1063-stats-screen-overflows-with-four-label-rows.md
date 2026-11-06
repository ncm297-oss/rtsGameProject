# BUG-1063: Stats screen: four label rows (three teams and a no-team seat, or four teams) push Back off a 1152 x 648 screen

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (as filed; the teamed menu-match overflow it did not cover is BUG-1065) |
| Found | 2026-11-05-0805, task M6-V16 (re-check, round 1) |
| System | Post-match stats screen (`game/scripts/MatchStats.cs`) |
| Fixed by | 8c0ad73 (M6-V16 rider, 2026-11-05-1206): `MatchStats.GraphHeight` 220 − `LabelRowHeight` 30 × label rows past `LabelRowsRoom` 2; `QaM6V16Test.StatsLayouts`' two rows plain checks (the 2026-11-05-1206 scene loop 76 / 76). Status field corrected by the Producer at the 2026-11-05-1716 PLAN (the 1206 log already listed it fixed) |

## Repro
1. Headless: `& $env:GODOT --headless --path game res://tests/QaM6V16Test.tscn -- --strict` fails with
   `teams 0, 1, 2, 3: Back (445, 616), (260, 44) leaves the (1152, 648) screen` (both passes) and
   `teams 4, 3, 2, 1: Back (445, 616), (260, 44) leaves the (1152, 648) screen`. Without `--strict` the rows print as
   `QA M6-V16 KNOWN BUG-1063 (open): ...` and the scene passes.
2. Windowed: `& $env:GODOT --path game res://tests/QaM6V16Test.tscn -- --shots <dir>`, open
   `qa-m6-v16-stats-3-teams-and-one.png`: the title is pushed up to the top edge and the Back button is cut through the
   middle by the bottom of the window (only its top 32 of 44 px show). `qa-m6-v16-stats-4-teams.png` is the same.
3. From the menu: a 4-slot setup with Team 1 / Team 2 / Team 3 / None (or Team 1-4), play, open Stats.

## Expected
The stats screen fits 1152 x 648 for every setup the menu allows (docs/03 "Implementation (M6-V16)" tests the grouped
screen with "fits 1152 x 648"; `StatsScreenCheck.Fits` is the check). Four seats can carry at most four label rows.

## Actual
Each label row adds about 29 px. Headings + 4 player rows + 2 labels (2v2, 1v1v2) fit; 4 labels do not:
- Teams 1, 2, 3 and a no-team seat: the "No team" row from the BUG-1061 fix is the fourth label. Before that fix this
  setup had three labels and fit, so the fix made it overflow.
- Teams 1, 2, 3, 4 (every seat its own team, i.e. a free-for-all with labels): four "Team n" rows. This overflowed in the
  first round too; neither TeamsTest nor the first QA round staged it.
The screen is still usable (Back half-visible and clickable, Esc closes it), hence S3.

## Notes
Options for the developer: shrink the graph's minimum height when the table grows, put the label in the player column of
the group's first row instead of a row of its own, or treat a match where every team has one seat as a free-for-all
(no labels). `QaM6V16Test.StatsLayouts` stages both setups; flip its `Known("BUG-1063", ...)` row to a plain check once
fixed.
