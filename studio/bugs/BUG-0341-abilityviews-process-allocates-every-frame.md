# BUG-0341: AbilityViews._Process allocates 96 B every frame (string action lookup); the 0 B row skips _Process

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-09-1155, task M4-V6a |
| System | view: ability views (and the targeting click path) |
| Fixed by | |

## Repro
1. `dotnet build RtsGame.sln`
2. `powershell -File tools/qa/scene-loop.ps1 -SkipPlayable -Filter QaV6aTest`

## Expected
M4-V6a acceptance criterion 4: "0 B at 500 units". The view's steady frame allocates nothing (docs/03; the AbilityViews
class remarks say "a steady frame allocates nothing").

## Actual
```
seed 1 AbilityViews._Process: 9600 bytes over 100 frames (not targeting)
QA V6A TEST FAIL: seed 1: AbilityViews._Process allocated 9600 bytes over 100 steady frames
seed 1 bytes: IsActionPressed(string) 96, AbilityPoint 0, PickCaster 0, AbilityOrder 0, A-move click 96 then 96
QA V6A TEST FAIL: seed 1 spam: the click path allocated 48000 bytes over 500 clicks
```
`game/scripts/AbilityViews.cs:126`:
`Sync(sim.World, (float)_runner.Alpha, GetViewport().GetMousePosition(), Input.IsActionPressed("order_queue"));`
The string literal converts to a new `StringName` on every call: 96 B per frame in every match, whether or not an
ability is armed (about 5.8 KB/s at 60 fps). `AbilityViewTest.Steady` measures `_views.Sync(...)` directly, never
`_Process`, so its 0 B row passes and misses this. Every other literal action lookup in `game/scripts` is in an input
handler (event-driven); this is the only per-frame one.

Secondary (same cause, older code): every targeting click allocates 96 B from
`bool queued = Input.IsActionPressed("order_queue");` in `SelectionController.AttackMoveClick` (the A-move click has
the same cost). The brief's QA focus asked for 0 B over a 500-click spam. Each click did send exactly one command.

## Notes
Fix: a `static readonly StringName OrderQueue = "order_queue";` (as `GroupActions` / `_cardActions` already do) used in
both places, and `AbilityViewTest.Steady` should measure the real `_Process` (or `SyncAll` should call it).
Regression rows: `game/tests/QaV6aTest.cs` ("AbilityViews._Process allocated", "the click path allocated").
