# Handoff: brief for the current / next session

Written by the Producer at the ACCEPT of session 2026-10-06-1503 (4 / 8 today). Both tracks ACCEPT
(sim M3-2, view M2-3b), 0 fix rounds. The owner added a **third track, `data`**, mid-session (commit
114deba); this session ran two tracks, the next one runs three.

## Where we are

- After the two merges `main` should hold: M3 2 / 8 (resources, gather loop), M2 8 / 10 (left: M2-6
  audio, M2-7 60 FPS check), 24 open bugs (S3 15, S4 9), none S1/S2. The conductor re-checks `main`
  after merging; expected: build 0 errors, 1 warning (CS8602 `game/tests/DebugOverlayTest.cs:173`,
  BUG-0084); non-Perf tests about 2,330 / 18 skipped / 0 failed; smoke PASS.
- Feature counters after this session: **sim 2 / 4, view 2 / 4, data 0 / 4** (hardening every 4th).
- **Bug numbers for the next session: sim from BUG-0080, view from BUG-0087, data from BUG-0090.**
- Shared-file rule for three tracks: docs/03 (sim: "Economy implementation"; view: "Rendering and
  presentation"; data: nothing, it edits `docs/factions/**` only), docs/01's change log (one row each,
  append), `studio/qa/coverage.md` (one row each). Name every `game/data/` file per track in the briefs:
  the sim track must not touch `game/data/factions/**` in a session where the data track works there.
- Sim API the view and data tracks rely on (keep additive): `World.Resources` / `Buildings` spans,
  `World.Gold` / `Wood`, `GameData.Resources` / `Buildings`, `NavGrid.Version`, `MapGenParams.Forests` /
  `GoldMines`, `SimConfig.Map`, `Command.SpawnUnit` / `SpawnBuilding` / `Gather`, `UnitStore.PrevFacing`.
- Merge watch-out: both tracks edited `studio/bugs/README.md` and `studio/qa/coverage.md` (sim changed
  the Economy row, view added a row above it); keep both sides.

## Sim track

### Next task candidates (sim)

1. **M3-2b (feature, QA full, small, ≤ 800 lines): BUG-0073 + BUG-0077, closing vs opening grid
   changes.** `NavGrid.BlockVersion` bumps on *closing* changes only (node or building placed);
   `Version` keeps bumping on every change. A cached field whose `BlockVersion` matches is *usable*
   (it never points into a blocked cell) even when its `Version` is stale; units follow it; the build
   pass refreshes stale-usable fields under the 2-per-tick cap after misses, oldest first; a unit on
   a cell with `NoDirection` in a usable field waits like a missing field. On a closing change, reset
   every walker's `BestRemaining` (progress mark) so the detour doesn't count as "no progress"
   (BUG-0077's cause: `MovementSystem` give-up rule). `PeekCached` returns the field units follow
   (documented); `BumpVersionForTests` is a closing bump so the view's never-stale tests hold; both
   versions hashed; golden regenerated once if the hash composition changes (trajectories identical).
   QA pins `FlowFieldCache`'s public surface (`FieldCacheHashQaTests`): coordinate, no new public
   members beyond what the brief names. Measure: QA's `EconomyQaTests` BUG-0073 Perf rows (20 workers
   chopping, 32 marching groups) should show longest field wait ≈ 0 at the shipped cadence; the
   skipped BUG-0077 row un-skips. Then BUG-0074 if cheap (validate tree footprint 1 x 1 or generalize
   `TryForest`).
2. **M3-3 building placement + construction** (QA full): ghost validity rule in the sim (the
   `BuildingStore.Fits` rule + the unit check + **no sealing of passable ground**, which also closes
   BUG-0078 and makes exposure mean "reachable"), `Command.Build` / construction progress with
   `t x 3 / (n + 2)`, costs deducted / refunded, destruction (`Free`), repair. The data track's full
   `buildings.json` lands first (next session), so M3-3 has every slot to place.
3. M3-4 production queues, M3-5 Age II / Forge (ships the `techs.json` schema the data track then
   fills), M3-6 is mostly data now (sim: `trainedAt` / `requires` resolution and validation).
4. Debt for the next sim hardening (2 feature sessions away): BUG-0079, BUG-0076, BUG-0071, BUG-0072,
   BUG-0008 / 0010, BUG-0005 before M5, BUG-0025 / 0026; docs/03 "Unreachable targets can't happen"
   sentence (BUG-0078) once M3-3 lands.

## View track

### Next task candidates (view)

1. **M2-6 placeholder audio (feature, QA light, ≤ 600 lines):** generated tones (no downloads, no
   third-party files) for select and command confirm, through an `AudioStreamPlayer` pool; volume from
   a setting with a `--mute` flag for tests; docs/02 "Audio" section if it exists, else docs/03
   "Rendering and presentation". Hash twin unchanged by sound.
