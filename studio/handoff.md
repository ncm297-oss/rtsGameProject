# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-10-17-0056** (the first full session of 2026-10-17, 1 / 8 after it runs). Base `main`
= 8d42118 (the 2036 merge). Golden `data-hash` on `main` **C059AD620DE581F1**; nothing planned this session moves it (the sim's task adds
no data field, see its brief). Counters before: **sim 2 / 4, view 2 / 4, data 0 / 4** (after this session: 3 / 4, 3 / 4, 1 / 4).
Bug ids this session: **sim from BUG-0850, view from BUG-0860, data from BUG-0870.** No open S1 / S2; S3 14, S4 15. Inbox: empty at the 0056 PLAN.

**Sizing note (0056):** the weekly usage was at 89 % with the 95 % stop about an hour from reset, so this session carries three *small, clear*
tasks instead of the pathing slice and a full-size look test. The pathing slice 2 brief (handoff 2036, in git history) is unchanged and
is the sim's next feature task after this one.

## Where we are

- **M5 3 / 9** (criteria 1, 2, 7 ticked; 3, 4, 5 advanced). **M6 1 / 10** (criterion 4 ticked; criterion 3's replay half built, its save
  half waits on the sim's Requests 42; criterion 1 waits on team, Requests 38; criterion 2 on Requests 39 = **this session's sim task**).
- The owner has not answered D19's review table, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780 ("accept" / "Catapult pass") or "densest cluster".

## Standing rules (1-29)

Unchanged 1-28 (see the 2036 handoff in git history for the long form; 28: `game/data/common/ui.json` is view-track text). **New 29 (0056):**
the art pipeline's tooling (`tools/asset-manifest.json`, `tools/import-assets.ps1`, docs/04 "Import process" steps 2-3) is **view-track
work** when it comes, by exception to the ownership table's `tools/**` line; until then imported files are copied by hand under
`game/assets/` with their source path stated in the licensing log row. The sim track never edits those two files.

---

## Sim track

### Current session plan: M6-S1, the match stats counters (Requests 39)

**Goal.** The stats screen (M6 criterion 2, docs/02 "Game flow" 5) needs numbers the sim does not count yet: gathered resources, units
trained, kills / losses split by units and buildings, buildings built / lost, the tick each tech was researched, and an army-value sample
over time for the graph. This task adds them to the sim as read-only per-player data so the view can fill its columns next session.

