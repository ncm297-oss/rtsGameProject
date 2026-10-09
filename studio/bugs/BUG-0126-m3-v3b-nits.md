# BUG-0126: M3-V3b nits: a locked building's ghost reads "Needs more", Age II reads "Locked" while queued or researched after a hall dies, the props relist still runs on building changes, and smaller leftovers

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (items 1-2 and 4 fixed) |
| Found | 2026-10-07-1715, task M3-V3b (QA) |
| System | HUD / command card / build ghost / props (view), `game/data/common/ui.json`, perf rows |
| Fixed by | items 1-2: df37a8c (M3-V4, see above); item 4: 08dc8e3 (M4-VH1, `SelectionController.CancelSelectedSite` drops a second press for the same site before the next tick; `CommandCardTest` presses twice and expects one Cancel); items 3, 5, 6 open |

## Repro
1. Items 1-2: with D3's `buildings.json` copied in (`git checkout origin/studio/2026-10-07-1415-data --
   game/data/factions/malazan/buildings.json game/data/factions/whirlwind/buildings.json`, restore with
   `git checkout HEAD -- ...`), `& $env:GODOT --headless --path game res://tests/CommandCardTest.tscn` prints
   `advanced-menu ghost (malazan_cadre_tower): CanPlace False Requires, shows 'Needs more'`. On shipped data the same
   line reads `CanPlace True None`.
2. Item 2: `& $env:GODOT --headless --path game res://tests/QaV3bTest.tscn` prints
   `QA M3-V3b NOTE: Age II researched, a hall slot lost: the button reads 'Locked' in 10 of 10 frames`.
3. Item 3: in a scratch clone, drop `"--no-bases"` from `game/tests/PropsViewTest.cs` and run it:
   `PROPS VIEW TEST FAIL: steady frames relisted: uploads 1, rebuilds 2`. (`QaM23bTest` with bases passes now: the
   minimap's resource layer is drawn once.)

## Expected / Actual
1. **"Needs more" on a locked building's ghost.** `ui.json` `placement.requires` is "Needs more". Train and research use
   "Locked" for the same rule. With D3's building `requires` (for example the Cadre Tower needs Age II), the red ghost
   for a locked building reads "Needs more", which sounds like a money problem ("Can't afford" is the money reason). The
   build-menu button for a locked building is also not greyed. Only the ghost tells you, after you pick the building.
   "Locked" on the ghost, plus `CanPlace`-style greying of Place buttons for a `Requires` reason, would match the
   production card.
2. **Age II reads "Locked" while it is queued, and after it is researched, once a hall slot is lost.** `CanResearch`
   answers `Requires` before `AlreadyResearched` and `AlreadyQueued` (M3-6's documented order). So if a hall dies
   while Age II is in the queue, the queued Age II's button reads "Locked", although the item survives and completes.
   After it is researched, the button still reads "Locked" until two distinct halls stand again. The card shows the
   sim's reason faithfully (0 mismatches in 450 frames). It is a wording issue: the view could show "Researched" /
   "In a queue" when `HasTech` / `IsTechQueued` is true, whatever the first reason is.
3. **Props relist on every building change** (the remainder of BUG-0107 item 3). The minimap's resource layer is keyed
   on the resource store's `FreeCount` now, but `PropLayout` still relists on `NavGrid.Version`, which each building
   spawn, site and cancel bumps. The upload is skipped when nothing changed, so this only costs a relist.
4. **Double Cancel** (the remainder of BUG-0122 item 2): two Cancel presses before a tick still enqueue two Cancels; the
   second finds nothing. Harmless.
5. **Perf row split.** BUG-0105's split gives the minimap dots 0.25 ms and the forced redraw 0.2 ms, so the combined
   refresh after a fell (measured 0.166 + 0.125 = 0.291 ms alone) is now allowed up to 0.45 ms, where one row used to
   hold it to 0.3 ms. The brief asked for the split, and the redraw now runs only after a fell. This is a note that
   the sum is no longer guarded.
6. **Resource bar under repair.** Repair spends money, so the bar rebuilds its gold / wood text whenever a total
   changes: 1,152-1,168 B over 300 repair ticks (the documented M3-V1 rule). `QaV3Test`'s strict row and `QaV3bTest`
   exclude those frames. Every other HUD element (panel, card, strip, rally, minimap, building views) measured 0 B. The
   panel's int-string table could serve the bar too.

## Verification of items 1-2 (2026-10-07-2014, M3-V4 QA)
- Item 1: `game/tests/QaV4Test.tscn` checks every Place cell of the B and V menus after every real frame (537 frames,
  2,124 cells, 0 mismatches). It compares against `CanPlace(NoAnchor)` and an independent oracle (required techs
  researched, an own finished building of each required type) through: bare; a Barracks site (Corral still locked);
  a finished Barracks (Corral live); the Barracks destroyed (Corral locked again in the same frame, and its open ghost
  turns red "Locked"); Age II queued; a hall lost while queued; researched; every hall lost after Age II (Tower and Yard
  stay live, the Corral re-locks). Money swings 0 to plenty: 503 live cells were shown with no money, so a Place
  button greys only for `Requires`. A locked button is never `Disabled`. The Cadre Tower ghost read "Locked" in 194 of
  194 frames while Age II researched, and `Blocked` (not `Requires`) the frame after.
- Item 2: Age II's button read "In a queue" with the sim at `Requires` (queued, Forge lost), and "Researched" with the
  sim at `Requires` (one hall slot left, and again with every hall lost).
- Items 3-6 not touched by M3-V4: still open.

## Re-check (QA 2026-10-08-2144, M4-VH1)
- Item 4 **fixed**. A second Cancel for the same site (slot and generation) on the same tick sends nothing, and docs/03
  says so. `CommandCardTest` emits Pressed twice and checks that exactly one Cancel is pending; it passes in both scene
  loops. Small leftover: the guard is not reset if the `Simulation` instance changes, filed as BUG-0250 item 3
  (theoretical).
- Items 3, 5 and 6 were not touched and are still open.
