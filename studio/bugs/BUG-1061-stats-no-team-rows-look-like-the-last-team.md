# BUG-1061: Stats screen: the no-team rows sit under the last team's label and read as members of that team

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-11-05-0805, task M6-V16 |
| System | Post-match stats screen (`game/scripts/MatchStats.cs`) |
| Fixed by | M6-V16 fix round 1 |

## Repro
1. `powershell -File tools/qa/scene-loop.ps1 -Filter QaM6V16Test` passes (it checks `DisplayOrder` and `TeamLabels`, which are right).
2. Run it windowed to see the screen:
   `& $env:GODOT --path game res://tests/QaM6V16Test.tscn -- --shots <dir>`, then open `qa-m6-v16-stats-1v1v2.png` and
   `qa-m6-v16-stats-3-slots.png`.
   - 1v1v2 (teams 1, 1, 0, 0, the shape docs/02 "Game flow" 2 names): the screen shows one label, "Team 1", and then four
     rows in one block: You, the ally, **then the two free-for-all seats (Crimson, Cobalt) with no break**.
   - 3 slots, teams 1, 0, 0: "Team 1", then all three rows in one block.
   - The same happens in the setup's own example from the menu: slots None / None / Team 1 / Team 1 shows "Team 1" over all
     four rows.

## Expected
Acceptance criterion 3 (M6-V16 brief): "the stats screen grouped under 'Team 1' / 'Team 2' with **none-team seats
ungrouped**"; docs/03 "Implementation (M6-V16)": "the players with no team" come after the teams, ungrouped. The player has
to be able to tell from the screen which seats were allies.

## Actual
`MatchStats.Show` adds a label row only before a team's first seat (`if (team != 0 && team != shownTeam)`), and nothing
before the first no-team seat. The no-team seats follow the last team's rows directly, so on screen they read as members
of that team. In the 1v1v2 above, Crimson and Cobalt (enemies of each other and of Team 1) look like Team 1 players. The
order (`RowOrder`) is right; only what the grid shows is wrong. `TeamsTest.CheckGrouped` walks the grid and checks the label
rows, but never checks that the first no-team row is set off from the last team's rows, and its only mixed case (`RowOrder`
of a 1v1v2) is checked without the screen.

## Notes
Smallest fix that stays data-driven: a label row before the first no-team seat when the match has a team (a new `ui.json`
string, e.g. `match_over.no_team` "No team"), or put the no-team seats first, before any label. Either needs the
`TeamsTest` layout check extended to a mixed match; `QaM6V16Test` already stages both shapes and saves the screenshots.

## Fix (M6-V16 round 1)
`MatchStats.ShowFor` adds a label row, `match_over.no_team` ("No team", new `ui.json` key), before the first seat with no
team when a team row came before it (`MatchStats.NoTeamLabel`); a free-for-all and an all-teamed match have none. docs/02
"Game flow" 4 and docs/03 "Implementation (M6-V16)" say so. Regression: `TeamsTest.MixedStats` opens the stats from the
pause menu on a 1v1v2 (teams 1, 1, 0, 0), teams 0, 1, 0, a free-for-all and a 2v2; `CheckGrouped`'s grid walk now fails a
player row that sits under another group's label (or none when the match has a team). With the label row disabled it
failed with "1v1v2: player 2 (team 0) shows under the team 1 label" (and the 0, 1, 0 rows likewise). Windowed screenshots
`qa-m6-v16-stats-1v1v2.png`, `teams-stats-1v1v2.png`, `teams-stats-3-slots.png` show "Team 1", its rows, then "No team"
over the others.

## QA re-check (2026-11-05-0805, round 1): verified
`QaM6V16Test` now reads the grid row by row (`CheckLayout`): "T1 P0 P1 N P2 P3" (1v1v2, from the banner), "T1 P0 N P1 P2"
(teams 1, 0, 0), "T1 P1 T2 P2 T3 P3 N P0" (teams 0, 1, 2, 3, opened twice: no stale label rows), "T2 P3 N P0 P1 P2"
(teams 0, 0, 0, 2), "T1 P3 T2 P2 T3 P1 T4 P0" (teams 4, 3, 2, 1) and "P0 P1 P2 P3" (free-for-all, opened twice), with every
label row's other cells empty. Screenshots `qa-m6-v16-stats-1v1v2.png` / `-3-slots.png` show "No team" over the free-for-all
seats. Follow-up: the extra label row makes teams 1, 2, 3 + a no-team seat overflow 1152 x 648 (BUG-1063, S3).
