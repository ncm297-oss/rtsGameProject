# BUG-1062: M6-V16 team nits: Load Game hides seats 3-4's team marks, a one-team match from the menu never ends, ally-ghost pick untested

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-05-0805, task M6-V16 |
| System | Load Game list, skirmish setup, enemy picker tests |
| Fixed by | |

## Repro
1. **Load Game: seats 3-4 cut off.** `& $env:GODOT --path game res://tests/TeamFilesTest.tscn -- --shots <dir>`, then open
   `team-files-load-game.png`: each 4-seat row's Players cell ends "... The Whirlwind (Normal) (T1), M…", so the
   "(T2)" marks of seats 3 and 4 never show at 1152 x 648. Only the hover tooltip shows the full text (the developer
   reported it). Criterion 3's "(T1)" is met, but on a 2v2 row you can't see the other side's marks.
2. **A one-team match from the menu never ends.** Setup: put every slot on Team 1 (or two slots on Team 1) and press Start.
   `World.MatchOver` needs two sides that built, so the match never ends and no banner ever comes; the AI seats have no
   enemy. The setup accepts it without a word. docs/03 "Implementation (M6-V16)" writes this down ("that match never
   ends ... as in the sim"), but docs/02 "Game flow" 2 doesn't, and nothing on the screen warns the owner.
   `TeamsTest.TeamFlagStart` starts exactly this match (`--team 1 1 --team 2 1`) and only checks the teams.
3. **The ally-ghost branch of `SelectionController.EnemyAt` has no test.** docs/03 says "a remembered building (ghost) of an ally
   in front of the cursor is no target either" (`EnemyTable.IsEnemyOwner(fog.Ghosts[ghost].Owner, ...)`), but no scene or
   xUnit test stages an ally's ghost. Reverting that line wouldn't fail anything.

## Expected
1. All seats' team marks readable on a 4-seat row (a narrower seat text, e.g. faction initials, or a wider Players column).
2. The setup either refuses a match with only one side (Start disabled, with a `ui.json` reason) or docs/02 says it is a
   sandbox. This is the owner's call.
3. A test that fails when the ghost check is reverted.

## Actual
As in the repro.

## Notes
None block the 2v2 the owner wants to play. Item 2 interacts with BUG-1042 (the AI never attacks when its scout only finds its
ally's base): the 2v2 depends on M6-S5's shared vision landing first, which the merge order (sim, then view) assumes.
