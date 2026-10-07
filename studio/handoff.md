# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session 2026-10-06-1744 (5 / 8 today; base b9af5f2). Three
tracks, all feature sessions (counters before this session: sim 2 / 4, view 2 / 4, data 0 / 4).
Incident 2026-10-06-1714 (GitHub unreachable at `git fetch`; no work planned or built) is resolved: the
network is back, the HOLD gates from that commit never reached `origin/main` and are not applied.

## Where we are

- `main` = b9af5f2: M3 2 / 8 (resources, gather loop), M2 8 / 10 (left: M2-6 audio, M2-7 60 FPS check),
  24 open bugs (S3 15, S4 9), none S1/S2. Producer checks at this PLAN: build 0 errors, 1 warning (CS8602
  `game/tests/DebugOverlayTest.cs:173`, BUG-0084); non-Perf tests green (numbers in the session log).
- **Bug numbers: sim from BUG-0080, view from BUG-0087, data from BUG-0090.**
- Shared-file rule: docs/03 (sim: "Flow fields" / "Navigation grid" / "Known limits"; view: a new "Audio
  (M2-6)" subsection under "Rendering and presentation"; data: nothing in docs/03), docs/01 change log (one
  row each, append), `studio/qa/coverage.md` (one row / note each, append). `game/data/**`: the data track
  owns both `factions/*/buildings.json` this session; the sim track touches no file under `game/data/`.
- **Golden replay conflict (expected):** both the sim task (hash composition changes) and the data task
  (`data-hash` changes) regenerate `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`. The conductor merges
  sim, then view, then data; on the conflict take the sim side, then on the merged tree run
  `$env:RTS_REGEN_GOLDEN=1; dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~ReplayGoldenTests`
  and commit the regenerated file, then run `ReplayGoldenTests` once more without the variable. Each branch's
  own proof (sim: trajectories identical; data: `k` lines byte-identical) stands on its branch.
- **Test ownership exception (one method):** `sim/Rts.Sim.Tests/DataValidationTests.cs` lines ~378-399
  (the shipped-buildings test) pins the roster to the two Town Halls and asserts `FindBuilding("malazan_barracks") == -1`;
  the data task may rewrite that one method (and nothing else in sim-owned test files); the sim task must not
  touch `DataValidationTests.cs` this session.

## Sim track

### Current session plan (sim): M3-2b — closing vs opening grid changes (BUG-0073 + BUG-0077)

Goal: a felled tree (an *opening* change) must not stall every walker on the map, and a building placed
in front of a marching column (a *closing* change) must not make the column give up. Today every grid
change makes every cached flow field stale at once; under the 2-builds-per-tick cap, continuous felling
leaves most groups standing (BUG-0073), and after a closing rebuild the higher detour cost reads as "no
progress" so half the column gives up (BUG-0077). This is the last piece before M3-3 (player-placed
buildings), where BUG-0077 would be S2.

Scope:
- `NavGrid.BlockVersion` (public get, hashed): bumps only on closing changes (`SetFootprint`: node spawn,
  building placed). `Version` keeps bumping on every change. `BumpVersionForTests` bumps both (a closing
  bump), so the view's "bump → relist / Peek null" tests still hold.
- `FlowField` carries a `BlockVersion` tag set at build. A cached field is **current** when
  `Version == grid.Version` and **usable** when `BlockVersion == grid.BlockVersion` (nothing it reaches
  has been blocked since it was built, so it never points into a blocked cell; it may miss a shorter route
  through newly opened cells). `Find` / `Contains` / `PeekCached` / `TryGetCached` answer for *usable*
  fields (`PeekCached` is documented as "the field units follow"). `Get` on a usable-but-stale slot rebuilds
  it in place (today's path).
- Build pass (`BuildMissingFields`): a needed goal with no usable field is a **miss**; one with a usable but
  stale field is a **refresh**. Under the same `MaxFieldBuildsPerTick` cap: misses first (oldest order
  first, as today), then refreshes (oldest field `Version` first, ties by goal cell). A unit standing on a
  cell whose direction is `NoDirection` in a usable-but-stale field (a cell opened after the build) waits
  like a missing field (`ActWait`) and its goal counts as a miss, not a refresh.
- Progress mark: when `grid.BlockVersion` differs from the value seen at the previous movement pass, reset
  every Moving unit's `BestRemaining` to +inf before the plan pass (the detour's first tick becomes a new
  best; `StuckTicks` follows the existing rule). No reset on opening changes. The "last seen" value lives on
  `World`; hash it or prove it derived (the QA reflection audit classifies every field).
