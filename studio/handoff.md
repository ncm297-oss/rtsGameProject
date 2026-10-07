# Handoff: brief for the current / next session

Written by the Producer at the ACCEPT of session **2026-10-07-1131** (third full session of 2026-10-07, cap 8). Sim M3-5
and view M3-V2 both ACCEPTed with 0 fix rounds; data STOPped cheaply. Next session: feature on all three tracks. Counters
after 1131: sim 2 / 4, view 2 / 4, data 2 / 4 (the data counter moves when D3 runs). **Bug numbers for the next
session: sim from BUG-0100 (0100, then 0112-0121), view from BUG-0123 (0123-0131), data from BUG-0132.**

## Where we are

- `main` after this session's merge: M0, M1, M2 Done; **M3 5 / 8** (resources, gather loop, placement + construction +
  repair sim half, production + rally + pop sim half, **Age II research + Forge upgrades**). HUD in progress: resource
  bar, right-click Gather / Repair / join, command card with grid hotkeys, build menus, build ghost, site Cancel are in;
  selection panel + production card are M3-V3. Data: units + buildings pinned; techs content (D3) now unblocked.
  27 open bugs (S3 16, S4 11), no S1 / S2.
- Producer checks at ACCEPT (see the session log): both branches build 0 warnings; non-Perf suites green; view smoke
  PASS; sim Perf / golden / allocation rows green alone.
- Shared-file rule unchanged: docs/03 (one subsection per track), docs/01 change log (append one row), coverage.md
  (append), `studio/bugs/README.md` (append rows). Keep every side at merge, sim then view then data.
- Golden: only the sim track regenerates it, and the data track when its content changes `data-hash` (checkpoints
  must stay byte-identical; the log says which moved and why). **Two tracks must never both regenerate the golden in
  one session**: next session the sim's M3-6 adds a field to `common/techs.json` (data-hash moves) and the data's D3
  changes faction files (data-hash moves) → the sim regenerates at its commit, the data track regenerates **after its
  merge on top of the sim's** (the conductor merges sim first; the data builder rebases / merges `main` and regenerates
  then). The data brief says so.
- **Process rule for every builder:** never kill processes you did not start (`taskkill /F /IM testhost.exe` or
  stopping `python` / `dotnet` processes by name risks the other tracks). Stop only a process you started and can name
  by PID. A hung test run is reported with its filter.
- **game/data file ownership next session:** sim M3-6 owns `game/data/common/techs.json` (Age II `requiresAnyOf`) and
  `game/data/common/rules.json` if it needs anything; data D3 owns `game/data/factions/*/techs.json`,
  `game/data/factions/*/buildings.json` (the `requires` values) and `docs/factions/**`. Neither touches the other's.
  The common techs' text polish (names / descriptions in `common/techs.json`) is **D4**, after M3-6 lands.

## Sim track

### Current session plan: M3-6 — `requires` resolution and gating (feature, QA full)

**Goal.** The M3 criterion "Age II research and unlocks" is half done: techs exist and research works, but nothing is
gated. M3-6 makes `requires` mean something: a unit can't be trained, a building can't be placed and a tech can't be
researched until its requirements are met, with the Age II "any two of" rule, so the owner's "build a full base and
reach Age II" playable has real progression. Also the loader nits that belong here (BUG-0098, BUG-0099 items 1 + 3,
BUG-0008, BUG-0010).

**Scope.**
- **Resolution at load**: every `requires` entry (units, buildings, techs) resolves to an int requirement: a tech id or
  a building *type* id (a building requirement is met by an own **finished** building of that type). Keep the strings
  on the defs for tools; add `RequiresTechs` / `RequiresBuildings` sorted `ImmutableArray<int>` per def (Producer
  default; the dev may pick one flat `Requirement` struct array instead, documented). `ContentHash` unchanged in
  meaning (it already covers the strings).
