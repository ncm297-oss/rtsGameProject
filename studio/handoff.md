# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-05-1206** (session 2 / 8 on 2026-11-05). **Sim M6-S6 ACCEPT (1 fix round:
BUG-1050 S2 fixed, criterion 5 restated as the mean-bank measure); view M6-V16 resume ACCEPT (BUG-1064 fixed, the held branch lands,
M6 criterion 1 ticked); data STOP (cheap).** Counters after: **sim 3 / 4, view 1 / 4, data 1 / 4.** **Open S1 / S2: none.** Golden
`data-hash` **69E7B3B8E6732BC2** (moved twice this session: the `startingResources` presets, then `high`'s `aiSpendAbove`; `k` lines
identical). **M5 4 / 9, M6 4 / 10.** Bug ids next: **sim from 1052, view from 1067, data from 1081.** The conductor merges sim to `main`
first, then `main` into the view branch and the view to `main`: the view's studio files (BUG-1065 / 1066, its QA report, its coverage row,
its index rows) land with it; the Producer's index rows for 1061-1064 (written in the sim worktree) win any conflict.

## Where we are

- **A 2v2 against two AIs with an AI ally is playable from the menu:** Skirmish → Add player twice → Team 1 / 1 / 2 / 2 → Start. Allies
  share vision, never attack each other, the banner speaks for your side, the stats group by team; every slot on one team greys Start out.
