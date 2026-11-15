# BUG-1470: A queued Stop / Hold / no-target cast disc hides the marker of the order before it

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-15-0506, task M6-V22 (QA) |
| System | view: queued-orders drawing (`game/scripts/OrderPathView.cs`) |
| Fixed by | |

## Repro
1. `& $env:GODOT --path game res://tests/OrderQueueViewTest.tscn -- --shots <dir>` (windowed).
2. Open `queue-mixed-default-zoom.png`. Look at the enemy Raider (top left): the soldier's queue is AttackMove, Attack on
   the Raider, Stop. Look at the Mage's queue (centre): Telas Fire at a point, then Hold.
3. Enlarged crops (QA run, 2026-11-15): `crop-enemy.png` and `crop-mage.png` in
   `C:\Users\Nick\AppData\Local\Temp\claude\162c01fae57881b0\dc73a381-f636-4f01-9846-83145c2f8d89\scratchpad\qa-m6v22\`.

## Expected
docs/02 "Queued orders (Shift)": "stop and hold position are a wider ring at the point before them"; docs/03
"Implementation (M6-V22)": "A no-place entry is a wider, lower disc (a ring round the previous point's marker)". The
red Attack marker and the violet cast marker should still be visible, with the Stop / Hold ring around them.

## Actual
The Stop disc is a solid off-white disc 1.8x the marker's radius drawn **on top of** the red Attack marker, which is
completely hidden (only the red line leading to it shows). The same for the Mage: the pale yellow Hold disc covers the
violet UseAbility marker entirely. A player who queues "attack that, then stop" sees only a white disc on the target.

Cause: both are instances of one `MultiMesh` drawn with `NoDepthTest = true` and the same `RenderPriority`, so the
order on screen is the instance order, not the height: the no-place disc is added after the marker it surrounds
(path order) and paints over it. The lower lift (`NoPlaceLift` 0.12 m v `MarkerLift` 0.18 m) has no effect without a
depth test. The headless rows (`FindMarker`) only check the instance data, so they pass.

## Notes
Possible fixes (the developer's call): draw the no-place entries as a real ring (an annulus or a torus mesh), or give
them their own `MultiMesh` with a lower `RenderPriority` so they draw first. A scene row could check the drawing order
(no-place instances before the marker they surround, or a ring mesh).
