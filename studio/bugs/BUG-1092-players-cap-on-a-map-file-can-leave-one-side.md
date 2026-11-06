# BUG-1092: `--players` capped to a map file's starts can leave every player on one team (a match that never ends)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-05-2148, task M6-V18 |
| System | launch options / match start (view: `LaunchOptions.Parse`, `Match` map cap) |
| Fixed by | |

## Repro
1. `& $env:GODOT --path game -- --players 4 --map raraku --team 1 1 --team 2 1 --team 3 2 --skirmish`
   (or headless: `& $env:GODOT --headless --path game res://tests/QaM6V18bTest.tscn`, row
   `QA M6-V18b KNOWN BUG-1092`; it fails the scene with `-- --strict`).

## Expected
BUG-1066's rule (docs/03 "Implementation (M6-V17)", `LaunchOptions.OneTeamWarning`): when all of the match's players are
on one team, `--team` is warned about and dropped, so the match is a free-for-all ("a match with one side never ends").
The setup applies it after the map cap (picking Raraku with slots 1-2 on Team 1 and 3-4 on Team 2 disables Start and shows
`setup.one_team`; QA checked this and it holds).

## Actual
`LaunchOptions.Parse` checks for one team against the asked `--players 4` (teams 1, 1, 2, 0: two sides, no warning).
`Match` then caps the players at Raraku's 2 starts (`MapPlayersWarning`), which leaves players 1 and 2 both on team 1:

```
capped one side: 2 players, teams 1,1, enemies False
```

`World.IsEnemy(0, 1)` is false. `World.Defeat` needs two sides that ever built before it can end the match, so this match
never ends (a human at the keyboard can only quit).

## Notes
- Only the command line reaches it (dev / test runs, `--map` is new in M6-V18). The menu's setup is safe.
- Likely fix: after `Match` caps `PlayerCount` for a map file, run the same one-team check over the capped count (or move
  the cap into `LaunchOptions.Parse`, which would need the data's player count), and warn and clear the teams as
  BUG-1066 does.
