# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-11-0739** (session 1 / 8 of 2026-11-11; a resume of the dead
2026-11-10-2116). Sim **M7-3b**, view **M7-VH1**, data **D-R37** all ACCEPTED (0 fix rounds each). **M5 8 / 9 (criterion 6
owner-only), M6 5 / 10, M7 0 / 4 (bullets 1-3 built as rules; tech and ability ids under the faction order; nothing shipped).**
Counters: **sim 4 / 4 (hardening next), view 0 / 4, data 0 / 4**. Bug ids next session: **sim 1220+, view 1230+, data 1240+**.
Open S1 / S2: none. Inbox New: empty at ACCEPT.

**This session (1317) in one line, as planned at its PLAN check below (it supersedes the 0739 proposal):** sim **M7-H1** (hardening, QA full, BUG-1191 first) · view **M7-V3a** (feature, QA standard) · data **D28**
(the Teblor roster, QA standard). The owner's "Teblor names" answer, if it is in the inbox, is applied inside D28.

## PLAN check 2026-11-11-1317 (session 2 / 8; all three tracks GO)

- **Inbox:** New is empty. No owner answer on the Teblor names: the page's words stand for D28.
- **`main` (451284f, a scratch clone, rule 38):** build 0 / 0; the default loop **8,631 / 0 / 23 of 8,654** (55 m 48 s beside two
  other suites); smoke PASS.