**Scope.**
- Extend `Economy/PlayerLedger` (and `World`'s read-only surface, mirroring `World.Kills` / `Losses`) with per-player counters:
  (a) `GatheredGold` / `GatheredWood`: counted at the drop-off delivery only (`EconomySystem.Deposit`, `World.AddToTotal` call at line ~131);
  refunds (`ConstructionSystem` ~280, `BuildingStore.Refund`), starting resources and any dev path count nothing. Give the deliver path its
  own ledger call rather than inferring from `AddToTotal`.
  (b) `UnitsTrained`: a queue item finishing in `ProductionSystem` (~241); summons (`SummonSystem`) and dev spawns (`Simulation` ~499) excluded.
  (c) `UnitKills` / `BuildingKills` / `UnitLosses` / `BuildingLosses`: split by `DeathEvent.IsBuilding` at the same point `CountDeath` runs
  (`World.cs` ~214); expiries and collapses stay uncounted, as today. `Kills` / `Losses` keep their meaning and stay hashed.
  (d) `BuildingsBuilt`: a construction completing (`BuildingStore` ~345, the `AddFinished(+1)` on completion); buildings spawned finished
  (the start Town Hall, ~431) excluded. `BuildingsLost` is (c)'s `BuildingLosses` (one counter, two names at most).
  (e) `World.TechResearchedTick(player, tech)` (or a span): the tick a tech finished, -1 before; set where `TechState` grants it. Per tech,
  not an Age field (owner direction 2026-10-14: Ages go; the view reads "time to Age II" from the `age_ii` tech until then).
  (f) Army-value samples: every `SimConfig.StatsSampleTicks` (default 200 = 10 s) record per player the summed gold + wood cost of its live
  non-worker units (use the predicate the AI's `IntruderWorth` / the economy already use for "worker"; name it once) into a preallocated
  per-player buffer of `SimConfig.StatsSampleCapacity` (default 1,024) ints; when full, sampling stops (documented). Read:
  `World.ArmyValueSamples(player)` → `ReadOnlySpan<int>` and `World.StatsSampleTicks`. One pass over the unit store per sample tick, none
  on other ticks, no allocation after construction.
- **Producer decisions:** (1) none of the new state is hashed (it decides no tick; the golden and every recorded replay stay valid);
  docs/03 "Save/load and replays" lists it as save state outside the hash for Requests 42. (2) The sample cadence is a `SimConfig` value,
  not a `rules.json` field, so `ContentHash` does not move and the owner's `user://replays` recordings stay playable; the owner may revisit.
- OUT: teams (Requests 38), the save format (Requests 42), the view's columns and `ui.json` labels (the view's next task), any change
  to `Kills` / `Losses` / the hash, `rules.json`, `game/data/`.

**Acceptance criteria.**
1. Each counter (a)-(d) has a unit test that counts the event and a test that the excluded path does not (a delivery v a refund; a trained
   unit v a summon v a dev spawn; the kill split for a unit and a building; a completed construction v the start hall).
2. (e): a researched tech's tick equals the tick `HasTech` first turned true; -1 before and for an unknown tech / player.
3. (f): samples at ticks 200, 400, ... hold the right sum (a scripted spawn / kill scenario); workers excluded; the buffer stops at capacity
   without a fault; `StatsSampleTicks` from `SimConfig` is honoured (a 50-tick config samples at 50).
4. Invariants in a fuzz (3+ AI-v-AI seeds, 2-4 seats, every tick or every 10 ticks): `UnitKills + BuildingKills == Kills`,
   `UnitLosses + BuildingLosses == Losses`, every counter monotone, `ArmyValueSamples.Length == min(tick / interval, capacity)`;
   a twin run (same seed, same commands) has identical counters and samples.
5. `ReplayGoldenTests` green with the golden untouched (`data-hash` C059AD620DE581F1, `seed21_playable.replay` unchanged); the full
   non-Perf suite green; 0 warnings; Perf paired A/B only if a per-tick path changed (the sample tick is the only one expected).
6. docs/03: an "Implementation (M6-S1)" section (what is counted where, what is excluded, the cadence and capacity, not hashed) and the
   "Save/load and replays" list extended; docs/02 "Game flow" 5 needs no change unless a column's meaning differs from it (say so).
7. The report names every public getter (the view's brief next session quotes them) and the production / test line counts.

**Design references.** docs/02 "Game flow" 5 (the columns); docs/03 "Save/load and replays"; docs/03 "Implementation (M5-4e)" (`LiveUnits` /
`ProductionBuildings` as the pattern for derived, unhashed ledger counts); STATE "Requests for the sim track" 39.
**Tests required.** `Economy/MatchStatsTests` (criteria 1-3), `Stress/MatchStatsFuzzStressTests` (criterion 4), the golden tests (5).
**Constraints.** No allocation on the tick path (preallocate at `World` construction); no LINQ in the sample pass; the worker predicate
from existing data (`UnitDef`), no id string compare; no Godot; no player-facing text; the hash untouched (criterion 5 proves it).
**QA tier:** standard. ~500-700 lines.

### Next task candidates (after this one, counter → 3 / 4; M5-H3 after one more)
1. **Pathing slice 2** (the 2036 brief: melee routes round own ranged units, unit-v-unit crowding away from buildings, far faces first;
   BUG-0790 folded in only if a few lines). QA full, ~800-1,000 lines, the golden's `k` lines may move.
2. M5-4c slice 2 (criterion 4: `gatherBonus`, `focusFire`, `pullBackWounded`). 3. Requests 38 (teams), then Requests 42 (the save
   format, now incl. the M6-S1 counters). 4. D14 b schema, M5-4d, D14 c, M5-5, M5-6.
**M5-H3** (after one more feature session): BUG-0764 first, BUG-0790, BUG-0791, BUG-0144, BUG-0113 item 2, BUG-0704 items 2-3.

### Watch-outs (sim)
- Only the sim moves the golden; this task must not (criterion 5). `SurroundClaim` / `SurroundHolder`, the Blackboard and now the stats
  counters are save state outside the hash: Requests 42 carries them.

---

## View track

### Current session plan: M6-V10, the art look test (criterion 5's first half)

**Goal.** Confirm the owner's chosen humanoid stack (Quaternius Universal Base Characters + Universal Animation Library, docs/04
"Characters and animation") reads well at RTS zoom, beside a stylized stand-in and today's primitive, so the owner can make the art
call before the art pass. No gameplay changes; the result is a test scene, a screenshot and licensing rows.

**Scope.**
- Copy by hand (standing rule 29; no manifest / script yet) into `game/assets/models/looktest/`: one Universal Base Character glTF
  (+ `.bin` + textures) from `C:/Dev/rtsGameProject/asset-sources/Universal Base Characters[Standard]/Base Characters/Godot - UE/`
  (the regular male, or the one that reads best); the UAL1 `.glb` from `.../Universal Animation Library[Standard]/Unreal-Godot/`
  (read the pack's `Godot_Setup.png` and `README.txt` for the AnimationPlayer setup); one Modular Outfit from `.../Modular Character
  Outfits - Fantasy[Standard]/Exports/glTF (Godot-Unreal)/Outfits/` only if it attaches without Blender; one Ultimate Fantasy RTS character
  FBX from `.../quaternius-ultimate-fantasy-rts-2022-08/Ultimate Fantasy RTS - Aug 2022/FBX/` as the stylized stand-in (KayKit is not
  on disk; Godot 4.7 imports FBX natively, if not cleanly use a building from the same pack). The folder names contain `[` `]`: use
  `-LiteralPath`. `asset-sources/` is at the repo root, not in the worktree, and never enters git.
- `game/tests/LookTest.tscn` + `LookTest.cs` (the `PropsShot` / `MarchShot` pattern): a flat ground patch at the game's scale (docs/04
  "Scale and dimensions"), the real `RtsCamera` at the default match zoom and pitch, three figures 3 m apart: the Universal character
  playing a UAL `idle` clip (and `walk` in place if a second clip is cheap) through an `AnimationPlayer` / `AnimationLibrary`, the
  stylized stand-in, today's unit primitive as `UnitViews` draws it. Team colour on the Universal character if the material names allow
  (docs/04 "Team color"), else say so. The scene checks the three meshes exist, the clip advances, no ERROR, prints the loop's PASS line.
- Two screenshots looked at by the dev: `docs/images/look-test-2026-10-17.png` (default zoom) and `-far.png` (max zoom-out), committed
  (LFS covers `.png`; check `git lfs ls-files`). The report says what the dev sees: readability, scale against the 1.8 m rule, softness.
- docs/04: licensing log rows for every pack whose files entered the repo (author Quaternius, the license as each pack's License txt
  states it, version / date from the folder, the source path copied from), the `.import` files committed, a short "Look test (M6-V10)"
  paragraph under "Import process" with the findings and the note that the manifest / import script land with the art pass.
- OUT: no unit view swap, no `UnitViews` / `Match` change, no gameplay, no data, no `tools/` script, no Mixamo, no decimation (report
  the tri count and texture sizes instead), no second character.

**Acceptance criteria.**
1. `LookTest.tscn` runs headless (in `tools/qa/scene-loop.ps1`) and windowed: the Universal character with a playing UAL clip, the
   stylized stand-in and the primitive side by side; the loop row prints PASS.
2. The two screenshots are committed through LFS (`git lfs ls-files` lists them) and described in the report.
3. docs/04 licensing rows for every imported pack; every imported file under `game/assets/models/looktest/` with its `.import`;
   `.gitattributes` already covers `.glb` / `.gltf` / `.fbx` / `.png` / `game/assets/**/*.bin`: any other extension is added to it
   **before** the first commit that adds the file.
4. `tools/qa/smoke.ps1` PASS; the whole scene loop green; `sim/` diff empty; the imported files total under ~40 MB (state the number).
5. The report gives the Universal character's tri count and texture sizes, whether FBX imported natively, and the dev's own read of
   the look at RTS zoom (for the owner's taste call under For your review).

**Design references.** docs/04 "Style guide", "Scale and dimensions", "Team color", "Characters and animation", "Import process",
"Animation setup", "Licensing log"; docs/01 change log 2026-10-08 (the owner's art direction).
**Tests required.** `LookTest.tscn` in the loop; `ShellTest` and the existing scenes untouched.
**Constraints.** Raw packs never enter git; binary types in `.gitattributes` before the first commit; views hold no gameplay state;
no C# text; CLAUDE.md definition of done 3 (look at the screenshot before claiming it works).
**QA tier:** light. ~300-500 text lines plus the binaries.

### Next task candidates (after this one, counter → 3 / 4; M6-VH3 after one more)
1. **The stats columns** (criterion 2) once M6-S1 is on `main`: `MatchStats.Columns` + `ui.json` labels for gathered, trained, built /
   lost, the kill split, time to Age II (`age_ii`'s researched tick), the army-value graph from `ArmyValueSamples`.
2. Save / load (criterion 3's other half) once Requests 42 lands. 3. `tools/export.ps1` when the owner's templates arrive. 4. Teams
   (Requests 38). 5. Criterion 6's playable run against the AI. 6. The art pass if the owner confirms the look.
**M6-VH3** (after one more): BUG-0830 first, BUG-0550 item 2, BUG-0281 item 2, BUG-0663, the `--colour` collision warning's wording.

### Watch-outs (view)
- Three Godot runs share the machine: wall-clock scene rows flake under load; rerun alone before filing.
- The look test touches `game/assets/`, `game/tests/`, `docs/04`, `docs/images/` only; `.gitattributes` first for any new binary type.

---

## Data track

### Current session plan: D20, re-print + re-word on M5-4f's numbers (QA light, ~200 lines, no number changed)

**Goal.** M5-4f changed the gather rates (gold 0.85 / wood 0.75), sized the AI's defense and made the executor skip a house at the hard
cap (D15 C). The faction pages' and docs/03's spending and house paragraphs and the pages' Defense lines were written on the old numbers
and the all-or-nothing recall; re-run the reports and re-word the prose so the docs say what the game does.

**Scope.**
- **First, any owner answer in the inbox** (none at the 0056 PLAN): D19's table, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780
  ("accept" closes it with a dated line on both pages; "Catapult pass" = a sweep first, then one header move the session the sim moves none).
- `Content/AiSpendingReportTests` re-run on `gatherRate` 0.85 / 0.75 and the sized defense; both pages' "Spending on the menu's map"
  paragraphs and docs/03's re-printed with the new numbers and the date.
- `Content/AiHouseRuleReportTests` re-run after D15 C; the house notes on both pages say the executor skips a house at the hard cap.
- Both pages' "Defense" lines re-worded for the proportional response, the knobs quoted from `rules.json` (`defendRatio` 1.5,
  `defendMinValue` 150, `recallHallHp` 0.5) and the one-Raider / big-raid / dying-hall cases in one sentence each.
- OUT: any `game/data/` file (no number moves; the golden stays), new content, the Shock pass, BUG-0780 (owner's call).

**Acceptance criteria.**
1. Both report tests run green on the shipped data and their printed paragraphs match the pages and docs/03 word for word where they are
   quoted (the existing report-conformance rows pass).
2. `docs/factions/malazan.md` and `docs/factions/whirlwind.md` Defense lines name the three knobs with the `rules.json` values, and a
   `QA/Content` row pins each quoted value to `rules.json` (a changed knob fails by name).
3. The house notes on both pages match D15 C's rule (a test row pins the sentence to the executor's behaviour as `AiHouseRuleReportTests` prints it).
4. No file under `game/data/` changed; `DataValidationTests` and the content suites green; the golden `data-hash` unmoved.
5. Each changed page section carries the date 2026-10-17 and the reason (M5-4f) per the report-regeneration convention in `studio/bugs/README.md`.

**Design references.** docs/02 "Economy" (the gather rates), docs/02 "AI opponent" (the defense paragraph as M5-4f wrote it), docs/03
"Implementation (M5-4f)". **Files:** `docs/factions/malazan.md`, `docs/factions/whirlwind.md`, docs/03's spending paragraph (shared file,
a small append-only edit), `sim/Rts.Sim.Tests/Content/*`, `sim/Rts.Sim.Tests/QA/Content/*`. **Sim-track tests that pin values this task
touches:** none (no number changes). **QA tier:** light.

### After that
The D14 b openings once the strategies schema is on `main`; the owner's tweaks; the M7-M9 rosters when their milestones open.

---

## Watch-outs (all tracks)

- **BUG-0764:** Perf rows red on both trees on this PC; an owner fresh-boot run is wanted before M5-H3 re-baselines. Paired A/B only.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/03: each track appends its own rows; the conductor unions them.
- Merge order when the sim lands: sim → view → data. The view's M6-V10 adds LFS objects: the push needs LFS (Git LFS 3.7 is installed).
