# Handoff: brief for the current / next session

Confirmed by the Producer at the PLAN of session **2026-10-07-1131** (third full session of 2026-10-07, cap 8; base
`28145d7`, main green: build 0 warnings, smoke and non-Perf suite rerun at plan time). This session: feature on **sim**
(M3-5, QA full) and **view** (M3-V2, QA standard); **data STOPs cheaply** (inbox empty, no schema to fill). Counters
after this session: sim 2 / 4, view 2 / 4, data 2 / 4. The briefs below are the 0925 handoff's, with the M3-5
refinements marked **(1131)**. **Bug numbers: sim from BUG-0098 (0098-0100 free, then 0112-0121), view from BUG-0108
(0108-0110 free, then 0122-0131), data from BUG-0132.**

## Where we are

- `main` after this session's merge: M0, M1, M2 Done; M3 4 / 8 (resources, gather loop, placement + construction +
  repair sim half, production + rally + pop sim half; HUD in progress: resource bar, right-click Gather, building and
  worker feedback, Town Hall + workers in the default match). 22 open bugs (S3 13, S4 9), no S1 / S2.
- Producer checks at ACCEPT: both branches build 0 warnings; non-Perf sim 2865 / 9 / 0, view 2812 / 9 / 0; view
  smoke PASS; sim Perf / golden / allocation rows green alone.
- Shared-file rule unchanged: docs/03 (one subsection per track), docs/01 change log (append one row), coverage.md
  (append), `studio/bugs/README.md` (append rows). Keep every side at merge, sim then view.
- Golden: only the sim track regenerates it. M3-5 adds hashed state (tech flags, research items); add the words only
  when non-default so the checkpoints stay byte-identical; the log says which moved and why.
- **Process rule for every builder (new):** never kill processes you did not start (`taskkill /F /IM testhost.exe`
  killed or risked the other track's test run this session). A hung test run is reported with its filter; the
  Producer handles it.
- The view's M3-V2 builds against the merged `main` (M3-4 is there), but **must not touch production reads** (queue,
  pop, rally): those are M3-V3, so the sim's M3-5 queue-item change (units or techs) lands first.

## Sim track

### Current session plan: M3-5 — Age II research and Forge upgrades (feature, QA full)

**Goal.** The next unmet M3 criterion: "Age II research and unlocks; Forge upgrades." Techs get a data schema, a
building researches them through the M3-4 production queue (one timer per building, AoE style), each player keeps a
tech state the view and M4 combat can read, and the upgrade bonuses are a read-only query combat will apply.
`requires` gating (who may train / place / research what) is M3-6; in M3-5 only the loader checks that `requires`
ids exist.

