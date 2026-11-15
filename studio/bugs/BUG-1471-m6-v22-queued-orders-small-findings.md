# BUG-1471: M6-V22 queued-orders nits: the mixed picture's Gather leg off screen, a full queue drops the first Shift building, no word when a queued Build is refused

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-15-0506, task M6-V22 (QA) |
| System | view: queued-orders drawing and the first Shift-placed building (`OrderPathView`, `OrderPath.QueuesBehind`) |
| Fixed by | |

## Repro
1. **The mixed picture.** `& $env:GODOT --path game res://tests/OrderQueueViewTest.tscn -- --shots <dir>`, then open
   `queue-mixed-default-zoom.png` and `queue-mixed-zoomed-out.png`. The scene prints "7 markers, 8 segments" for both.
2. **A full queue.** Select one worker walking a Move with 8 orders already queued (Shift + right click 8 times), open
   the build menu, Shift + click a green spot.
3. **A refused queued Build.** `QaM6V22Test`'s "refused" row: a walker, Shift + place a house (queued behind the walk),
   money spent before it arrives.

## Expected
1. Criterion 8: "a mixed queue at the default zoom and zoomed out" shows each kind's marker.
2. docs/02 "Queued orders": the first Shift-placed building replaces only a gather loop or idleness, else queues; a
   ninth order is ignored. Before M6-V22 the first placement always replaced, so it was always built.
3. Some feedback when a placement the player saw green is dropped.

## Actual
1. The Laborer's queued Gather goes to the nearest tree, which is off both frames in unexplored (black) land: its gold
   marker is in neither picture (6 of 7 markers visible), and the gold Gather leg and the blue Build leg coming back
   overlap into one whitish double line that runs off the top of the frame. The pictures still show the other kinds.
   (The developer's question 1, small markers at 60 m, is visible in the zoomed-out one.)
2. `QueuesBehind` says "queue" for any worker with orders queued, so with 8 queued the Build is sent queued and the sim
   drops it (`UnitQueueFull`): the click plays the Command sound, the ghost counts it as placed, and nothing is built.
   This is the 8-order rule applied as documented; it is listed because it is new for the first placement.
3. The queued Build pops, `ConstructionSystem.StartBuild` refuses for lack of money, the worker stands Idle and the
   marker goes, with no message. This was already true for the second and later Shift placements; the first one is
   now queued too when the worker is busy.

## Notes
Item 1: pick a tree in sight of the mixed group, or frame the shot on the whole path. Items 2-3 are the Producer's call
(for example, replace when the queue is full, or show the existing refusal alert when a queued Build is refused). The
developer's question 4 (a Shift placement in the same tick as another order to that worker decides on the older state)
belongs with these.
