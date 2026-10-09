# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-09-0125** (all three tracks accepted; sim M4-3b and view M4-V4 land
together). The next session's Producer confirms the base (`origin/main` after this integration), the inbox, build / tests /
smoke, then uses the three "Current session plan" sections below as the briefs.

## Where we are

- M0-M3 Done. **M4: 5 / 10** on `main`. Criterion 5 (fog) is almost whole: the fog is in the rules (M4-3a + M4-H1) and on screen
  (M4-V4), towers shoot and the sim keeps each player's last-known enemy buildings (M4-3b). It owes only **the view drawing
  those ghosts** (M4-V5: `FogView.CollectGhosts` is a hook returning 0). Then 6 abilities (M4-4), 7 stealth (M4-5), 8 the four
  signature abilities, 10 the fog-on sandbox Playable.
- Hardening counters: sim **1 / 4**, view **1 / 4**, data **4 / 4 → the next data session is D-H1 (hardening)**.
- Open S1 / S2: **none.** Open S3 worth knowing: BUG-0280 (sim: the build ghost's "Units in the way" counts hidden enemy units,
  an information leak now that the fog hides them; **first item of the next sim task**), BUG-0273 (the seed-21 Playable replay
  must be re-recorded; `GatherWedgeQaTests` row skipped), BUG-0270 (a tower on high ground is never revealed to its victims),
  BUG-0241, BUG-0251, BUG-0157, BUG-0144, BUG-0151.