**Scope.**
- **Schema** (Producer default, record it in docs/03 "Data format" + the file tree, docs/01 row; owner may revisit):
  `game/data/common/techs.json` holds the techs shared by every faction: `age_ii` and the Forge upgrades
  `melee_weapons_1 / _2`, `ranged_weapons_1 / _2`, `armor_1 / _2`; `game/data/factions/<id>/techs.json` holds that
  faction's upgrade (`moranth_supply`, `dryjhnas_prophecy`; ids from the faction pages' "Faction upgrade" lines).
  Entry: `{ id, displayName, description, researchedAt: <building slot id, e.g. "town_hall" / "forge">, cost {gold,
  wood}, researchTime (s → ticks), requires: [ids], effects: [ { stat, amount, appliesTo { attackType?, tags?,
  units?, siege?: bool } } ] }`. `stat` is one of `attack`, `armor`, `range`, `hp`, `abilityCooldown` (strings
  validated against a fixed list; amounts are ints for attack / armor / hp, floats in meters / seconds for range /
  cooldown). **(1131)** `hp` is needed for Dryjhna's Prophecy (Zealots +20 HP, `docs/factions/whirlwind.md` line 31);
  an `abilityCooldown` effect's `appliesTo.units` names the unit whose ability it shortens (Sapper for Cusser,
  Priest of the Whirlwind for Sandstorm); M4's abilities schema may add an `abilities` filter later. A
  tech id is unique across all techs files; `requires` entries must name an existing tech or building id (the
  Age II "any two of" rule is **M3-6**; write Age II's `requires` as `[]` now and the rule in the docs). **(1131)**
  Ship the `requires` values the docs give: level-2 upgrades `["<level_1 id>", "age_ii"]`, faction upgrades
  `["age_ii"]`; and extend the same id check to `units[i].requires` (the shipped Sapper / Zealot already carry
  `["age_ii"]`, which resolves once the tech exists; `CanTrain`'s `LockedByRequirement` stays "has any requires"
  until M3-6). Common
  techs resolve `researchedAt` per faction to that faction's building of the slot; a faction without the slot is a
  `DataError`. Both files are **required** (a missing file is one error), like the others. Numbers from docs/02
  "Tech" (Age II 400 G / 200 W, 60 s; the upgrade table) and the faction pages (faction upgrades 200 G / 150 W,
  45 s). Ship the entries with correct ids, numbers and effects and placeholder `displayName` / `description`
  text taken from docs/02 / the pages; the data track polishes the text (D3) and pins it.
- **Loader**: `TechDef` (ids → ints in ordinal order, `ResearchedAtSlot`, `ResearchTicks`, cost, `Effects` as a
  flat struct array, `Requires` strings, `Faction` -1 for common), `GameData.Techs`, `TechsResearchableAt(buildingType)`
  sorted `ImmutableArray<int>` per building type (like `UnitsTrainedAt`), `ContentHash` covers every field;
  `DataValidationTests` for each rule (unknown stat, unknown slot, duplicate id, bad `requires` id, missing file).
- **Commands** (not unit orders, not queueable; addressed like `Train`): `Research(player, building, techId)`,
  `CancelResearch(player, building, slotIndex)` (or let `CancelTrain` cancel any queue item: pick one, document it).
  Dropped for: no own finished building there, unknown tech, a tech not researched at that building's slot,
  already researched by this player, already queued by this player anywhere, full queue, can't afford.
  `World.CanResearch(player, slot, techId, out ResearchError)` is the one rule (view's button state).
- **Queue items**: a `BuildingStore` queue entry is a unit **or** a tech. Producer default: a parallel
  `QueueIsTech[Capacity * 5]` flag array (hashed through the production bits like the entries), `QueueTypeAt` keeps
  returning the type id and a new `QueueIsTechAt(slot, i)` says which; `Progress` / `TrainTicks` generalise
  (`ItemTicks(slot, i)`). Research reserves no pop; on completion the player's tech flag is set (`World.HasTech(player,
  tech)`; `World.Age(player)` = 2 once `age_ii` is researched) and nothing spawns. Cancel refunds in full. A building
  destroyed refunds its queued techs. A tech may be queued once per player at a time (the "already queued" check).
- **Bonus query** (read-only, allocation-free, for M4): `World.TechBonus(player, unitType, TechStat stat)` → the sum
  of the amounts of the player's researched techs whose `appliesTo` matches the unit (attack type from the unit's
  attack, tags, unit id, siege by tag / slot). Combat does not apply it yet (M4): say so in docs/03.
- **Building `requires` field**: schema + validation only (`buildings.json` entries get `requires: []` by default;
  the data track fills values in D3 against the faction pages; the sim ships the field and the loader check that
  every id names a tech or building). Gating in `CanPlace` is M3-6.
- **Hash**: tech flags per player (hash a word only when any flag is set), queue tech flags, research progress
  (already `Progress`). Golden checkpoints should not move; `data-hash` will.
- **CLI**: `--research` is out; the summary line may add `age N` after pop (cheap, optional).
- **Docs**: docs/03 "Economy implementation" new "Implementation (M3-5)" (schema, commands, queue items, tech state,
  bonus query, hash, cost), "Data format" (`techs.json` shape + file tree: common techs + faction techs; `requires`
  on buildings), "Tick model" phase 3 note (research timers), docs/01 row (Producer decisions: common / faction split,
  one timer per building, no pop for research, a tech queued once at a time), coverage row.
- **Out:** `requires` gating (M3-6), combat application of bonuses (M4), ability cooldown effects beyond the data
  (M4), the research button / card (view M3-V3), AI research (M5).

**Acceptance criteria.**
1. Shipped data loads with 0 errors; `GameData.Techs` holds 9 techs (7 common + 2 faction); Age II costs 400 / 200
   and takes 1,200 ticks; the Forge upgrade table matches docs/02 (`DataValidationTests` row per number); each
   faction upgrade matches its page (cost, time, effects: Moranth Supply `abilityCooldown -15` s on the Sapper and
   `range +4` on the Catapult; Dryjhna's Prophecy `hp +20` on the Zealot and `abilityCooldown -15` s on the Priest
   of the Whirlwind, per `docs/factions/whirlwind.md`).
2. Loader errors, one each at the field: unknown `stat`, unknown `researchedAt` slot, a slot the faction lacks,
   duplicate tech id across files, `requires` naming nothing (on a tech, a building **and a unit**), a missing techs
   file (common or faction).
3. `Research(age_ii)` at the Town Hall: gold / wood drop 400 / 200 at queue time; after exactly 1,200 ticks
   `HasTech(0, age_ii)` and `Age(0) == 2`; nothing spawned; pop unchanged throughout; a second `Research(age_ii)`
   while queued or after completion is dropped, totals unchanged.
4. A Forge (Armory) researches `melee_weapons_1` (100 / 50, 600 ticks); `TechBonus(0, heavy_infantry, attack)` is 0
   before and 1 after; `TechBonus(0, crossbowman, attack)` stays 0; `armor_1` gives +1 armor to every non-siege unit
   and 0 to the Catapult; the faction upgrade's effects resolve to the named units only.
5. Mixed queue: a Barracks can't research (not its slot) and a Forge can't train (nothing trained there, M3-4 rule);
   a Town Hall queue `[laborer, age_ii, laborer]` runs in order, the Laborer spawns, Age II completes, the Laborer
   spawns; cancelling index 1 refunds 400 / 200 and shifts; a Town Hall destroyed with Age II at 50 % refunds in full
   and `HasTech` stays false.
6. Research by the wrong player, at a site, at an enemy building, with an unknown id, or with a full queue is
   dropped; can't afford dropped.
7. Determinism + hash: twins over 3,000 ticks of random Train / Research / Cancel / Build / destroy commands on two
   players hash-identical every tick; reflection audit: mutate each tech flag, queue tech flag and research progress
   of a live building / player and see the hash change; `ReplayGoldenTests` green (checkpoints byte-identical;
   `data-hash` regenerated with the reason in the log).
8. Conservation: over the fuzz, totals + refunds balance against units + queued items + researched techs exactly.
9. Perf (Debug, alone): the M3-4 criterion-10 scene plus 10 Forges researching continuously stays under 1.3 ms a
   tick; `TechBonus` for 500 units x 2 stats under 0.05 ms; `ProductionSystem.Run` and the new applies allocate 0
   bytes (`AllocationTests` row).
10. Docs listed updated; `dotnet build` 0 warnings; non-Perf suite green; `buildings.json` entries accept `requires`
    (empty lists shipped; one invalid id = one error).

**Design references.** docs/02 "Tech" (Ages, Forge upgrades table), "Buildings" (Town Hall researches Age II, Forge
upgrades), `docs/factions/malazan.md` / `whirlwind.md` "Faction upgrade" lines; docs/03 "Tick model" phase 3,
"Economy implementation" + "Implementation (M3-4)" (queue storage, hash bits, cost), "Data format"; CLAUDE.md
rules 3-6, 8.

**Tests required.** `TechLoaderTests` / `DataValidationTests` additions (1-2, 10), `ResearchTests` (3-6),
`ResearchFuzzTests` twins + conservation (7-8), `StateHashTests` audit (7), `ResearchPerfTests` + `AllocationTests`
row (9), `ReplayGoldenTests` green.

**Constraints most at risk.** Per-tick allocation (effects matching must be a flat array walk, no LINQ, no
dictionaries: tags are already string arrays on `UnitDef`, so resolve `appliesTo` to int sets / bitmasks at load);
hard-coded stats (every number from data; "a tech once per player" is a rule); hashing derived state (tech flags are
state: hash them); player-facing text only in data; docs in the same commit; keep the M3-4 public reads working (the
view's M3-V3 will build on them).

**Debt to fold in only if a few lines:** none. BUG-0097 / 0095 / 0096 wait for the sim hardening session (after
M3-6 and one more feature session, or earlier if M3 completes first).

### After M3-5 (sim)
M3-6 `requires` resolution and gating: units (`CanTrain` LockedByRequirement → `HasTech` / own finished building of
the id), buildings (`CanPlace` new reason `Requires`, after `WrongFaction`; the data track's Requests 8a), techs
(level 2 needs level 1 + Age II; Age II needs any two of Infantry Hall / Ranged Hall / Shock Hall / Forge: a generic
`requiresAnyOf { count, of }` field), + loader nits BUG-0008 / 0010. Then the sim hardening session (BUG-0097 / 0095
plateau ids, BUG-0096 memo, door fuzz kinds 11+).

## View track

### Current session plan: M3-V2 — command card, worker build menus, build ghost and placement, Cancel / Repair (feature, QA standard)

**Goal.** The owner can build a base in the window: a command card bottom right with the docs' 5 x 3 grid hotkeys,
a worker's build menus (B basic, V advanced), a ghost that follows the cursor and turns green / red by the sim's own
`CanPlace`, one `Build` per selected worker on click, a selected site's Cancel, and right-click Repair on a damaged
own building. Minimal building selection (click a box) is in, so Cancel has something to act on; the production card
on that selection is M3-V3.

**Scope.**
1. **Command card** (`Hud/CommandCard`, bottom right, docs/02 "HUD layout"): a 5 x 3 grid of buttons keyed
   `Q W E R T / A S D F G / Z X C V B` (docs/02 "Grid hotkeys": production cards and build menus use the full grid;
   unit cards keep today's A / S / H keys and show them as buttons). Contents by the active Tab subgroup: non-worker
   units: Attack-move (A), Stop (S), Hold (H), Move (M: new, docs/02; the plain Move order ignoring enemies, today
   identical to a right-click); workers: those plus **Build basic (B)** and **Build advanced (V)** (Gather stays a
   right-click, no button); a selected site: **Cancel**; a selected finished building: nothing yet (M3-V3). Button
   labels for buildings are their `displayName`. UI command labels ("Stop", "Hold", "Cancel", "Build") are
   player-facing text and must come from data (CLAUDE.md rule 8). Producer default: add `game/data/common/ui.json`
   with `{ "commands": { "move": {displayName, hotkeyHint}, ... }, "placement": { "<reason>": text } }`, read by a
   view-side loader `game/scripts/UiText.cs` (fail-fast with a clear log line if a key is missing). The view track
   may add this one *view-only* JSON file under `game/data/common/` because this brief names it; the sim's
   `DataLoader` must not be touched.
2. **Build menus**: B lists the faction's Age I buildings (House, Camp, Infantry Hall, Ranged Hall, Shock Hall,
   Forge, in slot order), V the Age II ones (Caster Hall, Siege Works, Watch Tower), each on a grid key in order,
   label = `displayName`, tooltip = `description` + cost (data). Esc / right-click closes. Since M3-6 isn't in,
   every entry is enabled (the sim decides at placement); greying by `requires` comes with M3-V3 / M3-6.
3. **Build ghost**: picking an entry shows a translucent footprint box at the cursor's cell (anchor = the cell
   under the cursor minus half the footprint, so the box centres on the cursor), green when `World.CanPlace(player,
   type, anchor, out reason)` passes, red otherwise, with the reason shown as short data text (`ui.json`
   `placement.<reason>` entries: "Blocked", "Would wall ground off", "Can't afford" ...). **`CanPlace` once per frame
   at most, on the main thread between ticks** (docs/03 M3-3: it writes flow-field scratch). Read `SimRunner` first
   to see where ticks run; call `CanPlace` from the frame's `_Process` chain after the runner's tick step, never
   from another thread and never while a tick is running. Left
   click with a green ghost: one `Command.Build(player, worker, type, anchor)` per selected worker (the first places,
   the rest join: docs/03); Shift keeps the ghost up for another placement; the Command sound plays; the ghost
   closes otherwise. A red click does nothing (no sound). Esc / right-click cancels the ghost.
4. **Building selection** (minimal): a left click on a building box selects that building alone (clears units);
   a box-select never selects buildings; the selection ring / highlight is a flat outline on the footprint; Tab does
   nothing; the card shows Cancel for a site (and nothing for a finished building). `Command.Cancel(player, point)`
   on the key / button. The F12 label's selection count shows "building" when one is selected. Keep the API small:
   `SelectionController.SelectedBuilding` (slot or -1), cleared when the slot dies or is reused.
5. **Right-click Repair**: with workers selected, a right-click on an own **finished** building below full hp sends
   `Command.Repair(player, worker, point)` per worker (Shift queues); on an own site it sends `Build` with the site's
   type and anchor (join: docs/03 M3-3 says a Build at an own site's anchor joins it); on anything else a Move. Add
   the building case to `ContextOrder` next to the node case (a `ViewApi.BuildingPicker.SlotAt(buildings, cell)` read
   is fine; `Buildings.SlotAt` may already exist on the store: use it through a thin read-only wrapper if it is
   internal).
6. **Headless test scenes** (`game/tests`): card contents per selection kind; B / V menus list the right ids in
   order; the ghost's colour equals `CanPlace` on 200 random cursor points (seed 1) each frame; a green click
   enqueues N Builds (N selected workers) with the same anchor and type, a red click none; Shift keeps the ghost;
   a site click selects it and Cancel enqueues one `Cancel`; right-click Repair on a damaged building (via the
   store's `Damage` through reflection as QA did) enqueues N Repairs; 300 idle frames with the card open and a ghost
   up allocate nothing beyond the ghost's one-time nodes; `ui.json` keys all present. Screenshots: the card, the B
   menu, a green and a red ghost, a selected site with its Cancel, looked at before claiming.
- **Out:** production card / queue / rally / pop (M3-V3), selection panel (M3-V3), fog, building hp bars changes,
  real art, gating by `requires` (M3-6), rebinding UI.

**Acceptance criteria.**
1. `& $env:GODOT --path game`: with soldiers selected the card shows A / S / H / M buttons; with workers also B / V;
   pressing a button does what its key does (test: each button enqueues the same commands as the key).
2. B opens the basic menu with the six Age I buildings (data names, slot order, grid keys Q W E R T A), V the three
   Age II ones; Esc closes; entries show cost from data.
3. The ghost's colour matches `World.CanPlace` on every frame for 200 random cursor points, and `CanPlace` is called
   at most once per frame (counter in the test); the red reason text comes from `ui.json`.
4. A green click enqueues exactly one `Build` per selected live worker, same type and anchor; with Shift the ghost
   stays and a second click enqueues again; a red click enqueues nothing and plays no sound.
5. Clicking a site box selects it (units deselected), the card shows Cancel, pressing it enqueues one `Cancel` for
   the site's position; the site vanishes the next frame after the sim frees it; a dead / reused slot clears the
   selection.
6. Right-click on an own damaged finished building with 3 workers selected enqueues 3 `Repair`s (Shift queues); on
   an own site enqueues 3 `Build`s joining it; on an enemy building a Move.
7. `ViewApi` additions are read-only (hash twin over 400 ticks calling every new read each tick); no sim file
   outside `ViewApi/` in the diff; `game/data/common/ui.json` is the only data file touched.
8. 300 idle frames with the card open and a ghost up: 0 bytes from the card, ghost and building selection after
   warm-up; `--bench 10` avg stays under 1.5 ms.
9. `tools/qa/smoke.ps1` PASS; every scene PASS; non-Perf green; docs/03 "Rendering and presentation" gets an
   "Implementation (M3-V2)" subsection (card layout, `ui.json` shape, ghost rule, selection rule); docs/01 row for
   the `ui.json` decision and any other Producer default; the BUG-0106 file set to `wontfix` with the Producer's
   reason (criterion reworded, 2026-10-07-0925) and its README row updated.

**Design references.** docs/02 "Controls and camera" (table: right-click context command incl. build / repair, B / V
build menus, Esc; "Grid hotkeys"; "HUD layout"), "Buildings" (Age I vs Age II list), docs/03 "Implementation (M3-3)"
(`CanPlace` thread rule, reasons, join rule), "(M3-V1)" (view conventions, `ContextOrder`, allocation rules),
CLAUDE.md rules 1, 7, 8.

**Tests required.** `game/tests` scenes per criterion 1-6 and 8; xUnit `ViewApi` tests for any new read (building
picker, ghost anchor maths) + the hash twin (7); smoke PASS.

**Constraints most at risk.** Player-facing text in C# (every label through `ui.json` or `displayName`); `CanPlace`
called off the main thread or more than once per frame; per-frame allocation (ghost nodes pooled; button labels
set on change only); views holding gameplay state (the selected building is a view selection, not sim state); the
sim's `DataLoader` untouched (the view-only `ui.json` is read by `game/scripts` only).

**Debt to fold in only if a few lines:** BUG-0106 file status (criterion 9 above). BUG-0104 / 0105 / 0107 wait for
the view hardening session.

### After M3-V2 (view)
M3-V3 selection panel (portrait / stats / multi-select grid), production card on a selected finished building
(`UnitsTrainedAt`, `CanTrain` greying, `Train` / `CancelTrain`, queue + progress bar, Research button once M3-5 is
on `main` with `TechsResearchableAt` / `CanResearch`), rally marker + right-click SetRally on a selected building,
"Pop a / b" in the resource bar (`HalfPop` / `HalfPopCap`), then the M3 "Playable" session with the owner; then the
view hardening session (BUG-0104 / 0105 / 0107).

## Data track

### Current session plan: STOP (cheap)
Confirmed at the 1131 PLAN: no inbox tweaks to the D1 / D2 review; no schema the track needs is on `main`
(`techs.json` + building `requires` come with M3-5, merged at the end of this session). Counter stays 2 / 4. If the owner answers the D1 / D2 review
in the inbox, that tweak is the next data task (QA light; golden `data-hash` regen). **After M3-5 lands: D3** techs
content (names, descriptions, numbers pinned to docs/02 "Tech" and the faction pages' upgrade lines; building
`requires` values per the faction pages' Requires column; BUG-0111 + the BUG-0090 text recheck).

## Watch-outs (all tracks)

- Two feature tasks build and test at once: Perf rows can fail from CPU contention; a failure counts only alone.
  `TightBlob2500` has 0.02 ms of headroom alone (4.48 vs 4.5 ms): if it fails alone, that is a real regression.
- Never kill processes you did not start (see "Where we are").
- The view's default match spawns Town Halls through `SpawnBuilding` and workers through `SpawnUnit`; the sim's
  M3-5 must keep both dev commands' behaviour (finished building, never-seal; spawn past the cap but count).
- The sim's M3-5 changes the queue entry shape (unit or tech); the view's M3-V2 must not read queue state (M3-V3).
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" (suggested text under For your review, M1 entry).
- Builders: absolute paths only; never touch the owner's main checkout; worktrees on short paths.