- Hash: the grid's `BlockVersion` and each used slot's `BlockVersion` tag join `StateHash`. Golden
  regenerated once (the M3-1 / M3-2 procedure: the `k` lines change because of the hash composition; the
  command lines and every unit's trajectory are proven byte-identical before / after in the report).
- BUG-0074 only if cheap (≤ 60 lines): refuse (validation error) a forest tree type whose footprint isn't
  1 x 1, un-skip the QA BUG-0074 row. Otherwise leave it.
- docs/03 "Flow fields" (replace "Any passability change invalidates the cache"), "Navigation grid"
  (`Version` / `BlockVersion`), "Local movement" give-up rule, "Known limits"; docs/01 row.
- OUT: anything under `game/data/`; `DataValidationTests.cs`; `ViewApi/**`; M3-3 placement / construction;
  BUG-0078 (M3-3); time-sliced builds (BUG-0023); region versions; incremental repair; new public methods on
  `FlowFieldCache` (properties with no public setter are fine).

Acceptance criteria:
1. `NavGridTests`: `BlockVersion` bumps once on `SetResource` / `SetBuilding` / `BumpVersionForTests` and
   not on `ClearResource` / `ClearBuilding`; `Version` bumps once on all five; `StateHashTests` rows for
   the grid's `BlockVersion` and a field's tag.
2. `FlowFieldCacheTests`: after an opening change every cached field is still returned by `PeekCached`,
   `Contains` and `TryGetCached`; after a closing change none is; `Get` on a usable-stale slot rebuilds in
   place (same slot, `BuildCount` + 1, `Count` unchanged); the 10,000-peek hash twin still passes.
3. QA's `OneTreeFallsEveryTick_NoWalkerStandsWaitingForItsFieldMoreThan40Ticks` un-skipped and passing
   with a tighter bound in a dev twin: 32 groups walking, one far-away tree felled every tick for 100 ticks:
   longest field wait ≤ 2 ticks, 0 give-ups, every walker arrives; `BuildCount` grows by ≤ 2 per tick; avg
   tick on that scene under 0.5 ms (report the before / after).
4. A unit standing on a freshly opened cell (spawned or shoved there after the field was built, with its
   field usable-stale and `NoDirection` on that cell) waits (velocity 0, still Moving, no stuck count
   increase beyond the missing-field rule) until its goal is rebuilt within the cap, then walks; it never
   steps into a blocked cell (the existing per-tick invariants in `Stress/MovementStressTests` hold).
5. QA's `ABuildingDroppedOnAMarchingColumnsPath_NoUnitCenterEverEntersItsCells_AndTheColumnStillArrives`
   un-skipped and passing: 16 / 16 within 6 m of the goal; a dev test shows `BestRemaining` is +inf on the
   tick after a closing change for every Moving unit and untouched by an opening change.
6. `DeterminismTests`: twin hash every tick over 3,000 ticks with 20 chopping workers, 32 marching groups
   and 10 building drops; replay round trip with the same scene; `ReplayGoldenTests` green on the
   regenerated golden, with the trajectory proof in the report.
7. `AllocationTests`: 0 bytes per tick with felling every tick and refreshes running.
8. QA's `FieldCacheHashQaTests` public-method pin stays `CapacityFor, Contains, PeekCached`; the hashed /
   derived audit classifies the new tag and the "last seen" value.
9. M1 trajectories unchanged where no grid changes happen: `CrossMapScenario` seeds 1-8, crowd rows and the
   golden's command lines byte-identical; `Pathfinding/` perf rows (500 marching 0.62 ms, 2,500 tight blob
   ≤ 4.5 ms) within noise.
