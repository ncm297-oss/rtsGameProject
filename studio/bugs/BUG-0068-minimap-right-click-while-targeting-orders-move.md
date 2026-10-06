# BUG-0068: A minimap right-click while A-targeting orders a Move and leaves targeting armed

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-0655, task M2-3 |
| System | view input (SelectionController / Minimap) |
| Fixed by | M2-H1 (3dc0568): `SelectionController.CancelTargeting()`, called by the minimap's right-click branch while targeting; `QaM23Test` checks it |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM23Test.tscn`. The `TargetingWithMinimap`
   step selects 6 own units, presses A, then pushes a right press and release at the minimap's centre
   through `Viewport.PushInput`.
2. The log prints `A then minimap right-click: 6 Move commands, still targeting True`. The scene
   only prints this; it does not check it, so it stays green until this is decided.

## Expected
M2-3 brief: "Esc or a right-click cancels it (the right-click issues nothing)". On the 3D view a
right-click while targeting cancels and orders nothing (OrdersTest checks it). A right-click on the
minimap is still a right-click, so it should behave the same: cancel, no order. The other option is
to keep it as a deliberate exception, but then docs/03 has to say so.

## Actual
`Minimap._GuiInput` calls `_selection.Order(CommandKind.Move, p, queued)` without looking at
`Targeting`. So 6 Move commands are enqueued and `Targeting` stays true. The player's next left
click on the 3D view then attack-moves as well. docs/03 "Implementation (M2-3)" only says that "a
minimap click does not" end targeting (written about the left-click camera jump). It doesn't say
that a right-click there orders a Move while A stays armed.

## Notes
Fix sketch: in the minimap's `command` branch, if `_selection.Targeting`, cancel targeting and
order nothing (needs a public way to cancel, e.g. `CancelTargeting()`). Or treat the minimap
right-click as the A point (minimap attack-move is out of scope for M2-3). Either way, docs/03
should say which. Low impact: dev-only label, no cursor art yet.

## Re-check (2026-10-06-0905, M2-H1 commit 3dc0568): fixed
Verified: `QaM23Test.tscn` passes on 3dc0568 and fails on base scripts ("A then minimap right-click ordered 6 moves", "a minimap right-click did not cancel A targeting"). QA `game/tests/QaH1Test.tscn`: 211 minimap right-clicks in a 3,000-step random A / Esc / S / H / minimap / 3D / recall / death sequence. Each cancels targeting and orders nothing when A is armed, and orders one Move per live selected unit otherwise. 3D right-click and Esc are unchanged.
