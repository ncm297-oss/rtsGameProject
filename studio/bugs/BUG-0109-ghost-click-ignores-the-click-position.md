# BUG-0109: A placement click ignores where it lands: it builds at the last frame's drawn anchor, or is swallowed if that one was red

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-07-1131, task M3-V2 |
| System | build ghost / placement click (CommandCard.GhostClick, BuildGhost), view |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaV2Test.tscn` (row `ClickPositionVsDrawnAnchor`; `-- --strict`
   fails it). Seed 1, 3 workers, house ghost synced green on a free spot, then a left click whose position is on the
   own Town Hall (red), delivered before the ghost's next `_Process`.

## Expected
QA focus for M3-V2: "never a Build with a stale anchor after the cursor moved". The building goes under the click,
or (if that spot is red) nowhere.

## Actual
```
a click on the hall (red) placed 3 Builds at the last drawn anchor 9142
([Build u4 t2 (109,143), Build u11 t2 (109,143), Build u18 t2 (109,143)])
```
`SelectionController._UnhandledInput` calls `Card.GhostClick(shift)` without the event's position. `GhostClick` uses
`_ghost.Anchor` / `_ghost.Valid`, which the ghost computed in the previous frame's `_Process` from the mouse position
then. Input events arrive before `_Process`, so on every click the anchor is one frame old. With a flick-and-click,
the house goes where the cursor was a frame ago, not under the click. The reverse also happens: if the last drawn
anchor was red and the click lands on green ground, the click does nothing and plays no sound.

## Notes
At 60+ fps the gap is usually a cell or less, and the house lands where the ghost box was drawn (what the player
saw). That's why this is S3 and not S2. The test makes the gap large on purpose. Fix idea: in `GhostClick`, recompute
`PlacementGhost.Anchor` from the click's position. If it equals the drawn anchor, use the cached answer. If not, either
ask `CanPlace` for it (input handling is on the main thread and between ticks; the at-most-once-per-frame rule would
need the click to count as that frame's call) or ignore the click until the ghost catches up. When fixed, turn the
`Known("BUG-0109", ...)` row into a plain `Check`.