- **The finding that changed the plan (BUG-1191, S3, new):** the 0739 handoff called D28 unblocked. It is not, quite. On a second
  scratch clone with a Malazan copy added as `teblor` (the `AddTeblor` recipe, on disk): smoke PASS; the default loop **8,604 / 345 /
  23 of 8,972**: 279 failures in 24 `Content` / `QA.Content` classes (expected: the real roster fixes those) and **66 in 22 sim-owned
  classes** the data track may not edit; the scene loop 89 / 91 (`DebugOverlayTest`'s load flake and **`QaM6V11Test` "row 3
  UnitsKilled '68' (oracle '72')"**). Cause 1: pins and fixtures that assume two factions. Cause 2: `SimConfig.SeatOf`'s
  `p mod factions` re-seats every 3-4 player row on default seats (seat 2 becomes the third faction).
- **So:** sim M7-H1 gets BUG-1191 as its first item; the view track has work after all (M7-V3a, on a forged copy); D28 is built
  in parallel and **merges only if the merged tree is green, otherwise it is held on its branch one session** (precedent: D12, D23).
- **Verified claim:** 0739's "no `BinarySearch` / `Array.IndexOf` left in `sim/Rts.Sim`" is loosely worded: ten remain, all on
  common-only tables (statuses, projectiles, resources, damage classes, the order file's folders), which keep the global ordinal
  rule by docs/03; none is on a unit / building / tech / ability key. The claim's substance holds; no bug. The golden header is
  3A04D4E370C643DA; 16 content files read `ContentFactions.Keys`; teblor.md carries the Warrior x1.5.
- **Docs drift fixed:** docs/05's status table (M5 "6 / 9" to 8 / 9; M7 "Planned" to Started). **Not mine to edit:** CLAUDE.md's
  "Current milestone" line does not mention M7 (for the owner or an interactive session).
- **New standing rule 54:** before a task adds a faction, a data type or a default that every test reads, the Producer applies it
  on a scratch clone (a copy is enough) and runs the default loop and the scene loop at PLAN; "unblocked" is said only after that.
- **New Request 47 (sim, with M7-4):** an optional `scale` on a building row, so the Teblor buildings' 1.25x is data.
- **At ACCEPT:** merge sim, then view, then data on a scratch clone; the default loop, the pin classes with Stress, the scene loop.
  D28 lands only on a green merged tree. If D28 is held, the next sim task is "what the real roster still breaks" from D28's list
  (feature, before M7-4), with the data branch merged into the sim's scratch clone.
- **Files no two tracks share this session:** sim: `sim/**` except `ViewApi/**`, `Content/**`, `QA/Content/**`, the golden and
  `GatherWedgeQaTests.cs`; `tools/qa/third-faction-copy.ps1`. View: `game/**` except `game/data/**`, plus `game/data/common/ui.json`,
  `ViewApi/**`. Data: `common/factions.json`, `factions/teblor/**`, `docs/factions/teblor.md`, the two content test folders, the
  two rule-51 files. docs/03: sim and view both append a section (keep both, the sim's first).

## Standing rules (1-54; 54 is in the PLAN check above)

Unchanged 1-51 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle **and with no
non-studio load on the machine** (QA says what else was running); 43: every sim task that can change Normal AI behaviour names which
M5-5 rows it expects to move and re-measures the bound comments and docs/03's M5-5 table in the same commit, and a row that stops
ending is a finding, not a re-bound; 44: a session that dies after ACCEPT is re-verified on a scratch clone and integrated first, a new
task starts on a fresh branch; 45: a brief whose task changes Normal AI behaviour says what to do when a pinned criterion row stops
ending; 46: a branch held twice gets a terminal acceptance rule in its third brief, so no branch lives a fourth session; 47: QA's
re-check runs the default loop on the commit that includes QA's own suites, and a QA file that measures allocation declares
`[Collection(SerialCollection.Name)]`; 48: at PLAN the Producer checks every acceptance criterion against the brief's own out-of-scope
list; a figure the scope cannot move is "measure and report", never pass / fail; 49: no test pins the golden replay's header
`data-hash` as a literal (`GatherWedgeQaTests.SameGameDataHashes` is the model); 50: a sim task that changes a save or replay format
names the view scenes that pin those bytes and the view track re-checks them; 51: a data brief that re-pins sim-owned digests lists
the sim test folders and pin classes and says the `Stress` pin classes must be run; 52: a developer's report ends every test process
the developer started; a `testhost` or Godot left running in a worktree after the report is a finding; 53: the QA tier bounds the
suites QA runs: **light** = the task's filters + the default loop (`Category!=Perf&Category!=Stress&Category!=Soak`), never the full
suite; **standard** = the default loop + the Stress rows the task touches + the scene loop (view); **full** = the default loop with
Stress, then Perf alone under rule 42).
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests.SameGameDataHashes` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.

## Watch-outs (all tracks)
- **A dead session loses the developers' reports and their bug-file patches** (they live in the conversation / scratchpad): 2116
  died after its builds and this session rebuilt the proofs from QA's re-measurements. When a session is resumed, QA writes the
  proof lines for the closed bugs and the Producer applies them at ACCEPT (done at 0739 for BUG-1179 / 1126 / 1147).
- **Three test hosts at once** flake the wall-clock Perf and scene rows (rule 42 / 53). The default loop is ~25-30 min alone, ~45 min
  beside another track; the full non-Perf with Stress ~2 h 25 m. **The PC's speed varies with no load to blame (BUG-1170):** Perf
  alone was 203 / 0 / 3 at ~00:xx on 2026-11-11 and 188 / 15 / 3, 190 / 13 / 3 (branch), 174 / 29 / 3 (de591f0) at ~09:00 the same
  morning, idle. A Perf run is evidence only when a first run shows the PC at its recorded speed (`ASweepRetarget` near 16 us, not 80+).
- Merge order sim → view → data; `studio/bugs/README.md` and `studio/qa/coverage.md` are the usual conflicts, both sides kept;
  docs/03 conflicts when two tracks append an "Implementation" section at the same anchor (keep both, the sim's first).
- **Files no two tracks may share next session:** data **D28** is the only task that adds `game/data/factions/teblor/**`, appends
  `"teblor"` to `game/data/common/factions.json`, regenerates `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`'s header and adds
  its reason to `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` (rule 51: those two sim files are the data task's next session, and
  only for the header line, the checksum and the comment). Sim **M7-H1 edits neither of those two files, no `game/data` byte, and
  nothing under `Content/**` or `QA/Content/**`**; BUG-1190 item 1's assertion goes in a new test file, not in `GatherWedgeQaTests`.
- A developer must never kill another track's Godot or `testhost` process; a developer ends its own (rule 52).
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it.
- **Since M7-3b: the golden header's data hash is `3A04D4E370C643DA`** (`k` lines unchanged since M6-SH1). **Every save and
  recording made before M7-3b refuses `DataMismatch`** (`Save/PreM73bRefusalTests` pins it with de591f0's own fixtures in
  `Replays/pre_m7_3b/`). **D28 moves the header once more** (new keys in `ContentHash`) and so refuses every save again; no `k`
  line and no Malazan v Whirlwind digest may move (M7-3 + M7-3b's guarantee: a moved one is a finding, not a re-pin).
- **Tech and ability ids since M7-3b:** `age_ii` 0, `armor_1` 1, `armor_2` 2, `melee_weapons_1` 3, `melee_weapons_2` 4,
  `ranged_weapons_1` 5, `ranged_weapons_2` 6, `moranth_supply` 7, `dryjhnas_prophecy` 8; `cusser` 0, `telas_fire` 1, `sandstorm` 2.
  D28's `teblor_blood_oil_anointing` becomes tech 9 and `blood_oil_frenzy` ability 3 (appended).
- **Pinned matches on `main` after M7-3b (outcomes unchanged, digests re-measured):** the Hard v Normal tally **32 / 8 / 2** (Raraku
  10 / 4 / 0, Pale Hills 11 / 1 / 2, Vathar 11 / 3 / 0); `StandardPresetPinQaTests` (8 save scenes + 7 AI rows), `NonHardMatchPinQaTests`
  8, the 8 `CounterCompositionTests.TheNonHardPins_*`, `ReserveTwinTests` (2 rich rows), `SharedVisionFfaPinTests` (2): all re-pinned
  with the old value in a comment; **QA's `OutcomePinM73bQaTests` (Stress, 20 id-free fingerprints equal to de591f0's)** is the
  outcome pin that must hold through any later renumbering; `HardKnobsPinTests` / `HardKnobsMatchTests`, `ScoutManagerTests` (311),
  `GatherBonusTests` (169 / 191), the premap AI replay (81 commands; its Research ids moved 4 → 3 and 7 → 5 by key);
  `MatchCriterionTests` (Vathar 23,322 / Raraku 28,231 / Pale Hills 50,140 / generated 17,053 / FFA 56,896 / 2v2 26,070);
  `MapSlice2StressTests` (49,565 / 55,485); `D21OpeningsQaTests` / `DH6ThresholdQaTests` (outcomes only); `HuntStressTests`
  (75,382 / 53,892 / 64,226); the Teblor-copy FFA hash 6C448DFB90134A32 (`FactionOrderQaTests`), the four `FactionOrderTests` copy
  pins (Stress).
- **Known stalls / skips with ids on `main`:** Pale Hills FFA seed 1 (BUG-1068 / 1116), `FarExpandProbeQaStressTests` Pale Hills FFA
  seed 2 (BUG-1151), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (BUG-1156), BUG-1161's
  three rows, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the view's
  `QaM7V1Test` zoom-40 1.5 head row; `DebugOverlayTest`'s 1 ms frame row flakes under three-track load (passes on a rerun).
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.
- **The third faction's landing order:** sim M7-3 (done) → sim M7-3b (done) → data **D28** (next session) → sim M7-4 (the Frenzy
  aftermath) → data / sim balance → view (the setup's third row, the 1.25× building placeholder).

---

## Sim track

### Where we are
M7-3b on `main`. Counter **4 / 4: this session is the hardening.** Debt: BUG-1191 S3 (new), BUG-1190 S4, BUG-1173 S4, BUG-1170 S3
(machine state), BUG-1165 / 1166 S4, the three `SaveFormat` alias names, BUG-1161 S4, BUG-1149 / 1151 / 1152 S3, BUG-1150 S4, BUG-1010.

### Current session plan (2026-11-11-1317)

**TASK_ID:** M7-H1 · **TASK_TITLE:** The sim's hardening: make the sim-owned tests third-faction-ready (BUG-1191), then the forged `REGN` part (BUG-1165), off-grid regen rates (BUG-1166), BUG-1190, BUG-1173 · **SESSION_TYPE:** hardening · **QA_TIER:** full · **Bug ids:** 1220+

**Goal:** The Teblor roster (data task D28) is being written in parallel this session. It can merge only if every test
outside the data track's folders stays green once `game/data` holds a third faction, and the data track may not edit
those tests. Today they don't: on a scratch clone of `main` with a Malazan copy added as `teblor`, 66 rows in 22
sim-owned classes fail in the default loop, and every 3-4 player row on default seats is re-seated because
`SimConfig.SeatOf` gives player p faction `p mod factions` (read `studio/bugs/BUG-1191-*.md` first: it has the recipe,
the class list and typical messages). Make the sim-owned suite hold for any appended faction without changing one
pinned value, then pay the listed debt.

**Scope (in this order; items 1-3 are the session's must):**
1. **A tool:** `tools/qa/third-faction-copy.ps1 -Repo <path> [-Id <snake_case id>]`: adds a copy of Malazan's faction
   files as faction `<Id>` to `<Repo>\game\data` and appends it to `common/factions.json` (the same replacements as
   `Data/FactionOrderTests.AddTeblor`, with the tech and ability names derived from `<Id>` so it works for any id). It
   refuses when `factions/<Id>` already exists and when `<Repo>` is under `.claude\worktrees` or is the main checkout
   (it is for scratch clones only; say so in its header). Use it for step 2; QA, the view and the data track use it later.
2. **BUG-1191, the sim's part.** On a scratch clone with the copy applied (never in your worktree), run the default
   loop and the Stress category and fix every failing test outside `Content/**` and `QA/Content/**`, by these means only:
   - a pin about the two shipped factions pins them (a prefix of the tables, "the first two factions"), not the whole data;
   - a row that means Malazan v Whirlwind says so with explicit `Seats` (docs/03: explicit default seats hash the same as
     the defaults, so the pinned value must not move); a row about the default rule itself computes `p mod factions`;
   - a fixture that needs "a faction the data lacks" (`AddTeblor` and its users) picks an id the shipped data does not
     have, and one that needs "exactly two factions" (order-file error counts, byte-for-byte save pins like
     `SaveFormatTests.AGeneratedMatch_SavesFormat4_ByteForByte_AsBeforeMapFiles`) builds on a temp copy cut to the
     first two factions of the order (a shared helper next to `TestSim.ShippedFactions` is fine);
   - counts read the data (`14` units, the town-hall list, BUG-1173's ten per-faction ternaries: by slot from the data).
   **No pinned literal changes value** (digest, state hash, outcome tick, byte count, the golden). A pin that cannot hold
   under a third faction by these means is reported with its numbers, not re-pinned.
3. **The kill counter (BUG-1191's scene finding).** The view's `QaM6V11Test` reads "row 3 UnitsKilled '68' (oracle
   '72')" in a four-seat match (seed 11, map 128, seats malazan human, whirlwind ai hard, teblor ai normal, malazan ai
   hard) once seat 2 plays a third faction. Add a sim test with an independent oracle (count from `World.Deaths` each
   tick, by killer's owner) against the match stats' units-killed for every seat, in a four-seat AI match with seat 2 on
   a third faction (temp-dir copy), 21,600 ticks, seed 11 and two more seeds, and the same on the shipped two factions.
   If the sim's counter is wrong: fix it with the regression row, provided no Malazan v Whirlwind pin moves; if a pin
   would move, stop and report. If the sim's counter is right on every row, say so (the view track is checking the
   scene's oracle this session).
4. **BUG-1165:** a forged `REGN` part is refused at load with `SaveError.Malformed`: `LastDamagedTick <= tick` for live
   units and `-1` for free ones; `RegeneratingUnits` and each slot's count recomputed from (or checked against) the
   entries. The two skipped `RegenQaTests.AForgedRegenPart_IsRefused` rows un-skipped. No save-format number changes.
5. **BUG-1166 (Producer ruling: refuse, as `pop` does):** a `faction.json` `regen.hpPerSecond` and an `applyStatus`
   `regenerating` magnitude that is not a multiple of 0.05 is a load error naming the value and the step. The two
   skipped `RegenQaTests.AStatusMagnitude_ConvertsAsTheSameFactionRate` rows rewritten to the rule and un-skipped.
   docs/03 "Implementation (M7-1)" says so. (D28 writes `regen` 1 / 6 this session: on the grid.)
6. **BUG-1190:** (1) a row asserting `seed21_playable.replay`'s Research at tick 5556 is refused, **in a new test
   file**; (2) `TechDataQaTests`' hash loop runs to `d.Techs.Length`.
7. **BUG-1173:** item 1 a docs/03 "Implementation (M7-3)" line on a zero-worth pack unit; item 2 the Windows
   folder-case message; item 3 is done by step 2.
8. **Only if it fits:** the three `SaveFormat` alias names; BUG-1161 as its file says.
9. **BUG-1170, one measurement, no chasing:** one `Category=Perf` run, at a moment no other test host or Godot is
   running if you can find one; report the numbers and the PC's load. All green: propose closing it as machine state.
   Otherwise add the numbers to the file (as a patch) and move on. No bound changes, no profiling.

**OUT of scope:** any byte under `game/` (the view's `game/tests/QaM6V11Test` included); any byte under `game/data`;
`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` and `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` (D28 edits both
this session); everything under `sim/Rts.Sim.Tests/Content/**` and `QA/Content/**`; `sim/Rts.Sim/ViewApi/**`; changing
the default seat rule itself; any re-pin; any save or replay format number; BUG-1010; M7-4; Request 46 / 47.

**Size:** up to about 1,500 changed lines (QA's suites don't count). If steps 1-3 alone approach that, drop from the
bottom (8, then 7 item 2, then 6) and say what was dropped.

**Acceptance criteria:**
1. `tools/qa/third-faction-copy.ps1` exists, works for two different ids on a scratch clone (the data loads, smoke
   prints PASS there) and refuses a worktree path and an existing folder.
2. On a scratch clone with the copy applied: the default loop
   (`Category!=Perf&Category!=Stress&Category!=Soak`) has **0 failures outside `Content/**` and `QA/Content/**`**
   (66 today), and so has the Stress category apart from rows that are skipped or known-stalled on `main` today
   (named with their bug ids). The report gives before / after counts and the list of data-folder classes still red
   (the data track's, for information).
3. On the branch as it is (two factions): build 0 / 0 and the default loop green; **the diff changes no pinned
   literal's value** (list every test file touched and, for each, the means used from step 2); the Stress pin classes
   (`OutcomePinM73bQaTests`, `FactionOrder*`, `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`,
   `SharedVisionFfaPinTests`, `ReserveTwinTests`, `MatchCriterionTests`, `HuntStressTests`, `MapSlice2StressTests`)
   green and unedited in their pinned values.
4. The kill-counter rows of step 3 exist and are green, or the defect is fixed with its regression row, or (a pin would
   move) reported with the four missing kills identified.
5. BUG-1165: each forged shape refused with `SaveError.Malformed`, the rows un-skipped and green, 50 honest mid-match
   saves (some with a regenerating unit) load and re-save byte-identical.
6. BUG-1166: off-grid rates refused at load with the message; the two rows un-skipped; the shipped data loads; docs/03.
7. BUG-1190's two rows; BUG-1173 items 1 and 2 (each with its test or doc line).
8. The Perf run reported under step 9's rule; bug-file updates delivered as a patch in the scratchpad (you cannot
   write `studio/**`).
9. Determinism (the same hashes in 3 processes on a probe that saves and loads a regenerating unit); 0 B per tick
   (`AllocationTests`); `ArchitectureTests` green; smoke PASS; every test process you started is ended (rule 52).

**Design references:** `studio/bugs/BUG-1191`, `-1165`, `-1166`, `-1190`, `-1173`, `-1161`, `-1170`; docs/03
"Implementation (M7-3)" (the faction order, the proof on a copy), "Implementation (M7-1)" (the `REGN` part, the rate
conversion), "Save format (M6-S2)", the seat sections that state `p mod factions` (search "p mod factions"); docs/02
"Faction template".

**Tests required:** the readiness itself is the test (criterion 2's two runs on the copy); the kill-counter oracle
rows; the un-skipped `RegenQaTests` rows plus load-error rows for off-grid rates at both places; the seed-21 refusal
row; one row per `third-faction-copy.ps1` refusal is not needed (show the output in the report).

**Constraints (CLAUDE.md rules most at risk):** no golden or digest regenerated (testing rules: a changed hash needs
a reason, and here there is none to give); load validation only, nothing per-tick, no allocation in tick code; no
`Dictionary` / `HashSet` iteration in anything that recomputes save counts; no stat or faction id hard-coded in
`sim/Rts.Sim`; docs updated in the same commit (docs/03 only; not `docs/factions/**`).

**QA focus (full).** (1) Readiness, attacked: apply `third-faction-copy.ps1` with two ids (one sorting before `malazan`, one after
`whirlwind`; both appended) and a hand-made Whirlwind-based copy; the sim-owned default loop and the Stress pin classes must be green
on each; grep the diff for any removed or changed hex digest, tick or byte-count literal (none allowed). (2) The kill counter: your
own oracle on 5 seeds, four seats, a third-faction seat in each position. (3) Forged saves: every `REGN` field at its edges
(`LastDamagedTick` equal to, one past and far past the tick; a total one off; a dead slot carrying values), 10,000 mutated `REGN`
parts with no crash and no load of an inconsistent part; the honest round trip. (4) Regen rates 0.05, 0.049999, 0.1 + 1e-9, 1e300,
-0.05, at both places. Suites (rule 53): the default loop with Stress, then Perf alone under rule 42.

### Next task candidates
1. If D28 is held: "what the real roster still breaks" from D28's list, first.
2. **M7-4:** Blood-oil Frenzy's aftermath (Producer default: `applyStatus.onEnd {status, magnitude, duration}`), with Request 47
   (a building `scale`).
3. Request 46 (a per-faction `counters` override); the Teblor balance pass's sim side.

---

## View track

### Where we are
M7-VH1 on `main`. Scene loop 93 / 93. Counter **0 / 4**. Debt: BUG-1200 S4, BUG-1177 item 2 (the owner's taste), BUG-1115 item 4.

### Current session plan (2026-11-11-1317)

**TASK_ID:** M7-V3a · **TASK_TITLE:** The view ready for a third faction, proven on a forged copy: the stats scene's kill count (BUG-1191), a three-faction setup scene, the "Regenerating" cue, BUG-1200 · **SESSION_TYPE:** feature · **QA_TIER:** standard · **Bug ids:** 1230+

**Goal:** The Teblor data files (D28) are being written in parallel and are not on `main` yet, but everything the view
must do for a third faction can be built and proven now on a forged data copy, the way `QaM7V1Test` forges unit
scales. One thing is already known to break: with a third faction on disk, `QaM6V11Test` fails ("row 3 UnitsKilled
'68' (oracle '72')", `studio/bugs/BUG-1191-*.md`). Find out why, prove the skirmish setup and a match work with three
factions, and add the cue that shows a unit is regenerating (the Teblor's faction bonus).

**How to get a third faction (never in your worktree's `game/data`):** in a scene, forge a copy of `res://data`
under `user://` as `QaM7V1Test` does and add a faction to it: copy `factions/malazan/*` to `factions/<id>/`, replace
`malazan_` with `<id>_`, `moranth_supply` with `<id>_upgrade`, `"telas_fire"` with `"<id>_fire"`, `"cusser"` with
`"<id>_cusser"`, set `faction.json` `id` (and a different `displayName`), append `<id>` to `common/factions.json`
`order`. To reproduce the scene-loop failure itself, do the same by hand on a **scratch clone** (BUG-1191's recipe,
id `teblor`) and run `tools/qa/scene-loop.ps1 -Filter QaM6V11Test` there.

**Scope:**
1. **`QaM6V11Test` with a third faction (BUG-1191).** Reproduce on a scratch clone (seed 11, seats malazan human,
   whirlwind ai hard, teblor ai normal, malazan ai hard; fails at tick 21600). Identify the four kills the stats row
   and the oracle disagree on (who killed what, at which tick, why one side counts them). If the scene's oracle is
   wrong, fix the oracle. If the sim's counter is wrong, do not work round it: deliver a bug patch with the smallest
   repro you can state in sim terms (the sim track is adding a sim-side oracle row this session) and leave the scene
   asserting the truth. Either way the scene stays green on the shipped data.
2. **A new scene, `ThreeFactionTest`** (forged copy, a third faction with its own `displayName`): the skirmish setup's
   faction list shows three entries in the data's order with the data's names; an added third slot defaults to faction
   `s mod factions` (docs/03); a slot can pick the third faction; a 3-player match with one seat per faction starts
   and runs 600 ticks with no ERROR line; the third faction's hall, worker, production menu and tooltips read its data
   names; the stats screen lists it; a save of that match loads and a recording of it replays with every checkpoint
   equal; `--faction <id>` on the command line seats it. Take a `--screenshot` of the setup with three factions and of
   the running match, look at both, and describe them in the report.
3. **The "Regenerating" cue.** Read docs/03 "Implementation (M7-1)" for what the sim exposes (the faction's `regen`,
   `LastDamagedTick`, the `regenerating` status). When a selected unit is healing by regeneration this tick (the
   faction rule after its delay, or a `regenerating` status), the selection panel shows a cue with text from data: the
   `regenerating` status's `displayName` if the panel already lists statuses that way, otherwise one new `ui.json`
   key. It disappears when the unit is hit or at full health. If the view cannot tell from what the sim exposes, add a
   read-only accessor in `sim/Rts.Sim/ViewApi/` (copies values out, changes no state, tick or hash; with its test in
   the ViewApi test folder). Staged in a scene on a forged copy with `faction.json` `regen` `{ "hpPerSecond": 1,
   "delay": 6 }`: hurt unit, no cue before 6 s, cue after, gone on the next hit, gone at full health; the touched
   panel code allocates nothing per frame while the cue is steady.
4. **BUG-1200 items 1 and 2:** delete the stale comment above `UiText.IsTipFormat`; "1 seconds" (Producer ruling:
   the shipped sentences keep their words; the number-with-unit phrase comes from two new `ui.json` keys, singular
   and plural, and the tips' `{1}` receives the phrase, so exactly 1 reads "1 second" and 2 reads "2 seconds" as today).

**Files:** `game/**` except `game/data/**`; `game/data/common/ui.json` (this session it is yours alone; new keys only,
plus the two tip strings for item 4; first confirm `ui.json` is not part of `GameData.ContentHash`:
`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders` must stay green after your edit; if it goes red, revert
the `ui.json` edit and report, because D28 regenerates the golden header this session); `sim/Rts.Sim/ViewApi/**` and
its tests (read-only accessors only); docs/03 (an "Implementation (M7-V3a)" section, appended).

**OUT of scope:** `game/data/factions/**` and `common/factions.json` (D28's); the real Teblor row, names or colours
(M7-V3b, after D28 lands); the 1.25× building placeholder (it needs a number in the data: Request 47 for the sim
track; do not type a per-faction scale in C#); any sim change outside `ViewApi/`; `tools/**`; the art pass.

**Acceptance criteria:**
1. The report names the cause of 68 v 72 with the four kills identified. On a scratch clone with the third faction
   applied, `QaM6V11Test` is green (oracle fixed), or a sim bug patch with a sim-terms repro is delivered and the scene
   is unchanged in what it asserts. On the branch (shipped data) `QaM6V11Test` is green.
2. `ThreeFactionTest` exists, is in the scene loop, and checks every clause of scope item 2; the two screenshots were
   looked at and are described.
3. The cue scene checks the five moments of scope item 3 and 0 bytes per frame; the text comes from data (grep: no new
   player-facing literal in `game/scripts`).
4. BUG-1200: the comment gone; a forged Easy interval of exactly 1 s reads "1 second"; the shipped Easy and Hard tips
   read exactly as on `main` ("Thinks every 2 seconds ...", "... thinks every 0.5 seconds ...").
5. Build 0 / 0; `tools/qa/smoke.ps1` PASS; the whole scene loop green on the branch (93 + the new scenes; a
   `DebugOverlayTest` 1 ms row red under load counts only if it is red again alone); the default sim loop green if
   `ViewApi/` changed; the state hash of a 3,000-tick match identical with and without the view's accessor being called.
6. docs/03 "Implementation (M7-V3a)"; bug-file updates as a patch in the scratchpad; every Godot or test process you
   started is ended (rule 52), and you kill no other track's process.

**Design references:** docs/03 "Implementation (M7-1)", "Implementation (M7-V1)", "Implementation (M7-VH1)" (the
tips), the M6-V5 / M6-V16 sections on the setup's slots (`s mod factions`), "Implementation (M6-V11)" (the stats
columns and the scene's oracle); docs/02 "Game flow" (the skirmish setup) and "Factions"; `docs/factions/teblor.md`
"Faction bonus" (regeneration 1 HP/s after 6 s); `studio/bugs/BUG-1191`, `BUG-1200`.

**Tests required:** `ThreeFactionTest` and the cue scene (one scene may hold both if staged separately), the
`QaM6V11Test` fix or the bug patch, a ViewApi unit test if an accessor is added, the tips' rows in the existing
difficulty-tip scene.

**Constraints (CLAUDE.md rules most at risk):** rule 8 (every word the player reads comes from data); rule 7 (the
cue is read from sim state each frame, the view stores no gameplay state); rule 1 and the ViewApi rule (read-only,
no hash move); rule 6 (no per-faction constant in C#); visuals changed, so look at the screenshots before claiming.

**QA focus (standard).** The forged copy pushed: eight factions (`DataLimits.MaxFactions`) in the setup's list, a third faction
with a 40-character `displayName`, four slots each on a different faction where the data has four; a save and a recording of a
three-faction match loaded and replayed; the cue with 30 units selected, a unit hit every 5.9 s (never shows), a unit with the
status and the faction rule at once; "1 second" at exactly 20 ticks, 19 and 21 either side; `QaM6V11Test` on a scratch clone with the
third faction (green, or the sim bug's repro confirmed). Suites: the default loop, the scene loop, smoke.

### Next task candidates
1. **M7-V3b** once D28 is on `main`: the real Teblor row verified with a screenshot, the building placeholder at the data's scale
   (after Request 47), anything D28's list shows in view-owned scenes.
2. An owner item (the art call, ".NET 10 installed", templates, the sound notes) whenever one lands.

---

## Data track

### Where we are
D-R37 on `main`. Counter **0 / 4**. Open debt: BUG-1210 S4 (in D28).

### Current session plan (2026-11-11-1317)

**TASK_ID:** D28 · **TASK_TITLE:** The Teblor roster: the faction's data files written from `docs/factions/teblor.md`, pinned both ways, with BUG-1210 · **SESSION_TYPE:** feature · **QA_TIER:** standard · **Bug ids:** 1240+

**Goal:** Ship the third faction as data. `docs/factions/teblor.md` holds every id, number and player-facing word
(pin-ready since D-R36 / D-R37); the sim has every rule the page needs (`scale`, half-step pop, `regen`, `giant`,
the pack's `spawnCount` and `onHit` passive, the append-only faction order with techs and abilities under it). Write
the files from the page, pin the page to them both ways, and regenerate the golden replay's header once. The owner
left no answer on the Teblor names in the inbox, so the page's words stand as written.

**Scope / files (the only ones you may change):**
- `game/data/common/factions.json`: append `"teblor"` to `order` (last).
- `game/data/factions/teblor/**` (new): `faction.json` (`displayName`, `description`, the bonus name and text, `regen`
  `{ "hpPerSecond": 1, "delay": 6 }`, colours as the other factions carry them), `units.json` (seven rows from the
  Units table: `scale` 1.5 except the War-dog 1, class `giant` where the page says Giant, pop 2-3, train times as
  printed, the Warrior ×1.5 vs Mounted, the Destrier Rider ×1.5 vs Light, the Shaman's splash 1.5 m and detector 10 m,
  the War-dog `spawnCount` 3 with the `onHit` passive `{ "trigger": "onHit", "status": "slowed", "magnitude": 0.2,
  "duration": 2 }` and its Age II requirement), `buildings.json` (the ten rows), `techs.json`
  (`teblor_blood_oil_anointing`: two `armor` effects), `abilities.json` (`blood_oil_frenzy` as the page's Abilities
  section spells it; no aftermath, no damage share), `ai.json` (the `standard` "War band" opening: the 25 steps, the
  compositions, the attack and retreat lines, the `difficulties` block equal to the other factions' rows including
  `counterStrength`).
- `docs/factions/teblor.md`: remove "proposed" / "will hold" wording where the files now decide; measure and fill what
  the page marks "not measured" only where a content test already measures the same thing for the other factions
  (otherwise leave the sentence and say so in the report).
- `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`: the pins now run for three factions; BUG-1210's
  three items (rows derived from `ContentFactions.Keys`, the opening id from the page's High bullet, the Line unit by
  slot, `UniqueHall` through `Typed`); `PagePinReadinessTests` and `TeblorPageParseQaTests` folded into or replaced by
  the real pins where they now duplicate them.
- Under rule 51 only: `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` (the header's data-hash line and the
  checksum, nothing else) and the reason comment in `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs`.
- If the page needs a field a schema lacks: write it as a Request for the sim track in your report and leave the
  page's "not in the data" sentence. Never a C# change outside the two test folders.

**OUT of scope:** every Malazan and Whirlwind file byte; `game/data/common/**` other than the one line in
`factions.json` (`ui.json` is the view's this session); any sim-owned or view-owned test file other than the two
rule-51 files; the Frenzy aftermath and damage share (M7-4); a per-faction `counters` override (Request 46); the
buildings' 1.25× drawn size (Request 47); balance changes (the numbers are the page's; the balance pass comes after
M7-4); models and art.

**Known and expected (BUG-1191, read `studio/bugs/BUG-1191-*.md`):** with a third faction in `game/data`, tests the
data track does not own assume two factions (66 rows in 22 sim-owned classes on a Malazan copy, e.g.
`FactionOrderTests.TheShippedIds_*`, `QA.TeamQaTests`, `DataValidationTests.ShippedData_TrainedAt_ResolvesForAll14Units`;
the view's `QaM6V11Test`). The sim and view tracks are fixing those in parallel this session. **Do not edit them and
do not work round them.** On your branch they will be red; your job is to list them.

**Acceptance criteria:**
1. The shipped data loads with three factions (`DataLoader.LoadAll` on `game/data` returns no error; smoke prints
   PASS; `--skirmish` with a Teblor seat boots clean). Ids as M7-3b promises: `teblor_blood_oil_anointing` is tech 9,
   `blood_oil_frenzy` ability 3, every shipped unit, building, tech and ability id unchanged.
2. Every cell of the page's Units, Buildings, Techs, Abilities, openings, opening-1 steps, composition, Difficulty
   and "How the AI adapts" tables is pinned to the files both ways: the converted pin classes run 3 of 3 factions, a
   page cell changed by hand reads red and a file value changed by hand reads red (show one of each).
3. Every `displayName` and `description` in the Teblor files equals the page's "Owner review" table, character for
   character (pinned).
4. BUG-1210's three items.
5. One golden header regen with its reason in `GatherWedgeQaTests`' comment: the diff of
   `cross_map_seed1.replay` is the data-hash line and the checksum only; no `k` line moves.
6. **Zero failures in the default loop (`Category!=Perf&Category!=Stress&Category!=Soak`) under `Content/**` and
   `QA/Content/**`, and the four data filters (`Content` + `QA.Content` + `DataValidation` + `DataContentHash`) green
   except rows that are two-faction literals in a sim-owned file.** Every other red row on your branch is listed in
   the report with its class, test and one-line message, sorted into "a two-faction assumption (BUG-1191)" or
   "something the real roster breaks" (for example an AI test that iterates every faction and fails on the Teblor's
   numbers). The second kind is the valuable part of this list: be exact.
7. No Malazan v Whirlwind digest moves on explicit two-faction seats: run the sim pin classes with their Stress rows
   (`OutcomePinM73bQaTests`, `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, `SharedVisionFfaPinTests`,
   `ReserveTwinTests`, `CounterCompositionTests.TheNonHardPins_*`) and list each as green, or red with its message
   (a row red only because default seats now put a Teblor on seat 2 goes in the BUG-1191 list). None is edited.
8. Build 0 / 0. The report ends with the owner's review table: every Teblor unit, building, tech, ability and opening
   with its main numbers, each place the files differ from the page (there should be none) and why, and each "not in
   the data" item with the request it becomes.

**Landing rule (the Producer's, at ACCEPT):** your work is judged on criteria 1-8. It merges this session only if the
tree with the sim's and the view's branches merged first is green; otherwise it is accepted and held on its branch
for one session while the sim track fixes what your list shows. Either outcome is a success for this task.

**Design references (the numbers must match):** `docs/factions/teblor.md`, every table and the "Owner review" table;
docs/02 "Factions" (template slots, Line ×1.5 vs Mounted), "Faction template", "Damage type × armor class" (Giant),
"AI opponent"; docs/03 "Data schema", "Implementation (M7-1)" (scale, pop, regen, giant), "For the data track
(M7-2)" (the pack row, `onHit`), "Implementation (M7-3)" (the order file, the pack's worth: 120 / 0 over 3 is exact),
"For the data track (M5-1)" and "Implementation (M5-8a)" (`ai.json`); docs/01 change log 2026-11-10 (d).

**Tests required:** the page pins for three factions (existing classes, now 3 of 3); a Teblor roster row set in
`QA/Content/UnitRosterQaTests`; a content test that a Teblor AI seat on the `standard` opening sends its first wave
on the generated map (seed 1, Normal, 20 minutes) and names its tick, as the other factions' opening tests do; the
both-ways mutation rows of criterion 2.

**Constraints (CLAUDE.md rules most at risk):** rule 6 and rule 8 (every number and word in data; names only from
the page); the data-validation test after every data change; a golden hash changed on purpose is regenerated in the
same commit with the reason; no file under `game/data/` shared with another track (yours: `common/factions.json` and
`factions/teblor/**`; the view's: `common/ui.json`); end every test process you started (rule 52).

**QA focus (standard).** A Teblor AI seat plays 12,000 ticks on the generated map and on each of the three map files without a
refusal streak (name every refused command kind); Teblor v Malazan and Teblor v Whirlwind twins hash-equal in two processes; a
war-dog order spawns three and a bite slows its victim 20% for 2 s; regeneration starts 6 s after the last hit at 1 HP/s; a giant
takes x1.25 from pierce and magic; pop 2 and 3 count against the cap; a page cell changed reads red and a file value changed reads
red; smoke `--skirmish` with a Teblor seat boots clean. **Check the developer's list of red rows outside the data folders:** rerun
them, confirm each sorting ("two-faction assumption" v "the real roster breaks it"), and add any the list missed. Suites (rule 53):
the default loop, the four data filters, the sim pin classes named in criterion 7 with their Stress rows.

### Next task candidates
1. The owner's answers if any land (names, the Warrior's bonus, the counters, `goldShare`, BUG-0780, `counterStrength`).
2. The buildings' `scale` 1.25 once Request 47 is on `main`; the Teblor balance pass after M7-4.
