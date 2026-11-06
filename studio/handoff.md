# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-11-05-2148** (session 4 / 8 on 2026-11-05, scheduled; base `8f7477d`). Previous
session (1716): sim **M6-S7** and view **M6-V17** on `main`; data **D22** accepted but **still on its branch** (`origin/studio/2026-11-05-1716-data`,
d063028): the conductor could not run the golden merge-fix, so this session's data task starts with it. **Open S1 / S2: none.** Golden
`data-hash` on `main` **30C51BB619A7E650**. Bug ids next: **sim from 1055, view from 1067, data from 1082.**

## Where we are

- **Playable from the menu:** a 1v1 to 2v2 against AIs, Standard or High starting resources, teams, game speed, the generator's forests /
  mines; save / load / Watch; the stats screen fits every menu layout.
- **Hand-made maps have a format** (`game/data/maps/<file>.json`, docs/03 "Implementation (M6-S7)") and one map, **Raraku** (2p, 128,
  placeholder layout and text). **Nothing in the setup picks it yet: M6-V18 this session.**
- **The AI's gather reach** lifts once its scout's head ring is seen (or the sweep ends alive); a scout lost before that is replaced.
  **Open S3: BUG-1052** (unbounded replacements when every scout dies at the door), **BUG-1053** (no re-scout after the ring when no forest
  is known): M6-SH1's first product items this session.
- **A process cost to remove this session:** every data change (a knob, a text) moves the data hash, and three tests pin the golden
  replay's header to the shipped hash (`ReplayGoldenTests` via `ReplayPlayer`'s mismatch refusal, `QA/GatherWedgeQaTests.SameGameDataHashes`,
  `QA/Content/DescriptionWordingQaTests.TheShippedContentHash_IsTheGoldenReplaysHeader`). Two tracks moving the hash in one session always
  conflict on the golden (1716: the D22 merge-fix could not be run). M6-SH1 item (a) decouples them: the `k` lines stay the proof.
- **Open S3:** BUG-1052, BUG-1053 (sim), BUG-1010 (sim-side). **S4:** BUG-1049, BUG-1051 items 5 / 6, BUG-1054 item 3, BUG-1044's rest,
  BUG-0960's throw, BUG-1081 (data).
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 and D22 review tables, the Shock pass, `goldShare`,
  BUG-0780, "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates).

## Integration of this session (for the conductor; the next PLAN checks it)

Merge order **sim → view → data**, as usual. Expected conflicts and their rule:

