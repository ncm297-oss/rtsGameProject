# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-16-2036** (the third full session of 2026-10-16; the next session is the
first of 2026-10-17, 0 / 8). `main` after the merge = f3ac64a + the sim's M5-4f (5a731f9) + the view's M6-V9 (8ab63b1), merged sim → view.
Golden `data-hash` on `main` **C059AD620DE581F1** (moved once by M5-4f: the three defense knobs + `gatherRate`; the seed-21 recording
re-recorded whole at `sim/Rts.Sim.Tests/Replays/seed21_playable.replay`, 7,682 ticks).
Counters: **sim 2 / 4, view 2 / 4, data 0 / 4.** Bug ids next session: **sim from BUG-0850, view from BUG-0860, data from BUG-0870.**
No open S1 / S2; S3 14, S4 15 (BUG-0702 closed, BUG-0830 opened). Inbox: empty at the 2036 PLAN; check it first.

## Where we are

- **M5 3 / 9** (criteria 1, 2, 7 ticked; 3, 4, 5 advanced). **M6 1 / 10** (criterion 4 ticked; criterion 3's replay half built, its save
  half waits on the sim's Requests 42; criterion 1 waits on team, Requests 38; criterion 2 on Requests 39).
- The owner has not answered D19's review table, the Shock pass, `goldShare` 0.6 → 0.5, or BUG-0780 ("accept" / "Catapult pass").
- The owner's "densest cluster" cast rule (docs/01 2026-10-14) is **not built**: M5-4f seeds the cast group from the enemy nearest the
  caster (BUG-0702 item 1 closed on that). Listed under For your review as an option; build it only if the owner asks or a sim task
  touches `AbilityManager.Target` anyway.

## Standing rules (1-28)

Unchanged 1-27 (see the 1654 handoff in git history for the long form): BUG-0764's paired A/B rule for Perf rows; a new `Command`
kind into `CommandDoorFuzzStressTests`; per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>`; a data brief
names every sim-track test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the rest;
`git checkout HEAD -- path`; one system per brief, the dev's count excludes QA's files; no process killing by image name; **a dev never
loosens a QA row** (QA re-stages its own rows, with the reason in the file); the Producer's merged-tree check runs the whole scene loop
when the sim touches movement / the generator / `ResourcePlacer` / `ViewApi/StartBase` / liveness / the command door / the defeat rule
**or moves `gatherRate` / the golden**; a QA scene row for a bug left open prints `KNOWN BUG-nnnn`; 22. one track moves the golden
header per session; 23. a QA verdict needs its committed report file; 27. a dev who ends BLOCKED on a red QA row still hands the branch
to QA in the same session. **New 28:** `game/data/common/ui.json` is **view-track text** (the shell's and HUD's strings; the view has
edited it in every M6 task under its briefs): the ownership table's `game/data/common/**` line means the sim-loaded data files
(`rules.json`, `damage_table.json` ...). A view brief that adds keys names them; the sim track never edits `ui.json`.

---

## Sim track

### Next task candidates (feature, counter → 3 / 4; M5-H3 after one more)

1. **Pathing slice 2** (the owner's playtest note 5, continued; docs/03 "Implementation (pathing slice 1)" "Not yet (slice 2)"): a melee
   unit blocked by its own standing ranged units routes round them; unit-v-unit crowding away from buildings; smarter fill order (far
   faces first for the units that arrive last; the House row's far-corner jam 17-18 / 20). QA full (movement). ~800-1,000 lines; the
   golden's `k` lines may move (movement): re-record once, say why. **BUG-0790 (S3)** lives in the same code (`EconomySystem.StandPoint` /
   `WalkToFootprint`, `MovementSystem`'s crowd-arrival rule: a worker stands out of reach beside a free ring-cell spot): fold it in only if
   the fix is a few lines, then un-skip `GatherPocketStressTests (MineOneSide, 40)` and `Bug0790FaceProbeQaTests`' regression row; else M5-H3.
2. **M5-4c slice 2** (criterion 4): `gatherBonus` (Hard's 25 %), `focusFire`, `pullBackWounded` applied; D16's pins already name the numbers.
3. **Requests 39** (the stats counters: gathered, trained, built / lost, Age II time, army value over time) + **Requests 38** (teams: a
   `team` per seat in `SimConfig`, allied fog / targeting / defeat); then **Requests 42** (the save format: the Blackboard whole incl.
   M5-4f's marks, the surround claims, the flow-field cache, the fog bits; a round-trip test that compares the unhashed state directly).
4. D14 b schema (named openings per faction, picked by the seeded RNG; Easy the first), M5-4d (expansion, rebuilding, high-ground scouting,
   hunting the last units → criterion 3), D14 c, M5-5 (the 20-minute winner test → criterion 5), M5-6.

**M5-H3** (after one more feature session): BUG-0764 first (an owner fresh-boot Perf run wanted), BUG-0790 if still open, BUG-0791,
BUG-0144, BUG-0113 item 2, BUG-0704 items 2-3.

### Watch-outs (sim)
- Only the sim moves the golden; the data track's re-print this session changes no number, so no second header move.
- `SurroundClaim` / `SurroundHolder` and M5-4f's `DefenderGeneration` / `DefendAll` / `DefendAnswer` / `WaveMinHalfPop` / `WaveMinCost` are
  save state outside the hash: Requests 42 carries them (docs/03 "Save/load and replays").
- Any `rules.json` / data change moves `ContentHash` and makes every recorded replay in the owner's `user://replays` refuse to play
  (`load.refused`): by design, but say so in the For your review entry of the task that does it.

---

## View track

### Next task candidates (feature, counter → 3 / 4; M6-VH3 after one more)

1. **The look test** (M6 criterion 5's first half; docs/04): one Quaternius Universal Base Character with one Universal Animation Library
   clip through the docs/04 pipeline (import, scale, materials), standing beside a KayKit model and today's primitive on the M2 map; a
   windowed screenshot looked at; the licensing log rows in docs/04 for every pack touched. Packs are already in `asset-sources/` (owner
   note 2026-10-08); nothing to download. QA light / standard (import, smoke, the scene loop; no gameplay). Keep it to the test scene: no
   unit view swap yet (that is the art pass after the owner picks).
2. **Save / load** (criterion 3's other half) once Requests 42 is on `main`. 3. `tools/export.ps1` when the owner has the templates (Waiting
   on you). 4. Teams (Requests 38) and the stats columns (Requests 39) as the sim lands them. 5. Criterion 6's playable run against the AI.

**M6-VH3** (after one more): BUG-0830 (the replay folder: prune only readable stamped files, never the path just written, a "Replay"
label from `ui.json`, read only the header for the list), BUG-0550 item 2, BUG-0281 item 2, BUG-0663, the `--colour` collision warning's wording.

### Watch-outs (view)
- Three Godot runs share the machine: wall-clock scene rows flake under load; rerun alone before filing.
- `ReplayPlaybackTest` and `QaM6V9Test` record their own files (no committed `.replay` fixture): a data change on `main` moves nothing there.
- The look test touches `game/assets/` and `docs/04` only; every imported file goes in `.gitattributes` (LFS) before its first commit.

---

## Data track

### Next task: re-print + re-word on M5-4f's numbers (QA light, ~200 lines, no number changed)

- **First, any owner answer** in the inbox: D19's table, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780 ("accept" closes it with a dated
  line on both pages; "Catapult pass" = a sweep first, then one header move the session the sim moves none).
- `Content/AiSpendingReportTests` re-run on `gatherRate` 0.85 / 0.75 and the sized defense; both pages' "Spending on the menu's map"
  paragraphs + docs/03's re-printed; `Content/AiHouseRuleReportTests` after D15 C and the house notes on both pages.
- Both faction pages' "Defense" lines re-worded for the proportional response, the knobs quoted from `rules.json` (1.5 / 150 / 0.5).
- Design references: docs/02 "Economy" (the gather rates), docs/02 "AI opponent" (the defense paragraph), docs/03 "Implementation (M5-4f)".
  Files: `docs/factions/malazan.md`, `docs/factions/whirlwind.md`, `sim/Rts.Sim.Tests/Content/*`, `sim/Rts.Sim.Tests/QA/Content/*`. No `game/data/` file.

### After that
The D14 b openings once the strategies schema is on `main`; the owner's tweaks; the M7-M9 rosters when their milestones open.

---

## Watch-outs (all tracks)

- **BUG-0764:** Perf rows red on both trees on this PC; an owner fresh-boot run is wanted before M5-H3 re-baselines. Paired A/B only.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01: each track appends its own rows; the conductor unions them (coverage.md
  conflicted at the 2036 merge: both tracks appended a paragraph at the end).
- Merge order when the sim lands: sim → view → data.
