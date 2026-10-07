# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session **2026-10-07-0925** (second full session of 2026-10-07, cap 8; base
3498407 = `origin/main`). Feature session on **sim** (M3-4) and **view** (M3-V1); **data STOPs cheaply** (no inbox
tweaks, no schema to fill before M3-5). Counters after this session: sim 1 / 4, view 1 / 4, data 2 / 4.
**Bug numbers: sim from BUG-0097 (0097-0100 free, then 0112-0121), view from BUG-0106 (0106-0110 free, then
0122-0131), data from BUG-0132.**

## Where we are

- `main` at 3498407: M0, M1, M2 Done; M3 3 / 8 (resources, gather loop, placement + construction + repair sim half;
  data: units + buildings pinned to the faction pages). 20 open bugs (S3 12, S4 8), no S1 / S2. Producer checks at
  this PLAN: build 0 errors / 0 warnings; non-Perf suite rerun (numbers in the session log at ACCEPT).
- Shared-file rule: docs/03 (one subsection per track), docs/01 change log (append one row), coverage.md (append),
  `studio/bugs/README.md` (append rows). Append-only conflicts are expected there; keep every side, sim then view.
- Golden: only the sim track may regenerate it. M3-4 adds hashed state (queues, pop, rally); if the hash words are
  added only when non-default (the M1-7 / M3-3 precedent), the golden's checkpoints need not move at all. The log
  must say which checkpoints moved and why.
- The view's M3-V1 builds against the base commit, so it must not read anything M3-4 adds (`HalfPop`, queues): the
  pop slot of the resource bar stays hidden this session (M3-V3 fills it).

## Sim track

### Current session plan: M3-4 — production queues, rally points, population and cap, refunds (feature, QA full)

**Goal.** The next unmet M3 criterion: "Production queues (5 slots), rally points, population and cap, refunds on
cancel." A finished building trains the units whose `trainedAt` names it: up to 5 queued items, paid when queued,
refunded in full when cancelled; the head item takes `trainTime` ticks, reserves population when it *starts* (a full
cap pauses the queue), and spawns the unit on the nearest free cell outside the footprint, sent to the building's rally
point if one is set. Population (used / cap) per player is visible to the view, in half-pop units.

**Scope.**
- Loader: resolve each unit's `trainedAt` to a building type id at load (must exist, must belong to the same faction;
  one `DataError` each otherwise; `DataValidationTests` cover both). `requires` stays an unresolved string list
  (M3-5 / M3-6); in M3-4 a unit with a non-empty `requires` is **not trainable** (`LockedByRequirement`), so the Sapper
  and the Zealot wait for Age II to exist, which is the design truth today.
- `BuildingStore`: per-building flat queue arrays (`QueueCount`, `QueueTypeId[Capacity * 5]`, head first; entries past
  the count default), `Progress` (ticks done on the head item), rally (`HasRally`, `RallyPosition`). All reset on
  Alloc / Free; all hashed (add the words only when non-default so the golden's checkpoints can stay byte-identical;
  say in the log whether they moved).
- Commands (not unit orders, not queueable, addressed like `Cancel`: the player's own **finished** building covering
  `Position`): `Train(player, building, unitTypeId)`, `CancelTrain(player, building, slotIndex)`,
  `SetRally(player, building, target)` / `ClearRally`. Pick fields on `Command` for the second position / index
  (`TypeId` for the unit type and the slot index is acceptable; document it). Dropped, never thrown, for: a site, an
  enemy's or no building, an unknown or wrong-faction unit type, a type not trained there, a locked type, a full queue
  (5), can't afford. `CancelTrain` of a bad index is dropped; of the head item in progress loses the progress and
  releases its pop reservation; always refunds the full cost.
