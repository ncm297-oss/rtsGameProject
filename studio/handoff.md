# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-10-09-0724** (base `9cefc1f` = `origin/main` after the 0125
integration). All three tracks GO. The ACCEPT of this session replaces this file.

## Where we are

- M0-M3 Done. **M4: 5 / 10** on `main`. Criterion 5 (fog) owes only the view drawing the remembered buildings (M4-V5, this
  session). Then 6 abilities (M4-4, slice 1 this session), 7 stealth (M4-5), 8 the four signature abilities, 10 the fog-on
  sandbox Playable.
- Plan check this session: build 0 warnings, smoke PASS (tick 103), non-Perf suite run at the start (result in the session
  log). Inbox empty. Verified: `FogView.CollectGhosts` still returns 0 (the hook); `ConstructionSystem.Check` lines 60-63 test
  every alive unit with no fog check (BUG-0280 is real).
- Hardening counters: sim **1 / 4**, view **1 / 4**, data **4 / 4 → D-H1 (hardening) this session**.
- Open S1 / S2: **none.** Open S3: BUG-0280 (first item of the sim task), BUG-0273 (view, this session), BUG-0270, BUG-0241,
  BUG-0251, BUG-0157, BUG-0144, BUG-0151.
- The owner's balance answer (D6 entry under For your review) is still wanted; the data track changes no number until it comes.
- Bug ids this session: sim from **BUG-0300**, view from **BUG-0310**, data from **BUG-0320**.
- Weekly usage headroom is thin (88 % of the 95 % stop until 2026-10-10 02:00): every task is sized to finish in this session;
  a dev that runs out of room ships what is green and says what is left.
- Standing rule for sim briefs: **a task that changes a placement, vision, combat or data-loading rule runs the full suite
  incl. Perf once and the merged scene loop before it reports** (M4-3b broke two Perf rows, five view scenes and a replay
  that `Category!=Perf` never saw).

## Sim track

### Current session plan: M4-4a · BUG-0280, then the ability + status schema and Telas Fire end to end · feature · QA full

**Goal:** close the fog's last information leak (BUG-0280), then start M4 criterion 6 with the smallest real slice of the
data-driven ability system: the `statuses.json` / `abilities.json` schema, `Command.UseAbility`, one generic *target ground*
ability kind with `applyStatus` (and `damage`) effects, and the status store with two statuses (Burning, Slowed). The first
shipped entry is Telas Fire (docs/factions/malazan.md "Abilities": range 16 m, radius 3 m, cast 0.8 s, cooldown 25 s, Burning
10 magic damage / s for 4 s, no effect on buildings), which also starts criterion 8. **New system, real uncertainty: slice 1
only, about 800 lines of rules.** Zones, summons, self / aura, autocast, `abilityCooldown` techs and the AI are slice 2.