2. **M2-7 playable check** (QA standard): 100 placeholder units at 60 FPS windowed on this PC with the
   default 12 / 8 map, `PrevFacing` blending (view request 4, landed in M3-2), a stable screenshot set;
   then the **M2 end-of-milestone hardening** (BUG-0069, BUG-0070, BUG-0083, BUG-0084, **BUG-0085**:
   `StartLayout.Block` skips cells 8-adjacent to a `Resource` cell so the default army stands in a
   clearing; BUG-0086; export hygiene notes) → M2 sign-off (QA coverage: every M2 row ✅ Unit / fuzz /
   Determinism; check the minimap and camera rows' 🟡 Scenario columns are fine to leave).
3. M3 view side after M2: HUD resource bar (`World.Gold` / `Wood` exist now), selection panel, command
   card, build ghosts (after M3-3), worker / gather feedback (a worker's `Cargo` and state are public).

## Data track

New since the owner's note of 2026-10-06 (processed at this ACCEPT). Owns `game/data/factions/**`,
`docs/factions/**`, tests in `sim/Rts.Sim.Tests/Content/` and `QA/Content/`. Fills content against
schemas already on `main`; never changes C# outside its test folders; requests schema changes through
"Requests for the sim track" in STATE. QA tier `light` (balance passes `standard`). Every accepted data
task gets a For your review table (unit / building / tech, field, old → new, why; quoted names and
descriptions); the owner's inbox replies become the track's next task.

### What the current schemas support (as of this merge)

- `units.json`: all 7 slots per faction already shipped in M1-2 from the faction pages (checked by hand
  then: all 14 match). `trainedAt` / `requires` are unresolved strings until M3-6.
- `buildings.json` (M3-2 schema): `id, displayName, description, slot, footprint {width, height}, hp,
  armor, cost {gold, wood}, buildTime, popProvided, dropOff`; ten slots accepted
  (`DataLimits.BuildingSlotIds`); ids unique across factions; unknown fields are errors. **Shipped: the
  Town Hall only.** No test pins the building count (checked: tests use `Data.Buildings.Length`).
- Not yet: `techs.json`, `abilities.json`, `ai.json`, `statuses.json` (schemas arrive with M3-5 / M4 /
  M5). Faction `bonus` is text only (Whirlwind's 15 % gather bonus needs StatModifiers, M3-6).

### Next task candidates (data)

1. **D1 (feature, QA light): complete `buildings.json` for both factions**: the nine missing slots per
   faction from docs/02 "Buildings" (HP, armor, cost, build time, footprint, pop, drop-off; Camp is a
   drop-off, Town Hall and Camp only) with the names and ids from docs/factions/malazan.md and
   whirlwind.md "Buildings" tables (`malazan_billet`, `malazan_depot`, `malazan_barracks`,
   `malazan_crossbow_range`, `malazan_wickan_corral`, `malazan_cadre_tower`, `malazan_engineers_yard`,
   `malazan_armory`, `malazan_watchtower`; `whirlwind_tent`, `whirlwind_supply_cache`,
   `whirlwind_raider_camp`, `whirlwind_archer_camp`, `whirlwind_horse_lines`, `whirlwind_shrine`,
   `whirlwind_ram_yard`, `whirlwind_smithy`, `whirlwind_lookout_tower`), one-line `description` each in
   the faction's voice. Content tests in `sim/Rts.Sim.Tests/Content/BuildingContentTests.cs`: every
   slot present once per faction, numbers equal the docs/02 table (a table in the test, not the
   loader's), names equal the faction pages, exactly two `dropOff` per faction. **The data hash
   changes, so the golden replay (`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`) is regenerated
   once** (`RTS_REGEN_GOLDEN=1`, the M3-1 / M3-2 procedure): only the `data-hash` and `checksum` lines
   may change; every `k` checkpoint line and command line must be byte-identical (no unit stat moved),
   and the brief says so. Watch: the CLI's `FindBuilding(faction, TownHall)` takes the first match, and
   `Fits` uses the footprint, so bigger rosters change nothing in M3-2's tests; `BuildingDataQaTests`
   pins "empty list loads" only. Size ≤ 300 lines of JSON + ≤ 200 of tests. Files: both
   `game/data/factions/*/buildings.json` (the sim track stays out of them this session).
2. **D2: docs/factions pages** carry a stats table per building (today only names), so the pages stay
   the design source for the data track; `techs.json` content once M3-5 ships the schema; unit
   `description` polish; AI build orders (`ai.json`) once M5 ships the schema; M7-M9 faction data when
   their milestones open. Balance passes (QA standard) after the M4 combat sandbox.

## Watch-outs

- Perf rows fail from CPU contention while another track's QA runs: a failure counts only when it
  fails again alone (view saw one uncaptured single failure this session; two reruns green).
- Three tracks now build and test at once: expect longer wall-clock runs; the conductor merges sim,
  then view, then data.
- Size: this session landed near budget on code (sim 1,340 / 1,500; view ~800) and over on tests
  (sim 1,270 / 1,000; view 780 / 600). Keep the explicit test budget in briefs and ask the dev to say
  when it's exceeded.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" and has no CLI line (STATE's
  For your review carries the suggested text).
- Builders: absolute paths only; never touch the owner's main checkout. Remote Control unavailable in
  unattended sessions. The routine's hourly schedule may be gone (see Waiting on you).