10. docs/03 and docs/01 updated; `game/data/**`, `DataValidationTests.cs`, `ViewApi/**` untouched;
    `Command` layout and `SimConfig` unchanged (additive only).

Design references: docs/03 "Flow fields" (cache tagging, build cap, PeekCached contract), "Local
movement" (BestRemaining / StuckTicks / GiveUpTicks = 20), "Navigation grid"; BUG-0073 and BUG-0077 notes;
docs/03 "Economy implementation (M3-1)" (depletion only opens cells).

Tests required: the dev tests named in criteria 1-7; the two QA rows un-skipped; golden regenerated with
proof; `--filter Category!=Perf` green; Perf rows run alone once.

Constraints: no allocation in per-tick code (the refresh list is a preallocated buffer on `World`); no
`Dictionary` iteration; hash everything that decides which units wait (BUG-0021 principle); no Godot; no
new public mutators on the cache; docs in the same commit. Size: ≤ 800 code lines (uncertain semantics),
tests ≤ 600; say when exceeded.

### Next task candidates (sim)

1. **M3-3 building placement + construction** (QA full): the ghost validity rule in the sim
   (`BuildingStore.Fits` + the unit check + **no sealing of passable ground**, which closes BUG-0078 and
   makes exposure mean "reachable"), `Command.Build` / construction progress with `t x 3 / (n + 2)`, costs
   deducted / refunded, destruction (`Free`), repair. D1 (this session) gives it every slot to place.
2. M3-4 production queues, M3-5 Age II / Forge (ships the `techs.json` schema and a building `requires`
   field, both requested by the data track), M3-6 `trainedAt` / `requires` resolution and validation.
3. Debt for the next sim hardening (after 1 more feature session): BUG-0079, BUG-0076, BUG-0071,
   BUG-0072, BUG-0008 / 0010, BUG-0005 before M5, BUG-0025 / 0026; docs/03 "Unreachable targets can't
   happen" sentence (BUG-0078) once M3-3 lands.

## View track

### Current session plan (view): M2-6 — placeholder audio for select and command

Goal: the game makes a sound when you select your units and when you give them an order, so the audio
plumbing exists from M2 (docs/04 "Placeholder art": generated tones per event) and the M2 criterion
"Placeholder audio for select and command" is met. No downloads, no third-party files: every sound is
synthesized in code at startup.

Scope:
- `game/scripts/Sfx.cs` (a Node under `Match`): a small pool of `AudioStreamPlayer`s (8) and a table of
  events → generated `AudioStreamWav` clips (16-bit PCM, 44.1 kHz, built once at `_Ready`): `Select` (one
  short tone, ~60-80 ms, quick decay) and `Command` (a short two-note confirm, ~120 ms). Distinct enough
  to tell apart; peak ≤ 0.9 full scale; attack / release ramps so clips never click. The table is the
  extension point for M3 / M4 events (build complete, attack, death, alert); no further events now.
- Hooks in `SelectionController`: `Select` when a user action (click, box, double-click / Ctrl-click,
  group recall) leaves the selection non-empty and changed; `Command` from `Order(...)` when at least one
  command was enqueued (Move, AttackMove, Stop, Hold, queued or not, from the 3D map or the minimap). Not
  on Tab, Ctrl + digit assign, a click on empty ground, an order with nothing selected, or a dropped order.
- Rate limits: at most one `Select` and one `Command` per frame (a box over 100 units is one sound), and a
  minimum gap of 50 ms between repeats of the same event, so click spam doesn't buzz.
- `--mute` in `LaunchOptions` (mutes the master bus; the counters below still run). A `SfxVolumeDb`
  constant (placeholder for the M6 settings volume).
- Headless: `tools/qa/smoke.ps1` must still PASS (headless Godot runs the dummy audio driver; playing a
  stream must not log an ERROR); if it does, the node skips playback headless but still counts.
- Counters for tests: `Sfx.PlayCount(SfxEvent)` and `Sfx.LastPlayedFrame(SfxEvent)`.
- docs/03 "Rendering and presentation": a new "Audio (M2-6)" subsection (events, hooks, rate limits,
  `--mute`, where the M6 volume setting plugs in); docs/01 row. The Producer ticks the roadmap criterion.
