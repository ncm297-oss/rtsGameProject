# BUG-0136: Headless smoke FAILS: `ui.json: missing states.attacking` (new `UnitState.Attacking`)

| Field | Value |
| --- | --- |
| Severity | S1 |
| Status | fixed on merge (the view track adds `states.attacking`); verified |
| Found | 2026-10-07-2014, task M4-1 |
| System | view text (`game/scripts/UiText.cs`, `game/data/common/ui.json`) vs sim `UnitState` |
| Fixed by | view track's `game/data/common/ui.json` (`"attacking": "Attacking"`) |

## Repro
1. `powershell -File tools/qa/smoke.ps1` in the sim worktree at f2879b9.

## Expected
`PASS` (CLAUDE.md definition of done; the gate the whole repo shares).

## Actual
```
FAIL: log: ERROR: ui.json: missing states.attacking (a non-empty string)
   at: Rts.Game.UiText Rts.Game.UiText.get_Shared() (res://scripts/UiText.cs:118)
   [2] void Rts.Game.Match.Start(...) (game/scripts/Match.cs:92)
```
The match still starts and runs (stopped at tick 85), but the gate is red.

## Notes
`UiText` checks a label for every `UnitState`; M4-1 added `Attacking = 5`. The fix is one `states.attacking` string in
`game/data/common/ui.json` (view track's file, player-facing text from data per rule 8), so it needs the Producer to
route it (view track, or a named exception for the sim track) before or with the merge. The sim must not merge to
`main` alone, or the view track's smoke goes red too.

## Re-check 2026-10-07-2315 (QA, fix round 1)
With the view's key copied into this worktree's `ui.json`, `tools/qa/smoke.ps1` prints PASS (tick 86, `Rts.Sim 0.0.1`, no ERROR); reverted afterwards, it fails again with this error, as expected until the two branches merge.
