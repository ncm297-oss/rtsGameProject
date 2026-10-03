# Bugs

One file per bug, filed by the QA inspector (or anyone). The Producer triages them; open S1/S2
bugs outrank new features.

- Name: `BUG-<nnnn>-<short-slug>.md`, numbered in order (look at the highest existing number).
- Status moves `open` → `fixed` (with proof) or `wontfix` (with the Producer's reason).

## Template

```markdown
# BUG-<nnnn>: <title>

| Field | Value |
| --- | --- |
| Severity | S1 / S2 / S3 / S4 |
| Status | open |
| Found | <SESSION_ID>, task <TASK_ID> |
| System | <e.g. pathfinding, economy, data loader, HUD> |
| Fixed by | <commit and regression test, when fixed> |

## Repro
1. <exact steps or the failing test name and command>

## Expected
<what the docs or criteria say should happen>

## Actual
<what happens, with output excerpts>

## Notes
<suspected cause, related bugs>
```

## Index

| Id | Sev | Status | Title |
| --- | --- | --- | --- |
| [BUG-0001](BUG-0001-smoke-exit-code-hides-script-load-failure.md) | S3 | fixed | Headless smoke run exits 0 when the C# script fails to load |
| [BUG-0002](BUG-0002-sln-release-builds-game-debug.md) | S4 | open | RtsGame.sln Release configuration builds RtsGame in Debug |
| [BUG-0003](BUG-0003-simmath-sin-out-of-range-for-huge-angles.md) | S3 | fixed | SimMath.Sin/Cos return values far outside [-1, 1] for huge angles |
| [BUG-0004](BUG-0004-rejected-enqueue-advances-sequence.md) | S3 | fixed | A rejected Enqueue (queue full) still advances the player's sequence counter |
| [BUG-0005](BUG-0005-command-sort-quadratic-under-flood.md) | S3 | open | CommandQueue insertion sort is O(n^2); 10k interleaved commands stall ~107 ms |
| [BUG-0006](BUG-0006-spawn-accepts-non-finite-position.md) | S4 | fixed | SpawnUnit accepts NaN/Infinity positions into sim state |
| [BUG-0007](BUG-0007-loader-accepts-huge-numbers-as-infinity-or-overflow.md) | S3 | open | Data loader turns huge numbers into float Infinity or a negative int instead of rejecting them |
| [BUG-0008](BUG-0008-loader-duplicate-json-keys-last-wins.md) | S3 | open | Duplicate JSON keys are silently resolved last-wins |
| [BUG-0009](BUG-0009-loader-null-entries-in-requires-tags.md) | S4 | open | Null or blank entries in `requires` / `tags` are copied into GameData |
| [BUG-0010](BUG-0010-loader-does-not-check-faction-slots.md) | S4 | open | A faction with no units, or a missing or doubled template slot, loads clean |
