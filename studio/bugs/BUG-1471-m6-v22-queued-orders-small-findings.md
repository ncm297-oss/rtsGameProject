# BUG-1471: M6-V22 queued-orders nits: the mixed picture's Gather leg off screen, a full queue drops the first Shift building, no word when a queued Build is refused

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 3 only; items 1-2 fixed by M6-VH8, session 2026-11-15-1536, verified by QA; item 3 is Request 58 for the sim track; the same-tick gap in item 2 is BUG-1501) |
| Found | 2026-11-15-0506, task M6-V22 (QA) |
| System | view: queued-orders drawing and the first Shift-placed building (`OrderPathView`, `OrderPath.QueuesBehind`) |
| Fixed by | M6-VH8 (items 1-2; item 3 a Request) |

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

## Fix (M6-VH8)
1. The mixed picture is re-staged 12 m from the tree it gathers, with its own held enemy; the camera frames the box of
   the markers. `OrderQueueViewTest` prints "7 of 7 markers in frame" at 40 m and 60 m, headless and windowed.
2. `SelectionController.OrderBuild` leaves out a worker whose Build would be queued onto a full queue; with none left,
   nothing is sent, the Refused sound plays and the ghost stays armed without counting the click (rows "full queue" and
   "one full and one idle worker").
3. Not built: the sim drops a queued Build refused at its turn silently and exposes nothing the view can read for it.
   Request for the sim track (docs/03 "Implementation (M6-VH8)"): a per-tick list of queued orders dropped at their turn
   (unit, owner, kind, the Build's `PlacementError`) behind a read-only `ViewApi` accessor; the view would then play the
   Refused sound and show the `ui.json` `placement` text over the unit. Item 3 stays open.
