# BUG-0300: Smoke FAILs on the M4-4a tree: the new `UnitState.Casting` has no `ui.json` `states.casting` text

| Field | Value |
| --- | --- |
| Severity | S2 (acceptance criterion 7 "smoke PASS" not met on the sim branch, nor on its merge with the view head) |
| Status | fixed (M4-4a fix round 1; cross-track ui.json line, Producer to rule) |
| Found | 2026-10-09-0724, task M4-4a (reported by the developer as out of scope, confirmed by QA) |
| System | view HUD text (`game/scripts/UiText.cs`) vs sim enum `UnitState` |
| Fixed by | M4-4a fix round 1: `"casting": "Casting"` in `game/data/common/ui.json` `states` |

## Repro
1. `powershell -File tools/qa/smoke.ps1` on `studio/2026-10-09-0724-sim` (9c5517a), or on a scratch merge of it with
   `studio/2026-10-09-0724-view` (215838b).

## Expected
docs/03 / CLAUDE.md definition of done: smoke prints PASS. Every `UnitState` the sim can report has player-facing text
in data (architecture rule 8).

## Actual
```
FAIL: log: ERROR: ui.json: missing states.casting (a non-empty string)
   at: Rts.Game.UiText Rts.Game.UiText.get_Shared() (res://scripts/UiText.cs:132)
   [2] void Rts.Game.Match.Start(...)  (Match.cs:114)
```
The game still boots and runs (the match ran to tick 86), but the gate is red.

With `"casting": "Casting",` added to `game/data/common/ui.json` `states` (scratch merge only): smoke PASS and the scene
loop green (see the QA report).

## Notes
M4-4a added `UnitState.Casting = 6`; `UiText` requires a `states.<name>` entry for every enum value (a good guard: it
caught this). `ui.json` is view-owned this session, so the sim developer correctly did not touch it. Producer's call:
land the one line in the view track (or in the merge commit) before accepting M4-4a, or accept with this open.