**Scope:**
1. **BUG-0280 (first commit, alone):** the units-in-the-way loop in `ConstructionSystem.Check` skips an enemy unit the placing
   player cannot see (`Fog.CanSeeUnit(player, i)` false); when the worker arrives and the hidden unit is still inside the
   footprint, the Build is refused as it is today for a seen unit. Hidden enemy *buildings* still refuse as `Blocked`
   (footprints can't overlap; say so in docs/03). Regression test + a docs/03 "Buildings" line.
2. **Schema (sim-owned):** `game/data/common/statuses.json` (`{ "statuses": [ {id, displayName, description, kind, ...} ] }`,
   kinds shipped: `burning` = damage over time with a damage type and amount per second; `slowed` = movement speed x
   (1 - magnitude)); `game/data/factions/<id>/abilities.json` (`{ "abilities": [ {id, displayName, description, kind, range,
   radius, castTime, cooldown, duration, affects, effects[]} ] }` per docs/02 "Ability system"; kinds accepted now:
   `targetGround`; effects: `damage {type, amount}` and `applyStatus {status, magnitude, duration}`; the other kinds / effects
   are *recognized and refused* with "not supported yet" naming the field, so slice 2 adds code, not schema); `UnitDef.Abilities`
   (ids resolved at load, max 4); `DataValidationTests` rows (unknown status id, negative or zero cooldown, radius above a
   `DataLimits` cap, range above `MaxSight`, an unknown kind, an ability id used by a unit of another faction); `ContentHash`
   covers both files; a faction without an `abilities.json` loads as empty. **Minimum entries:** `common/statuses.json` with
   `burning` and `slowed`; `malazan/abilities.json` with `telas_fire` (numbers from the faction page, `affects: enemy_units`);
   `malazan/units.json`: the Cadre Mage gets `"abilities": ["telas_fire"]` (that one line is the only edit to a faction units
   file; `whirlwind/abilities.json` is not created unless the loader needs it, then `{ "abilities": [] }`). Player-facing text
   only in those files.
3. **`Command.UseAbility(player, unit, abilityIndex, targetX, targetY[, queued])`:** dropped like a bad Gather when the unit is
   dead / not the player's / lacks the ability / the ability is on cooldown / the point is off the map; accepted out of range:
   the unit **walks until in range, then casts** (the existing walk-then-act path of Gather / Attack; document the choice).
   Replay format bump if the command line needs new fields (golden regen once, reason in the commit).
4. **`AbilitySystem`** (a phase per docs/03's tick order, placed and documented like `TowerSystem` was): the cast timer
   (`castTime` → ticks; the unit stands still and is interruptible by a new order, which cancels the cast without starting the
   cooldown), the resolve at the timer's end on the units in `radius` of the point (spatial hash, `affects` rule: Telas Fire
   hits enemy units only; `damage` goes through `DamageCalc` as the given type, `applyStatus` through the store), the per-unit
   cooldown counters (ticks, hashed, decrement every tick or store an end tick). Only the nearest selected caster casts
   (docs/02): that is a **view-side** choice when several are selected; the sim casts for each command it gets.
5. **`StatusStore`** (docs/03 "Abilities, statuses, zones": a fixed array of up to 8 per unit, `(statusId, magnitude,
   ticksRemaining, sourcePlayer)`, hashed): the stacking rule (same status refreshes duration and keeps the stronger
   magnitude; different statuses stack); Burning ticks its damage (per second → per tick, deterministic rounding documented, a
   kill credits `sourcePlayer`), Slowed multiplies the unit's movement speed where `MovementSystem` reads it (recomputed when
   the set changes, not every tick); statuses expire on time and die with the unit.
6. Docs/03 "Implementation (M4-4a)" (the phase, the rules, the drops, the rounding), "Data format" rows for both files and
   `UnitDef.Abilities`, the tick-order list; docs/01 rows for every Producer default; "For the view (M4-V6)": the read-only
   surface (per unit: ability count, cooldown remaining, cast in progress + its point; active statuses; a one-tick cast /
   resolve event list like `World.Deaths`); "For the data track": what `abilities.json` accepts now.

**Out of scope:** zones (Sandstorm, Darkness), summons, self / aura, autocast, `abilityCooldown` tech effects (loaded since
M3-5; wire in slice 2), Cusser / Sharpers / Zealot passives, stealth (M4-5), the AI, anything under `game/` except
`game/data/common/statuses.json`, `game/data/factions/malazan/abilities.json` and the one Cadre Mage line; `ViewApi/`,
`Content/`, `QA/Content/`, `docs/factions/**`, `ui.json`; `game/data/factions/*/buildings.json` (the data track's D-H1 may edit
their text fields).

**Acceptance criteria:**
1. BUG-0280: a hidden enemy unit under the footprint no longer refuses `CanPlace` and a seen one still does; the Build is
   refused when the worker arrives with the unit still there; `NeverSealTests`, `ConstructionFuzzStressTests`, `PlacementTests`
   green; BUG-0280 set fixed with the test name.
2. The schema loads the shipped entries and refuses each bad value above with an error naming the file and field;
   `ContentHash` covers both files (a `DataContentHashTests` row each); the golden diff touches `data-hash` / `checksum` only.
3. A Cadre Mage ordered to cast Telas Fire at a point: in range it stands for 0.8 s then every enemy unit within 3 m of the
   point is Burning; each takes 10 magic damage a second for 4 s (40 total, armor ignored: `DamageCalc` magic rule), own units
   and every building are untouched; out of range it walks first; on cooldown the command is dropped; a new Move during the
   cast cancels it with no cooldown; the cooldown is 25 s from the resolve.
4. Statuses: reapplying Burning refreshes the 4 s and keeps the stronger magnitude; Slowed at 0.3 makes a unit cover 70 % of
   its distance over 100 ticks (one test through a test-only ability or a direct store call); both expire on time; a unit
   that dies drops its statuses and frees the slot.
5. Determinism: `Stress/AbilityFuzzStressTests` (3+ seeds x 2,000 ticks of hostile UseAbility spam incl. out-of-range, dead
   casters, points off the map: twins equal every tick, replay round trip); `StateHashTests` rows for cooldowns, cast timers
   and every status field.
