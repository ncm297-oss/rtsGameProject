# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-08-2144** (all three tracks accepted; the fog lands on `main` with
this integration). The next session's PLAN re-checks `main`, the inbox and the caps before using this.

## Where we are

- M0-M3 Done. **M4: 5 / 10** on `main`. Criterion 5 (fog) has its sim half on `main` (M4-3a + M4-H1); it still owes the view's
  shader / hiding / minimap fog (M4-V4) and the sim's ghost list + shooting towers (M4-3b).
- Hardening counters: sim **0 / 4**, view **0 / 4**, data **3 / 4** (data's next hardening after one more feature task).
- Open S1 / S2: **none** (BUG-0219 and BUG-0240 fixed this session). Open S3 worth knowing: BUG-0241 (sim, a chase that never
  ends when unreachable targets are taken in turn along a cliff; also pre-existing), BUG-0243 (data: one stale time on the
  Malazan page), BUG-0251 (view: `EconomyViewTest` can FATAL at Godot shutdown after PASS under load; rerun once), BUG-0157.
- Integration order used: view → data → sim (every pushed `main` green). The sim merge has a precondition: QA's
  `[Collection(SerialCollection.Name)]` on `QA/FogVisibleBitsQaTests` (its Perf row and allocation measurement trip
  `SerialCollectionTests`). Expected `main` after it: the three branch tips merged, studio files unioned.
- The owner's balance answer (D6 entry under For your review) is still wanted; the data track changes no number until it comes.
- Bug ids next session: sim from **BUG-0270**, view from **BUG-0280**, data from **BUG-0290**.

## Sim track

### Current session plan: M4-3b, towers that shoot, the ghost list, placement on explored ground · feature · QA full

**Goal:** finish the sim's share of M4 criterion 5. Buildings can carry an attack (the Watch Tower / Lookout Tower: docs/02
"10 pierce / 2 s, range 18; sight 24; detector 16 m"), each player keeps a "last known" list of enemy buildings it has seen
(ghosts in explored fog), and a building may be placed only on ground the player has explored.

**Scope:**
1. **Schema (sim-owned):** `BuildingDef.Attack` (optional, the unit `attack` object: `type`, `damage`, `range`, `cooldown`,
   `windup`, `projectile` (aimed), `targets`) and `BuildingDef.Detector` (optional radius, ≤ `DataLimits.MaxSight`; stored
   and validated now, used by M4-5), with validation and `ContentHash`; ship the two towers' values in
   `game/data/factions/*/buildings.json` (the minimum the sim's tests need; the data track's D8 does not touch those files
   this session: name them in both briefs). `DataValidationTests` rows.
2. **Buildings that shoot:** a finished building with an attack scans every `ScanInterval` ticks (staggered by slot) within its
   range through the spatial hash, takes a target by the unit priority (nearest enemy unit; never a building), fires a projectile
   from its footprint centre at the unit's `Fire` rule (led the same way), honours vision (its owner must see the target: the
   building's own sight circle counts like a unit's, rule (a)), applies Forge attack bonuses if docs/02 says so (check: towers get
   `ranged` upgrades? If the docs are silent, no bonus, Producer default, say so in docs/01). A site (under construction) never
   shoots. Death / damage unchanged. Hash: the building's cooldown / target fields.
3. **Ghost list:** per player, for each enemy building slot ever seen: the type, anchor cell and generation when last seen, kept
   until the player sees the cell again and the building is gone (then dropped) or sees it again (refreshed). Hashed. Read-only
   view surface `World.Fog.Ghosts(player)` (a span of structs) for the view's M4-V4 / the minimap. An explicit `Attack` on a
   ghosted building the player cannot see now is **accepted** (docs/03 said M4-3b relaxes that for buildings): the unit walks to
   it and attacks when seen; if the building is gone when the cell comes into sight the order ends.
4. **Placement:** `World.CanPlace` adds `PlacementError.Unexplored` (ordered after the terrain checks, before units in the way)
   when any footprint cell is unexplored for the player; the view's `ui.json` key `placement.unexplored` is the view's
   (Requests entry; the view may add the key in the same session).
5. Docs/03 "Vision, detection, fog" (the "Not yet" list shrinks), "Implementation (M4-3b)", the data-format rows; docs/02 is
   the reference; docs/01 rows for any Producer default.

**Out of scope:** abilities / statuses / zones (M4-4), stealth / detection behaviour (M4-5: the field is stored only), the AI,
anything under `game/` except `game/data/common/**` and the two `buildings.json` values, `ViewApi/`, `Content/`, `QA/Content/`,
`docs/factions/**` (the data track's D8 pins the towers' text after this lands).

