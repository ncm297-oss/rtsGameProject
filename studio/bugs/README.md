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
| [BUG-0001](BUG-0001-smoke-exit-code-hides-script-load-failure.md) | S3 | open | Headless smoke run exits 0 when the C# script fails to load |
| [BUG-0002](BUG-0002-sln-release-builds-game-debug.md) | S4 | open | RtsGame.sln Release configuration builds RtsGame in Debug |