6. Perf: a row with 500 units, 50 Burning + 50 Slowed live, 20 casts a second: ability + status phases ≤ 0.1 ms a tick, 0 B
   per tick (`AllocateNothing` row).
7. Build 0 warnings; **the full suite incl. Perf once**; the merged scene loop (merge the view branch head into a scratch tree:
   the view's M4-V5 is in flight); smoke PASS; docs updated; bug files set.

**Design references:** docs/02 "Abilities and status effects" (kinds, effect vocabulary, stacking rule), "Combat" (magic
ignores armor); docs/factions/malazan.md "Abilities" (Telas Fire row); docs/03 "Abilities, statuses, zones", "Tick order",
"Data format", "Implementation (M4-2b)" (splash shapes to reuse), "Implementation (M4-3b)" (how a new phase and hashed field
were documented); BUG-0280.

**Tests required:** loader rows (`Data/AbilityLoaderTests`, `Data/StatusLoaderTests`); `Abilities/AbilitySystemTests`;
`Abilities/StatusStoreTests`; `StateHashTests` rows; the fuzz; the Perf row; the BUG-0280 regression.

**Constraints:** no per-tick allocation; no `Dictionary` iteration; no Godot in `Rts.Sim`; determinism (golden regen once,
reason in the commit); the sim track never edits `ViewApi/`, `Content/`, `QA/Content/`, `docs/factions/`, `ui.json`,
`sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` or `studio/bugs/BUG-0146-seed21-wood-wedge.replay` (the view's BUG-0273 this
session), `game/data/factions/*/buildings.json` (the data track's). Bug ids from BUG-0300.

**QA focus (full):** casts at the range edge and the cooldown boundary (tick before / tick of); the caster dies or is moved
mid-cast; a target that dies the tick the DoT lands; stacking the same status from two casters of different magnitude and
`sourcePlayer`; 8 statuses on one unit then a 9th; the hash under hostile cast spam (twins); a replay recorded with casts;
BUG-0280's leak (sweep a ghost over hidden enemies: never a refusal that depends on a hidden unit); the merged scene loop.

### Watch-outs (sim)

- `World.Neighbors` / `FlowFields.BuildScratch` are shared scratch; the fog's layers lie over `BuildScratch` in phase 12.
- Any test that orders an explicit Attack must stage the target in the owner's sight (`CombatScenes.Spot` / `Spotter`).
- Test scenes that place buildings far from their workers need `TestSim.Explored` (or `BuildMaps.NewSim`).
- Next sim hardening (counter 2 / 4 after this): BUG-0241, BUG-0157's kept-chase rule, BUG-0270, BUG-0275 items 1-2,
  BUG-0271 / 0272 (decide or wontfix), BUG-0144, BUG-0142 items 1-2, BUG-0113 item 2, BUG-0094, BUG-0242.

## View track

### Current session plan: M4-V5 · the ghosts on screen, the "Unexplored" hover row, BUG-0273 · feature · QA standard

**Goal:** finish M4 criterion 5: enemy buildings the player has seen stay on screen as darkened ghosts in explored fog (from
the sim's `Fog.Ghosts(local)`, on `main` since 9cefc1f), a right-click on a ghost is an Attack on that building (the sim accepts
it), the build ghost's "Unexplored" text is proven in a scene, and the seed-21 Playable replay is re-recorded.

**Scope:**
1. **Ghosts:** `FogView.CollectGhosts` reads `fog.Ghosts(Player)`: an entry with `Known` whose building the player does not see
   now (`BuildingShown` false, or the slot holds another building: generation differs) is a ghost at its anchor in its type's
   box, drawn darkened (the fog's prop material or a dedicated ghost material), no bar; dropped when the sim drops the entry
   or the building is seen again (then the real view). `BuildingViews` draws them (pooled, allocation-free). BUG-0275 item 1:
   a remembered site draws as a finished building until the sim adds a site flag (document it).
2. **Right-click on a ghost:** `SelectionController.EnemyAt` / `ContextTarget` pick ghosts too (a `BuildingPicker` overload over
   the ghost list, or the same ray test on remembered footprints) → `Command.Attack(..., isBuilding: true)` with the ghost's
   handle (slot + remembered generation); the red ring sits on the remembered footprint. Minimap ghost squares in the fog
   layer's darkened area if cheap (optional).
3. **"Unexplored" hover row:** a scene test hovers the placement ghost over unexplored ground and checks it reads red with
   `placement.unexplored`'s text (the coverage gap QA named).
4. **BUG-0273:** re-record `studio/bugs/BUG-0146-seed21-wood-wedge.replay` from `M3PlayableTest -- --seed 21` on the tree
   (the scripted build now picks explored ground), update `RecordedDataHash`, un-skip the row in
   `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` (**named exception: the view may edit that one sim test file this session**);
   check the row plays every checkpoint. **Note:** the sim's M4-4a moves `data-hash` this session, so the re-recorded
   replay's `RecordedDataHash` will need one more regen at integration or at the next view session; say in the report which
   tree it was recorded on.
5. BUG-0281 item 3 if a few lines (pick over the shown list captured at the last dot refresh, or refresh the dots when
   `RefreshedTick` moves); items 1-2 are the M6 fog-look pass's unless trivial.
6. Docs/03 "Implementation (M4-V5)"; the M4-V4 section's "Ghosts" paragraph updated; the `CollectGhosts` TODO removed.

**Out of scope:** the fog's look (M6), ability feedback (M4-V6, after the sim's M4-4a lands), stealth visuals (M4-5), any sim
file outside `ViewApi/` and the named exception, `game/data/**` (no new `ui.json` key expected; if one is needed it is
view-owned).

**Acceptance criteria:**
1. Windowed on seed 1: scout the enemy base, walk away: its buildings stay as darkened ghosts; one destroyed while unseen
   stays until the ground is seen again, then vanishes; a screenshot looked at and named.
2. Scene checks every tick: ghosts drawn == the sim's entries not currently seen (0 mismatches over 900 ticks on 2 seeds); a
   right-click on a ghost sends one Attack per selected unit with the ghost's handle, and the units walk there.
3. The hover row: the placement ghost over unexplored ground is red with "Unexplored"; over explored ground as before.
4. BUG-0273: the seed-21 replay plays all its checkpoints (`GatherWedgeQaTests` un-skipped and green on the view branch;
   `RecordedDataHash` updated; `M3PlayableTest --seed 21` headless PASS).
5. Build 0 warnings; smoke PASS; scene loop green (new rows included); 0 B per frame with ghosts at `--units 500`; hash twins;
   docs/03 updated.

**Design references:** docs/02 "Vision and fog of war" (ghosts); docs/03 "Vision, detection, fog" ("For the view": `Fog.Ghosts`
/ `GhostCount`, "the list changes only on an update tick"), "Implementation (M4-3b)" (the ghost list's rules),
"Implementation (M4-V4)" (the hook); BUG-0273 / 0275 / 0281.

**Tests required:** `ViewApi/FogViewTests` ghost rows (collect == the sim's entries, the "slot holds another building" case);
the scene rows above; the un-skipped `GatherWedgeQaTests` row.

**Constraints:** `ViewApi/` stays read-only; no gameplay in views; the view edits no `sim/**` file outside `ViewApi/` and its
test folders except the named `GatherWedgeQaTests.cs`; `game/data/` untouched. Bug ids from BUG-0310.

**QA focus (standard):** ghost == sim list every tick on 2 seeds incl. a building destroyed unseen and a slot reused; a
right-click on a ghost whose building is gone (the order ends when the ground is seen); the hover row at the fog edge; 0 B at
2,000 units; the seed-21 replay's checkpoint count; the `--no-fog` scenes still green.

### Watch-outs (view)

- `Fog.Ghosts(player)` changes only on update ticks (`tick % 4 == 1`); `Version(player)` moves then too.
- BUG-0251: `EconomyViewTest` can FATAL at shutdown after PASS under load (rerun once; test-side fix at the next view hardening).
- Next view hardening (counter 2 / 4 after this): BUG-0251, BUG-0250 (3 items), BUG-0281 leftovers, BUG-0148 item 1,
  BUG-0126 items 3 / 5 / 6, export hygiene (M6).

## Data track

### Current session plan: D-H1 · the first data hardening · hardening · QA light

**Goal:** the data track's debt after eight feature tasks: BUG-0290, BUG-0090's last open item (the towers' shooting /
detection text against the `attack` / `detector` fields on `main` since M4-3b), the content tests' shared page-table helpers,
docs/factions drift. **No number changes** (the owner's balance answer is still wanted). The inbox is empty this session, so
no review tweak comes first.

**Scope:**
- **BUG-0290:** `CounterTriangleMarginsTests.Compare` reports a header mismatch *and* still compares the rows (or says rows
  were not checked); BUG-0243's D8 note corrected.
- **BUG-0090 recheck:** a `BuildingContentTests` row that each building whose description says it shoots or spots hidden
  enemies has `BuildingDef.Attack` / `Detector` set, and each building with those fields says so (both ways; a data mutation
  and a text mutation each fail naming building and field). The shipped sentences ("watches the approaches, spots hidden
  enemies and shoots at intruders") already match: **expect no text edit.** If a sentence really is stale, the edit moves
  `data-hash` (text is in `ContentHash`): regenerate the golden in the same commit and say so in the report (the sim's
  M4-4a moves it too; the Producer reconciles at integration).
- **Shared helpers:** the content tests' page-table readers (`Cells`, the section finders in `BuildingContentTests` /
  `UnitContentTests` / `TechContentTests` / `CounterTriangleMarginsTests`) into one `Content/PageTables.cs`; no assertion changes
  (QA diffs the printed outputs before / after).
- **docs/factions drift:** both pages' Buildings tables against `buildings.json` for the towers (an Attack / Detector column is
  **D9's**, a feature; here only check nothing already on the pages contradicts the data), the "Balance baseline" prose, the
  Sight column; the "Casters fight without their signature abilities until M4-4" sentences stay (true until M4-4a merges).
- Bug files + index rows; `studio/qa/coverage.md` paragraph.
- **Files this track may touch:** `docs/factions/**`, `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`,
  `game/data/factions/*/buildings.json` **text fields only** (`displayName` / `description`, and only if stale), bug files,
  `studio/qa/**`.

**Out of scope:** any balance number; D9's Attack / Detector columns and pins (next feature task); abilities content (after
M4-4a's schema is on `main`: D10 fills `abilities.json` for both factions); `game/data/common/**`,
`game/data/factions/*/units.json` and `*/abilities.json` (the sim's M4-4a creates / edits those this session).

**Acceptance criteria:**
1. BUG-0290: the pre-D8 page in a scratch clone reports the header and the stale cells (or says rows unchecked); the file
   note fixed.
2. Every tower description sentence about shooting / detection is checked by a test against `BuildingDef.Attack` / `Detector`
   (both ways: a data mutation and a text mutation each fail naming building and field); BUG-0090 closed or its last item
   marked done.
3. The shared helpers compile with no test's assertions changed (QA diffs the printed outputs before / after).
4. Content + DataValidation green; no diff under `sim/Rts.Sim/`, `game/data/common/`, `*/units.json`, `*.replay` (unless a text
   edit moved `data-hash`: then the golden regen in the same commit with the reason); build 0 warnings; bug files set.

**Design references:** docs/02 "Buildings" (the Watch Tower row), "Stealth and detection"; `docs/factions/malazan.md` /
`whirlwind.md` Buildings tables; BUG-0090, BUG-0290.

**Tests required:** the BUG-0290 rows; the tower description rows; the existing content suite green.

**Constraints:** no C# outside `Content/` and `QA/Content/`; no number changes; QA light. Bug ids from BUG-0320.

**QA focus (light):** criteria, build / tests, docs conformance; the before / after printed tables identical; the mutation
checks; `git diff --stat` of the forbidden paths empty.

### Watch-outs (data)

- After D-H1 (counter 0 / 4): **D9** (the towers' Attack / Detector columns and pins on both pages), then **D10** (abilities
  content: Cusser, Sandstorm's data once zones exist, descriptions for every ability, against M4-4a's schema on `main`), then
  the full balance pass once the fog-on sandbox gives numbers.

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the 35-scene loop and smoke on each **merged** result; push only green states.
  Default order sim → view → data unless a first commit fixes a red row.
- **Golden `data-hash`:** the sim's M4-4a moves it (new files in `ContentHash`); the view's re-recorded seed-21 replay carries
  a `RecordedDataHash` from its own tree; the data track should not move it. At integration the Producer regenerates once on
  the merged tree if two sides moved it.
- Expected conflicts: `studio/qa/coverage.md` (append all sides), docs/03 (one subsection per track), docs/01 (rows appended),
  `studio/bugs/README.md` (union by id; a track that changes an existing index row in place should expect the union to keep
  both versions when another track appends right after it: the Producer dedupes at ACCEPT).
- **Perf:** three tracks' suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name; QA reruns `SerialCollectionTests`
  after committing its own files.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
