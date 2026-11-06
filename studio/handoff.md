# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-11-05-1716** (session 3 / 8 on 2026-11-05, scheduled; base `310159f` =
`origin/main`). All three tracks **GO**: sim **M6-S7** (the map-file format, slice 1, BUG-1047 / 1048's rule first; counter 3 / 4 → 4 / 4,
so **M6-SH1 is next**), view **M6-V17** (the setup's starting-resources list; 1 / 4 → 2 / 4), data **D22** (the presets' text + the pages'
High line + BUG-1080; 1 / 4 → 2 / 4). **Open S1 / S2: none.** Golden `data-hash` **69E7B3B8E6732BC2** on `main`; **two tracks move it this
session** (sim: the map files hashed; data: the presets' text): the conductor merges sim first, then `main` into the data branch, where the
golden conflicts and is **regenerated once more on the merged tree** (`RTS_REGEN_GOLDEN=1`, `k` lines unchanged) before the data merge.
Bug ids: **sim from 1052, view from 1067, data from 1081.** PLAN-time paperwork done: BUG-1062 / 1063's Status fields now say fixed.

## Where we are

- **A 2v2 against two AIs with an AI ally is playable from the menu:** Skirmish → Add player twice → Team 1 / 1 / 2 / 2 → Start.
- **Starting resources exist in the rules, not in the setup yet:** `rules.json` `startingResources` `standard` 200 / 200 and `high`
  1000 / 1000 (`aiSpendAbove` 200); `SimConfig.StartingResources`; save format 4, replay format 8 (the recorder writes the oldest format
  that holds the match: `standard` FFA 6, `standard` team 7, else 8). `SimRunner` passes no preset yet: **M6-V17 this session.**
- **No map format yet:** every map comes from `MapGenParams` (`SimConfig.Map`, `Replay.Map`, the save header). M6 criterion 7 needs three
  hand-made maps: **M6-S7 this session** builds the format and ships one map.
- **Open S3:** BUG-1047 / 1048 (sim, fixed by M6-S7's first scope item), BUG-1065 (view, a rider in M6-V17). **S4:** BUG-1049, BUG-1051
  items 1 / 5 / 6, BUG-1066, BUG-1080.
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780,
  "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates).

## Standing rules (1-37)

Unchanged.

---

## Sim track

### Current session plan (this session)

**TASK M6-S7 · Feature (counter 3 / 4 → 4 / 4): a map-file format for M6 criterion 7's three hand-made maps, slice 1, with BUG-1047 / 1048's
rule first.** QA full. Bug ids from 1052. Files under `game/data/` this task may touch: a new `game/data/maps/` folder; `rules.json` **only
for keys other than `startingResources[*].displayName` / `description`** (the data track edits those two text keys this session; if you
touch `rules.json`, say so in the report so the conductor merges sim first and D22 rebases its two lines). Nothing in `ViewApi/**`
(read-only accessors excepted if a map id needs exposing: say so), nothing in `game/**` but `game/data/maps/**`.

**Goal.** docs/05 M6 criterion 7 needs three hand-made maps (Raraku, Pale Hills, Vathar Crossing) that a save / replay rebuilds exactly;
today every map comes from the generator (seed + `MapGenParams`). Before a hand-made map can put a start's nearest forest past 64 m of its
hall, BUG-1047 / 1048's decided rule must be in (docs/01 2026-11-05 row; the bug files' Producer triage): the AI's gather reach lifts once
the scout's head ring has been seen, not when the scout dies and not after the whole-map sweep; while the reach holds, a resource kind with
no known node in reach takes the nearest known node of that kind.

**Scope (slice 1, ~800-1,200 lines changed; the format is new, so this is the first part):**
1. **BUG-1047 / 1048's rule** in `ScoutManager` / `AiPlayer.InGatherReach` / `KnowsNode` (or wherever the reach is decided): (a) the reach
   lifts when the head ring is visited by any own unit's sight (not on the scout's death, not after the full sweep); (b) a kind with no node
   within reach falls back on the nearest known node of that kind. Un-skip `GatherReachQaTests.NearestForestJustPastTheReach_AtTheShippedReach_WoodByTick5000`
   and `TheScoutDiesBeforeTheSweepEnds_TheSeatStillGathersWood_By4000`; both green. Re-check `SharedVisionFfaPinTests` (5), QA's
   `StandardPresetPinQaTests` (20) and the 27 pairing runs: the rule fires only where the reach hid every node of a kind or the scout died
   early, so the pinned scenes should not move; **if any does, name the scene and why in the report** (the Producer decides whether the move
   is the rule working).