**Acceptance criteria:**
1. The schema loads the two towers (attack 10 pierce / 2 s cooldown, range 18, detector 16) and refuses bad values (range 0,
   negative detector, a building `projectile` that is a lob, `targets: buildings`) with errors naming the field; `ContentHash`
   covers both; golden `data-hash` regenerated once (every `k` line identical, since no unit moves differently).
2. A finished Watch Tower kills an unprotected enemy walking through 18 m; a site does not shoot; a tower never targets a
   building; a target its owner cannot see (unexplored plateau above) is ignored; the shot is a projectile that the view's
   `World.Projectiles` span shows; hash twins equal over a 2,000-tick fuzz with towers on three levels.
3. The ghost list: a building seen once stays listed when the cell falls back to explored; it is dropped the first update the cell
   is visible again and the building is gone; it is refreshed on being seen again; an Attack on a listed-but-unseen building is
   accepted and ends cleanly when the building is gone; hashed (a `StateHashTests` row per field).
4. `CanPlace` returns `Unexplored` for a footprint touching unexplored cells and nothing else changes for explored ground
   (`NeverSealTests` and `ConstructionFuzzStressTests` green).
5. `TightBlob2500` alone within 4.6 ms; a 20-tower x 500-unit brawl row reports its cost (budget: ≤ 0.2 ms a tick for the towers);
   0 B per tick in the tower scan.
6. Build 0 warnings; non-Perf suite green on the branch and on a scratch merge with the view branch; scene loop 33 / 33 there
   (towers now shoot in the `--units 500` start: check `AttackOrderViewTest` / `QaV6Test` / `QaV7Test` still hold, or stage them);
   smoke PASS; docs updated; bug files set.

**Design references:** docs/02 "Buildings" (the Watch Tower row), "Vision and fog of war" (ghosts, placement), "Combat"
(priorities, projectiles); docs/03 "Vision, detection, fog" ("Not yet"), "Implementation (M4-2b)" (Fire / lead), "Buildings"
(`CanPlace` order); BUG-0090 (the towers' text).

**Tests required:** loader rows; `Combat/TowerTests` (shoot, site, no buildings, vision, cooldown); `Vision/GhostListTests`;
`StateHashTests` rows; a tower fuzz (`Stress/`) with twins + replay round trip; the `CanPlace` rows; a Perf row.

**Constraints:** no per-tick allocation (the ghost list is a fixed-capacity store per player sized by the building capacity);
no `Dictionary` iteration; determinism (golden regen once, reason in the commit); no Godot in `Rts.Sim`; the sim track never
edits `ViewApi/`, `Content/`, `QA/Content/`, `docs/factions/`; studio files append-only; **do not edit
`sim/Rts.Sim.Tests/Content/CounterTriangleMarginsTests.cs`** (D8 rewrites it this session).

**QA focus (full):** towers vs the fog (a tower on level 0 must not shoot a unit on level 1 beyond the lip; one on level 2 shoots
down), the ghost list under hostile sequences (seen, destroyed unseen, rebuilt in the same slot with a new generation, seen
again), an Attack on a ghost whose slot was reused by an own building; placement at the fog edge (one cell unexplored);
twins and replay round trip; the 33-scene loop on the merge (towers change the `--units 500` start).

### Watch-outs (sim)

- `World.Neighbors` / `FlowFields.BuildScratch` are shared scratch; the fog's layers lie over `BuildScratch` in phase 12.
- Any test that orders an explicit Attack must stage the target in the owner's sight (`CombatScenes.Spot` / `Spotter`).
- BUG-0241 (chase livelock along a cliff) is the first item of the next sim hardening, with BUG-0157's kept-chase rule
  (bound by the mean), BUG-0144, BUG-0142 items 1-2, BUG-0113 item 2, BUG-0094, BUG-0242.

## View track

### Current session plan: M4-V4, the fog on screen · feature · QA standard

**Goal:** the player sees the fog: unexplored ground black, explored ground darkened, visible ground clear; enemy units hidden
unless seen; enemy buildings hidden unless seen or ghosted (the ghost list arrives with the sim's M4-3b this session: draw
ghosts only if it is on the merged tree by the time the view integrates, else leave a hook and say so); the minimap draws the
fog and sends Attacks on enemy dots.

**Scope:**
1. **Terrain fog:** an R8 texture of `World.Fog.Visibility(local)` (one byte per cell), re-uploaded when `Version(local)` moves
   (every 4 ticks at most), sampled by the terrain shader: 0 → black (unexplored), 1 → darkened (~40 %), 2 → clear; props
   (trees, mines) and corpse / rubble markers under the same rule (hidden on unexplored, darkened on explored). A
   `--no-fog` dev flag for the existing scenes that look at the whole map (list them in docs/03 as for `--no-combat`).
2. **Units:** an enemy unit's view is hidden when `!Fog.CanSeeUnit(local, slot)` (hp bars, flashes, streaks of its shots too:
   a projectile is drawn only if its current cell is visible to the local player); it reappears without a pop when seen.