1. **Sim** (`studio/2026-11-05-2148-sim`): adds `rules.json` `ai.scoutReplacements` (and maybe `ai.spendDownQueue`); **does not regenerate
   the golden** (its item (a) makes the golden's `data-hash` line informational). Deletes one test in the data track's folder by named
   exception: `QA/Content/DescriptionWordingQaTests.TheShippedContentHash_IsTheGoldenReplaysHeader`.
2. **View** (`studio/2026-11-05-2148-view`): no `sim/` files but `ViewApi/SaveSummary.cs` (read-only map id) and QA's test files; `ui.json`
   keys. No data files. Should merge clean.
3. **Data** (`studio/2026-11-05-2148-data`): has merged `origin/studio/2026-11-05-1716-data` (D22) into itself, edits `maps/raraku.json`'s
   two text keys, **regenerates the golden once on its branch** and moves `GatherWedgeQaTests.SameGameDataHashes`. At the merge into
   `main`: `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` should not conflict (the sim did not touch it: take the data's);
   **`sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` conflicts if the sim landed (the sim removed the literal list): take `main`'s (the sim's)
   version.** If the sim was rejected, nothing conflicts and the data's versions land as in 1716's plan. `studio/qa/coverage.md`: keep
   every track's appended row.
4. After the merges: `tools/qa/smoke.ps1` on `main`; `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReplayGoldenTests|FullyQualifiedName~GatherWedgeQaTests|FullyQualifiedName~DescriptionWordingQaTests|FullyQualifiedName~DataValidationTests"`
   on `main` must be green whatever subset of tracks landed (the Producer checks this at ACCEPT; the matrix is in the sim brief).

## Standing rules (1-37)

Unchanged.

---

## Sim track

### Current session plan

**M6-SH1 — the sim's hardening (counter 4 / 4). Type `hardening`, QA `full`.**

Goal: pay the sim's debt before M6's second map slice: remove the golden-replay / data-hash coupling that makes every two-track data
change a merge incident, fix the two S3 edge cases of the M6-S7 reach rule (BUG-1052 / 1053), and clear the S4 nits the logs flagged
(BUG-1051 items 5-6, BUG-1054 item 3, BUG-1049, BUG-1044 item 3, BUG-0960's last throw) as the size budget allows. No new feature.

Scope, in this order (stop when the 1,500-line budget is near; everything from (e) on is "as it fits"):

(a) **Decouple the golden tests from the data hash** (test files, docs/03 "Testing strategy" and the M6-S7 golden paragraph, nothing in
    `Rts.Sim`): `ReplayGoldenTests.CrossMapSeed1_ReproducesEveryCheckpoint` plays the file with its header's data hash substituted by the
    shipped one (`ReplayTestRun.With(replay, dataHash: TestSim.Data.ContentHash())`), so the `k` lines are the pin and a data change that
    does not move them needs no regeneration; the file's `data-hash` line becomes "the hash at recording time" (documented in the test
    summary and docs/03). `QA/GatherWedgeQaTests` drops `SameGameDataHashes` and the `RecordedDataHash` equality (its checkpoint check,
    `CheckpointPrefixTicks`, is the proof it already names; keep one comment line of history pointing at git). **Named exception, data
    folder:** delete `QA/Content/DescriptionWordingQaTests.TheShippedContentHash_IsTheGoldenReplaysHeader` (the data track is told not to
    touch that file this session). Grep every reader of `ReplayTestRun.GoldenPath` / `cross_map_seed1` (`ReplayFormatTests`, `Save/SaveScenes`,
    `QA/OrderQaTests`, `QA/StartingResourcesQaTests`, `QA/StandardPresetPinQaTests`) and make each one independent of the header's hash
    equalling the shipped one. **Do not regenerate the golden in this task** (the knob in (b) moves the shipped hash; the decoupled tests
    must stay green with the file's `30C51BB619A7E650` header: that is the proof the decoupling works). `RecordingTheGoldenRunTwice_*` stays.
    Keep a test that the golden is refused by `ReplayPlayer.Run` *without* the substitution (`ReplayError.DataMismatch`), so the product's
    refusal of foreign recordings is still pinned.
(b) **BUG-1052 (S3): bound the scout replacements.** New `rules.json` `ai.scoutReplacements` (integer, default **2**, validated 0-8 in
    `DataValidationTests`, hashed like every `ai.*` knob, `AiRules` field). **No new saved blackboard state and no save-format bump** (the
    `AIST` layout is unchanged since M6-S3; a byte there changes every AI save and the `PreMapFixtures` proof): derive the count from state
    the sim already saves, e.g. the own player's `UnitLosses` (M6-S1, `MatchStats`, saved) read through a new read-only `PlayerView` accessor
    of the **own** seat's losses (own stats are not hidden information; say so in the accessor's summary), compared against the knob:
    "while the own unit losses are ≥ `scoutReplacements`, a lost scout is not replaced". A derived rule that counts something else is
    fine if it is saved state and the brief's rows pass; say which. **After the cap the reach lifts as a death did in M6-S5** (the
    Producer's default; `ScoutPhase` 2). Measure in the capped rows the farthest gather target from the own hall: **if any row sends a
    gatherer more than 150 m from its hall, hold the reach instead (phase 0 with no replacement → a new phase value is new state: prefer
    phase 2 plus `ReachLifted` false only if it needs no new byte; else report and keep lifting)**, and say so in docs/02 + the report.
    Un-skip `QA/ReachRuleQaTests.EveryScoutKilledAtOnce_*` (bound ≤ 4 scouts lost by 6,000, workers ≥ 15, wood > 0 or the hold reported).
    `AnEnemyCampOnTheHeadRing_CostsTheSeatAtMostThreeScouts` stays green. **The 20 `StandardPresetPinQaTests` rows, the 5
    `SharedVisionFfaPinTests` rows and the 27 pairing reports must not move** (no scout dies in them); if one moves, name the scene and why.
    A loaded seat equals the saved one (`Ai/AiSaveTests` style row at a tick between the first and second scout death).
(c) **BUG-1053 (S3): re-scout while a kind has no known node.** When `ScoutPhase` is 2 (the reach lifted), the scout is gone and some
    resource kind has **no known node at all** (`KnowsNode` false for every node of that kind), send one more scout (phase back to 0 →
    the next think picks a worker; `Waypoints` as for a replacement), bounded by the same `scoutReplacements` rule as (b). Un-skip
    `QA/ReachRuleQaTests.ScoutKilledAfterTheRing_*` (wood > 0 by 6,000 on the three rows). A seat that knows a node of every kind never
    re-scouts (a row), so the pins above do not move.
(d) **BUG-1051 items 5-6:** `Stress/StartingResourcesAiStressTests`' 10:00 assertion becomes a printed measure (the mean-bank one stays
    the assertion, per the Producer's restated criterion); `ProductionManager.SpendDownQueue` → `rules.json` `ai.spendDownQueue` (1-5,
    default 2, validated, hashed). `GatherWedgeQaTests` no longer needs a pin move (see (a)).