- OUT: music; attack / death / build sounds (M3 / M4); a settings screen (M6); third-party audio; any
  change under `sim/**` (no ViewApi work is needed; tone synthesis lives in `game/scripts/`, note the
  architecture scan forbids `Math.Sin` inside `Rts.Sim`, so keep it on the Godot side); BUG-0084's warning
  (M2 hardening).

Acceptance criteria:
1. `game/tests/SfxTest.tscn` (headless, prints `SFX TEST PASS`): each clip has the expected length ± 5 ms,
   peak ≤ 0.9, first and last 2 ms under 0.05, no NaN; the two clips differ.
2. Same scene, through the real `Match` with injected input: click-select an own unit → `Select` 1; box
   over 100 → still 1 more; click empty ground → no `Select`; double-click a type → 1; group recall (digit)
   → 1; Tab → 0; Ctrl + 1 → 0; selecting the same single unit again → 0.
3. Orders: right-click with a selection → `Command` 1; Shift + right-click → 1; S → 1; H → 1; A then click
   → 1; minimap right-click → 1; right-click with nothing selected → 0; an order dropped whole (queue
   overflow, as in `QaM24Test`) → 0; 50 right-clicks in one frame → 1.
4. Rate limit: two `Select`s 20 ms apart → 1 play; 60 ms apart → 2.
5. `--mute`: master bus muted, counters still increment; without it the bus is unmuted; a bad or
   value-taking `--mute` form is handled like the other flags (it takes no value).
6. `tools/qa/smoke.ps1` PASS; every existing `game/tests/*.tscn` scene still PASS (they run the real
   `Match`); the sim's StateHash is untouched by sound (trivially: `Sfx` reads nothing from the sim; say so
   in the docs).
7. Windowed: the dev runs `& $env:GODOT --path game`, selects and orders, hears both sounds, and reports
   it (one line); the frame-time label shows no change at 2,000 units (`--units 1000 --zoom 60`).
8. docs/03 and docs/01 updated; no change under `sim/**` or `game/data/**`.

Design references: docs/04 "Placeholder art" (Audio line); docs/02 "Presentation" (alerts show a sound:
not this task, but the event table should make it a one-line addition); docs/03 "Rendering and
presentation" (where the new subsection goes); docs/02 "Settings" (SFX volume, M6).

Tests required: `SfxTest.tscn` as above; all existing scenes; smoke.

Constraints: C# only; views hold no gameplay state; no per-frame allocation once clips are built
(`Play` must not allocate); rebindable actions untouched; absolute paths in any file I/O; Godot types stay
out of `Rts.Sim`. Size: ≤ 600 code lines, tests ≤ 400; say when exceeded.

### Next task candidates (view)

1. **M2-7 playable check** (QA standard): 100 placeholder units at 60 FPS windowed on the default 12 / 8
   map, `PrevFacing` blending (landed in M3-2), a stable screenshot set; then the **M2 end-of-milestone
   hardening** (BUG-0069, BUG-0070, BUG-0083, BUG-0084, **BUG-0085** (`StartLayout.Block` skips cells
   8-adjacent to a `Resource` cell), BUG-0086, export hygiene notes) → M2 sign-off (every M2 coverage row
   ✅ Unit / fuzz / Determinism).
2. M3 view side after M2: HUD resource bar (`World.Gold` / `Wood`), selection panel, command card (Tab
   subgroups), build ghosts (after M3-3), worker / gather feedback (`Cargo`, `Gathering` / `Returning`).

## Data track

Owns `game/data/factions/**`, `docs/factions/**`, tests in `sim/Rts.Sim.Tests/Content/` and
`QA/Content/`. Fills content against schemas already on `main`; never changes C# outside its test folders
(one named exception this session, below); requests schema changes through "Requests for the sim track"
in STATE. QA tier `light` (balance passes `standard`). Every accepted data task gets a For your review table
(unit / building / tech, field, old → new, why; quoted names and descriptions); the owner's inbox replies
become the track's next task.

### Current session plan (data): D1 — complete `buildings.json` for both factions

