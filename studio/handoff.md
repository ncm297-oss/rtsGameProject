# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session **2026-10-16-2036** (the third full session of 2026-10-16; 3 / 8 after it).
Base `main` 66437d8 (= e48cb0e + the view's M6-V5). Sim **resumes M5-4f** (held on `studio/2026-10-16-1654-sim`, 8ac7b43 / 9670e9b)
for its QA round; view **M6-V9 replay playback**; data **STOP (cheap)**.
Counters before: sim 1 / 4, view 1 / 4, data 0 / 4. Bug ids this session: **sim from BUG-0820, view from BUG-0830, data from BUG-0840**.
PLAN checks: `main` builds 0 warnings (15 s); non-Perf suite result in the session log; no open S1 / S2; S3 14 / S4 15; inbox empty.

## Where we are

- M5 2 / 9 (criteria 3-5 advanced, 7 built on the held branch), M6 1 / 10 (criterion 1 only team left, 4 ticked).
- The owner has not answered D19's review table, the Shock pass, `goldShare` 0.6 → 0.5, or BUG-0780. Nothing in the inbox.
- `CLAUDE.md` line 6 is fixed on the held sim branch only; it lands with M5-4f.

## Standing rules (1-27)

Unchanged (see the 1654 handoff in git history for the long form): BUG-0764's paired A/B rule for Perf rows; a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>`; a data brief names
every sim-track test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the rest;
`git checkout HEAD -- path`; one system per brief, the dev's count excludes QA's files; no process killing by image name; **a dev
never loosens a QA row** (QA re-stages its own rows, with the reason in the file); the Producer's merged-tree check runs the whole
scene loop when the sim touches movement / the generator / `ResourcePlacer` / `ViewApi/StartBase` / liveness / the command door /
the defeat rule **or moves `gatherRate` / the golden**; a QA scene row for a bug left open prints `KNOWN BUG-nnnn`; 22. one track
moves the golden header per session; 23. a QA verdict needs its committed report file; 27. a dev who ends BLOCKED on a red QA row
still hands the branch to QA in the same session.

---

## Sim track

### Current session plan: M5-4f round 1 — QA full + the BUG-0790 ruling, on the held branch (feature, counter → 2 / 4)

**Start (dev, first act):** the work is on `origin/studio/2026-10-16-1654-sim` (8ac7b43 implement, 9670e9b state). Bring it into this
session's branch: `git merge origin/studio/2026-10-16-1654-sim` (or merge `origin/main` into a checkout of that branch; same tree).
Expected overlap with `main` since 4d0c455: `docs/02`, `docs/03`, `docs/05` (the view's M6-V5 paragraphs: keep both sides),
`studio/bugs/README.md` (union), `studio/qa/coverage.md` (keep `main`'s M6-V5 row), `CLAUDE.md` (keep the branch's line 6).
`studio/STATE.md` / `handoff.md` / `sessions/` are identical on both. Build 0 warnings, then **hand to QA**; the dev acts only on what
QA files (bug ids from BUG-0820).

**QA (full), in this order:**
1. **BUG-0790 ruling** (`Stress/GatherPocketStressTests.Pocket_NoGathererWedgesOutOfReach_ConservationExact(MineOneSide, 40)`,
   seeds 2 and 8, failing ticks 2260 / 1149). Replay each to its failing tick and look at the face: did the stuck worker have a free
   standing spot within `EconomyConstants.Reach` (1.25 m) of the footprint? **Yes** = a sim crowding fault: skip the row with
   `Skip = "BUG-0790: ..."` (S3 stays open for M5-H3; write what you saw in the bug file). **No** (every spot in reach held; the
   straight-line `SomeoneBetween` misses the crowd) = the check's geometry: re-stage the check so a crowd at the face counts as
   "someone between", reason in the file, the bug closed as a test fault. Either way the row ends green and no bound is loosened.
   Report the old-rate A/B (0.7 / 0.6 green) as the control.
2. **The sized answer through the command log only**: one Raider → ≤ 2 home units and the wave on; 8 → part of the wave; 12 → the
   whole wave; the hall under 0.5 → the whole wave; a hit with nobody seen → 150. A 3-4 seat AI-v-AI fuzz with a per-think oracle
   (a defender's generation set only for a free army unit; the picked worth within half a unit of the answer unless the army is
   short; a pulled member's `WaveGeneration` 0; `WaveStartHalfPop` never below the live members' half-pop) and hash twins; the
   whole-wave recall's cooldown; the Blackboard arrays outside `StateHash` (scribble row); determinism of the heap order (two runs,
   the same log; a mutant that breaks slot ties the other way must change the log).
3. **The golden**: re-record `M3PlayableTest -- --seed 21 --break 19` once more and diff against `Replays/seed21_playable.replay`
   (header and `k` lines agree up to the shorter run; a few ticks' difference is the frame-time scene); `ReplayGoldenTests` 2 / 2;
   the cross-map `k` lines byte-identical to base.
4. **The re-stagings**: `Ai/DefenseManagerTests` and `QA/DefenseQaTests` set `defendMinValue` 5,000 in their own data: confirm every
   row still attacks what its name says (the trigger, the point, BUG-0670 / 0671 / 0673, the knobs) and the knob-edge Theory keeps
   the shipped values; name any row made weaker.
5. **`EconomyManager`'s per-think cache**: identical picks (a 2,000-tick 3-seat match, cache on and off through a copy of the build:
   the same command log) and `AiThinkCostQaTests` forests:6 under 1 ms.
6. **Perf alone, paired base / head** (BUG-0764): `AiPerfTests` 1 ms gate rows, `AiThinkCostQaTests`, `AiWaveCostQaTests`;
   BUG-0791's rise is noted, not a finding (ungated).
7. The full non-Perf suite green; smoke PASS; **the whole scene loop on the merged tree** (the view's `FreeForAllTest` / `QaM6V5Test`
   / `ShellTest` pin seats, starts and a wipe-out at tick 257 on seed 1; `gatherRate` may move a view scene that pins a gathered
   amount at a tick: re-stage with the reason, don't loosen).

**Acceptance criteria** (the 1654 PLAN's 1-8; the Producer re-judges on the whole): (1) `ProportionalDefenseTests` show criterion 7's
four sentences through the command log; (2) the three knobs load, validate at the edges, change the hash; (3) `AiVersusAiTests` green
and no `DefenseQaTests` row weaker than its name; (4) D15 C's Theory; (5) BUG-0702 item 1's rows green; (6) `ReplayGoldenTests` 2 / 2
with QA's second recording diffed; (7) **the full non-Perf suite green with BUG-0790 ruled as above**, Perf A/B clean; (8) docs in the
same commit, the count stated. **OUT:** anything beyond QA's findings (no gather-crowding fix unless QA shows a sim fault *and* the
fix is a few lines in `MovementSystem`'s stand-point logic; else skip + S3).

### After M5-4f lands
Pathing slice 2, M5-4c slice 2 (knobs → criterion 4), Requests 39 (+ 38 teams), **Requests 42 (the save format)**, D14 b schema,
M5-4d (+ hunting the last units → criterion 3), D14 c, M5-5, M5-6. **M5-H3** after four feature sessions (2 / 4 after this one):
BUG-0764 first, then BUG-0790 if still open, BUG-0791, BUG-0144, BUG-0113 item 2, BUG-0704 items 2-3.

### Watch-outs (sim)
- Only the sim moves the golden (already moved on the branch, once, whole); no second header move in the round.
- `SurroundClaim` / `SurroundHolder`, the defense marks and M5-4f's `DefenderGeneration` / `DefendAll` / `DefendAnswer` /
  `WaveMinHalfPop` / `WaveMinCost` are save state outside the hash: Requests 42 carries them.
- A REJECT this time for the *same* reason (an unruled red row) would be the second rejection of M5-4f: the STOP rule. Rule BUG-0790.

---

## View track

### Current session plan: M6-V9 replay playback (M6 criterion 3's replay half) · feature, QA standard, ~800-1,000 lines, counter → 2 / 4

**Goal.** Criterion 3 is "Save/load and replay playback". The save format is the sim's (Requests 42); replay playback needs no sim
change: `Rts.Sim.Replays` is public (`ReplayFormat.TryReadFile` / `WriteFile`, `Replay` with header + `Commands` + `Checkpoints`,
`ReplayRecorder` already attached by `SimRunner.Start` when `RecordCheckpointInterval` > 0, `ReplayError`), and the shell's start
bases are enqueued as `SpawnBuilding` / `SpawnUnit` commands (`Match.cs` 356-357), so a recording reproduces them. Let the player
watch a recorded match from the menu.

**Scope.**
- Every match the shell starts (menu or `--skirmish`) records: `RecordCheckpointInterval` = `ReplayRecorder.DefaultCheckpointInterval`
  (100); on match end (banner) or Quit to menu, write `user://replays/<yyyyMMdd-HHmmss>-seed<seed>.replay` (`ReplayFormat.WriteFile`
  of `Recorder.ToReplay()`). Keep at most N files (a documented constant in `SimRunner` or a `ReplayStore` script; delete the oldest).
  Dev / test scenes that already set `RecordCheckpointInterval` are unchanged. The wall clock is allowed **only** for the filename
  (view code; never passed into the sim).
- Main menu → **Load Game** (new `ui.json` `menu.load` key; the main menu today reads Skirmish / Settings / Quit) opens a list of
  `user://replays/*.replay`, newest first; columns from the header: date (from the filename), seed, seats (faction + human / AI per
  seat), length (`TickCount` / 20 → m:ss); files `TryReadFile` refuses are skipped and counted in a one-line note. Buttons Watch /
  Delete / Back, all text from `ui.json` (`load.*`).
- **Watch**: `SimRunner` plays the file tick by tick, the way `ReplayPlayer.Run` does but through the runner's clock: `SimConfig` from
  the header (seed, player count, capacities, `Map`, `Combat`), **every seat `SeatController.Human`** with the header's faction (no AI
  runs; its commands are in the log); a fresh `ReplayRecorder` on the playback sim; before each `Tick`, enqueue every logged command
  whose `Tick == TickNumber + 1` in log order and check the stamp (tick, sequence) against the log; after each tick compare the newest
  checkpoint against the file's; a mismatch (or `ReplayError` on open) stops playback with a banner from `ui.json`
  (`load.mismatch` / `load.refused`) and Back to menu. `data.ContentHash() != replay.DataHash` is refused before the first tick with
  `load.refused`. Playback ends at `TickCount` with the match-over banner path (Victory / Defeat / Draw / "Replay ended") and Back to menu.
- During playback: camera free, minimap works, game speed works (and pause), selection allowed (card greyed), **no order of any kind
  reaches the sim** (the input layer's order senders are off; the playback sim's command log equals the file's).
- `--replay <path>` launches straight into playback (for scenes and the owner).
- `ui.json` for every label; docs/02 "Game flow" 1 (Load Game = replays until the save lands); docs/03 "Implementation (M6-V9)" and
  a line in "Save/load and replays" that the shell writes `.replay` files to `user://replays/`.
- Tests: a headless scene `ReplayPlaybackTest` that (a) runs a scripted 600-tick 2-seat match with one AI through the shell path,
  writes the file, plays it back to the end: every checkpoint equal, the banner shown, the playback command log byte-equal to the
  file's; (b) a 3-seat (2 AI) match the same; (c) a copy of the file with its `data-hash` line altered is refused with `load.refused`;
  (d) a copy with one checkpoint hash altered stops with `load.mismatch` at that tick; (e) a junk file in `user://replays/` is skipped
  by the list; (f) orders during playback: the right-click / A / S / H senders issue nothing. `ShellTest` rows for the menu path
  (Load Game → list → Watch → Back). Per-process `user://` as BUG-0770's rule: no test reads or writes the player's real folder.
  **No committed `.replay` fixture**: the data hash moves with every data change (the sim's M5-4f moves it this session); tests
  record their own files.
- **OUT:** save / load (Requests 42), teams (38), the stats columns (39), export, replay seeking / rewind, any `sim/` change (a
  read-only `ViewApi` accessor only if the runner needs one; name it in the report).

**Acceptance criteria.** (1) A match played from the menu leaves a `.replay` in `user://replays/` that Load Game lists with the right
seed / seats / length. (2) Watching it reproduces every checkpoint (`ReplayPlaybackTest` (a), (b)). (3) A wrong data hash is refused
before the first tick; a wrong checkpoint stops at that tick; both show the `ui.json` text ((c), (d)). (4) No order reaches the sim
during playback: the playback command log equals the file's ((f)). (5) `--replay <path>` works; the list skips junk ((e)). (6) Smoke
PASS, the whole scene loop green, 0 warnings, docs/02 + docs/03 updated, the count stated, `git diff -- sim/` empty or read-only
`ViewApi/` only.

**Design references.** docs/02 "Game flow" 1 (Main menu: Skirmish, Load Game, Settings, Quit); docs/03 "Save/load and replays"
(format 6, checkpoints every 100 ticks, `DataMismatch`); CLAUDE.md rule 2 (no wall clock into the sim), rule 7 (views hold no
gameplay state), rule 8 (text from `ui.json`).

**Constraints at risk.** The wall clock for the filename stays in view code; `FixedFrameDelta` untouched; `SimRunner`'s tick loop
must enqueue logged commands *before* the tick they're stamped for, exactly as `ReplayPlayer` does, or the stamp check fails;
playback with AI seats must build them Human.

### After
The look test (criterion 5: one Quaternius character + animation through the docs/04 pipeline beside KayKit and today's primitive;
a screenshot; the licensing log), save / load when Requests 42 lands, `tools/export.ps1` when the owner has the templates, teams
(38), the stats columns (39). **M6-VH3** after four feature sessions (2 / 4 after this one): BUG-0550 item 2, BUG-0281 item 2,
BUG-0663, the `--colour` collision warning's wording.

### Watch-outs (view)
- Three Godot runs share the machine: wall-clock scene rows flake under load; rerun alone before filing.
- The sim's M5-4f lands this session (if its round passes): `gatherRate` moves any view scene that pins a gathered amount at a tick.
  The sim's QA re-stages those on the merged tree; the view dev should not pin gathered amounts in the new scene.

---

## Data track

### Current session: STOP (cheap)

No inbox answer; M5-4f is not on `main` (its round runs this session), so no schema or number the data track needs has landed.

### The session after M5-4f lands: re-print + re-word, QA light, ~200 lines, no number changed
- `Content/AiSpendingReportTests` re-run on the new `gatherRate` and the sized defense; both pages' "Spending on the menu's map"
  paragraphs + docs/03's re-printed; `Content/AiHouseRuleReportTests` after D15 C and the house notes on both pages.
- Both faction pages' "Defense" lines re-worded for the proportional response (the knobs quoted from `rules.json`: 1.5 / 150 / 0.5).
- **First, any owner answer**: D19's table, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780 ("accept" closes it with a dated line
  on both pages; "Catapult pass" = a sweep first, then one header move the session the sim moves none).

### After that
The D14 b openings once the strategies schema is on `main`; the owner's tweaks; the M7-M9 rosters when their milestones open.

---

## Watch-outs (all tracks)

- **BUG-0764:** Perf rows red on both trees on this PC; an owner fresh-boot run is wanted before M5-H3 re-baselines. Paired A/B only.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01: each track appends its own rows; the conductor unions them.
- Merge order when the sim lands: sim → view; the Producer's merged-tree check runs the whole scene loop then (the sim moves
  `gatherRate` and the golden; the view adds `ReplayPlaybackTest`).
