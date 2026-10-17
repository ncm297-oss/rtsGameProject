# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-16-1654** (the second full session of 2026-10-16; 2 / 8 after it).
View **M6-V5 ACCEPT** (merges alone); sim **M5-4f held on `studio/2026-10-16-1654-sim` (8ac7b43) for its QA round**; data STOP.
Counters after: sim 1 / 4, view 1 / 4, data 0 / 4. Next bug ids: **sim from BUG-0820, view from BUG-0830, data from BUG-0840**
(the sim's round, if the conductor runs it in the 1654 session, continues from BUG-0792).
Checks at ACCEPT: view branch build 0 warnings, smoke PASS ×2, `FreeForAllTest` / `QaM6V5Test` / `AiSeatTest` / `ShellTest` PASS
alone, QA's loop 61 / 61 and non-Perf 6,417 / 6,425 (0 failed); sim branch build 0 warnings, the task's slice 407 / 407, the
dev's full non-Perf 6,446 / 1 failed (BUG-0790) / 7 skipped; golden on `main` **AB811AD47F68D0AE** (the sim branch C059AD620DE581F1).

## Where we are

- `main` after this session = e48cb0e + the view's M6-V5. **M5 2 / 9, M6 1 / 10** (criterion 1 advanced: 2-4 slots in, team left).
  No open S1 / S2. S3 / S4: 29 open (BUG-0790 S3 and BUG-0791 S4 new, BUG-0770 closed).
- The owner has not answered D19's review table, the Shock pass, `goldShare` 0.6 → 0.5, or BUG-0780.
- `CLAUDE.md` line 6 is fixed **on the held sim branch** ("M5 (sim) / M6 (view); M3 data Done"); it lands with M5-4f.

## Standing rules (1-26)

Unchanged, as in the 0847 handoff: BUG-0764's paired A/B rule for Perf rows; a new `Command` kind into `CommandDoorFuzzStressTests`;
per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>`; a data brief names every sim-track test that pins
a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the rest; `git checkout HEAD -- path`; one
system per brief, the dev's count excludes QA's files; no process killing by image name; **a dev never loosens a QA row** (QA
re-stages its own rows, with the reason in the file); the Producer's merged-tree check runs the whole scene loop when the sim
touches movement / the generator / `ResourcePlacer` / `ViewApi/StartBase` / liveness / the command door / the defeat rule
**or moves `gatherRate` / the golden**; a QA scene row for a bug left open prints `KNOWN BUG-nnnn` (an xUnit row: `Skip` with the
id); 22. one track moves the golden header per session; 23. a QA verdict needs its committed report file; 24-26 as before.
**27 (new, BUG-0790):** a dev who ends BLOCKED on a red QA row still hands the branch to QA (the row's owner) in the same
session; "BLOCKED" skips QA only for a build break or a red that is the dev's own test.

---

## Sim track

### Current session plan: M5-4f round 1 (QA full + the BUG-0790 ruling) on the held branch

**For the conductor:** if the round runs inside the 1654 session, it is QA first on `studio/2026-10-16-1654-sim` (8ac7b43); the dev
acts only on what QA files. If it runs as the next session's sim task, the same: the dev's first act is `git merge origin/main`
(the view's M6-V5: `game/**` only, no conflict expected with the sim's files; `studio/bugs/README.md` unions), then hand to QA.

**QA (full), in this order:**
1. **BUG-0790 ruling** (`Stress/GatherPocketStressTests.Pocket_NoGathererWedgesOutOfReach_ConservationExact(MineOneSide, 40)`,
   seeds 2 and 8). Replay each to its failing tick (2260 / 1149) and look at the face: did the stuck worker have a free standing
   spot within `EconomyConstants.Reach` (1.25 m) of the footprint it failed to take? **Yes** = a sim crowding fault: skip the row
   with `Skip = "BUG-0790: ..."` (S3 stays open for M5-H3; write what you saw in the bug file). **No** (every spot in reach held;
   the straight-line `SomeoneBetween` misses the crowd) = the check's geometry: re-stage the check so a crowd at the face counts
   as "someone between", the reason in the file and the bug closed as a test fault. Either way the row ends green and no bound
   is loosened. Report the old-rate A/B (0.7 / 0.6 green) as the control.
2. The brief's QA focus for M5-4f: the sized answer through the command log only (one Raider → ≤ 2 home units and the wave on;
   8 → part of the wave; 12 → the whole wave; the hall under 0.5 → the whole wave; a hit with nobody seen → 150); a 3-4 seat
   AI-v-AI fuzz with a per-think oracle (a defender's generation set only for a free army unit; the picked worth within half a
   unit of the answer unless the army is short; a pulled member's `WaveGeneration` 0; `WaveStartHalfPop` never below the live
   members' half-pop) and hash twins; the whole-wave recall's cooldown; the Blackboard arrays outside `StateHash` (scribble row);
   determinism of the heap order (two runs, the same log; a mutant that breaks slot ties the other way must change the log).
3. **The golden**: re-record `M3PlayableTest -- --seed 21 --break 19` once more and diff against `Replays/seed21_playable.replay`
   (the header and the `k` lines up to the shorter run agree; a tick-count difference of a few ticks is the frame-time scene);
   `ReplayGoldenTests` 2 / 2; the cross-map `k` lines byte-identical to base.
4. **The re-stagings**: `Ai/DefenseManagerTests` and `QA/DefenseQaTests` set `defendMinValue` 5,000 in their own data: confirm every
   row still attacks what its name says (the trigger, the point, BUG-0670 / 0671 / 0673, the knobs) and that the knob-edge Theory
   keeps the shipped values; name any row made weaker.
5. **`EconomyManager`'s per-think cache**: identical picks (a 2,000-tick 3-seat match with the cache on and off through a copy of
   the build: the same command log) and `AiThinkCostQaTests` forests:6 under 1 ms.
6. **Perf alone, paired base / head** (BUG-0764's rule): `AiPerfTests` 1 ms gate rows, `AiThinkCostQaTests`, `AiWaveCostQaTests`;
   BUG-0791's rise is noted, not a finding (ungated).
7. The full non-Perf suite green; smoke PASS; the whole scene loop on a scratch merge with `main` (the view's new `FreeForAllTest`
   / `QaM6V5Test` / `ShellTest` pin seats, starts and a wipe-out at tick 257 on seed 1; `gatherRate` may move a view scene that
   pins a gathered amount: re-stage with the reason, don't loosen).

**Acceptance criteria** as the 1654 PLAN brief (criteria 1-8); criterion 7 now reads "the full non-Perf suite green with BUG-0790
ruled as above". **OUT:** anything beyond QA's findings (no gather-crowding fix in this round unless QA shows a sim fault *and*
the fix is a few lines in `MovementSystem`'s stand-point logic; else skip + S3).

### After M5-4f lands
Pathing slice 2, M5-4c slice 2 (knobs → criterion 4), Requests 39 (+ 38 teams), **Requests 42 (the save format: the Blackboard
whole incl. M5-4f's marks, the surround claims, the flow-field cache, the fog bits; a round-trip test comparing those directly,
docs/03 "Save/load and replays")**, D14 b schema, M5-4d (+ hunting the last units → criterion 3), D14 c, M5-5, M5-6. **M5-H3**
after four feature sessions (1 / 4 now): BUG-0764 first, then BUG-0790 if still open, BUG-0791, BUG-0144, BUG-0113 item 2,
BUG-0704 items 2-3.

### Watch-outs (sim)
- Only the sim moves the golden (already moved on the branch, once, whole); no second header move in the round.
- `SurroundClaim` / `SurroundHolder`, the defense marks and M5-4f's `DefenderGeneration` / `DefendAll` / `DefendAnswer` /
  `WaveMinHalfPop` / `WaveMinCost` are save state outside the hash: Requests 42 carries them.
- The Perf gates: BUG-0764. A/B on the same machine state, or no claim.

---

## View track

### Current session plan: replay playback (M6 criterion 3's replay half) · feature, QA standard, ~800 lines

**Goal.** Criterion 3 is "Save/load and replay playback". The save format is the sim's (Requests 42, after M5-4f); replay
playback needs no sim change: `Rts.Sim.Replays` (`ReplayFormat.TryReadFile`, `Replay` with header, command log and checkpoints;
`ReplayError.DataMismatch`) is public and the recorder already writes `.replay` files in tests. Let the player watch a recorded
match from the menu.

**Scope (the Producer confirms at PLAN; the alternative is the art look test, criterion 5, with the owner's packs in `asset-sources/`).**
- Every match the shell starts records its commands and checkpoints (the sim's recorder) and writes `user://replays/<date>-<seed>.replay`
  on match end / Quit to menu (a size cap from `ui.json`? no: a constant in `SimRunner`, documented).
- Main menu → **Load Game** opens a list of `user://replays/*.replay` (newest first; the header's seed / seats / length as columns;
  text from `ui.json` `load` keys) → Watch: the match re-runs from the file's seed and seats with the recorded commands applied at
  their ticks; the checkpoints are compared as they pass (a mismatch stops with the `ui.json` data-mismatch / desync text, as
  docs/03 says); the camera is free, no orders are accepted (selection allowed, the card greyed), game speed works, the
  match-over banner shows at the end with "Back to menu"; `--replay <path>` for dev / test runs.
- `ui.json` for every label; docs/02 "Game flow" 1 (Load Game = replays until the save lands), docs/03 "Implementation (M6-V9)".
- A headless scene `ReplayPlaybackTest`: record a scripted 600-tick match, play it back, every checkpoint equal, the banner;
  a file with a wrong data hash refused with the text; junk files skipped in the list. `ShellTest` rows for the menu path.
- **OUT:** save / load (Requests 42), teams (38), the stats columns (39), export, any `sim/` change (a read-only `ViewApi`
  accessor only if the recorder's state needs one; name it).

**Acceptance criteria.** (1) A match played from the menu leaves a `.replay` the list shows. (2) Watching it reproduces every
checkpoint (a hash twin against the live run). (3) A mismatched file is refused with the data text. (4) No order reaches the sim
during playback (the command log of the playback = the file's). (5) Smoke PASS, the loop green, 0 warnings, docs, the count, `sim/`
diff empty or read-only `ViewApi/`.

### After
The look test (criterion 5: one Quaternius character + animation through the docs/04 pipeline beside KayKit and today's primitive,
a screenshot; the licensing log), save / load when Requests 42 lands, `tools/export.ps1` when the owner has the templates,
teams (38), the stats columns (39). **M6-VH3** after four feature sessions (1 / 4 now): BUG-0550 item 2, BUG-0281 item 2,
BUG-0663, the `--colour` collision warning's wording (QA's M6-V5 note).

### Watch-outs (view)
- The owner has not tried the real-cursor confinement, the Keybinds tab or the new 3-4 player setup on the desk.
- Three Godot runs share the machine: wall-clock scene rows flake under load; rerun alone before filing.
- When the sim's M5-4f lands, `gatherRate` moves any view scene that pins a gathered amount at a tick: re-stage, don't loosen.

---

## Data track

### Current session: STOP (cheap) unless the inbox has an answer

M5-4f is not on `main` yet (its QA round first), so the re-print waits; no schema the data track needs is on `main`.

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
- Merge order when the sim lands: sim → view; the Producer's merged-tree check runs the whole scene loop then.