2. **A map file**: `game/data/maps/<id>.json` (the dev proposes the exact shape; docs/03 records it): `id` (snake_case), `displayName`,
   `description`, `players` (2-4), `size` (128 or 256), the terrain (a text heightmap of levels 0-2 with ramps, **or** a generator seed +
   `MapGenParams` + a list of hand edits, whichever keeps the file readable and the loader small), start locations (one per player; the
   generator's contract kept: on one level, at least half a map apart, each with 2 mines and a forest in reach), resource nodes (mines
   with gold, forests / trees). Loaded by `DataLoader` into `GameData.Maps` (file order; ids unique; ≥ 8 refusal rows in
   `DataValidationTests`: bad size, a start off the map or on a cliff, starts too close, a start without 2 mines or a forest, an unknown
   level, a duplicate id, a missing `displayName`), **every field in `ContentHash`** (golden regen once in the same commit, `k` lines unchanged).
3. **Selection by id:** `SimConfig.MapId` (`""` = generated, the default; `Validate` refuses an unknown id); `World` builds the terrain,
   nav grid, start locations and resource nodes from the file when set, otherwise from `MapGenerator` as today. **Save format 5 / replay
   format 9** carry the id (the save: a string **after** the preset string, so `ViewApi/SaveSummary.TryRead` keeps reading formats 3-4
   unchanged and the view can extend it; the replay: a `map <id>` line after `starting-resources`), **behind the same oldest-format rule**:
   a generated-map match writes today's formats (4 / 8, or 6 / 7 by the oldest-format rule) **byte for byte**; formats 3-4 / 3-8 still load
   (as generated). Rule 36: no new save / replay signature.
4. **One hand-made map shipped as the proof:** a small **Raraku** (2 players, 128, desert: open centre, few forests, long sightlines; docs/02
   "Map and terrain" table); `displayName` "Raraku", a one-line `description` (placeholders are fine: the data track writes the final text
   once the format lands). Pale Hills and Vathar Crossing are slice 2. `AiScenes` (or the 20-minute AI-vs-AI harness) runs a Normal v Normal
   match on it once to the end without errors; the report prints the result and the match length.
5. **Docs:** docs/03 "Implementation (M6-S7)" (the file shape, the loader, `MapId`, the format bytes with the save header's offsets, the
   oldest-format rule extended), the save / replay format notes; docs/02 "Map and terrain" gains two sentences (hand-made maps are files
   under `game/data/maps/`, selected by id, hashed with the rest of the data); a coverage row; the bug index rows (1047 / 1048 fixed, new ones).
6. **OUT:** the setup's map list (view, next session once this lands), mirrored-terrain tooling, Pale Hills / Vathar Crossing, biome art,
   per-map AI openings, `rules.json` numbers, the data track's text keys.

**Acceptance criteria:**
1. `dotnet build RtsGame.sln` 0 warnings; non-Perf suite green; Perf alone green (a Perf failure counts only when it fails again alone).
2. `GatherReachQaTests`' two BUG-1047 / 1048 rows un-skipped and green; `SharedVisionFfaPinTests` 5 / 5, `StandardPresetPinQaTests`
   20 / 20 and the 27 pairing runs unmoved, **or** every moved cell named with its cause in the report.
3. A map file loads into `GameData.Maps`, is validated (≥ 8 refusal rows, each one error) and hashed (`DataContentHashTests`: every field
   and their order move the hash); the golden regenerated once, `k` lines unchanged.
4. A match on the shipped map is deterministic (two processes, equal per-tick hashes over ≥ 3,000 ticks), saves and loads hash-equal
   (format 5), records and plays back to every checkpoint (format 9); a generated-map match still writes formats 4 / 8 (or 6 / 7 by the
   oldest-format rule) **byte for byte** (a test diffs the bytes against a fixture recorded before this change).
5. `ViewApi/SaveSummary.TryRead` reads a format-5 header without a code change (or with a read-only extension named in the report); the
   new field's offset is written in docs/03.