- **Starting resources exist in the rules, not in the setup yet:** `rules.json` `startingResources` `standard` 200 / 200 (bit for bit
  today's start) and `high` 1000 / 1000 (`aiSpendAbove` 200: the AI spends a `high` start down); `SimConfig.StartingResources`;
  **save format 4** (the preset string after the team bytes; format 3 loads as `standard`), **replay format 8** (`starting-resources`
  line after `combat`; the recorder writes the oldest format that holds the match: `standard` FFA 6, `standard` team 7, else 8).
  `SimRunner` passes no preset yet (default `standard`): the view's M6-V17 sets it at match start **and in `StartPlayback`**.
- **Open S3:** BUG-1047 / 1048 (sim: the gather reach's two blind spots, one decided rule, before the map-file format), **BUG-1065** (view:
  a teamed four-seat match from the menu overflows the stats screen by ~22 px: the three AI "opened with" lines; BUG-1063's graph shrink
  covered human-seat screens only). **S4:** BUG-1049, BUG-1051 items 1 / 5 / 6 (sim nits; item 1 is the view's `SaveSummary` doc line),
  BUG-1066 (view: `--team` one-team starts silently; Back / Start slide), BUG-1080 (data wordings).
- **Paperwork for the next PLAN's drift check:** BUG-1062 / 1063's files (landed with the view branch) still say "open" in their Status
  fields (fixed / fixed as filed → 1065); docs/03 "Implementation (M6-V16)" says four label rows "still fit 1152 x 648" (human seats
  only; fix with BUG-1065).
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780,
  "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates). **The owner's recordings and saves from
  before this session refuse to play** (the data hash moved).

## Standing rules (1-37)

Unchanged. Rule 31 held without a re-print this session: the 27 pairing runs print identical on 5368cfe and on head (`standard` has no
`aiSpendAbove`, so the spend-down rule cannot fire there).

---

## Sim track

### Current session plan (next session)

**Feature (counter 3 / 4 → 4 / 4): a map-file format for M6 criterion 7's three hand-made maps, with BUG-1047 / 1048's rule first.**
QA full. Bug ids from 1052. Files under `game/data/` this task may touch: a new `game/data/maps/` folder and `rules.json` only if a knob
is needed (the data track's D22 edits `rules.json` `startingResources[*].displayName` / `description` **text only** the same session:
if the sim must touch `rules.json`, it edits other keys and says so in its report, or the Producer moves D22).

**Goal.** docs/05 M6 criterion 7 needs three hand-made maps (Raraku, Pale Hills, Vathar Crossing) that a save / replay rebuilds exactly;
today every map comes from the generator (seed + `MapGenParams`). Before a hand-made map can put the nearest forest past 64 m of a hall,
BUG-1047 / 1048's decided rule goes in (docs/01 2026-11-05-0805: the reach lifts once the head ring is seen, not when the scout dies; a kind
with no node in reach takes the nearest known one).

**Scope (slice 1, ~800-1,200 lines; the design is new, so plan the first part):**
1. BUG-1047 / 1048's rule in `ScoutManager` / `AiPlayer.InGatherReach` / `KnowsNode`; the two skipped `GatherReachQaTests` rows un-skipped;
   the soak and the FFA pins re-checked (the rule fires only where the reach hid every node: the five pinned FFA scenes must not move, or
   the brief says which scene moves and why).
2. A **map file** (`game/data/maps/<id>.json` or a text heightmap + a placement list: the dev proposes, docs/03 records): id, `displayName`,
   `description`, size, the heightmap (or its generator seed + hand edits), start locations (2-4), resource nodes; loaded by `DataLoader`
   into `GameData.Maps` (validated: sizes, starts apart and on one level, every start with 2 mines + a forest in reach as `MapGenerator`
   guarantees), **hashed** into `ContentHash` (golden regen once).
3. `SimConfig.Map` stays `MapGenParams`; a hand-made map is selected by id (`SimConfig.MapId`, "" = generated) and both headers carry it
   (save format 5 / replay format 9 behind the same oldest-format rule: a generated map writes today's formats byte for byte). `World`
   builds from the file instead of the generator when set. Rule 36: no new save / replay signature.
4. **One** hand-made map shipped as a proof (a small Raraku), the other two in a later slice; `AiScenes` run on it once.
5. Docs: docs/03 "Implementation (M6-S7)" + the formats; docs/02 "Maps" if a section is missing; coverage row; the index.
6. **OUT:** the setup's map list (view), mirrored-terrain tooling, the other two maps, biome art.

**Acceptance criteria:** (1) build 0 warnings, non-Perf green, Perf alone green; (2) `GatherReachQaTests`' two BUG-1047 / 1048 rows
un-skipped and green; the FFA pins and the 27 pairing runs unmoved (or the moved cells named); (3) a map file loads, validates (≥ 8 refusal
rows), hashes; (4) a match on it is deterministic (twin), saves / replays round-trip (format 5 / 9), a generated-map match still writes
formats 4 / 8 (or 6 / 7 / 8 by the oldest-format rule) byte for byte; (5) `SaveSummary` reads a format-5 header unchanged (the new
field after the preset string; name its offset in docs/03); (6) docs + index; (7) nothing in `ViewApi/**` / `game/**` but `game/data/maps/**`.

**Design references:** docs/05 M6 criterion 7, docs/02 "Maps" / "Game flow" 2, docs/03 "Implementation (M5-4a)" (start locations),
"Save format (M6-S2)", "Implementation (M6-S6)" (the oldest-format rule). **Constraints:** rule 36; `SaveSummary`'s shape; no per-tick
allocation; the golden regen in the same commit as the hash change.

### Next task candidates (in order)

1. **M6-SH1 (sim hardening at 4 / 4):** BUG-1049, BUG-1051 items 5-6 (the stress row's 10:00 assertion → print only; `SpendDownQueue` →
   `ai.spendDownQueue`), BUG-1044's rest, BUG-0960's throw, the movement S3s (BUG-0911 / 0889 / 0144), BUG-1022 / 1026 notes.
2. The other two hand-made maps; M5-4c slice 2; M5-4d; BUG-1010's lever; D14 c; M5-5.

### Watch-outs (sim)

- `ViewApi/SaveSummary.TryRead` stops after the team bytes; M6-S6's preset string follows them; a new header field goes **after** it.
- `GatherWedgeQaTests.SameGameDataHashes` moves with every `rules.json` / data field; `ReplayGoldenTests` regen with `RTS_REGEN_GOLDEN=1`.
- `StandardPresetPinQaTests` (20 digests) and `SharedVisionFfaPinTests` (5) pin `standard` play to 5368cfe: any AI or movement change
  moves them on purpose or not; say which.
- `Stress/StartingResourcesAiStressTests` asserts the noisy 10:00 measure on nine tuned pairings (BUG-1051 item 5): if it trips after an
  AI change, check the mean-bank column before suspecting the change.
- `VisionGatePerfTests` fresh-process row 3.66-3.69 ms against 4 ms; `PropsMeasureTests.MinimapRefresh_*` 0.34 ms: both flake beside
  another test host (rerun alone).

---

## View track

### Current session plan (next session)

**Feature (counter 1 / 4 → 2 / 4): M6-V17, the setup's starting-resources list (Request 43's view half).** QA standard. Bug ids from
1067. Files under `game/data/` this task may touch: `game/data/common/ui.json` only (the data track's D22 edits `rules.json` text).

**Goal.** docs/02 "Game flow" 2: the skirmish setup offers starting resources, Standard / High. M6-S6 put the presets in the rules;
the setup, the command line, the match start, playback and the Load Game list must carry the pick.

**Scope:**
1. A **Starting resources** list in the setup (a row beside Game speed; items = `world.Data.Rules.StartingResources` in file order,
   `DisplayName` shown, default `standard`; the tooltip or a line under it = `Description`); `MatchArgs` adds `--resources <id>` only when
   not `standard`; `LaunchOptions` parses it (an unknown id warned and ignored → `standard`).
2. `SimRunner`'s match start builds its `SimConfig` with `StartingResources`; **`SimRunner.StartPlayback` passes `replay.StartingResources`**
   (without it a High recording stops at its first checkpoint); `StartLoaded` already gets it from the header.
3. Load Game rows name a non-standard preset (a `ui.json` key, e.g. `load.preset` "{0}, High"); `ReplayStore.TryReadHeader` already
   accepts format 8 (it scans for `ticks`); `SaveSummary` reads format 4 unchanged; fix its FormatVersion doc line (BUG-1051 item 1).
4. The match log's "Match started" line names the preset.
5. **Riders only if a few lines:** BUG-1065 (count the opened-with lines in the graph's shrink; flip `MenuFourLabelStats`' KNOWN row;
   correct docs/03's "still fit" sentence), BUG-1066 (a `--team` one-team warning; reserve the one-team line's room).
6. **OUT:** `sim/**` outside `ViewApi/` read-only accessors (none expected), per-slot presets, new presets (data), team colours.

**Acceptance criteria:** (1) build 0 warnings, the whole scene loop green on the merged tree, smoke PASS; (2) the list shows the presets'
`displayName` from the data (no C# literal), default Standard, `--resources high` → `World.Gold[p] == 1000` for every seat at tick 0
(a scene row); (3) a High match recorded from the menu is format 8 and **Watch** plays it to every checkpoint; a High match saved and
loaded resumes High (a scene row); (4) Load Game lists a High save and recording with the preset; a Standard file as before; (5) riders
each with a scene row that fails without the fix; (6) `git diff --stat -- sim/` = `ViewApi/` doc line at most; every new string in `ui.json`
(`ShellTest` key count moves).

**Design references:** docs/02 "Game flow" 2, docs/03 "Implementation (M6-S6)" ("Requests for the view"), "Implementation (M6-V15)" (the
Game speed list pattern), "Implementation (M6-V16)". **Tests required:** a `game/tests/StartingResourcesTest.tscn` (or rows in
`ShellTest` / `QaM6V15Test`'s pattern), `ReplayPlaybackTest` / `SaveLoadTest` High rows; the loop.

### Next task candidates

1. M6-VH5 (the view hardening; may be pulled forward): BUG-1065 / 1066 if not taken as riders, docs/03's "still fit" sentence.
2. (e) `tools/export.ps1` once the export templates are downloaded; (f) criterion 6's music once a track is dropped in; the map list once
   a map format exists (sim next session); the sound re-cuts when the owner listens.

### Watch-outs (view)

- **Run the scene loop on the merged tree** (the view branch after `main` is merged in) before claiming green: the 0805 lesson.
- `ViewApi/SaveSummary` reads the header by shape: the preset string sits after the team bytes (tick offset + 4 + players); a map id
  will follow it next sim session.
- A High recording watched through `StartPlayback` without the preset mismatches at checkpoint 1: the symptom of scope item 2 missing.
- `scene-loop.ps1 -Filter` is a file glob; the loop is 76 scenes, ~23 min; `PropsMeasureTests.MinimapRefresh_*` (Perf) flakes beside
  another test host.

---

## Data track

### Current session plan (next session)

**Feature (counter 1 / 4 → 2 / 4): D22, the starting-resources text + the pages' High line + BUG-1080.** QA light. Bug ids from 1081.
Files this task may touch: `game/data/common/rules.json` **`startingResources[*].displayName` / `description` only** (a named exception
to the sim's file: text keys, nothing numeric; the sim's next task edits other keys if any and says so), `docs/factions/malazan.md`,
`docs/factions/whirlwind.md`, `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`.

**Goal.** M6-S6 shipped placeholder text for the two presets ("Standard" / "Every player starts with the usual gold and wood.", "High" /
"Every player starts with five times the usual gold and wood."); the player reads these in the setup's list (M6-V17, the same session).
The faction pages say nothing about what the AI does at High.

**Scope:** (1) final `displayName` / `description` for both presets (plain, ≤ 200 chars, no Malazan proper nouns: a preset is generic;
the setup's list has ~240 px for the name); a review table for the owner (old → new, quoted); (2) one line per faction page under the
AI section: at High the AI plays its usual openings and, while it holds more than 200 gold + wood, brings its next timed step forward,
trains at every producer and leans its gatherers (docs/02 "Game flow" 2, docs/03 "Implementation (M6-S6)"); quote the measured numbers
(`cadre_rush` 5:00 bank 70-466 at High against 1,539-1,769 before the rule, from `studio/qa/2026-11-05-1206-M6-S6.md`) as a report line,
not a pinned cell unless a test prints it; (3) BUG-1080's two wordings; (4) a `Content/` test pinning the two texts (so a sim edit of
`rules.json` can't silently change them) and that each description names no number other than the preset's own; (5) golden regen once
(text is hashed; the `k` lines must not move); (6) **no number changes.**

**Acceptance criteria:** (1) build / tests green, `DataValidationTests` green; (2) the two texts final and quoted in the review table;
(3) both pages' High line, BUG-1080's wordings; (4) the pin test; (5) `git diff -- game/data/common/rules.json` shows text lines only;
the golden's `k` lines unchanged; (6) `AiOpeningPairsReportTests` / `D21OpeningsQaTests` unmoved.

**Design references:** docs/02 "Game flow" 2, "Economy" table; docs/03 "Implementation (M6-S6)"; docs/01 2026-11-05 row (the numbers
are a Producer default the owner may revisit, so the text must not promise them as final: "five times" is fine if `high` stays 1000;
prefer wording that survives a retune, e.g. "a large stockpile of gold and wood").

### Next task candidates

1. The owner's answers (D21 names / descriptions / the Malazan standard wave, D19's table, the Shock pass, `goldShare`, BUG-0780).
2. The hand-made maps' `displayName` / `description` once the map format lands; D14 c; M7's rosters.

### Watch-outs (data)

- `rules.json` is the sim's file: this task is a named text-only exception; if the sim's map-format task also touches `rules.json`, the
  conductor merges sim first and D22 rebases its two lines.
- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} print "take it out (BUG-1010)" when
  their case passes: take them out then.
- Every "Age II against ..." cell on both pages is pinned by `AiOpeningPairsReportTests.AssertAgeTwoCells`; High changes none of them
  (the pairings run at `standard`).

---

## Watch-outs (all tracks)

- Three tracks a session now: three test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- **The owner's recordings and saves made before 2026-11-05-1206 refuse to play** (the data hash moved with the presets).
- Two tracks may touch `rules.json` next session (sim: maybe; data: text only). The conductor merges sim first; the Producer named the
  keys in each brief.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase and commits each dev's work at every phase
  boundary (`sessions/2026-10-27-0658-incident.md`).