- The owner's balance answer (D6 entry under For your review) is still wanted; the data track changes no number until it comes.
- Bug ids next session: sim from **BUG-0300**, view from **BUG-0310**, data from **BUG-0320**.
- Lesson from this session (for every sim brief): **a task that changes a placement, vision or combat rule runs the full suite
  incl. Perf once and the merged scene loop before it reports** (M4-3b broke two Perf rows, five view scenes and one replay the
  dev's `Category!=Perf` run never saw).

## Sim track

### Current session plan: BUG-0280, then M4-4 slice 1 (the ability schema and the generic ability system) · feature · QA full

**Goal:** close the fog's last information leak (BUG-0280), then start M4 criterion 6: a data-driven ability system. docs/02
"Abilities" defines ability kinds (target ground, self / aura, summon), costs / cooldowns, status effects and zones; the four
signature abilities (Telas Fire, Sapper Sharpers + Cusser, Sandstorm, Zealot passives) are criterion 8 and come after. This is
a new system with real uncertainty: **plan only the first slice (~800 lines)**: the schema + loader + validation + one or two
generic ability kinds end to end, with statuses, and leave zones / summons / the rest for M4-4 slice 2.

**Scope:**
1. **BUG-0280 (first commit, alone):** `ConstructionSystem.Check`'s units-in-the-way loop skips an enemy unit the placing
   player cannot see (`Fog.CanSeeUnit(player, i)` false); the Build may still fail when the worker arrives (then the usual
   refusal). Hidden enemy *buildings* still refuse as `Blocked` (footprints can't overlap; note it in docs/03). A regression
   test (a hidden enemy under the footprint: `None` for the ghost, the Build refused on arrival) + docs/03 "Buildings" line.
2. **Schema (sim-owned):** `common/abilities.json` and `common/statuses.json` (or per faction, as docs/02 / docs/03 "Data
   format" say: check and follow them); `UnitDef.Abilities` (ids resolved at load), validation (`DataValidationTests` rows,
   errors naming the field), `ContentHash`; ship **only the minimum entries the sim's own tests need** (the data track fills
   content after). `Command.UseAbility(player, unit, ability, target)` (replay format bump if needed, golden regen once with
   the reason).
3. **Generic system, slice 1:** `AbilitySystem` (phase per docs/03's tick order), per-unit cooldowns (hashed), one targeted
   kind (ground point with radius: damage / status on units in the area, friendly fire per docs/02) and the status store
   (`StatusStore`: per-unit timed stat modifiers, hashed, applied through `DamageCalc` / movement speed); the AI and the view
   later. Zones (Darkness / Sandstorm vision blockers) and summons are **slice 2**.
4. Docs/03 "Implementation (M4-4a)", "Data format" rows, docs/01 rows for every Producer default; "For the view (M4-V6)" with
   the read-only surface (ability state per unit, active statuses, an event list for casts).

**Out of scope:** the four signature abilities' data (criterion 8, after the data track's content), zones, summons, stealth
(M4-5), anything under `game/` except `game/data/common/**`, `ViewApi/`, `Content/`, `QA/Content/`, `docs/factions/**`.

**Acceptance criteria:**
1. BUG-0280: a hidden enemy unit under the footprint no longer refuses `CanPlace`; a seen one still does; the Build is refused
   when the worker arrives and the unit is still there; `NeverSealTests`, `ConstructionFuzzStressTests`, `PlacementTests` green;
   the QA row that pinned the leak (if any) flipped.
2. The schema loads the test entries and refuses bad values (unknown status id, negative cooldown, radius above a limit, a
   target kind the ability doesn't take) with errors naming the field; `ContentHash` covers abilities and statuses.
3. A unit with the test ability casts it on a ground point: the command is accepted only in range and off cooldown (else
   dropped like a bad Gather, documented), the effect lands on the units in the radius with docs/02's friendly-fire rule, the
   cooldown counts down and is hashed; statuses expire on time and modify what docs/02 says they modify.
4. Determinism: a fuzz (`Stress/`) with casts under hostile orders, twins equal every tick, replay round trip; `StateHashTests`
   rows per new field.
5. Perf: the ability / status phases cost ≤ 0.1 ms a tick at 500 units with 50 statuses live (a Perf row reports it); 0 B per tick.
6. Build 0 warnings; **the full suite incl. Perf once**; the merged scene loop (the view may be building M4-V5 at the same time:
   merge the view branch head into a scratch tree and run the loop there); smoke PASS; docs updated; bug files set.

**Design references:** docs/02 "Abilities", "Status effects", "Combat" (friendly fire); docs/03 "Tick order", "Data format",
"Implementation (M4-2b)" (projectile / splash shapes to reuse), "Vision, detection, fog" (zones are M4-4 slice 2); BUG-0280.

**Tests required:** loader rows; `Abilities/AbilitySystemTests`; `Abilities/StatusStoreTests`; `StateHashTests` rows; a fuzz with
twins + replay round trip; a Perf row; the BUG-0280 regression.

**Constraints:** no per-tick allocation; no `Dictionary` iteration; determinism (golden regen once, reason in the commit); no
Godot in `Rts.Sim`; the sim track never edits `ViewApi/`, `Content/`, `QA/Content/`, `docs/factions/`, `ui.json`; **does not touch
`sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` or `studio/bugs/BUG-0146-seed21-wood-wedge.replay`** (the view's BUG-0273 this
session); `game/data/factions/**` untouched (the data track's D-H1 may pin the towers' values from them).

**QA focus (full):** casts at the range edge and at the cooldown boundary; a target that dies mid-cast; stacking of the same
status; the hash under hostile cast spam (twins); BUG-0280's leak closed (sweep a ghost over hidden enemies: never a refusal
that depends on a hidden unit); the merged scene loop.

### Watch-outs (sim)

- `World.Neighbors` / `FlowFields.BuildScratch` are shared scratch; the fog's layers lie over `BuildScratch` in phase 12.
- Any test that orders an explicit Attack must stage the target in the owner's sight (`CombatScenes.Spot` / `Spotter`), unless
  the target is a remembered building (M4-3b).
- Test scenes that place buildings far from their workers need `TestSim.Explored` (or `BuildMaps.NewSim`).
- Next sim hardening (counter 1 / 4): BUG-0241 (chase livelock along a cliff), BUG-0157's kept-chase rule (bound by the mean),
  BUG-0270 (a per-(building, player) reveal), BUG-0275 items 1-2 (a `Site` flag on `BuildingGhost`; the test seam), BUG-0271 /
  0272 (decide or wontfix), BUG-0144, BUG-0142 items 1-2, BUG-0113 item 2, BUG-0094, BUG-0242.

## View track

### Current session plan: M4-V5, the ghosts on screen, the "Unexplored" hover row, BUG-0273 · feature · QA standard

**Goal:** finish M4 criterion 5: enemy buildings the player has seen stay on screen as darkened ghosts in explored fog (from the
sim's `Fog.Ghosts(local)`, on `main` since this integration), a right-click on a ghost is an Attack on that building (the sim
accepts it), the build ghost's "Unexplored" text is proven in a scene, and the seed-21 Playable replay is re-recorded.

**Scope:**
1. **Ghosts:** `FogView.CollectGhosts` reads `fog.Ghosts(Player)`: an entry with `Known` whose building the player does not see
   now (`BuildingShown` false, or the slot holds another building: generation differs) is a ghost at its anchor in its type's
   box, drawn darkened (the fog's prop material or a dedicated ghost material), no bar; dropped when the sim drops the entry
   or the building is seen again (then the real view). `BuildingViews` draws them (pooled, allocation-free). Note BUG-0275 item
   1: a remembered site draws as a finished building until the sim adds a site flag (document it).
2. **Right-click on a ghost:** `SelectionController.EnemyAt` / `ContextTarget` pick ghosts too (a `BuildingPicker` overload over
   the ghost list, or the same ray test on remembered footprints) → `Command.Attack(..., isBuilding: true)` with the ghost's
   handle (slot + remembered generation); the red ring sits on the remembered footprint. The minimap: ghost squares in the
   fog layer's darkened area if cheap (optional).
3. **"Unexplored" hover row:** a scene test hovers the placement ghost over unexplored ground and checks it reads red with
   `placement.unexplored`'s text (the coverage gap QA named).
4. **BUG-0273:** re-record `studio/bugs/BUG-0146-seed21-wood-wedge.replay` from `M3PlayableTest -- --seed 21` on the merged tree
   (the scripted build now picks explored ground), update `RecordedDataHash` and un-skip the row in
   `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` (**named exception: the view may edit that one sim test file this session; the
   sim track doesn't touch it**); check the row plays every checkpoint.
5. BUG-0281 items that are a few lines (item 3: pick over the shown list captured at the last dot refresh, or refresh the dots
   when `RefreshedTick` moves); items 1-2 are documented as the M6 fog-look pass's unless trivial.
6. Docs/03 "Implementation (M4-V5)"; the M4-V4 section's "Ghosts" paragraph updated.

**Out of scope:** the fog's look (M6), ability feedback (M4-V6, after the sim's M4-4), stealth visuals (M4-5), any sim file
outside `ViewApi/` and the named exception, `game/data/**` (no new `ui.json` key expected; if one is needed it is view-owned).

**Acceptance criteria:**
1. Windowed on seed 1: scout the enemy base, walk away: its buildings stay as darkened ghosts; one destroyed while unseen stays
   until the ground is seen again, then vanishes; a screenshot looked at and named.
2. Scene checks every tick: ghosts drawn == the sim's entries not currently seen (0 mismatches over 900 ticks on 2 seeds);
   a right-click on a ghost sends one Attack per selected unit with the ghost's handle, and the units walk there.
3. The hover row: the placement ghost over unexplored ground is red with "Unexplored"; over explored ground as before.
4. BUG-0273: the seed-21 replay plays all its checkpoints (`GatherWedgeQaTests` un-skipped and green; `RecordedDataHash` updated;
   `M3PlayableTest --seed 21` headless PASS).
5. Build 0 warnings; smoke PASS; scene loop green (new rows included); 0 B per frame with ghosts at `--units 500`; hash twins;
   docs/03 updated.

**Design references:** docs/02 "Vision and fog of war" (ghosts); docs/03 "Implementation (M4-3b)" (the ghost list's rules, "For
the view" `Fog.Ghosts` / `GhostCount`), "Implementation (M4-V4)" (the hook); BUG-0273 / 0275 / 0281.

**Tests required:** `ViewApi/FogViewTests` ghost rows (collect == the sim's entries, the "slot holds another building" case);
the scene rows above; the un-skipped `GatherWedgeQaTests` row.

**Constraints:** `ViewApi/` stays read-only; no gameplay in views; the view edits no `sim/**` file outside `ViewApi/` and its test
folders except the named `GatherWedgeQaTests.cs`; `game/data/` untouched.

**QA focus (standard):** ghost == sim list every tick on 2 seeds incl. a building destroyed unseen and a slot reused; a right-click
on a ghost whose building is gone (the order ends when the ground is seen); the hover row at the fog edge; 0 B at 2,000 units;
the seed-21 replay's checkpoint count; the `--no-fog` scenes still green.

### Watch-outs (view)

- `Fog.Ghosts(player)` changes only on update ticks (`tick % 4 == 1`); `Version(player)` moves then too.
- BUG-0251: `EconomyViewTest` can FATAL at shutdown after PASS under load (rerun once; test-side fix at the next view hardening).
- Next view hardening (counter 1 / 4): BUG-0251, BUG-0250 (3 items), BUG-0281 leftovers, BUG-0148 item 1, BUG-0126 items 3 / 5 / 6,
  export hygiene (M6).

## Data track

### Current session plan: D-H1, the first data hardening · hardening · QA light

**Goal:** the data track's debt after eight feature tasks: BUG-0290, BUG-0090's description recheck now that the towers' `attack`
/ `detector` are in the schema, the content tests' shared helpers, docs/factions drift. No number changes (the owner's balance
answer is still wanted); if an inbox answer on the D6 proposal is present, **that tweak comes first** (QA light, golden
`data-hash` regen, a For your review table).

**Scope:**
- **BUG-0290:** `CounterTriangleMarginsTests.Compare` reports a header mismatch and still compares the rows (or says rows were
  not checked); the BUG-0243 file's D8 note corrected.
- **BUG-0090 recheck:** every building description that states a requirement or a tower's attack / detection is checked against
  the data now (`requires` since M3-6; `attack` / `detector` since M4-3b): a `BuildingContentTests` row that the two towers'
  descriptions mention their shooting and detection consistently with the data (`RequiresText`-style, no numbers in prose unless
  the pages carry them); fix any stale sentence (text-only edits in `buildings.json` are a data edit: say so, golden regen).
- **Shared helpers:** the content tests' page-table readers (`Cells`, the section finders in `BuildingContentTests` /
  `UnitContentTests` / `TechContentTests` / `CounterTriangleMarginsTests`) into one `Content/PageTables.cs`; no behaviour change.
- **docs/factions drift:** both pages' Buildings tables against `buildings.json` for the towers' new fields (an Attack / Detector
  column is **D9's**, a feature; here only check nothing already on the pages contradicts the data), the "Balance baseline"
  prose, the Sight column.
- Bug files + index rows; `studio/qa/coverage.md` paragraph.
- **Files this track may touch:** `docs/factions/**`, `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`,
  `game/data/factions/*/buildings.json` **text fields only** (`displayName` / `description`), bug files, `studio/qa/**`.

**Out of scope:** any balance number; D9's attack / detector columns and pins (next feature task); abilities content (after
M4-4's schema).

**Acceptance criteria:**
1. BUG-0290: the pre-D8 page in a scratch clone reports the header and the stale cells (or says rows unchecked); the file note fixed.
2. Every tower description sentence about shooting / detection is checked by a test against `BuildingDef.Attack` / `Detector`
   (both ways: a data mutation and a text mutation each fail naming building and field).
3. The shared helpers compile with no test's assertions changed (QA diffs the printed outputs before / after).
4. Content + DataValidation green; no diff under `sim/Rts.Sim/`, `*.replay` (unless a text edit moved `data-hash`: then the
   golden regen in the same commit with the reason); build 0 warnings; bug files set.

**Design references:** docs/02 "Buildings" (the Watch Tower row), "Stealth and detection"; `docs/factions/malazan.md` /
`whirlwind.md` Buildings tables; BUG-0090, BUG-0290.

**Tests required:** the BUG-0290 rows; the tower description rows; the existing content suite green.

**Constraints:** no C# outside `Content/` and `QA/Content/`; no number changes; the sim's M4-4 touches `game/data/common/**`
this session (abilities / statuses): the data track touches only the two `buildings.json` text fields if at all.

**QA focus (light):** criteria, build / tests, docs conformance; the before / after printed tables identical; mutation checks.

### Watch-outs (data)

- D9 after D-H1: the towers' Attack / Detector columns and pins on both pages (schema on `main` since M4-3b); then abilities
  content once M4-4's schema lands; the full balance pass once the fog-on sandbox gives numbers.
- The sim's M4-4 ships `common/abilities.json` / `statuses.json` with minimum entries this session: do not touch `game/data/common/`.

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the 35-scene loop and smoke on each **merged** result; push only green states.
  Default order sim → view → data unless a first commit fixes a red row.
- Expected conflicts: `studio/qa/coverage.md` (append all sides), docs/03 (one subsection per track), docs/01 (rows appended),
  `studio/bugs/README.md` (union by id; **a track that changes an existing index row in place should expect the union to keep
  both versions when another track appends right after it: the Producer dedupes at ACCEPT**).
- **Perf:** three tracks' suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name; QA reruns `SerialCollectionTests`
  after committing its own files.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