6. Docs per scope item 5; the coverage row; the bug index; BUG-1047 / 1048's files set to fixed with the test names.
7. `git diff --stat -- sim/Rts.Sim/ViewApi game` shows nothing but `game/data/maps/**` (and a named `SaveSummary` read-only extension).

**Design references:** docs/05 M6 criterion 7; docs/02 "Map and terrain" (size, levels, ramps, the mines-per-start rule, the shipped-maps
table); docs/02 "AI opponent" (the reach rule's intent); docs/03 "Implementation (M5-4a)" (start locations), "Save format (M6-S2)",
"Implementation (M6-S6)" (the oldest-format rule, the preset string's place), "Implementation (M6-S5)" (the reach). docs/01 2026-11-05
(the BUG-1047 / 1048 decision).

**Tests required:** the two un-skipped QA rows; `DataValidationTests` map rows; `DataContentHashTests` map rows; a `MapFileTests` (load,
build, determinism twin, start contract); `SaveFormatTests` format 5 + format 4 byte-equality; `ReplayFormatTests` format 9 + format 8 / 7 / 6
byte-equality; an `AiScenes` row on the shipped map.

**Constraints:** rule 36 (no new signature); `SaveSummary`'s shape; no per-tick allocation (the map build runs once at `World` creation);
no `System.Random`, no Dictionary iteration in the loader's output order (file order, arrays); the golden regen in the same commit as the
hash change; player-facing text (`displayName` / `description`) only in the map file.

### Next task candidates (in order)

1. **M6-SH1 (sim hardening at 4 / 4, next session):** BUG-1049, BUG-1051 items 5-6 (the stress row's 10:00 assertion → print only;
   `SpendDownQueue` → `ai.spendDownQueue`), BUG-1044's rest, BUG-0960's throw, the movement S3s (BUG-0911 / 0889 / 0144), BUG-1022 / 1026
   notes, any S3 / S4 M6-S7 leaves.
2. Map format slice 2 (Pale Hills, Vathar Crossing, mirrored-terrain helper); M5-4c slice 2; M5-4d; BUG-1010's lever; D14 c; M5-5.

### Watch-outs (sim)

- `ViewApi/SaveSummary.TryRead` stops after the team bytes; M6-S6's preset string follows them; the map id goes **after** it.
- `GatherWedgeQaTests.SameGameDataHashes` moves with every data field; `ReplayGoldenTests` regen with `RTS_REGEN_GOLDEN=1`.
- `StandardPresetPinQaTests` (20 digests) and `SharedVisionFfaPinTests` (5) pin `standard` play to 5368cfe: the reach rule can move a scene
  only where a kind had no node in reach or the scout died early; say which.
- `Stress/StartingResourcesAiStressTests` asserts the noisy 10:00 measure (BUG-1051 item 5): if it trips, check the mean-bank column first.
- `VisionGatePerfTests` fresh-process row 3.66-3.69 ms against 4 ms; `PropsMeasureTests.MinimapRefresh_*` 0.34 ms: both flake beside
  another test host (rerun alone).

---

## View track

### Current session plan (this session)

**TASK M6-V17 · Feature (counter 1 / 4 → 2 / 4): the setup's starting-resources list (Request 43's view half) + riders BUG-1065 / 1066.**
QA standard. Bug ids from 1067. Files under `game/data/` this task may touch: `game/data/common/ui.json` only (the data track edits
`rules.json` text; the sim adds `game/data/maps/`).

**Goal.** docs/02 "Game flow" 2: the skirmish setup offers starting resources, Standard / High. M6-S6 put the presets in the rules; the
setup, the command line, the match start, playback and the Load Game list must carry the pick, or a High recording cannot be watched.

**Scope:**
1. A **Starting resources** list in the setup (a row beside Game speed, the M6-V15 pattern; items = `world.Data.Rules.StartingResources`
   in file order, `DisplayName` shown, default `standard`; the tooltip or a line under it shows `Description`); `MatchArgs` adds
   `--resources <id>` only when not `standard`; `LaunchOptions` parses it (an unknown id warned in the log and ignored → `standard`).
2. `SimRunner`'s match start builds its `SimConfig` with `StartingResources`; **`SimRunner.StartPlayback` passes `replay.StartingResources`**
   (without it a High recording stops at its first checkpoint); `StartLoaded` already takes it from the header.
3. Load Game rows name a non-standard preset (a `ui.json` key, e.g. `load.preset` "{0}, High" using the preset's `DisplayName`);
   `ReplayStore.TryReadHeader` already accepts format 8; `SaveSummary` reads format 4 unchanged; fix its stale FormatVersion doc line
   (BUG-1051 item 1, `ViewApi/SaveSummary.cs`, a comment only).
4. The match log's "Match started" line names the preset.
5. **Riders:** BUG-1065 (S3: count the "opened with" lines in the graph's shrink so a teamed four-seat menu match fits 1152 x 648; flip
   `QaM6V16Test.MenuFourLabelStats`' KNOWN row to a plain check; correct docs/03 "Implementation (M6-V16)"'s "still fit 1152 x 648"
   sentence to say what fits now and how), BUG-1066 (S4: a `--team` all-one-team start logs a warning and falls back to a free-for-all,
   or refuses with the same `setup.one_team` reason; the one-team line's room reserved so Back / Start don't slide).
6. **OUT:** `sim/**` outside `ViewApi/` (the `SaveSummary` comment and, if needed, a read-only accessor), per-slot presets, new presets
   (data), team colours, the map list (next session, after M6-S7 lands), `rules.json`.

**Acceptance criteria:**
1. Build 0 warnings; the whole scene loop green **on the merged tree** (the view branch after `main` is merged in); smoke PASS.
2. The list shows the presets' `displayName` from the data (no C# literal; a test asserts the item texts equal the data's), default
   Standard; `--resources high` → `World.Gold[p] == 1000 && World.Wood[p] == 1000` for every seat at tick 0 (a scene row).
3. A High match recorded from the menu is format 8 and **Watch** plays it to every checkpoint (a scene row); a High match saved and loaded
   resumes High (a scene row: the bank after load equals the bank before save).
4. Load Game lists a High save and a High recording with the preset in the row; a Standard file reads as before (a scene row).
5. Each rider has a scene row that fails without its fix (BUG-1065: the six menu-started four-seat layouts fit 1152 x 648; BUG-1066: the
   `--team 1 1 --team 2 1` start logs / refuses and the Back / Start positions are equal with and without the one-team line).
6. `git diff --stat -- sim/` = `ViewApi/` comment line (and a named read-only accessor at most); every new string in `ui.json` (the
   `ShellTest` key count moves); docs/03 "Implementation (M6-V17)" + the corrected M6-V16 sentence; docs/02 "Game flow" 2's "the setup
   screen's list is the view's next task" → built in M6-V17.

**Design references:** docs/02 "Game flow" 2; docs/03 "Implementation (M6-S6)" ("Requests for the view"), "Implementation (M6-V15)" (the
Game speed list pattern), "Implementation (M6-V16)". **Tests required:** `game/tests/StartingResourcesTest.tscn` (or rows in `ShellTest` /
`QaM6V15Test`'s pattern), `ReplayPlaybackTest` / `SaveLoadTest` High rows, the rider rows; the loop. **Constraints:** player-facing text from
data only; views hold no gameplay state; the sim's preset is read through `World.Data.Rules`, never copied.

### Next task candidates

1. **M6-V18: the setup's Map list** (generated + `GameData.Maps` by `displayName`; `--map <id>`; `SimRunner` passes `MapId`; `SaveSummary`
   reads format 5's map id for the Load Game list) once M6-S7 lands this session.
2. M6-VH5 (view hardening at 4 / 4, or earlier when nothing else is unblocked); (e) `tools/export.ps1` once the export templates are
   downloaded; (f) criterion 6's music once a track is dropped in; the sound re-cuts when the owner listens.

### Watch-outs (view)

- **Run the scene loop on the merged tree** before claiming green: the 0805 lesson.
- `ViewApi/SaveSummary` reads the header by shape: the preset string sits after the team bytes; **the sim adds a map id string after the
  preset string this session (format 5)**: don't assume the preset string ends the header.
- A High recording watched through `StartPlayback` without the preset mismatches at checkpoint 1: the symptom of scope item 2 missing.
- `scene-loop.ps1 -Filter` is a file glob; the loop is 76 scenes, ~23 min; `PropsMeasureTests.MinimapRefresh_*` (Perf) flakes beside
  another test host.

---

## Data track

### Current session plan (this session)

**TASK D22 · Feature (counter 1 / 4 → 2 / 4): the starting-resources text + the pages' High line + BUG-1080.** QA light. Bug ids from 1081.
Files this task may touch: `game/data/common/rules.json` **`startingResources[*].displayName` / `description` only** (a named exception to
the sim's file: text keys, nothing numeric; the sim's M6-S7 edits other keys if any), `docs/factions/malazan.md`, `docs/factions/whirlwind.md`,
`sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, the golden (regen).

**Goal.** M6-S6 shipped placeholder text for the two presets ("Standard" / "Every player starts with the usual gold and wood.", "High" /
"Every player starts with five times the usual gold and wood."); the player reads these in the setup's list (M6-V17, the same session).
The faction pages say nothing about what the AI does at High.

**Scope:** (1) final `displayName` / `description` for both presets (plain, ≤ 200 chars, no Malazan proper nouns: a preset is generic; the
setup's list has ~240 px for the name; wording that survives a retune of the numbers, e.g. "a large stockpile of gold and wood" rather than
"five times"); a review table for the owner (old → new, quoted, one-line why); (2) one line per faction page under the AI section: at High
the AI plays its usual openings and, while it holds more than its spend-down threshold, brings its next timed step forward, trains at every
producer and leans its gatherers toward the scarcer kind (docs/02 "Game flow" 2, docs/03 "Implementation (M6-S6)"); quote the measured
numbers (`cadre_rush` 5:00 bank 70-466 at High against 1,539-1,769 before the rule, from `studio/qa/2026-11-05-1206-M6-S6.md`) as a
report line, not a pinned cell unless a test prints it; (3) BUG-1080's two wordings; (4) a `Content/` test pinning the two texts (so a sim
edit of `rules.json` can't silently change them) and that each description names no number but the preset's own; (5) golden regen once
(text is hashed; the `k` lines must not move); (6) **no number changes.**

**Acceptance criteria:** (1) build / tests green, `DataValidationTests` green; (2) the two texts final and quoted in the review table;
(3) both pages' High line, BUG-1080's wordings; (4) the pin test; (5) `git diff -- game/data/common/rules.json` shows text lines only; the
golden's `k` lines unchanged; (6) `AiOpeningPairsReportTests` / `D21OpeningsQaTests` unmoved.

**Design references:** docs/02 "Game flow" 2, "Economy" table; docs/03 "Implementation (M6-S6)"; docs/01 2026-11-05 row (the numbers are
a Producer default the owner may revisit, so the text must not promise them as final).

### Next task candidates

1. The owner's answers (D21 names / descriptions / the Malazan standard wave, D19's table, the Shock pass, `goldShare`, BUG-0780).
2. **The hand-made maps' `displayName` / `description`** (Raraku this session's placeholder; Pale Hills / Vathar Crossing with slice 2);
   D14 c; M7's rosters.

### Watch-outs (data)

- **The golden moves twice this session** (sim: the map files; data: the text). The conductor merges sim first, then `main` into the data
  branch: the golden conflicts; regenerate it once more on the merged tree (`RTS_REGEN_GOLDEN=1`), check the `k` lines, commit, then merge.
- `rules.json` is the sim's file: this task is a named text-only exception; if M6-S7 also touches `rules.json`, D22 rebases its two lines.
- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} print "take it out (BUG-1010)" when
  their case passes: take them out then.
- Every "Age II against ..." cell on both pages is pinned by `AiOpeningPairsReportTests.AssertAgeTwoCells`; High changes none of them.

---

## Watch-outs (all tracks)

- Three tracks a session: three test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- **The owner's recordings and saves made before 2026-11-05-1206 refuse to play** (the data hash moved with the presets); **they move again
  this session** (maps, text).
- Two tracks touch `rules.json` this session at most (sim: maybe, other keys; data: two text keys) and two move the golden (sim, data). The
  conductor merges sim → main, then main → data (regen), then data; view carries no golden change.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase and commits each dev's work at every phase
  boundary (`sessions/2026-10-27-0658-incident.md`).