- **`requiresAnyOf`** (new optional tech field, Producer default per the 0925 handoff: `{ "count": 2, "of": [slot ids
  or building ids] }`): Age II needs any two of Infantry Hall / Ranged Hall / Shock Hall / Forge (docs/02 "Ages"). Write
  it as building **slot** ids so the common tech works for every faction (resolved per faction to that faction's
  building types, like `researchedAt`). Validation: `count` 1..`of.Length`, every id a slot (or a building id of the
  tech's own faction), one error at the field. Ship it on `age_ii` in `common/techs.json`. Other techs: absent.
- **Gating** (the one rule per kind, read-only, allocation-free; the view's greying reads them):
  - `World.CanTrain`: `LockedByRequirement` becomes real: every required tech `HasTech`, every required building type
    has an own finished instance (scan `BuildingStore`, bounded; a per-player per-type finished count kept by the store
    is fine if cheaper: it is derived state, not hashed).
  - `World.CanPlace`: new reason `Requires`, checked **after `WrongFaction` and before `OffMap`** (cheap, before the
    flood; the view's `ui.json` needs the text `placement.requires`: tell the view track through Requests, the view's
    `UiText` fails fast until it has it, by design; the view's M3-V3 brief adds the key).
  - `World.CanResearch`: new reason `Requires` (after `NotResearchedHere`, before `AlreadyResearched`): required techs
    researched, required buildings finished, and `requiresAnyOf` satisfied (count of distinct listed slots with an own
    finished building ≥ `count`).
  - Requirements are checked **when the command applies** (queue time), not again at completion: a Barracks destroyed
    mid-training doesn't cancel the item (AoE rule; document it). Age II completing doesn't retro-unlock queued items
    (nothing was queued that needed it).
- **Loader nits folded in** (the code is open anyway): BUG-0098 (an empty `units` / `tags` list is one error, not
  "every unit"), BUG-0099 item 1 (a `requires` cycle, including self, is one error at the entry that closes it) and
  item 3 (a tech id equal to a building id is one error), BUG-0008 (duplicate JSON keys in any data file: one error;
  `System.Text.Json` reads last-wins, so detect with a `Utf8JsonReader` pre-pass per file, bounded by the file size),
  BUG-0010 (a faction with no units, or a missing / doubled unit template slot, is one error each; the ten building
  slots likewise if not already pinned). Un-skip the QA rows those bugs skip.
- **Data the sim ships** in `common/techs.json`: `age_ii.requiresAnyOf` only. Faction `buildings.json` `requires`
  values are the **data track's D3** (same session, other files): the sim's gating tests use test fixtures (a loaded
  data copy with `requires` edited in memory / a temp folder), as `TechLoaderTests` already does.
- **CLI**: nothing new (the `age N` print exists).
- **Docs**: docs/03 "Implementation (M3-6)" (resolution, the three gates and their reason order, `requiresAnyOf`,
  queue-time rule, nits), "Data format" (`requiresAnyOf` shape; `requires` now resolved), docs/01 row (Producer
  decisions: queue-time check, building requirement = finished own building of the type, any-of by slot), coverage row,
  update the M3-5 "Not yet" line.
- **Out:** the view's greying / reason text (M3-V3), AI (M5), the common techs' text (D4), combat use of bonuses (M4).

**Acceptance criteria.**
1. Shipped data loads with 0 errors; `age_ii.requiresAnyOf` = any 2 of infantry_hall / ranged_hall / shock_hall /
   forge; the Sapper and the Zealot resolve to `RequiresTechs = [age_ii]`; level-2 upgrades to `[level_1, age_ii]`;
   faction upgrades to `[age_ii]`.
2. Loader errors, one each: `requiresAnyOf.count` 0 / > `of.Length` / missing; an `of` entry that is neither a slot nor
   an own-faction building id; a `requires` cycle (2-cycle and self); a tech id equal to a building id; an empty
   `units` / `tags` filter (BUG-0098); duplicate JSON keys in each of the five file kinds (BUG-0008); a faction with
   no units / a missing / a doubled unit slot (BUG-0010).
3. `CanTrain(sapper)` → `LockedByRequirement` before Age II, `None` after `age_ii` completes (same tick, phase 3 → the
   next command applies in phase 1 of tick + 1); a unit requiring a building type is locked until an own instance is
   **finished** (a site doesn't count; an enemy's doesn't count), and unlocked again by a second instance if the first
   is destroyed; the queued item survives the building's destruction.
4. `CanPlace` with a fixture building requiring Age II → `Requires` before Age II (and `Requires` outranks `OffMap` /
   `Blocked` / `CannotAfford` in the reason order; `WrongFaction` outranks it); `None` after; `Build` is dropped while
   locked with totals unchanged.
5. `CanResearch(age_ii)` → `Requires` with 0 or 1 of the four halls finished (two sites don't count; two of the same
   slot count once); `None` with any 2 distinct; `melee_weapons_2` → `Requires` until both `melee_weapons_1` and
   `age_ii` are researched; the faction upgrade until `age_ii`.
6. Determinism + hash: twins over 3,000 ticks of random Train / Research / Build / Cancel / destroy with requirements
   that come and go hash-identical every tick; the reflection audit shows no new hashed-but-missing state (the finished
   counts, if kept, are derived and recounted by the fuzz every tick); `ReplayGoldenTests` green (checkpoints
   byte-identical, `data-hash` regenerated for `requiresAnyOf` with the reason in the log).
7. Conservation over the fuzz exact (locked commands never move money).
8. Perf (Debug, alone): the M3-5 criterion-9 scene with every command passing through the three gates stays under
   1.3 ms a tick; 5,000 locked Train / Build / Research commands in one tick under 3 ms; `AllocationTests` rows 0 bytes
   for the gates.
9. Docs listed updated; `dotnet build` 0 warnings; non-Perf suite green; the QA rows skipped for BUG-0008 / 0010 / 0098
   / 0099 un-skipped and passing.

**Design references.** docs/02 "Ages" (Age II needs any two of the four halls; what Age II unlocks), "Buildings" table
(Requires column), "Tech" (level 2 needs level 1 + Age II); the faction pages' Requires columns and "Faction upgrade"
lines; docs/03 "Implementation (M3-4)" (`CanTrain` reasons), "(M3-3)" (`CanPlace` reason order), "(M3-5)" (`CanResearch`
reason order, `requires` check); CLAUDE.md rules 3-6.

**Tests required.** `RequirementLoaderTests` (1-2), `RequirementGatingTests` (3-5), `RequirementFuzzTests` twins +
conservation (6-7), `StateHashTests` audit (6), `RequirementPerfTests` + `AllocationTests` rows (8), `ReplayGoldenTests`
green; QA rows un-skipped (9).

**Constraints most at risk.** Per-tick allocation (resolve to ints at load; bounded store scans or a derived count);
hashing derived state (a finished-count cache is derived; don't hash it, recount in the fuzz); hard-coded rules
("any two of" comes from data, not C#); the reason order is a documented contract the view reads; keep the M3-4 / M3-5
public reads working (the view's M3-V3 builds on them this same session); the sim ships only `age_ii.requiresAnyOf` in
`game/data/`, nothing in `factions/**` (the data track owns those files this session).

**Debt to fold in only if a few lines:** none beyond the listed loader nits. BUG-0097 / 0095 / 0096 wait for the sim
hardening session.

### After M3-6 (sim)
M3's sim side is complete after M3-6. Then the **sim hardening session** (counter 3 / 4 after M3-6, so one more feature
session is allowed first, but M3 is complete for the sim: the end-of-milestone hardening comes first): BUG-0097 / 0095
(plateau-bounded free-cell search + a per-plateau "no free cell this tick" memo), BUG-0096 (SealsGround refusal memo),
BUG-0094 (test grove seal check), door fuzz kinds 11-15, M3-5 / M3-6 doc polish. After that the sim starts M4 combat
(attack / damage formula first) while the view and data tracks finish M3.

## View track

### Current session plan: M3-V3 — selection panel, production card, rally, pop (feature, QA standard)

**Goal.** Finish M3's HUD criterion: a selection panel (portrait / stats / multi-select grid), a production card on a
selected finished building (train buttons greyed by `CanTrain`, research buttons by `CanResearch`, queue with
progress, cancel), a rally marker with right-click SetRally, and "Pop a / b" in the resource bar. With this the owner
can play M3's "build a full Malazan base and reach Age II" in the window.

**Scope.**
1. **Selection panel** (`Hud/SelectionPanel`, bottom centre, docs/02 "HUD layout"): one unit: portrait placeholder (a
   coloured square with the unit's `displayName`), hp (current / max from the store), attack / armor / range / speed
   from `UnitDef` (+ `TechBonus` added and shown as "+N" in a second colour; the bonus isn't applied in combat yet,
   M4: say so in docs/03), state in plain words from `ui.json` (`states.idle` ... : new keys); several units: a grid
   of up to 24 portraits (type-coloured squares with a count overflow "+N"), the active Tab subgroup highlighted,
   clicking a portrait selects that unit alone; a building: name, hp, and (finished) the production card.
2. **Production card** (the command card's cells while a finished own building is selected): buttons for
   `GameData.UnitsTrainedAt(type)` then `TechsResearchableAt(type)` in order, label `displayName`, hint the grid key,
   cost label, tooltip `description` + cost + "needs ..." from the def's `Requires` strings mapped to display names
   (data). Greyed with the reason text when `CanTrain` / `CanResearch` refuses (reasons in `ui.json` `train.<reason>`
   / `research.<reason>` keys, snake_case of the enums like `placement`; **include the M3-6 `Requires` reason keys for
   `placement`, `train` and `research` now**, so the view compiles and loads against either `main` state: `UiText`
   must accept a key with no matching enum member yet without error, and still fail fast on a missing key for an
   existing member). Press = `Command.Train` / `Command.Research` at the building's footprint centre. A **queue strip**
   above the card: up to 5 items (type-coloured squares with a letter / the tech's name), the head with a progress bar
   from `Progress / ItemTicks(slot, 0)`; clicking an item sends `CancelTrain(player, point, index)`; the Command sound
   on train / research / cancel; the Age II item's completion flashes the resource bar's "Age II" (data text).
3. **Rally**: with an own finished building selected, a right-click on the ground sends `Command.SetRally(player,
   buildingCell, target)`; on a node the same (the sim gathers from a node rally); the marker is a small flag
   (placeholder cone) at `RallyPosition` while `HasRally`, with a thin line from the building's edge; a right-click on
   the building itself sends `ClearRally`. Rally on the minimap too (the minimap right-click while a building is
   selected = SetRally).
4. **Pop in the resource bar**: "Gold N  Wood N  Pop a / b" from `HalfPop` / `HalfPopCap` (`n / 2`, ".5" for odd),
   "Pop" from `ui.json`; red when `HalfPop >= HalfPopCap`.
5. **Fold in (same code, small fixes):** BUG-0110 (a non-object `ui.json` root is one error: one line), BUG-0109 (the
   placement click recomputes the anchor from the click position; if it differs from the drawn anchor, ask `CanPlace`
   for it as that frame's one call, else ignore the click), BUG-0108 (the right-click context point: ray-pick the
   building box first (`BuildingPicker.PickRay`, any owner), use its footprint centre; fall back to the ground pick; do
   the same for resource nodes if `PropLayout` exposes box heights cheaply, else note it). Turn the `Known(...)` QA rows
   into plain `Check`s.
6. **Headless test scenes**: panel contents for one unit / mixed / building; production card buttons = `UnitsTrainedAt`
   + `TechsResearchableAt`, greying = `CanTrain` / `CanResearch` every frame on 200 random states; press enqueues one
   `Train` / `Research`; queue strip equals the store every frame while 5 items run; cancel click index; rally
   right-click = `SetRally`, on the building = `ClearRally`, marker position; pop text vs `HalfPop`; BUG-0108 / 0109 /
   0110 rows pass strict; 300 idle frames 0 bytes with the panel + card + queue up; screenshots (panel one unit, panel
   mixed, production card with a queue, a rally flag), looked at.
- **Out:** fog, real portraits, hotkey rebinding, the M3 "Playable" session itself (next, with the owner), `Requires`
  greying beyond the text keys (it lights up by itself once M3-6 is on `main`: nothing to do in the view).

**Acceptance criteria.**
1. One selected unit shows name / hp / attack / armor / range / speed from data (+ bonus column after a tech, test via
   `World.Techs` reflection or a researched tech through the queue); mixed selection shows the grid with the active
   subgroup highlighted; a portrait click selects that unit alone.
2. A selected Barracks shows its trainable units in `UnitsTrainedAt` order, a Town Hall its units then Age II, an
   Armory the Forge upgrades then Moranth Supply; greying and reason text equal `CanTrain` / `CanResearch` on 200 random
   states (money, pop, queue full, already researched, queued elsewhere); a press enqueues exactly one command at the
   footprint centre; a greyed press enqueues nothing and plays no sound.
3. The queue strip equals `QueueCount` / `QueueTypeAt` / `QueueIsTechAt` / `Progress / ItemTicks` every frame through a
   `[laborer, age_ii, laborer]` run; clicking item k enqueues `CancelTrain(k)`.
4. Right-click ground with a building selected = one `SetRally`; on the building = one `ClearRally`; the flag stands at
   `RallyPosition` within a frame; minimap right-click = `SetRally`.
5. The resource bar reads "Pop 5 / 10" at the start (5 workers, one Town Hall) and "Pop 5.5 / 10" after a half-pop
   unit; red at the cap.
6. BUG-0108 / 0109 / 0110 fixed: QA's `QaV2Test` rows pass with `-- --strict`; dev regression rows for each.
7. `ViewApi` additions read-only (hash twin over 400 ticks calling every new read each tick); no sim file outside
   `ViewApi/` in the diff; `game/data/common/ui.json` the only data file touched; `ui.json` carries `placement.requires`,
   `train.*` and `research.*` keys (incl. `requires`) and loads against today's `main`.
8. 300 idle frames with panel + production card + queue strip + rally flag: 0 bytes after warm-up; `--bench 10` avg
   under 1.5 ms.
9. `tools/qa/smoke.ps1` PASS; every scene PASS; non-Perf green; docs/03 "Implementation (M3-V3)" (panel, card, queue
   strip, rally, pop, `ui.json` additions, the BUG-0108 / 0109 fixes); docs/01 row; bug files BUG-0108 / 0109 / 0110 set
   to `fixed` with the test names, README rows updated (the Producer applies the `studio/**` part if the dev can't:
   write the exact text in the report).

**Design references.** docs/02 "HUD layout" (selection panel, command card, resource bar with pop), "Controls and
camera" (right-click sets rally on a selected building; minimap), "Grid hotkeys", "Production" (queue of 5, cancel
refunds), "Ages"; docs/03 "Implementation (M3-4)" (reads: `CanTrain`, `QueueCount`, `Progress`, `TrainTicks`, `HasRally`,
`RallyPosition`, `HalfPop`), "(M3-5)" (`CanResearch`, `TechsResearchableAt`, `QueueIsTechAt`, `ItemTicks`, `TechBonus`),
"(M3-V2)" (`ui.json`, card modes, `CanPlace` rule); CLAUDE.md rules 1, 7, 8.

**Tests required.** `game/tests` scenes per criterion 1-6 and 8; xUnit `ViewApi` tests for new pure helpers (queue
strip layout, pop text, rally geometry) + the hash twin (7); smoke PASS.

**Constraints most at risk.** Player-facing text in C# (every label / state name / reason through `ui.json` or
`displayName`; "+N" number formats are fine); per-frame allocation (the panel rebuilds its strings only when a shown
value changes: hp changes every tick under repair, so cache per-value strings or use a numeric label); views holding
gameplay state; `CanPlace` still once per frame after the BUG-0109 change; the sim's `DataLoader` untouched.

**Debt to fold in only if a few lines:** BUG-0108 / 0109 / 0110 (above, planned). BUG-0122 (S4 nits) and BUG-0104 /
0105 / 0107 wait for the view hardening session (after M3-V3 and the Playable session, counter 3 / 4 then: the view's
end-of-M3 hardening).

### After M3-V3 (view)
The M3 **Playable** session: the Producer writes playtest instructions under For your review ("build a full Malazan
base and reach Age II"); the criterion is ticked when the owner says so in the inbox, or by the Producer after a
scripted headless run of the full base + Age II path if the owner is silent for a day (autopilot rule). Then the view
hardening session (BUG-0104 / 0105 / 0107 / 0122 + whatever the playtest finds), then M4 view work (combat feedback,
fog shader) once the sim's M4 lands.

## Data track

### Current session plan: D3 — techs content and building requirements (feature, QA light)

**Goal.** M3-5 shipped the techs schema with placeholder text; the data track makes the tech content the design source
says: faction upgrade names and descriptions in each faction's voice, the building `requires` values from the faction
pages, pins against docs/02 "Tech" and the pages, and the BUG-0111 / BUG-0090 text recheck. The common techs' text
(`common/techs.json`) is **D4** (the sim edits that file this session).

**Scope** (files: `game/data/factions/malazan/techs.json`, `game/data/factions/whirlwind/techs.json`,
`game/data/factions/malazan/buildings.json`, `game/data/factions/whirlwind/buildings.json`, `docs/factions/malazan.md`,
`docs/factions/whirlwind.md`, tests under `sim/Rts.Sim.Tests/Content/` and `sim/Rts.Sim.Tests/QA/Content/`; **nothing
under `game/data/common/`**):
1. **Faction upgrades**: keep ids, costs (200 / 150), time (45 s), `researchedAt: forge`, `requires: ["age_ii"]` and the
   effects exactly as shipped (they match the pages; a pin fails if they drift); polish `displayName` / `description`
   (one or two sentences, the faction's voice, say what it does in player terms with the numbers: "Cusser cooldown
   45 s → 30 s, Catapult range +4 m").
2. **Building `requires`** per the pages' Requires column (docs/02 "Buildings"): Shock Hall ← the faction's Infantry
   Hall id; Caster Hall, Siege Works, Watch Tower ← `age_ii`; everything else `[]`. The loader checks the ids exist
   (M3-5); gating lands with M3-6 (same session, sim) and picks these up on `main`.
3. **Pins**: `Content/TechContentTests` (faction upgrades vs the pages' "Faction upgrade" lines: cost, time, effects,
   units named; building `requires` vs the pages' Requires column with names substituted, as `BuildingContentTests.G`
   does; descriptions mention every effect's number); QA `QA/Content/TechRosterQaTests` (every faction has exactly one
   faction upgrade at the Forge; ids snake_case; `requires` ids resolve to the own faction's building or a common
   tech; no faction upgrade names another faction's unit).
4. **Pages**: a "Techs" table on each faction page (id, name, researched at, cost, time, requires, effects) for the
   faction upgrade, plus a line pointing at docs/02 "Tech" for the shared upgrades; the Buildings table's Requires
   column stays the source (fix any wording the pin needs).
5. **BUG-0111** (page-pin gaps: the false "+N pop" claim, Provides free text, invariant culture in `UnitContentTests.G`,
   blank line before the pin line) and **BUG-0090** text recheck (the building descriptions' "needs Age II" / "needs a
   Legion Barracks" now match a `requires` value: assert each such sentence has one).
6. **Golden**: the content change moves `data-hash`; regenerate `cross_map_seed1.replay` **after merging `main` with
   the sim's M3-6** (the conductor merges sim first; if the sim track is late, regenerate on your branch and tell the
   conductor the sim's regen wins and yours is redone at merge). Every checkpoint byte-identical (no stat moved).
- **Out:** `common/techs.json` text (D4), new techs, balance changes, anything in C# outside the test folders.

**Acceptance criteria.**
1. Shipped data loads with 0 errors; both faction upgrades keep cost 200 / 150, 45 s (900 ticks), `forge`, `["age_ii"]`
   and their effects (Moranth Supply: Sapper `abilityCooldown` -15 s, Catapult `range` +4; Dryjhna's Prophecy: Zealot
   `hp` +20, Priest `abilityCooldown` -15 s), pinned to the pages by `TechContentTests`.
2. `requires` values: Shock Halls `[<infantry hall id>]`, Caster Hall / Siege Works / Watch Tower `["age_ii"]`, the
   other six `[]`, both factions, pinned to the pages' Requires column.
3. Every faction-upgrade description names each effect with its number; every building description that says "needs
   X" has a matching `requires` entry (BUG-0090 closed for buildings; towers' attack stays M4).
4. BUG-0111 items fixed (tests catch a false "+N pop", Provides text drift, culture-independent range formatting).
5. The pages' Techs tables exist and match the data (test reads the page).
6. Golden regenerated once with every checkpoint byte-identical; `dotnet build` 0 warnings; non-Perf suite green.
7. For your review table in the dev report: field, old → new, one-line why, every new name and description quoted.

**Design references.** docs/02 "Tech" (Ages, Forge table, faction upgrades), "Buildings" (Requires column);
`docs/factions/malazan.md` line 27 / `whirlwind.md` line 31 ("Faction upgrade"), the pages' Buildings tables; docs/03
"Data format" (`techs.json` and `requires` shapes); CLAUDE.md rules 6, 8.

**Tests required.** `Content/TechContentTests`, `Content/BuildingContentTests` additions (requires pin, BUG-0090 /
0111), QA `QA/Content/TechRosterQaTests`; `DataValidationTests` + `ReplayGoldenTests` green.

**Constraints most at risk.** No C# outside the two test folders; no edit under `game/data/common/` (the sim's); the
golden regen order (above); player-facing text only in data; descriptions must not promise what the schema can't
express (towers' attack = M4).

### After D3 (data)
D4: `common/techs.json` text polish (Age II and the six Forge upgrades: names and descriptions pinned to docs/02
"Tech"), after M3-6 lands; then the data track STOPs until M4's `abilities.json` / `statuses.json` schemas (Telas Fire,
Cusser, Sandstorm, Zealot passives) and the tower attack / sight / detector fields. Data hardening (BUG-0090 towers
part) rides with M4 content.

## Watch-outs (all tracks)

- Three feature tasks build and test at once: Perf rows can fail from CPU contention; a failure counts only alone.
  `TightBlob2500` has 0.02 ms of headroom alone: if it fails alone, that is a real regression.
- Never kill processes you did not start (see "Where we are").
- Golden regen order: sim first, data after merging the sim (above). The view never regenerates it.
- The view's `UiText` fails fast on a missing key for an existing `PlacementError` member: M3-6 adds `Requires` to the
  enum, so M3-V3 **must** ship `placement.requires` (and tolerate keys for members that don't exist yet), or the merged
  `main` boots without a command card and the smoke gate fails. If M3-V3 merges before M3-6 the key is simply unused.
- The sim's M3-6 changes `CanTrain` / `CanPlace` / `CanResearch` reason enums (new `Requires` members): the view's
  `ui.json` keys are snake_case of the enum names, so the member must be spelled `Requires` in all three enums.
- The data track's D3 and the sim's M3-6 both move `data-hash`: see the regen order; the conductor should merge sim,
  then view, then data, and expect the data branch to carry the final golden.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" (suggested text under For your review, M1 entry).
- Builders: absolute paths only; never touch the owner's main checkout; worktrees on short paths.