(e) **BUG-1054 item 3:** one sentence in docs/02 "Map and terrain" (a save's map id names the map; the terrain comes from the save's own
    heightmap; a recording rebuilds from the id). Close the bug (items 1-2 are already done).
(f) **BUG-1049:** item 1 (the docs/03 "Free-for-all after the reach rule" parenthetical: the five pinned scenes unchanged, AI seats whose
    raiders see an enemy's nodes play differently by design); item 2 (one sentence in docs/03 on the two anchors, or one shared anchor if
    it is a few lines and no pin moves); item 4 (a test that builds a second drop-off site while the scout is out, killing the surviving
    `InGatherReach` mutant); item 3 a note in the test. Close if all four are done.
(g) **BUG-1044 item 3:** widen `TeamArchitectureTests`' scan to `Abilities/`, `Vision/`, `Ai/` (raw owner compares at enemy sites), and add
    the missing regression rows the bug lists where one is cheap (the in-loop allied re-check after a slot is freed and refilled; the
    detector skipping an ally). Item 2 is the view's (done in M6-V16); item 1 fixed in M6-S5. Close or leave the rest listed.
(h) **BUG-0960's remaining throw** (the one forged value that still throws after M5-H4; the bug file names it): refuse it `Malformed`
    in `ReadFrom`, a `SaveRefusalTests` row.
(i) If budget remains: the movement S3s **BUG-0911 / 0889 / 0144** (each needs its own analysis; take at most one and only with a failing
    row first), BUG-1022 / 1026 (tuning notes only, no number moves this session).

Out of scope: Pale Hills / Vathar Crossing, the mirrored-terrain helper, M5-4c slice 2, M5-4d, D14 c, any save / replay format bump,
`ViewApi/**` beyond nothing (the `PlayerView` accessor is `Ai/`, not `ViewApi/`), `game/**` other than `rules.json`, any number in
`ai.json`, the data track's folders other than the one named deletion.

Acceptance criteria:
1. `dotnet build RtsGame.sln` 0 warnings; `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf` green; Perf alone green (a contention
   failure counts only if it repeats alone); `tools/qa/smoke.ps1` PASS.
2. **Decoupling proof:** after (b) moves the shipped data hash, `ReplayGoldenTests`, `GatherWedgeQaTests` and every `GoldenPath` reader pass
   with the golden file **byte-identical to `main`'s** (`git diff main -- sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` empty);
   `TheShippedContentHash_IsTheGoldenReplaysHeader` is gone; a test pins that `ReplayPlayer.Run` refuses the unsubstituted golden with
   `DataMismatch`; docs/03 "Testing strategy" says regeneration is for `k`-line moves only.
3. **BUG-1052:** `ai.scoutReplacements` in `rules.json` (default 2), validated (a `DataValidationTests` row for an out-of-range value),
   hashed (`DataContentHashTests` row); `EveryScoutKilledAtOnce_*` un-skipped and green with its bound; no new field in `Blackboard.Save.cs`;
   `SaveFormat.CurrentVersion` still 5; the 20 + 5 pins and the 27 pairing reports unchanged (or each move named).
4. **BUG-1053:** `ScoutKilledAfterTheRing_*` un-skipped and green; a row proving a seat knowing a node of every kind never re-scouts.
5. **BUG-1051 items 5-6** done (the stress row prints the 10:00 measure; `ai.spendDownQueue` validated and hashed, `SpendingDown` behaviour
   unchanged at the default, pinned by the existing M6-S6 rows).
