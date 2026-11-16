# BUG-1470: A queued Stop / Hold / no-target cast disc hides the marker of the order before it

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-VH8, session 2026-11-15-1536; verified by QA on re-taken pictures at 20 / 40 / 60 m; `OrderQueueViewTest` ring rows, `QaM6VH8Test` row R) |
| Found | 2026-11-15-0506, task M6-V22 (QA) |
| System | view: queued-orders drawing (`game/scripts/OrderPathView.cs`) |
| Fixed by | M6-VH8 |

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

## Fix (M6-VH8)
A no-place entry (Stop, Hold, a no-target cast) is now a real ring: a flat torus (`OrderPathView.RingMesh`, hole 0.7 of its
outer radius: 0.63 m against the 0.5 m marker) in its own multimesh (`RingsNode`), drawn after the lines and before the
markers (render priorities 1 / 2 / 3). A second no-place entry at the same point is the next ring out (no overlap). The red
Attack marker and the violet cast marker show inside their rings in `queue-mixed-*.png` (20, 40, 60 m). Regression rows in
`OrderQueueViewTest` ("rings"): the ring mesh is a torus whose hole is wider than a marker, its own multimesh, drawn before
the discs (a filled disc over the marker, as before, fails all three); a Move disc, a Stop ring round it, a Hold ring round
that, without overlap. Status: fixed, awaiting QA.