3. **Buildings:** an enemy building is drawn only when `CanSeeBuilding(local, slot)`; if the sim's ghost list is on the merged
   tree, draw a ghost (the last-known type at its anchor, darkened, no hp bar) from `Fog.Ghosts(local)`; a right-click on a ghost
   is an Attack on the building (the sim accepts it, M4-3b).
4. **Minimap:** the fog layer (black / darkened) under the dots; enemy dots only where visible; a right-click on an enemy dot
   is an Attack on it (the M4-V2 "waits for fog" item); `placement.unexplored` key in `ui.json` ("Unexplored") shown by the ghost
   when `CanPlace` says so (if M4-3b has landed; else the key only).
5. Scene test `FogViewTest.tscn` (seeds 1 / 6): texture == fog bytes every update, hidden units == `!CanSeeUnit`, hidden
   buildings == `!CanSeeBuilding`, 300 steady frames 0 B at `--units 500`, hash twin; the `--no-fog` scenes unchanged.

**Out of scope:** the fog's look (the M6 art pass), ability feedback (M4-4), stealth visuals (M4-5), any sim file outside
`ViewApi/` (read-only additions only there), `game/data/**` except `ui.json`'s new key.

**Acceptance criteria:**
1. Windowed on seed 1: the map starts black beyond the base's sight, clears as units walk, darkens behind them; a screenshot
   (`--screenshot`) looked at and named in the report.
2. Enemy units and buildings appear only when seen (the scene's per-frame checks against `CanSeeUnit` / `CanSeeBuilding`, 0
   mismatches over 900 ticks); their shots are hidden with them.
3. The minimap's fog layer matches the texture; a right-click on a visible enemy dot sends one Attack per selected unit; on a
   hidden one a Move (documented).
4. Texture uploads only when `Version` moves (count them: ≤ ticks / 4 + 1); 0 B per frame in the fog sync at 500 units.
5. Build 0 warnings; smoke PASS; scene loop green (new scene included); view Perf rows alone within budget; docs/03
   "Implementation (M4-V4)" + the `--no-fog` list; hash twins equal.

**Design references:** docs/02 "Vision and fog of war"; docs/03 "For the view (M4-V4)", "Vision, detection, fog" Known limits
(1): combat may fire at a unit the screen still hides for up to 4 ticks: draw the shot only from a visible cell.

**Tests required:** `ViewApi/FogViewTests` (the texture packer against the bytes, the hide rule, 0 B), `FogViewTest.tscn`, the
scene loop.

**Constraints:** `ViewApi/` stays read-only; no gameplay in views; player-facing text in `ui.json` only; the view never edits
`game/data/factions/**` or `sim/**` outside `ViewApi/` and its own test folders.

**QA focus (standard):** the texture against the bytes on every update of 3 seeds; units at the fog edge (a cell boundary) over
many frames; a unit revealed by a high-ground hit shown for exactly the reveal; the minimap Attack on a dot whose unit died this
tick; the `--no-fog` scenes still green; 0 B at 2,000 units.

### Watch-outs (view)

- `World.Fog.Visibility(player)` changes only on ticks where `tick % 4 == 1`; `Version` is the cheap change test.
- BUG-0251: `EconomyViewTest` can FATAL at shutdown after PASS under load (rerun once; test-side fix at the next view hardening:
  dispose the scene's references and `GC.Collect()` before `Quit`).
- Next view hardening: BUG-0251, BUG-0250 (3 items), BUG-0148 item 1, BUG-0126 items 3 / 5 / 6, export hygiene (M6).

## Data track

### Current session plan: D8, the shared harness, the stale time, the bullet anchor · feature · QA light

**Goal:** close the last D6 / D7 nits: `Content/CounterTriangleMarginsTests` calls the sim's public harness instead of its copy
(BUG-0230 item 2, BUG-0240), both pages' balance tables are re-printed from it and pinned row by row (BUG-0243: the Malazan page's
Lancer-v-Archer seat-1 row reads 21.5 s / 912 hp; the sim now plays it in 22.0 s / 888 hp), and `TechContentTests.G` reads the
Ages bullet from the file's own lines (BUG-0260).

