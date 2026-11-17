# BUG-1501: The full-queue Build check reads the applied queue only: two queued Builds in one tick for a worker at 7 both pass, the sim drops the second silently

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (M6-V23, session 2026-11-15-2136; verified by QA: `QaM6VH8Test` rows R / F, `QaM6V23Test` rows P / R) |
| Found | 2026-11-15-1536, task M6-VH8 |
| System | `SelectionController.OrderBuild` (BUG-1471 item 2) |
| Fixed by | M6-V23 |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6VH8Test.tscn`, row F's last line.
2. A worker walks a Move with 7 queued Moves. Without a tick between them, `OrderBuild(house, a1, queued: true)` and
   `OrderBuild(house, a2, queued: true)`.

## Expected
BUG-1471 item 2 / docs/03 "Implementation (M6-VH8)": a Build that would be queued onto a full queue is refused in the
view (Refused sound, nothing sent, the ghost does not count it).

## Actual
`(F) a worker at 7 queued, two queued Builds in one tick: view returned 1 and 1 (2 sent, 0 refused in the view); queue after the tick 8`.
The check is `u.QueueCount[h] >= QueueCapacity`, and commands enqueued this tick are not counted, so the second Build is
sent, the Command sound plays and the ghost counts it, then the sim drops it (`UnitQueueFull`) with no feedback: the
silent drop BUG-1471 item 2 was about, in a narrower window.

## Notes
The window is one tick (50 ms at 1x, 25 ms on Fast), so two Shift clicks a human makes rarely land
in it; a Shift-queued Move or Attack between ticks also counts against it. A fix could add the selection's own Build
commands still pending this tick (the view already reads `PendingCommandCount`), or keep a per-unit count of queued
commands sent since the last tick.

## M6-V23
Fixed: every unit order of `SelectionController` goes through `SendUnitOrder`, which counts a queued one per unit slot
until the tick it applies in has run (a command sent between ticks applies in the second tick after it, so two batches
are kept); `OrderBuild`'s full check is `QueueCount + PendingQueuedOrders >= 8`. Row F's last line now checks:
`(F) a worker at 7 queued, two queued Builds in one tick: view returned 1 and 0 (1 sent, 1 refused in the view, 1 Refused sound); queue after the tick 8`.
`OrderQueueViewTest` adds a Shift-queued Move then a Build onto 7 (the Build refused; one tick on the Move is still
counted; applied, the count is 0; with the queue back at 7 the same click is sent). An unqueued order sent in the same
window is not subtracted (errs toward refusing).
