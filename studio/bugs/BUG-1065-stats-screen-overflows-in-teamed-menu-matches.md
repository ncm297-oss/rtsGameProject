# BUG-1065: Stats screen: any teamed four-seat match started from the menu pushes Back off a 1152 x 648 screen (the opened-with lines)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-05-1206, task M6-V16 (re-check of the BUG-1063 rider) |
| System | Post-match stats screen (`game/scripts/MatchStats.cs`) |
| Fixed by | |

## Repro
1. Headless: `& $env:GODOT --headless --path game res://tests/QaM6V16Test.tscn -- --strict` fails with (the new
   `MenuFourLabelStats` row; without `--strict` they print as `QA M6-V16 KNOWN BUG-1065 (open)`):
   - `menu teams 1, 2, 3, 0: Back (446, 626), (260, 44) leaves the (1152, 648) screen` (same for 1, 2, 3, 4 and 0, 1, 2, 3)
   - `menu teams 1, 1, 2, 2: Back (446, 628), (260, 44) leaves the (1152, 648) screen` (a 2v2: two label rows)
   - `menu teams 1, 2, 0, 0: Back (446, 627), (260, 44) ...` (a 1v1v2)
   - `menu teams 0, 0, 0, 0` fits (Back at y 587).
2. Windowed: `& $env:GODOT --path game res://tests/QaM6V16Test.tscn -- --shots <dir>`, open `qa-m6-v16-stats-menu-1230.png`:
   the title sits on the top edge and only the top ~22 of Back's 44 px show.
3. By hand: Skirmish, Add player twice, Team 1 / Team 1 / Team 2 / Team 2, Start, Esc, Stats.

## Expected
The stats screen fits 1152 x 648 for every setup the menu allows (BUG-1063's Expected; docs/03 "Implementation (M6-V16)"
now says "four label rows ... still fit 1152 x 648").

## Actual
A match started from the setup has three AI seats, so the stats screen carries three "opened with" lines (M6-V13, ~24 px
each) under the table. `TeamsTest` and `QaM6V16Test.StatsLayouts` stage their teamed screens with human (or executioner)
seats, which have no opened-with lines, so they fit. BUG-1063's fix (the graph gives up 30 px per label row past two)
covers only those: with the three lines, every teamed four-seat screen is 20-24 px too tall, including the plain 2v2
(two label rows, untouched by the fix), which was already over when M6-V16 was accepted. Usable (Back is half visible
and clickable, Esc closes), hence S3, like BUG-1063.

## Notes
BUG-1063 should stay open (or be closed into this one): its fix is right for its repro but not for a menu match.
Fix ideas: count the opened-with lines in the graph's shrink (the graph's height is the only slack), or put each
opened-with text in the seat's row. Flip `QaM6V16Test.MenuFourLabelStats`' `Known("BUG-1065", ...)` to a plain check when fixed.
