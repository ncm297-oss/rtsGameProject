# BUG-0663: M6-VH1 nits: the duplicate-colour warning says "Ignoring --colour" when only slot 1's flag was given

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-13-0527, task M6-VH1 (QA) |
| System | view: `game/scripts/LaunchOptions.cs` (duplicate-colour rule) |
| Fixed by | |

## Repro
1. `& $env:GODOT --path game -- --colour 1 2` (slot 1 picks Ochre; slot 2 has no flag and defaults to Ochre).

## Expected
A warning that says what happened: slot 2's default moved to colour 1 because slot 1 took colour 2.

## Actual
`Ignoring --colour: slots 1 and 2 both on colour 2; slot 2 takes colour 1 (colours are unique per match).` Nothing was
ignored: slot 1's flag was applied and slot 2 had no flag. The colours are right (QaVh1Test fuzz: 800 streams, 0 wrong).

## Notes
Wording only. Also seen: the AI-gathering check reads one tick's snapshot (`Gathering` / `Returning` only while a worker stands
at a node or drop-off; walking is `Moving`). On AiSeatTest's own match (seed 1, Hard) ticks 400-440 always have a worker
standing, so it is stable, but on Normal 18 % of ticks 300-500 over 18 openings have none (QaVh1Test prints it), so a snapshot
check copied to another match would be flaky. QaVh1Test checks "first gathering tick <= 400" instead.