6. Every bug touched has its status line updated with the test that proves it (the dev may edit `studio/bugs/**` and the index for this
   task); docs/02 "AI opponent" states the cap and the re-scout in one or two sentences; docs/03 "Implementation (M6-S7)" gains an
   "M6-SH1" note; `studio/qa/coverage.md` one appended row.
7. `git diff --stat main...HEAD -- sim/Rts.Sim/ViewApi game` = `game/data/common/rules.json` only.

Design references: docs/02 "AI opponent" (the reach paragraph, lines ~433-440: "looks near home, then trusts what it knows"), docs/03
"Implementation (M6-S5)" + "(M6-S7)" (the reach rule, the head ring, the replacement), docs/03 "Testing strategy" (the golden paragraph
to rewrite), docs/03 "Save/load and replays" (the oldest-format rule and the `AIST` section: unchanged), CLAUDE.md rule 6 (knobs in data).

Tests required: the rows named in criteria 2-5; a save → load equality row across a scout death with the cap engaged; the
`DataValidationTests` / `DataContentHashTests` rows for both knobs; the existing pins unchanged.

Constraints most at risk: no new allocation in `ScoutManager` / `AiPlayer` think code (the losses accessor returns an int); the AI reads
the world only through `PlayerView` (the new accessor is own-seat only); no `Dictionary` iteration; `rules.json` is the only `game/`
file; the data track's folders are off limits except the one named deletion; **do not regenerate the golden**; the sim's QA folder
(`QA/*.cs` outside `QA/Content/`) is the sim's to edit.

### QA focus (sim)

Attack (b) and (c) hardest: the capped rows across seeds 1-10 (every scout killed at the door; killed at 0 / 200 / 2,000 ticks; a camp of
4 on the ring; a camp that moves), workers kept, the farthest gather target per row (the 150 m rule), a save at a tick between two scout
deaths loads equal and plays on hash-equal, the un-skipped rows' bounds not just met but with margin. Mutants: the cap off, the cap at 0,
the re-scout condition inverted, the losses read from the wrong seat. Pins: 20 standard, 5 FFA, 27 pairings byte-identical. For (a):
prove the golden is independent of the header's hash (edit the `data-hash` line to junk on a scratch copy: the decoupled test still
passes on the `k` lines; a `k`-line edit fails it), and that the product still refuses a foreign recording. Perf: `VisionGatePerfTests`
/ `PropsMeasureTests` only count when they fail alone.

### Next task candidates (after this)

1. **Map format slice 2:** Pale Hills (4p, 256, forest: dense woods with paths) and Vathar Crossing (2-4p, steppe: a river of slopes with
   fords), a mirrored-terrain helper, `AiScenes` rows on each; the data track writes their text after.
2. M5-4c slice 2 (the Hard knobs), M5-4d (expansion / rebuilding / hunting), BUG-1010's lever, D14 c, M5-5.

### Watch-outs (sim)

- `ScoutManager.Sweep`'s death branch: `board.ScoutPhase = ai.ReachLifted() ? 2 : 0`. `ReachLifted` is cached per think (`_reachLifted`).
- `Replays/premap/` and `Save/PreMapFixtures` pin ab54ddb's bytes; they move only with a deliberate format change (none this session).
- `VisionGatePerfTests` fresh-process row 3.66-3.69 ms against 4 ms; `PropsMeasureTests.MinimapRefresh_*` 0.34 ms: both flake beside
  another test host (rerun alone). `Stress/RarakuTwinStressTests` is slow (12,000 ticks × 2 processes).

---

## View track

### Current session plan

**M6-V18 — the setup's Map list. Type `feature` (counter 2 / 4 → 3 / 4), QA `standard`.**

Goal: let the player pick Raraku (and any later map file) in the skirmish setup, and carry the pick through start, save / load, Watch and
Load Game, so M6 criterion 7's "in skirmish setup" half is real for the one hand-made map that exists. docs/03 "Implementation (M6-S7)" →
"Requests for the view" is the contract.

