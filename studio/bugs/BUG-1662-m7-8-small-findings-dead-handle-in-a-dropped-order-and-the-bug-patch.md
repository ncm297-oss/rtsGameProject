# BUG-1662: M7-8 small findings: a dropped order's unit may already be dead; the switch report's seeds are one match; the bug patch does not apply

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 1 fixed by M7-H3; item 2 documented on the report class; item 3 a process note) |
| Found | 2026-11-24-1431, task M7-8 |
| System | `Orders/DroppedOrder.cs` docs, docs/03 "For the view track (Request 58)", `Stress/ReserveSwitchReportTests`, studio process |
| Fixed by | M7-H3 (item 1: doc text only) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DroppedOrderQaTests.ADroppedOrder_OfAUnitKilledInTheSameTick"`:
   a worker's queued Gather is dropped in phase 7 of tick 14 and the worker is killed in phase 11 of the same tick; the
   tick's list holds the entry and `Units.IsAlive(entry.Unit)` is false when the view reads it.
2. `Stress/ReserveSwitchReportTests` (the docs/03 "For the data track (M7-8)" table): every cell is 0, 8 or 16.
3. `git apply -p0 m78-bugs.patch` (the developer's bug patch) fails: "bad git-diff - inconsistent new filename on line 39"
   (the two new-file hunks carry `b/` in the `diff --git` line but not in `+++`).

## Expected
1. `DroppedOrder`'s doc says "(alive: an order is dropped only for a live unit)"; docs/03 tells the view to read the list
   "as `Deaths`". A view that shows text over the unit should be told the unit may be gone, as `DeathEvent.Victim`'s doc
   says ("stale by the time it is read").
2. A table of "16 seats" per order measures 16 seats.
3. A patch handed to QA applies as instructed.

## Actual
1. The entry names a unit that died later in the same tick; nothing says so.
2. The eight Pale Hills seeds play the same match per start (the developer says so under "Read with care"), so the table
   is two data points per cell; criterion 10 is met as a measurement, but the data track's re-measure must use other maps
   or seat-swapped rows (the developer's own advice).
3. QA wrote BUG-1660 / BUG-1661 and the BUG-1610 / BUG-1621 notes by hand from the patch's text.

## Notes
- Item 1's fix is one doc line in `DroppedOrder.cs` and one in docs/03 "For the view track"; no code.

## Fix (M7-H3, 2026-11-25)
- Item 1: `Orders/DroppedOrder.cs`'s `Unit` doc now says the unit was alive when its order was dropped but may have died
  later in the same tick (phase 11 after phase 7), so the handle can be stale when read, as `DeathEvent.Victim`'s; check
  `Units.IsAlive` before showing anything. docs/03 "For the view track (Request 58)" says the same. No code changed.
- Item 2: `Stress/ReserveSwitchReportTests`' summary now says the eight Pale Hills seeds play alike (two seats a row) and
  points at the data track's four-map rows; docs/03 already said so under "Read with care".
- Item 3: this patch was made with `git diff` in a scratch clone and checked with `git apply --check` before handing over.