Goal: both factions get their full ten-building roster in data (today only the Town Hall exists), so
M3-3 (placement and construction) has every slot to place and the owner can review the names, numbers and
descriptions. Numbers come from docs/02 "Buildings"; names and ids from the faction pages.

Scope:
- `game/data/factions/malazan/buildings.json` and `game/data/factions/whirlwind/buildings.json`: add the
  nine missing slots per faction, in the slot order of `DataLimits.BuildingSlotIds` after the existing
  Town Hall entry (which stays first and byte-identical): `house`, `camp`, `infantry_hall`, `ranged_hall`,
  `shock_hall`, `forge`, `caster_hall`, `siege_works`, `watch_tower`. Ids and `displayName`s from
  docs/factions/malazan.md and whirlwind.md "Buildings" tables (`malazan_billet` "Billet", `malazan_depot`
  "Quartermaster's Depot", `malazan_barracks` "Legion Barracks", `malazan_crossbow_range` "Crossbow Range",
  `malazan_wickan_corral` "Wickan Corral", `malazan_armory` "Armory", `malazan_cadre_tower` "Cadre Tower",
  `malazan_engineers_yard` "Engineers' Yard", `malazan_watchtower` "Watchtower"; `whirlwind_tent` "Tent",
  `whirlwind_supply_cache` "Supply Cache", `whirlwind_raider_camp` "Raider Camp", `whirlwind_archer_camp`
  "Archer Camp", `whirlwind_horse_lines` "Horse Lines", `whirlwind_smithy` "Smithy", `whirlwind_shrine`
  "Shrine of the Whirlwind", `whirlwind_ram_yard` "Ram Yard", `whirlwind_lookout_tower` "Lookout Tower").
- Numbers per slot, both factions identical (docs/02 table; `hp`, `armor`, `cost {gold, wood}`,
  `buildTime` seconds, `footprint`, `popProvided`, `dropOff`): House 500 / 3 / 0 / 50 / 20 / 2x2 / +8 / no;
  Camp 600 / 3 / 0 / 75 / 25 / 2x2 / 0 / **yes**; Infantry Hall 1200 / 4 / 0 / 150 / 40 / 3x3 / 0 / no;
  Ranged Hall 1200 / 4 / 0 / 150 / 40 / 3x3; Shock Hall 1200 / 4 / 75 / 150 / 45 / 3x3; Forge 1000 / 4 /
  100 / 100 / 40 / 3x3; Caster Hall 1200 / 4 / 150 / 150 / 50 / 3x3; Siege Works 1400 / 4 / 150 / 200 /
  55 / 3x3; Watch Tower 800 / 5 / 50 / 125 / 35 / 2x2. Exactly two `dropOff: true` per faction (Town Hall,
  Camp).
- `description`: one sentence each, player-facing, in the faction's voice, that says what the building
  does (the slot's "Provides" column: "+8 population", "drop-off for gold and wood", "trains Raiders",
  "upgrades", "trains Priests; needs Age II", "watches 24 m and shoots"), ≤ 160 characters, ends with a
  period, no lore-only text. Unit names in descriptions must match the units.json `displayName`s.
- The schema has no `requires` field (Shock Hall ← Infantry Hall; Caster Hall, Siege Works, Watch Tower
  ← Age II) and no tower attack / sight / detector fields: leave them out and list them in the report as
  requests for the sim track (the Producer records them in STATE).
- Tests, new file `sim/Rts.Sim.Tests/Content/BuildingContentTests.cs` (shipped data via
  `TestDataDir.Shipped` / `TestSim.Data`): (a) each faction has exactly ten buildings, one per slot, Town
  Hall first; (b) ids and `displayName`s equal a table in the test copied from the faction pages;
  (c) every numeric field equals a table in the test copied from docs/02 (not the loader's values);
  (d) exactly two drop-offs per faction and which; (e) every unit's `trainedAt` is a building id of its
  own faction whose slot matches the unit's slot (worker → town_hall, line → infantry_hall, ranged →
  ranged_hall, shock → shock_hall, caster → caster_hall, siege → siege_works; unique: Sapper →
  siege_works, Zealot → infantry_hall); (f) every `description` is non-empty, ≤ 160 chars, trimmed,
  ends with a period, and names only units that exist in the faction's `units.json`.