- `ProductionSystem.Run` in tick phase 3 (before construction / economy), buildings in slot order: if the head item
  hasn't started, start it when `HalfPop + unit.HalfPop <= HalfPopCap` (else wait, no progress); count ticks; at
  `trainTicks` spawn (see below); if no cell is free, the item stays complete and retries next tick (never stack).
  Spawn placement = the push-out ring search of M3-3 / M3-H1 (share it; **cap the ring walk at the level's bounding
  box**, the BUG-0095 lesson, then wait). The spawned unit gets the Move rule to the rally point if set; if the rally
  point is on a resource node and the unit is a `worker`, it gets `Gather` instead (docs/02: right-click sets the
  rally; AoE's "rally on a mine" rule); otherwise it stands Idle.
- Population: per-player `HalfPop` (sum of live units' `HalfPop`, kept incrementally, plus reservations of started
  items) and `HalfPopCap = min(sum of finished own buildings' HalfPopProvided, rules HalfPopCap)`. Dev `SpawnUnit`
  bypasses the cap but counts (so tests and the view agree on the number). A building freed (Damage to 0) lowers the
  cap; nothing dies, training just pauses; its queue is refunded in full. A site has no queue and provides no pop.
- Public reads for the view (read-only, allocation-free): `World.HalfPop` / `World.HalfPopCap` (per-player spans),
  `Buildings.QueueCount`, `QueueTypeAt(slot, i)`, `Progress`, `TrainTicks(unitType)`, `HasRally`, `RallyPosition`,
  `UnitDef.TrainedAtTypeId`. Also `GameData` helpers the view's M3-V3 production card will need: "which unit types
  does building type b train" as a precomputed `ImmutableArray<int>` per building type (sorted by id).
- CLI: `tools/Rts.Cli` prints pop in its per-player summary line; optional `--train` is **out**.
- Docs: docs/03 "Economy implementation" (new "Implementation (M3-4)" subsection: rules, storage, hash, cost),
  "Tick model" phase 3 note, "Orders and unit states" (new kinds), "Data format" (`trainedAt` resolved), docs/01 row
  for any Producer decision, coverage row.
- **Out:** techs / Age II / Forge upgrades (M3-5), `requires` resolution (M3-6), AI build orders (M5), rally and queue
  visuals / HUD (view M3-V3), rally on a building (repair / garrison), unit death releasing pop (combat M4: just make
  `UnitStore.Free` release the pop so M4 gets it for free).

**Acceptance criteria.**
1. `Train` on a Legion Barracks queues a Heavy Infantry: gold / wood drop by 54 / 20 at queue time; after exactly
   `round(14 x 20) = 280` ticks a Heavy Infantry owned by the player stands on a free cell 8-adjacent to the footprint
   (ring 1) on the building's level; the queue is empty. Five items queue; the sixth is dropped with totals unchanged.
2. `CancelTrain` refunds the full cost of the item (queued or in progress), the later items shift down, and a
   cancelled in-progress head loses its progress and its pop reservation. Cancel of index 5 / an empty slot is dropped.
3. Population: a Town Hall gives cap 10 (20 half-pop); 5 workers spawned by `SpawnUnit` make `HalfPop` 10; training
   pauses with the head item unstarted when `HalfPop + unit.HalfPop > HalfPopCap` and resumes the tick a House
   finishes (cap 18); `HalfPopCap` never exceeds `rules.popCap`; a Lancer (pop 2) counts 4 half-pop. `SpawnUnit`
   past the cap still spawns and counts.
4. A spawn with no free cell on the building's level waits (one unit per cell, no stacking; `PlacementTests`-style
   oracle); the ring walk is capped at the level's bounding box (a 1-cell plateau Keep with 400 blocked rings doesn't
   scan the map: < 0.2 ms per spawn, measured).
5. Rally: `SetRally` + a trained unit walks to the rally point (arrives within `ArrivalDistance`); a worker rallied
   onto a mine ends up `Gathering` that mine; `ClearRally` leaves spawned units Idle at the edge. Rally on an enemy's
   building / a site / no building is dropped.
6. Locked types: `Train` of the Sapper / Zealot (`requires: ["age_ii"]`) is dropped, totals unchanged; the type
   trained elsewhere (a Laborer at a Barracks) is dropped; wrong faction (player 1 training `malazan_laborer`) dropped.
7. Loader: `trainedAt` resolved for all 14 shipped units (`UnitDef.TrainedAtTypeId` = the id of the named building);
   an unknown id and a cross-faction id are each one `DataError` naming the field (`units[i].trainedAt`).
8. Determinism + hash: twins over 3,000 ticks of random Train / CancelTrain / SetRally / Build / Cancel commands on
   two players stay hash-identical every tick; every new field is in the hash (reflection audit extended: mutate
   each queue entry / progress / rally / pop word of a live building and see the hash change); `ReplayGoldenTests`
   green (golden regenerated only if the log explains which checkpoints moved; format stays 3, new kinds in `c` lines).
9. Conservation: over the fuzz, `gold + wood spent - refunds` equals the sum of the costs of units that exist or are
   queued / in progress (exact ints), and `HalfPop` equals the recount of live units + started reservations every tick.
10. Perf (Debug, alone): 500 marching units + 20 halls with full queues producing continuously + 50 gathering workers
    average under 1.3 ms a tick over 2,000 ticks; `ProductionSystem.Run` and the three command applies allocate 0 bytes
    (`AllocationTests` row).
11. Docs listed above updated; `dotnet build` 0 warnings; non-Perf suite green; CLI summary line shows pop.