**Scope:**
- `sim/Rts.Sim.Tests/Content/CounterTriangleMarginsTests.cs`: delete the private `Fight` / `TimeToKill` copies; call
  `Rts.Sim.Tests.Scenario.CounterTriangleScene.Fight(keyA, keyB, seatA)` / `TimeToKill(attacker, n, building)`; print the same
  table; **pin every printed row to the page's row** (units left, hp, cost kept, time), so a sim change that moves a number fails
  the test naming the pair, seat and column (the test is the alarm; the page is updated by the data track after a Producer OK).
- Both pages' "Balance baseline" tables: re-print from the harness on the merged `main` and copy in (only the Lancer seat-1 row
  should change: 912 → 888 hp, 21.5 s → 22.0 s; say so if anything else moves and stop to report).
- `TechContentTests.G`: take the Ages clause's bullet from the file's lines (a bullet starts at a line beginning "- ") and anchor
  the clause to that bullet's end, as QA's `AgesRuleQaTests.ClauseBullet` does; the " - Or the Forge alone." mutant fails G.
- BUG-0230, 0240, 0243, 0260 files and index rows set.
- **Files this track may touch:** the two pages, `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, the four bug
  files, `studio/qa/**`. **No `game/data/` edit** (the sim's M4-3b edits the two `buildings.json` this session).

**Out of scope:** any balance number (the owner's answer on the D6 proposal is still wanted); the towers' `attack` / `detector`
pins (D9, after M4-3b is on `main`); abilities (M4-4).

**Acceptance criteria:**
1. `CounterTriangleMarginsTests` has no private fight harness (grep: no `Flat(` / `Place(` scene setup of its own beyond the
   siege rows' call); its 14 printed lines equal `Scenario/CounterTriangleTests`' on the same tree.
2. Every printed row is pinned to the page: a scratch mutation of one page cell (a time, an hp) fails naming pair, seat and column.
3. The Malazan page's Lancer seat-1 row reads 888 hp / 22.0 s; no other number on either page changed (QA diffs the tables).
4. BUG-0260's same-line mutant fails G; the nine BUG-0230 / 0260 mutants fail G.
5. Content + DataValidation green; no diff under `game/`, `sim/Rts.Sim/`, `*.replay`; build 0 warnings; bug files set.

**Design references:** `docs/factions/malazan.md` / `whirlwind.md` "Balance baseline"; docs/02 "Ages"; BUG-0230 / 0240 / 0243 /
0260; `Scenario/CounterTriangleScene.cs` (the harness and its constants).

**Tests required:** the row pins; the G anchor; the existing content suite green.

**Constraints:** the data track writes no C# outside `Content/` and `QA/Content/`; the pages' other tables byte-identical; no
`game/data/` edit this session.

**QA focus (light):** criteria, build / tests, docs conformance: the 14 lines against the scenario's, the page mutations, the
mutant sweep; confirm no `game/` diff.

### Watch-outs (data)

- The sim's M4-3b edits `game/data/factions/*/buildings.json` (the towers' `attack` / `detector`) this session: D8 does not
  touch `game/data/`. D9 pins the towers' text once that is on `main`.
- Data's hardening counter is 3 / 4 after D8 → the session after is a data hardening (D-H1): BUG-0090 leftovers, the content
  tests' shared helpers, docs/factions drift.

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the 33-scene loop and smoke on each **merged** result; the order is view → data
  → sim unless a branch's first commit fixes a red row (then that branch first). Push only green states.
- Expected conflicts: `studio/qa/coverage.md` (append all sides), docs/03 (one subsection per track), docs/01 (rows appended),
  `studio/bugs/README.md` (union by id).
- **Sessions:** the 3-hour lock window declared a live session dead twice; the owner has the suggestion under For your review.
  A conductor should check for running studio processes before resuming.
- **Perf:** three tracks' suites at once fail 4-15 wall-clock rows on base and head alike; a failure counts only alone.
- QA scratch directories carry the agent's name (`qa<session>_*`); never `rm -rf` a shared scratchpad name.
- **Every QA brief:** after committing its own test files, QA reruns `SerialCollectionTests` (a Perf row or an allocation
  measurement outside the Serial collection reds the suite; it happened in 1814 and 2144) and the full non-Perf suite's
  final count must include its own files.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
