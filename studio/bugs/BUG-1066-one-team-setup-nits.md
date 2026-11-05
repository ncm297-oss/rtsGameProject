# BUG-1066: One-team refusal nits: the command line starts a one-team match without a word; Back / Start jump left when the reason shows

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-05-1206, task M6-V16 (re-check of the BUG-1062 item 2 rider) |
| System | `LaunchOptions` `--team`, skirmish setup buttons row |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game --quit-after 200 -- --skirmish --players 4 --team 1 1 --team 2 1 --team 3 1 --team 4 1 --mute`:
   the log says `Match started: ... seats malazan human, whirlwind ai normal, ...` and nothing else: no warning, and the
   "Match started" line doesn't name the teams. The match can never end.
2. Setup: put every shown slot on Team 1. `setup.one_team` appears in the buttons row (`teams-setup-one-team.png` from
   `TeamsTest -- --shots`), and since the row is centred, Back and Start slide ~250 px left; they slide back when the line hides.

## Expected
1. Per the Producer's decision (docs/01 2026-11-05) only the setup refuses; docs/03 says "`--team` itself still takes one
   team (as the sim)", so accepting it is documented. A one-line warning (like the junk `--team` warnings) would
   still tell a dev why the match never ends.
2. The buttons stay put (reserve the line's room, or put the line on its own row if the height allows).

## Actual
As in the repro. Neither affects play from the menu.

## Notes
Judged against the brief: the decision is scoped to the setup's Start, so item 1 is not a criterion miss.
