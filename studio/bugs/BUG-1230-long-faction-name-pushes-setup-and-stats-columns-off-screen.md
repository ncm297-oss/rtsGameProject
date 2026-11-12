# BUG-1230: A long faction `displayName` pushes the setup's Team column and the stats screen's last columns off a 1152 x 648 window

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-11-1317, task M7-V3a (QA standard: "a third faction with a 40-character `displayName`") |
| System | view: `SkirmishSetup` slot grid, `MatchStats` table |
| Fixed by | |

## Repro
1. `game/tests/QaM7V3aTest.tscn` forges eight factions, the third (`qf2`) named "The Forty Character Faction Display Name"
   (40 characters). Run it windowed with shots:
   `& $env:GODOT --path game res://tests/QaM7V3aTest.tscn -- --shots <dir>`.
2. Look at `qa-eight-factions-setup.png` and `qa-four-factions-stats.png` (the scene's window is 1152 x 648, the size every
   view scene uses).

## Expected
A faction name the data accepts (no length limit in the loader or docs/03) keeps every setup control and every stats
column on screen, or the data caps the length with a load error.

## Actual
- **Setup:** each faction list is an `OptionButton` with a 240 px minimum and no clipping, so every list in the column grows to
  the widest item: 438 px for the 406 px name at 20 px (measured by the scene: list rect `(193, 323), (438, 40)`). The
  grid widens past the screen: the **Team column (header and all four lists) is off the right edge**, so a player cannot
  set teams; the labels start at x = 0 and the title is off centre. With today's names (14 characters) the Team column ends at
  x ~1136 of 1152, so any name wider than about 250 px (~25 characters at 20 px) starts pushing it out.
- **Stats screen:** the player-name cell widens the table; the "Age II at" and "Defeated at" columns are cut off at the
  right edge (the "A" of "Age" at x ~1142).
- Nothing crashes; the match itself, its save and its replay are fine (`QaM7V3aTest` passes).

## Notes
- Pre-existing (not caused by M7-V3a); found by the QA focus row. The shipped and planned names (Malazan Empire,
  The Whirlwind, Teblor) are short, so nothing plays wrong today.
- Possible fixes (the developer's / Producer's call): clip and ellipsize the list's text (`ClipText` / `TextOverrunBehavior`)
  with the full name in the tooltip and the popup; cap the stats name cell; or a data limit on `displayName` length with a
  load error (a sim loader change: the sim track).