**Design references.** docs/02 "Economy" (pop table: Town Hall +10, House +8, hard cap 100, half-steps; starting
state pop 5 / 10), "Buildings" (what each slot trains), "Controls" (right-click sets the rally point); docs/03
"Economy implementation" lines 1096-1097 (5 slots, pay at queue, refund on cancel, pop reserved when training
*starts*, full cap pauses), "Tick model" phase 3, "Implementation (M3-3)" push-out ring rule; `docs/factions/*.md`
Units tables (Train time, Trained at); CLAUDE.md rules 3-6.

**Tests required.** `ProductionTests` (criteria 1-6), `DataValidationTests` additions (7), `ProductionFuzzTests`
twins + conservation (8-9), hash reflection audit extension (8), `ProductionPerfTests` (10), `AllocationTests` row
(10), `ReplayGoldenTests` green.

**Constraints most at risk.** Allocation in the per-tick production loop (no lists, no LINQ); `Dictionary` for
"which types does b train" (use sorted `ImmutableArray`s built at load); wall clock / unseeded RNG (none); hard-coded
stats (5 slots is a rule constant in `EconomyConstants`, every number from data); player-facing text (none in C#);
docs in the same commit. Keep the public setup API additive (the view's base build must keep compiling).

**Debt to fold in only if a few lines:** none. BUG-0095 / 0096 (S3) wait for the next sim hardening session.

### After M3-4 (sim)
M3-5 Age II research + Forge upgrades (ships `techs.json` and the building `requires` field the data track asked for,
Requests 8; the Producer defaults the research-site question to docs/02: Age II at the Town Hall, upgrades at the Forge);
M3-6 `requires` resolution and validation (+ loader nits BUG-0008 / 0010).

## View track

### Current session plan: M3-V1 — HUD resource bar, worker orders and feedback in the window (feature, QA standard)

**Goal.** The first view half of M3: the owner can see the economy run and drive it. The default match gets a Town
Hall and workers per player, a resource bar shows gold and wood, a right-click on a tree or mine with workers selected
sends them gathering, and workers and buildings show what they're doing.

**Scope.**
1. **Default match** (`Match.Start`): per player, a finished Town Hall (the faction's `town_hall` slot building, via
   the dev `SpawnBuilding` command, which obeys the never-seal rule) on an open 4 x 4 spot nearest the start block, on
   the block's level, no tree / mine / cliff edge in the footprint's 8-ring; then `--workers N` (new launch option,
   default `rules.startingWorkers` = 5) worker-slot units of the player's faction (`World.FactionOf`) next to it. If no
   spot fits (seed-dependent), log a warning and skip the Town Hall (the match still runs). `--workers 0` and the bench
   scenes' numbers: keep the bench's 100 / 1,000 units as they are; the 5 workers are extra.
2. **Building views** (`BuildingViews`): one placeholder box per live building (footprint size x 3 m tall, player
   colour, slate for a site), created / destroyed by polling `Buildings.Alive` / `Generation` each frame (the props
   precedent), positioned from `Cell`; a progress bar over a site (`Work` / `WorkNeeded(type)`) and a hit-point bar
   when `Hp` < max. Buildings are selectable later (M3-V3); not now.
3. **Resource bar** (top-right of the `Hud` CanvasLayer, docs/02 "HUD layout"): "<gold displayName> N  <wood
   displayName> N" for player 0, names from `faction.json` `resources.gold / wood.displayName`, numbers from
   `World.Gold` / `Wood`; label text rebuilt only when a number changes (the M2-H2 overlay rule). **No pop slot** this
   session (M3-4 is building it in parallel; M3-V3 adds "Pop a / b").
4. **Right-click Gather**: with any `worker`-slot unit selected, a right-click whose ground point lies on a cell of a
   live resource node sends `Command.Gather(player, unit, point)` for each selected worker (Shift = queued, as Move)
   and a plain `Move` for every selected non-worker; the cursor-to-node resolution is a read-only `ViewApi` helper
   (`ResourcePicker.NodeAt(world, cell)` → slot or -1, through the store's cell lookup if it has one, else a bounded
   footprint scan; hash-twin proof). Minimap right-click stays a Move. The Command sound plays as for a Move.
5. **Worker feedback**: a small cargo marker above a unit while `Cargo > 0` (gold-coloured or wood-brown by
   `CargoKind`); a state tint or ring colour for `Gathering` / `Returning` / `Building` (the `UnitState` read exists);
   the F12 overlay's second line adds "workers gathering N, returning N, building N".
6. Headless test scenes (`game/tests`): a Match boots with the Town Hall + workers on seeds 1 / 6 / 31 (both players,
   Town Hall present, workers alive, gold / wood labels present and equal to `World.Gold[0]`); right-click on a mine
   makes the selected workers `Gathering` within 60 ticks and gold rises within 1,200 ticks; non-workers in the same
   selection get a Move; a site's progress bar appears (spawn a site through a `Build` from a test worker) and the box
   vanishes on `Cancel`; a 300-frame idle run allocates nothing in the resource bar / building views. Screenshots
   (`--screenshot`) of the Town Hall with workers hauling, and the resource bar, looked at before claiming.
- **Out:** command card, build menu, build ghost / `CanPlace` (M3-V2); production UI, rally marker, pop (M3-V3, after
  M3-4 is on `main`); fog; building selection; real models.

**Acceptance criteria.**
1. `& $env:GODOT --path game` shows per player a Town Hall box and 5 workers beside it on seeds 1 / 6 / 31 (test scene
   + screenshot); `--workers 0` shows the Town Hall only; a seed with no spot logs one warning and runs.
2. The resource bar reads "Gold 200  Wood 200" (data names) at start and updates within one frame of the sim total
   changing (test: a dev deposit, or a worker's round trip; label equals `World.Gold[0]`).
3. Right-click on a mine / tree with 5 workers selected: all 5 reach `Gathering` within 60 ticks (test scene), gold /
   wood rises within 1,200 ticks; the same click with 5 workers + 5 soldiers selected: workers gather, soldiers Move
   (Shift queues both). A right-click on plain ground stays a Move for everyone.
4. Cargo markers appear while carrying and disappear at deposit; state tints match `UnitState` (test reads the view
   node's state each frame against the sim).
5. A construction site shows a box + progress bar growing with `Work`; the box disappears within one frame of the
   site's `Alive` going false (Cancel). Finished buildings show a hit-point bar only when damaged.
6. `ViewApi` additions are read-only: hash-twin test over 400 ticks calling every new read each tick; no sim file
   outside `ViewApi/` in the diff.
7. 300 idle frames: 0 bytes allocated by the resource bar, building views and cargo markers (test row); the bench's
   avg frame at 100 units per player stays under 1.5 ms with the Town Halls and workers present (`--bench 10` run).
8. `tools/qa/smoke.ps1` PASS; every test scene PASS; non-Perf suite green; docs/03 "Rendering and presentation"
   gets an "Implementation (M3-V1)" subsection; docs/01 row for any Producer decision.

**Design references.** docs/02 "Controls and camera" (right-click context command: gather; HUD layout: resources
top-right), "Economy" (starting state: Town Hall, 5 workers, 200 / 200); docs/03 "Implementation (M3-2)" (gather
reads), "(M3-3)" (`Buildings` reads, `CanPlace` thread rule), "Rendering and presentation" M2 subsections (view
conventions, `--no-hud`, allocation rules); CLAUDE.md rules 1, 7, 8.

**Tests required.** `game/tests` scenes per criterion 1-5 and 7; xUnit `ViewApi` tests for `ResourcePicker` (every
cell of every node resolves; non-node cells -1; fuzz 50 maps) and the hash twin (6); smoke PASS.

**Constraints most at risk.** Player-facing text from data only (resource names from `faction.json`; no "Gold"
literal in C#); views hold no gameplay state; `CanPlace` not called (M3-V2); no per-frame allocation (label rebuilt
on change; marker nodes pooled); `ViewApi` read-only; the M3-4 sim branch merges first, so touch no file outside
`game/**` and `sim/Rts.Sim/ViewApi/**` plus the shared docs (append-only).

**Debt to fold in only if a few lines:** none. BUG-0104 / 0105 wait for the next view hardening session.

### After M3-V1 (view)
M3-V2 command card (Tab subgroups, grid hotkeys per docs/02 "Controls"), worker build menu (B), build ghost +
placement (`World.CanPlace` once per frame between ticks, one `Command.Build` per selected worker), Cancel / Repair
buttons; M3-V3 production UI (queue, progress, rally marker, pop in the resource bar) once M3-4 is on `main`; then the
M3 "Playable: build a full Malazan base and reach Age II".

## Data track

### Current session plan: STOP (cheap)
No inbox tweaks to the D1 / D2 review; no schema the track needs is on `main` (`techs.json` + building `requires` come
with M3-5 / M3-6; `abilities.json` / `statuses.json` and tower fields with M4; `ai.json` with M5). BUG-0111 (S4,
test-only) rides with the next data task that touches `Content/*ContentTests`. Counter stays 2 / 4. If the owner
answers the D1 / D2 review in the inbox, that tweak is the next data task (QA light; golden `data-hash` regen).

## Watch-outs (all tracks)

- Two feature tasks build and test at once: Perf rows can fail from CPU contention; a failure counts only alone.
  `TightBlob2500` has 0.02 ms of headroom alone (4.48 vs 4.5 ms): if it fails alone, that is a real regression.
- The view's default match now spawns Town Halls through `SpawnBuilding`; the sim's M3-4 must keep that dev command's
  behaviour (finished building, never-seal) unchanged.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" (suggested text under For your review).
- Builders: absolute paths only; never touch the owner's main checkout; worktrees on short paths.