Scope:
- `SkirmishSetup`: a **Map** row (label from a new `ui.json` key `setup.map`): item 0 "Generated" (a `ui.json` key `setup.map_generated`),
  then every `GameData.Maps` entry by `DisplayName` in the data's (file) order, each `Description` as the item's tooltip (as the presets).
  Picking a map: `SimConfig.MapId = map.Id`, `Map = MapGenParams.Default with { Width = map.Size, Height = map.Size }`; the Map size /
  Forests / Gold mines rows are disabled (greyed, values ignored) while a map is picked and come back for "Generated"; the slots are capped
  at `MapDef.Players` (extra slots removed with a visible line, or Add player disabled: say which); the seed row stays (the AI's RNG).
- `MatchArgs` / `LaunchOptions`: `--map <id>` (only for a map file; snake_case ≤ 64; an unknown id warned by `Match` and played as generated).
- `SimRunner.Start` passes `MapId`; **`StartPlayback` passes `replay.MapId`**; `StartLoaded` takes the header's (the sim rebuilds from the
  save's own heightmap: the id only names it, BUG-1054 item 3).
- `ReplayStore.TryReadHeader`: read format 9's `map` line after `starting-resources` (it already accepts version 9). `ViewApi/SaveSummary`:
  read format 5's map id string right after the preset string, **read-only** (`MapId`, "" when absent or cut; never throws), with its
  FormatVersion doc updated. Load Game's players cell names the map for a map-file save or recording (`ui.json` `load.map`, e.g. "{0}; map:
  {1}" with the map's `DisplayName` looked up from the data, the raw id if unknown); generated rows read exactly as before.
- The "Match started" / "Save loaded" / "Replay started" log lines name the map id (dev text, not player text). `Match`'s "placed fewer
  than asked" check is skipped when `MapId` is set (a map file's counts are its own).
- `tools/qa/smoke.ps1` unchanged. A dev scene `MapListTest` (items and tooltips equal the data; the pick sets the config; the disabled
  rows; the slot cap) and docs/03 "Implementation (M6-V18)" + docs/02 "Game flow" 2 (the setup's Map list).

Out of scope: any map content or text (`game/data/maps/**` is the sim's layout and the data's text), minimap / terrain art per biome,
per-map AI openings, Pale Hills / Vathar Crossing (not on `main`), the look / art pass, music, the export script, `rules.json`.

Acceptance criteria:
1. Build 0 warnings; `tools/qa/smoke.ps1` PASS; the scene loop green **on the merged tree** (merge `origin/main` first if the sim landed
   mid-session; the loop is ~78-80 scenes, 20-25 min; `FreeForAllTest` may need a rerun alone); non-Perf xUnit green.
2. Skirmish → Map → **Raraku** → Start: the match plays on Raraku (the hall at cell (22, 64) for slot 1 and (105, 63) for slot 2, both on
   level 0; `World.Config.MapId == "raraku"`), the Map size / Forests / Gold mines rows disabled, three or four slots refused or capped at 2
   with a visible reason; back to "Generated" restores the rows and the slot count. `MapListTest` asserts the items' texts and tooltips
   against `GameData.Maps` (no literal map name in C#).
3. A Raraku match saved from Esc → Save is format 5 (`SaveFormat.CurrentVersion`) and loads from Load Game onto Raraku, hash-equal to the
   uninterrupted run at the save tick + 1,000; a generated match still saves format 4 (`SaveFormat.MaplessVersion`) byte-identical to before.
4. A Raraku match from the menu is recorded as format 9 (`map raraku` after `starting-resources`) and **Watch** plays it to every
   checkpoint (the real `StartPlayback` passes `replay.MapId`); a generated match still records format 8 or lower exactly as before.
5. Load Game lists a Raraku save and recording with the map named (`load.map`), formats 4-8 rows unchanged; `--map raraku` starts Raraku,
   `--map junk` / `--map ""` / `--map` with a 65-character id warn and play generated.
6. `git diff --stat main...HEAD -- sim/` = `ViewApi/SaveSummary.cs` (+ QA's own test files); `SaveSummary.MapId` reading changes nothing
   (a row: hash, tick and save bytes unchanged after a read); `ui.json` gains only the new keys; docs updated.

Design references: docs/03 "Implementation (M6-S7)" (the whole section: `MapDef`, `SimConfig.MapId` + `Validate`'s refusals, save format 5 /
replay format 9 layouts and offsets, "Requests for the view"); docs/03 "Implementation (M6-V17)" (how the preset travels: mirror it);
docs/02 "Game flow" 2 and "Map and terrain"; docs/05 M6 criterion 7.

Tests required: `MapListTest` (dev scene); the save / load / Watch rows above in a QA scene (`QaM6V18Test`); `ViewApi/SaveSummaryTests`
rows for `MapId` (format 5 present, format 4 absent, a cut header); `ShellTest`'s key counts updated.

Constraints most at risk: views hold no gameplay state (the setup holds an index into `GameData.Maps`; the sim validates the pick); player
text only in `ui.json` / the map file (the map name comes from `MapDef.DisplayName`); `ViewApi/` additions read-only; a view test must not
assert "a generated save is `CurrentVersion`" (it is `MaplessVersion` 4).

### QA focus (view)

The pick's round trips on the merged tree: Raraku start → save at 200 / 3,600 → load → hash-equal; Raraku record → Watch to every
checkpoint; the two generated-map formats byte-identical to `main`'s fixtures; Load Game with formats 4 / 5 saves and 7 / 8 / 9 recordings
side by side; the slot cap under every path (Add player with a map picked, a map picked with four slots open, `--players 4 --map raraku`);
`--map` fuzz (12+ values incl. path separators and unicode); the disabled rows cannot be edited by keyboard; layouts at 1152 × 648 and
1280 × 720 with the new row (Start's bottom edge on screen at four slots). `SaveSummary.MapId` on forged / truncated format-5 headers never
throws.

### Next task candidates

1. M6-VH5 (view hardening at 4 / 4): BUG-1081 item 2 (wrap the long tooltip) if the owner minds, the loop's flaky `FreeForAllTest` start.
2. (e) `tools/export.ps1` once the export templates are downloaded; (f) criterion 6's music once a track is dropped in; the sound
   re-cuts when the owner listens; the Pale Hills / Vathar Crossing rows appear in the list by themselves once the sim ships them.

### Watch-outs (view)

- **Run the scene loop on the merged tree** before claiming green; the sim's M6-SH1 lands first and changes `rules.json` (the hash):
  recordings made during the session before that merge refuse afterwards (expected; re-record the QA fixtures on the merged tree).
- A Raraku match's `World.Config.Map` has `Width` / `Height` = 128 and every other generator field at its default.
- `scene-loop.ps1 -Filter` is a file glob; `FreeForAllTest` crashed once at startup under three hosts (0xC06D007F), 5 / 5 alone.

---

## Data track

### Current session plan

**D23 — land D22 (the golden merge-fix), then Raraku's name and description, then BUG-1081's checker row. Type `feature` (counter 2 / 4 →
3 / 4), QA `light`.**

Goal: get last session's accepted D22 (the presets' final text) onto `main` by regenerating its golden on a tree that has the sim's
M6-S7, then give the first hand-made map the words the player will read in the setup's new Map list, and close BUG-1081's checker gap.

Scope, in order:
0. **The D22 merge-fix.** On the new branch (from `main` 8f7477d): `git merge origin/studio/2026-11-05-1716-data`; the two conflicts
   (`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`, `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs`) are resolved by **taking `main`'s
   version of both** (the regen in step 3 rewrites them once for D22 + D23 together). Check D22's files came along: `rules.json`'s two
   descriptions, both faction pages' High bullet, `Content/StartingResourcesContentTests.cs`, `QA/Content/D22PresetTextQaTests.cs`,
   BUG-1080's status, D22's coverage row. Commit "D22: merge onto main's tree (golden regenerated with D23 below)".
1. **Raraku's text** (`game/data/maps/raraku.json`, **only** the `displayName` and `description` keys; the layout is the sim's): keep the
   name **"Raraku"**; a one-line description in the presets' register: ≤ 200 characters, the desert / open centre / few forests / long
   sightlines of docs/02 "Map and terrain"'s table (and the mesas of docs/03's layout paragraph if it fits), **no number, no faction word,
   no "placeholder"**, two players implied not stated (the setup shows the cap). A `Content/MapTextContentTests.cs` pin (the name and the
   description in file order, the ≤ 200 limit, the checker of step 2 applied to every shipped map's text so Pale Hills / Vathar Crossing
   are checked when they arrive).
2. **BUG-1081 item 1:** `StartingResourcesContentTests.ProperNames` gains each faction's id and the bare words of each faction display name
   ("Malazan", "Whirlwind", ...), shared with the map checker (one helper in `Content/`); a mutant row "a Malazan quartermaster" fails.
   Close BUG-1081 item 1 in the bug file (item 2 is the view's tooltip width: leave open, note it).
3. **The golden, once, at the end:** `RTS_REGEN_GOLDEN=1 dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~ReplayGoldenTests`, then
   without; **every `k` line unchanged** (the diff is `data-hash` + `checksum` only); move `QA/GatherWedgeQaTests.SameGameDataHashes`' second
   value to the new hash with a "// D23: Raraku's text (+ D22's presets' text)" note (named exception, as D22's). **Do not touch
   `QA/Content/DescriptionWordingQaTests.cs`** this session (the sim deletes one test in it by named exception; a second edit would conflict).
4. docs/factions: nothing. `studio/qa/coverage.md`: one appended row (D23). A **review table** for the owner (old → new text, why) in the
   report so the Producer can quote it.

Out of scope: any map layout key (terrain, ramps, starts, mines, forests), `rules.json`, `ai.json`, any number anywhere, the faction
pages, the owner's pending answers (none arrived), Pale Hills / Vathar Crossing (not on `main`).

Acceptance criteria:
1. Build 0 errors; the data filter green (`DataValidationTests`, `DataContentHashTests`, `StartingResourcesContentTests`,
   `MapTextContentTests`, `D22PresetTextQaTests`, `ReplayGoldenTests`, `GatherWedgeQaTests`, `AiOpeningPairsReportTests`, `Map/MapFileTests`);
   the full non-Perf suite green on the branch.
2. D22's content is on the branch (the two descriptions in `rules.json` are the 154 / 164-character ones; both pages' High bullet present).
3. `git diff main -- game/data/maps/raraku.json` is exactly the two text lines; `MapFileTests` (the layout rows) unchanged and green.
4. The description meets the brief (≤ 200, no digit, no faction word, no "placeholder", names the desert / open centre / long sightlines);
   `MapTextContentTests` pins it and runs the checker over every `GameData.Maps` entry; the mutant rows fail on a digit, a faction word,
   "placeholder" and 201 characters.
5. `ProperNames` holds the bare faction words; the "a Malazan quartermaster" mutant fails; BUG-1081 item 1 marked fixed with the test name.
6. The golden's diff against `main` is the `data-hash` and `checksum` lines only; `GatherWedgeQaTests.SameGameDataHashes` moved once.
7. The report carries the review table (field, old, new, one-line why) and the quoted final text.

Design references: docs/02 "Map and terrain" (the Shipped maps table: Raraku, desert, "Open center, few forests, long sightlines"; the
biome sentence: sand, scrub, rock outcrops, sparse palms); docs/03 "Implementation (M6-S7)" Raraku paragraph (the mesas and buttes, the
open centre); the presets' register in `rules.json` (D22's two sentences) for tone and length.

Tests required: `Content/MapTextContentTests` (pins + checker + 4 mutants); the `ProperNames` mutant row; the golden `k`-line check
(`git diff` in the report).

Constraints most at risk: only the two named keys of the sim's map file; no C# outside `Content/` + the one `GatherWedgeQaTests` literal;
`DescriptionWordingQaTests.cs` untouched; one golden regen, at the end; no number in the text.

### QA focus (data)

`raraku.json` text-only by JSON diff against `main` (every layout key byte-identical); the `k` lines of the golden identical; the D22 merge
brought every D22 file and nothing else (diff the branch against `main` + the D22 branch's merge-base diff); the checker's mutants (digits
in words: "two mesas" is allowed? the brief says no digit, words are fine: check the checker's rule is digits + the ratio words, not number
words); the description under 200 and readable as a tooltip; `MapFileTests` unmoved.

### Next task candidates

1. The owner's answers (D21 names / descriptions / the Malazan standard wave, the D22 text, D19's table, the Shock pass, `goldShare`,
   BUG-0780); Pale Hills / Vathar Crossing's text with slice 2; D14 c; M7's rosters.

### Watch-outs (data)

- After M6-SH1 lands, the golden's `data-hash` line is informational and `SameGameDataHashes` is gone: **from next session no data task
  regenerates the golden unless its `k` lines move** (then the sim track is involved). This session is the last time the literal moves.
- `rules.json` and `game/data/maps/*.json` are the sim's files; a data task touching their text keys is a named exception in the brief.
- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} print "take it out (BUG-1010)" when
  their case passes: take them out then.

---

## Watch-outs (all tracks)

- Three tracks a session: three test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- **The owner's recordings and saves made before 2026-11-05-1716 refuse to play** (the data hash moved); they move again this session
  (the sim's knobs, D22 + D23's text).
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase and commits each dev's work at every phase boundary.
