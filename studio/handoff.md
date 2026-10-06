# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session 2026-10-06-1255 (3 / 8 today). Both tracks ACCEPT, 0 fix
rounds. Feature counters: sim 1 / 4, view 1 / 4 (next hardening after 3 more feature sessions each).

## Where we are

- `main` after the conductor merges (sim first, then view): sim branch build 0 warnings; view branch 1
  warning (CS8602 in `game/tests/DebugOverlayTest.cs:173`, BUG-0084; `game/` is not warnings-as-errors).
  Non-Perf and smoke numbers in the session log. Inbox: nothing new.
- Open bugs 22 (S3 14, S4 8), none S1/S2. Roadmap: M0 Done, M1 Done, M2 7 / 10 (view), M3 1 / 8 (sim).
- Bug numbers next session: **sim from BUG-0077, view from BUG-0085.**
- Shared-file rule: both tracks append to docs/03, docs/01's change log and `studio/qa/coverage.md` in
  *different* sections. Neither track adds a public member to `FlowFieldCache` (QA pins its surface).
- Now on `main` for the view: `World.Resources` (read-only spans `Alive`, `Generation`, `TypeId`, `Cell`,
  `Remaining`; `IsAlive`, `HandleOf`), `World.ResourcePlacement`, `GameData.Resources` (`ResourceDef`:
  `Key`, `DisplayName`, `Resource` gold | wood, `FootprintWidth` / `Height`), `NavFlags.Resource`,
  `MapGenParams.Forests` / `GoldMines` (default 0: the view must ask for them). No depletion events: views
  poll `Alive` / `Generation` (or compare `NavGrid.Version`, which bumps once per node placed or freed).

## Sim track

### Next task candidates (feature session)
1. **M3-2 worker gather / return loop** (QA full). Needs a drop-off: docs/02 says Town Hall or Camp. Two
   ways: (a) a minimal `BuildingStore` with pre-placed drop-off buildings (data `buildings.json` with
   `town_hall` only, footprint 4 x 4, placed next to each player's start block; construction is M3-3),
   or (b) M3-3 placement first and gathering second. Recommended: (a), with the store built to be M3-3's
   store (footprint blocks the nav grid like a resource node, `Version` bump, same open-ground + reach
   rule). The brief must settle two things found this session:
   - **BUG-0075 (hollows):** a worker gathers only from a node cell 8-adjacent to the passable cell it
     stands on, so interior trees can't be felled before the trees around them; then no hollow can
     form. Write it into docs/03 "Economy implementation" and close BUG-0075 by the rule + a test.
   - **BUG-0073 (every fall invalidates every field):** depletion only *opens* cells, so a field built
     before a fall is still safe to follow (just not shortest). Options: keep serving stale fields after
     an open-only change and rebuild lazily (a `NavGrid.BlockVersion` for closing changes that must
     invalidate, vs `Version` for all changes); or accept and bound (workers fell a tree every few
     seconds in play, 32-128 fields rebuild at 2 per tick = 0.8-3 s of stale steering). Decide in the
     brief; the lazy-rebuild route touches `FlowFieldCache` (QA pins its surface: coordinate the pin).
   - Gather data from `rules.json` (`workerCarry`, `gatherRate`, `nodeSearchRadius`), `Gather` command
     (unit handle + node handle, or a cell), worker state machine in tick phase 4, automatic drop-off =
     nearest by walking distance (flow-field cost, not Euclid) with the search radius for the next node
     when one depletes. Player resources per player in `World` and the hash.
2. Fold in only if cheap: `PrevFacing` (request 4) when `UnitStore` is touched; BUG-0074 (validate that
   the forest placer's tree type is 1 x 1, or generalize `TryForest` to footprints).
3. After: M3-3 building placement + construction, M3-4 production queues, M3-5 Age II, M3-6 full data.

### Debt for the next sim hardening (3 feature sessions away)
BUG-0071, BUG-0072, BUG-0074, BUG-0076 (items 2-4; item 1 fixed), BUG-0008 / 0010 (or M3-6), BUG-0005
before M5, BUG-0025 / 0026 (the view's churn note confirms 0025 at 64 goals).

## View track

### Next task candidates (feature session)
1. **M2-3b trees and gold mines as MultiMesh** (QA standard), completing M2 criterion 3 ("trees and
   rocks as MultiMesh"; rocks are decorative and may be skipped or a few placeholder boulders): `Match`
   builds its map with resources (`MapGenParams { Forests, GoldMines }`; launch flags `--forests` /
   `--mines`, defaults a design choice for the PLAN, e.g. 12 / 8 so the opening view shows a forest);
   one placeholder mesh per resource type (cone + trunk for a tree, a 2 x 2 dark block for a mine, sized
   from the footprint), instances from `World.Resources` placed on `TerrainHeight`, hidden when `Alive`
   goes false (poll per frame or when `NavGrid.Version` changes; 0 bytes per frame; <= 0.2 ms at 4,096
   nodes); minimap marks nodes (optional); debug overlay shows resource cells red already (any `Blocked`
   bit). Tests: `ViewApi` instance layout vs the store on hand maps and generated maps, a hash twin, a
   scene with a depletion (`BumpVersionForTests` can't free a node: a QA test may need an internal seam
   or reflection; the dev test can use a fresh world with and without a node).
2. **M2-6 placeholder audio** (QA light): generated tones for select and command, no downloads.
3. **M2-7 playable check** 100 units at 60 FPS (plus `PrevFacing` blending if request 4 lands) → M2
   end-of-milestone hardening (BUG-0069, BUG-0070, BUG-0083, BUG-0084, export hygiene notes) → M2 sign-off.

## Watch-outs

- Perf rows fail from CPU contention while the other track's QA runs: a failure counts only when it
  fails again alone (this session: 3 rows failed under load, identical numbers on base and HEAD alone).
- Both tracks landed ~2x their size budget, mostly tests. Next briefs: give a separate line budget for
  tests, or say "tests excluded".
- The view's `Match` builds maps with `MapGenParams` defaults (no resources) until the trees task changes
  it; the CLI's `--forests` / `--mines` are the only way to see resources today.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" and has no CLI line.
- Builders: absolute paths only; never touch the owner's main checkout. Remote Control unavailable.