- **One sim-owned test method may change:** `DataValidationTests.cs` lines ~378-399 (the shipped-buildings
  test) pins the roster to the two Town Halls and `FindBuilding("malazan_barracks") == -1`. Rewrite that
  method only: the Town Hall assertions apply to the `town_hall` entry of each faction (keep every one of
  them), the exact-list assertion becomes "the first building of each faction is its Town Hall and ids are
  unique", and the absent-id probe uses `malazan_no_such_building`. Nothing else in that file.
- Golden: `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` regenerated once (`$env:RTS_REGEN_GOLDEN=1`
  then `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~ReplayGoldenTests`, then once more
  without). Only the `data-hash` and `checksum` lines may differ from `main`; every `k` checkpoint line and
  command line byte-identical (paste the diff in the report). The conductor handles the merge conflict with
  the sim track's regen (see "Where we are").
- OUT: `units.json`, `faction.json`, `common/**`; any C# outside `Content/` and the one named method;
  docs/factions stats tables (D2); balance changes to the Town Hall; `techs` / `ai` content (no schema).

Acceptance criteria:
1. `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf` green, including `ShippedData_LoadsWithNoErrors`,
   `DataValidationTests` (rewritten method), `BuildingDataQaTests` (the "empty list loads" row is
   untouched), `DataContentHashTests`.
2. `BuildingContentTests` (a)-(f) exist and pass; (c)'s table is typed from docs/02, not computed.
3. Both files: 10 entries, Town Hall entry byte-identical to `main`, slot order as above, ids unique
   across both files.
4. Golden regenerated; diff vs `main` = `data-hash` + `checksum` only (report shows it).
5. `dotnet run --project tools/Rts.Cli -- run --seed 1 --units 50 --workers 10 --forests 12 --mines 8 --ticks 2000`
   still ends `player 0 gold 250 wood 300` and twin runs match (the Town Hall is found by slot).
6. `tools/qa/smoke.ps1` PASS (the game loads the data at boot).
7. The report lists every new `displayName` and `description`, quoted, per faction, and the schema
   requests (`requires`, tower attack / sight / detector).

Design references: docs/02 "Buildings" (table + the Camp rationale), "Economy" (drop-off = Town Hall and
Camp; Population: House +8), "Tech / Ages" (what needs Age II); docs/factions/malazan.md and whirlwind.md
"Buildings", "Units" (names for descriptions), "Lore hook" / "Playstyle" (voice).

Tests required: `Content/BuildingContentTests.cs` (a)-(f); the rewritten `DataValidationTests` method;
golden regen; full non-Perf suite; smoke.

Constraints: data only (no stat in C#); snake_case ids; unknown fields are load errors (don't invent
fields); player-facing text only in data; `buildTime` in seconds (the loader converts); absolute paths.
Size: ≤ 300 lines of JSON, ≤ 250 of tests (incl. the one rewritten method); say when exceeded.

### Next task candidates (data)

1. Owner review tweaks from the inbox (first whenever present).
2. **D2: docs/factions pages** carry a stats table per building (today only names), so the pages stay the
   design source; unit `description` polish (check every one reads as a player-facing line).
3. Waiting on schemas: `techs.json` + building `requires` (M3-5 / M3-6), `abilities.json` /
   `statuses.json` (M4), `ai.json` build orders (M5); M7-M9 faction data when those milestones open; balance
   passes (QA standard) after the M4 sandbox.

## Watch-outs

- Three tracks build and test at once: Perf rows fail from CPU contention; a failure counts only when it
  fails again alone.
- Golden conflict between sim and data (rule in "Where we are"); the Producer re-runs `ReplayGoldenTests`
  on `main` after the merges.
- `DataValidationTests.cs`: data edits one method, sim stays out of the file.
- Size: last session landed near budget on code and over on tests; keep the explicit test budgets.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" and has no CLI line (STATE's For your
  review carries the suggested text).
- Builders: absolute paths only; never touch the owner's main checkout. Remote Control unavailable in
  unattended sessions. The routine's hourly schedule may be gone (see Waiting on you).
