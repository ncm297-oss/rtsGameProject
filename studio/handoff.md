# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-14-1246** (session 2 / 8 of 2026-11-14, started 12:46, about 8 hours).
Sim **M8-1** ACCEPT (feature, 0 fix rounds, QA full PASS_WITH_ISSUES), view **M6-V21** ACCEPT (0, QA standard
PASS_WITH_ISSUES), data **D32** ACCEPT (0, QA standard PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10,
M7 3 / 4, M8 started (0 / 3).** Counters: **sim 1 / 4, view 2 / 4, data 2 / 4**. Bug ids next session: **sim 1430+, view
1440+, data 1450+**. Open S1 / S2: none. The next session is 3 / 8 of 2026-11-14 (or 1 / 8 of the 15th).

**The inbox holds three owner requests from 2026-11-14, not yet processed (they landed after the PLAN): process them
first at the next PLAN; inbox requests come before roadmap work.** (1) Gatherers may overlap a bit while collecting
(sim; a data knob; movement). (2) Teblor models first: the five humanoids at the front of the art queue (view). (3)
Shift-click queues orders: audit what queues, close gaps, make the queue visible (sim + view).

**Next session in one line (proposal):** sim **the gatherer-overlap request** (feature, QA full; a `rules.json` knob,
one hash move, the golden and the AI pins re-measured: rules 41 / 43 / 45) · view **the Teblor humanoids** (BUG-1412 and
BUG-1410 first; feature, QA standard) · data **a light task with no hash move** (BUG-1400 first; BUG-1421 items 3-4,
BUG-1360; or the Shadow page made pin-ready), because the sim moves the economy and the hash that session.

## Where things stand

- **`main` after this session = sim 7b1a0f5 + view 35cceb9 + data c2fa20e** (plus this ACCEPT's studio files). The
  Producer's scratch clone of that tree (`acc1246` in the session scratchpad; conflicts only in
  `studio/bugs/README.md` and `studio/qa/coverage.md`, both sides kept): build `--no-incremental` **0 errors / 1
  warning (BUG-1412)**; the default loop **9,359 / 0 failed / 19 skipped of 9,378 in 20 m 28 s (beside the scene loop)**; smoke **PASS**; the scene loop **105 of 105, first pass**.
- **Forest stealth is a rule, on test data only (M8-1).** `faction.json` `forestStealth { status, delay, exceptSlots }`;
  `ForestStealthSystem` runs in phase 5 before `StealthSystem.Run`; per-unit state in `UnitStore.Forest`
  (`ForestStealthStore`: `LastAttackTick`, `Held`), allocated, hashed (state-word bit 22) and saved (`FRST` after
  `REGN`) only when the data has a faction with the block; one status entry is shared with a timed stealth
  (`StatusSystem.Hold`). No shipped file, pin, digest or format moved (`CurrentVersion` 10). docs/03 "Implementation
  (M8-1)" has **"For the data track (M8-1)"** (the exact block the Shadow `faction.json` will carry) and "For the view
  (M8-1)" (nothing new is needed). Cost alone: forest brawl 1.21 ms a tick, 0 bytes; 2,000 hidden units 0.73 ms.
- **Six unit types are figures (M6-V21)**, the clips follow the game speed, the Heavy Infantry and the Raider swing a
  sword clip, the Archer keeps the one-handed shot (no bow clip in UAL1 / UAL2). **The Producer's bench, alone (load
  3-4 %, no other test host): 400 figures 10.11 ms while 90 %+ alive (worst 1 % 14.52; limit 16.7), 400 capsules
  0.77 ms; 1,000 figures 26.87 ms (worst 1 % 32.79), 1,000 capsules 1.29 ms; 4,542 / 12,219 draw calls.** docs/03
  "Implementation (M6-V21)" still prints the developer's figures (11.26 / 28.17, two other test hosts running): the
  view's next task replaces them with these.
- **The Teblor AI's second pass shipped (D32, variant X7, `ai.json` alone).** Data hash and golden header
  **5353DF8A1473FC6D** (was 27036D3A2F2BA1CB); the golden's 15 `k` lines unchanged; save format 10. Every save and
  recording from before is refused `DataMismatch` (fixtures in `Content/Fixtures/pre_d32/`). 64 Normal matches: the
  target on 8 of 8 seeds, defeated 10 (was 14), a Shaman in 27 (was 4), a second Longhouse in 18 (was 0), Age II by
  10:00 in 55, a wave in 62. **The price (BUG-1420 S3): Breakers, Destrier Riders and War-dogs are fielded in 0 of 64
  on Normal (as before) and are now gone on Easy (Breakers 8 -> 0) and Hard (Breakers 22 -> 0, Riders 18 -> 5, dogs
  8 -> 0, the Anointing 43 -> 19)**; Hard is beaten less (18 -> 10). The "64 matches" are 34 distinct ones on Normal /
  Hard (the named maps barely vary by seed).
- **The Frenzy report on D32's opening** (`FRENZY_REPORT_OPENING=Content/Fixtures/teblor_standard_d32.json`, 32
  matches on the menu's map): a Shaman in 5, 8 casts sent / 8 landed, defeated 10. On the 64-match row 22 casts
  landed in 18 matches. So a fielded Shaman does cast with today's rule; Request 53's second half is a reading, not
  yet a rule. docs/03 "Implementation (M7-7)"'s table is D31's opening (a Producer note under it says so). The
  report's "army pop 15:00 (sum)" reads 0 for both openings with 19 undefeated matches: nobody has looked at why.
- **Why the expansion waited (D32):** gold, not list order: the bank held the Longhouse's cost in 36 of 22,926 thinks,
  and "base defended" was true in 14,088 thinks, which switches the reserve off. Two Requests for the sim track are
  in STATE (54: count only a real raid as "defended"; 55: a `train` step that may spend through an earlier step's
  reserve).
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's list).
- **After integration, at the PLAN:** set BUG-1391 to fixed (items 1, 2, 4 by D32; item 3 by M8-1: the Producer could
  not edit it in the sim worktree without a conflict with the data's edit).

## Standing rules (1-65)


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
**54:** before a task adds a faction, a data type or a default that every test reads, the Producer applies it on a scratch clone
and runs the default loop and the scene loop at PLAN; "unblocked" is said only after that. **55:** that rehearsal uses a
copy that exercises every hook the new content will use (a passive, a scale, a pack, an aura, a new radius), or the content
branch itself when it exists; a Malazan copy alone missed 28 rows this session.
**56:** a "is drawn right" claim about new content is checked on a screenshot of that content in the shipped data,
not on a forged copy with other numbers (M7-V1's 1.5× looked right on a 0.4 m unit; the real giants are 0.75 m).
**57:** a QA regression scene or test that is red by an open S3 reaches `main` only by an explicit ruling at ACCEPT
(named in the session log and STATE's Tests row), with the fix as the first item of that track's next brief; until then
every loop's expected result names that one row. Two sessions red = the fix becomes the whole task.
**58:** a test helper that forges a data row must tolerate the shipped data already carrying it (look the id up,
then add or edit), and when docs/03 tells the data track which rows to write, the sim's QA applies them on a scratch
clone and runs the task's classes (BUG-1280).
**59:** a developer who writes a fix for "the data track's rows break our tests" runs its own new tests on a copy
carrying those rows before reporting (M7-6's fix broke on exactly the rows it was written for: BUG-1310 / 1311, one fix
round). A sim test about "every shipped row" reads the count from the shipped file, never a literal.
**60 (lifted 2026-11-13-2306):** BUG-1321 is closed for 400 figures (10.1 ms alone); model types may be imported again.
**61:** before more than ten unit types are models, a view task measures a 1,000-figure crowd of mixed types alone
(rule 42) and reports what 16.7 ms would take (merged surfaces, a shared skeleton or baked animation); every art brief
reports the 400- and 1,000-figure bench with the capsule figure from the same window and reads the "while 90%+ alive"
column. An LFS size criterion names "the art pass's binaries", never "total LFS".
**62:** a developer runs the full default loop after its last edit, or its report says in its first lines that
it did not (two sessions running a developer fixed one row and re-ran only the affected classes).
**63:** a scratch folder's name starts with its track and task (`m7v4-...`, `d31-...`), is created new, and no
agent writes into, cleans or resets a folder it did not create (2026-11-14-0616: the view developer reset another
agent's clone named `clone`; nothing committed was lost). A brief that asks for a scratch clone says so.
**64:** "build 0 / 0" in a report means `dotnet build RtsGame.sln --no-incremental` after the last edit (an
incremental build hid two warnings in a new test file: BUG-1380, one fix round).
**65 (new):** QA's re-check ends with `dotnet build RtsGame.sln --no-incremental` on the commit that holds QA's own
files, and its report says so (2026-11-14-1246: QA's new `QaM6V21Test` carried a CS8600 warning that the developer's
0 / 0 could not have seen: BUG-1412). A warning in a QA file with the developer's commit clean is S3, fixed first in
that track's next task; in the developer's own files it stays S2 (BUG-1380).
**61 (amended 2026-11-14-1246):** the measurement is done (M6-V21: 1,000 mixed figures 26.9 ms alone; merged surfaces
are worth about 4-5 ms, a shared skeleton or baked animation up to about 8 ms, both together about 14-15 ms). The owner
asked for the Teblor figures first (inbox 2026-11-14), so the checkpoint moves: **merged surfaces are the view task
right after the Teblor humanoids**, and every art brief still reports the 400- and 1,000-figure bench alone.
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.
(`SameGameDataHashes` is the name of the rule in that class's comments, not a test method: do not write it as a
criterion's test name again.)

## Watch-outs (all tracks)
- **Three factions ship now.** A fixture that means "the two original factions" uses `TestSim.TwoFactionData`,
  `TestDataDir.CopyOfShippedTwoFactions()` / `CutToFirstFactions(2)` (sim) or `ThirdFactionForge` (view, cut to two).
  A pin about "every shipped unit" must be true for the Teblor too (a bite, a pack, a scale, a self-cast aura, radius
  0.75, regeneration).
- **Radius 0.75 is a fourth radius class** (+131,072 bytes on a 1024 map; the shipped-data memory row allows it by
  class count). A single-type 0.75 m crowd arrives about 40% less often than a 0.4 m one on the crowd sweep's maps
  (`Stress/WideCrowdStressTests`, a report with no bound): 500 to 4 points 172.6 v 288.1 mean arrivals, 128 to 64 goals
  58.4 v 112.1. If a sim task changes movement, re-run it and print both columns.
- **The scene loop is all green: 105 scenes** (QA's `QaM6V21Test` is new; `QaM6V21Shots` and `ModelShots` are windowed tools, not in the loop). `QaM6V20Test`
  samples after a forced collection (objects flat at 3,252); BUG-1350 is fixed by cause, so an ERROR line there is a
  finding. Rows that flake under load are listed below; any other red row is a finding.
- **Model animation is manual:** every model's `AnimationPlayer` is in manual mode and `UnitViews._Process` advances it
  through `CrowdAnimator` (48 nearest every frame, the rest 1 frame in 3). A scene that calls `Sync` directly and wants
  motion calls `AdvanceAnimations(delta, eye)` too. A model shows no worker tint (`ShownTint` still records the state).
  Since M6-V21 the figures follow the game speed (`UnitViews.AnimationRate`: the runner's speed, 0 while the tree is
  paused); a replay that has reached its end is not paused, so its figures keep moving in place (a small view item).
  Each model type has its own `AnimationLibrary` (state names -> the shared clips, from the clip map in the `.glb`'s
  glTF extras); a slot's type and model are set before the fog check.
- **Six unit types are models** (Laborer, Crossbowman, Heavy Infantry; Camp Follower, Raider, Desert Archer); 15 of the
  two first factions' types and all seven Teblor types are capsules. A scene that reads a slot's own mesh, overlay or
  material for a model type reads nothing that is drawn (BUG-1410: `QaV5Test` line 203): read `ModelMeshOf(slot) ??
  ViewOf(slot)`.
- **Unit models are on by default** (`UnitViews.UseModels`; `--placeholder-art` forces capsules). Five scenes pin capsules
  and set it false: `EconomyViewTest`, `QaM6V3Test`, `QaM7V3cTest`, `ScaleViewTest`, `ShellTest`. A new scene that reads a
  slot node's own mesh or material must do the same or read `ModelMeshOf`. The model files are Git LFS: a clone needs
  `git lfs pull` then an import (a pointer file gives the capsule and one warning, never an `ERROR`).
- **`tools/import-assets.ps1` needs Blender 5.2 and `asset-sources/` (the main checkout's)**; its outputs are committed and
  a re-run is byte-identical (QA, fresh clone, 180 s). `tools/asset-manifest.json`, `tools/import-assets.ps1` and
  `tools/art/**` are the view track's from now on (the rest of `tools/**` stays the sim's): say so in each brief.
- **The Teblor rows are on `main`.** A Frenzy status id sorts after `stealthed`, and `teblor_frenzied` is the last row
  of `common/statuses.json`: a brief that adds a status says where it sorts (`StatusLoaderTests.Shipped_Regenerating_*`
  pins the sorted order on purpose). The two golden data-hash pins
  (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`)
  are sim-owned: a data brief that moves the hash is handed them by name (rule 51).
- **Two tracks must not both move the data hash in one session** (each regenerates the golden's header and checksum:
  a certain conflict). A sim task that ships a value in `game/data/common/**` and a data task that ships a faction
  file are planned in different sessions, or one of them lands test data only.
- **A red Stress row on `main`, by ruling (rule 57): `Content/TeblorLandingOpeningReportTests.TheRows_ChangeTheMatch_FromTheFirstFrenzyOn_Report`**
  (BUG-1400, red since D31; the default loop does not run it). First item of the data's next brief; until then a
  "default loop with Stress" run expects exactly this one failure.
- **`main` builds 0 errors / 1 warning** until BUG-1412 is fixed (`game/tests/QaM6V21Test.cs` line 257, `string?`):
  first item of the view's next brief.
- **Every data change moves the golden header now** (the Teblor use `damageBonus`, `onEnd`, `statusDuration`, a
  building `scale`, `carry` and a per-opening share): batch data changes into one hash move a session, and say in the
  report that older saves and recordings stop loading.
- **`QaM7V3aTest` fails on purpose at six shipped factions** ("no room for three clones under 8"). `ThreeFactionTest` and
  `RegenCueTest` never show the real Teblor (the forge cuts them away): M7-V3c adds a scene that does.
- **`tools/qa/third-faction-copy.ps1`'s display name "<id> (Malazan copy)" trips BUG-1230** at 1152 x 648 with five
  factions (5 setup scenes red from name width): use short ids or expect them.
- **A QA scratch scene left a data copy** at `%APPDATA%\Godot\app_userdata\RtsGame\qa-teblor-shot\` (could not be
  deleted: permissions). Harmless. Scratch scenes should write under the scratchpad, not `user://`.
- **A dead session loses the developers' reports and bug patches** (they live in the conversation / scratchpad). When a session
  is resumed, QA writes the proof lines and the Producer applies them at ACCEPT.
- **Three test hosts at once** flake the wall-clock Perf and scene rows (rules 42 / 53). The default loop is ~25-30 min alone,
  ~45-90 min beside other suites. **BUG-1170 is closed as machine state:** a Perf run is evidence only when `ASweepRetarget`
  reads near 16 us (85 us = the PC's slow state; do not chase it).
- Scene rows that flake under load and pass alone: `DebugOverlayTest` and `QaM25Test` (1 ms rows), `MinimapTest` (0.5 ms),
  `BenchTest` (7 s), `QaH1Test` (600 s timeout).
- Merge conflicts to expect: `studio/bugs/README.md`, `studio/qa/coverage.md` (both sides kept), docs/03 when two tracks
  append an "Implementation" section (keep both, the sim's first).
- A developer must never kill another track's Godot or `testhost`; a developer ends its own (rule 52). **The developer cannot
  write `studio/**`:** bug-file updates come as a patch in the scratchpad; the Producer applies it.
- `tools/qa/third-faction-copy.ps1 -Repo <scratch clone> [-Id <id>]` makes a Malazan copy; a Whirlwind-shaped copy is made
  by hand (`Stress/M7H1KillCounterQaStressTests.AddWhirlwindCopy` is the recipe); a Teblor-shaped one is the `teblor`
  folder under another id (QA's `barghast`).

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
- **The third faction's order of work now:** the sim's gatherer-overlap change (the owner's request; it moves the
  economy, so every opening measurement is redone after it) -> data **D33** (BUG-1420: Breakers, Riders and War-dogs
  back in the army without losing the Shamans and the expansion; BUG-1421) as the start of the balance pass -> the
  sim reads the Frenzy report again (Request 53's second half) -> the Teblor models (the view's next task, the
  owner's request). M7 criterion 4 is ticked after the balance pass and the Teblor models; the end-of-M7 hardening
  follows it.

- **Session length:** 2026-11-13-1118 ran 9.5 hours, 2026-11-13-2306 about 6, 2026-11-14-0616 about 6, **2026-11-14-1246
  about 8** (no fix round; QA full on the sim beside two standard passes, every default loop 1 h 26 m to 2 h 12 m
  beside the others against about 21 m alone). A full-tier sim task beside two measuring tasks is too much for this
  PC: when the sim is on QA full, give one of the other tracks a light task.

---

## Sim track

### Where we are
M8-1 on `main`: forest stealth on test data. Counter **1 / 4**. Fixed: BUG-1370, BUG-1391 item 3. No new sim bug
(BUG-1400 was found by the sim's QA but is the data's file). Debt: BUG-1161's three alias names S4, BUG-1149 / 1151 /
1152 S3, BUG-1150 S4, BUG-1010; notes: `[0]` lookups left in `FrenzyRulesLoaderTests`, `StatusDurationTechTests`,
`DataContentHashTests`, QA's `M7H1QaTests`; a JSON `null` block loads as absent in `forestStealth` / `regen` but is an
error in `reserveArmyShare` (pick one policy at the next hardening); QA's first combined run had one failure that did
not recur and could not be named (watch for a load-sensitive row).

### Next task candidates
1. **The owner's request: gatherers may overlap a bit while collecting** (inbox 2026-11-14; feature, QA **full**:
   movement). At the PLAN:
   - Read `MovementSystem`'s separation and what a Gathering / Returning / drop-off-queuing worker counts as there
     (the note says idle units are shoved, holding and attacking ones are walls). Generic: any worker of any faction,
     only while gathering at a node or waiting / delivering at a drop-off; armies and marching units keep today's
     separation.
   - The amount is a data knob in `game/data/common/rules.json` (the sim's file), not a C# literal. Producer default
     to record in docs/01 and STATE: the share of the separation push kept between two such workers (propose 0.5;
     1 = today). Decide at the PLAN whether the shipped value changes this session (then: one hash move, the golden
     regenerated with the reason, older saves refused once more, and **the data track gets no hash move that
     session**) or the schema lands on test data first.
   - **It moves the economy, so it moves every AI match.** Rules 41 / 43 / 45: name the M5-5 rows, the Hard v Normal
     tally, the opening pins and the content report rows expected to move; re-measure the bounds and docs/03's tables
     in the same commit; a row that stops ending is a finding. Size it as uncertain work (about 800 lines; QA's
     suites don't count). The golden replay tests re-run; say why if the `k` lines change.
2. **The owner's request: Shift-click queues orders, the sim half** (inbox 2026-11-14). Audit which unit orders append
   with the queue flag today (move, attack-move, attack, gather, repair, garrison, patrol if any, rally, abilities,
   build); close the gaps with the same flag, no new mechanic; expose the queued orders to the view read-only if
   `ViewApi` lacks them (that part may be the view's). The view then draws the queue. Tests per order kind; golden re-run.
3. **M8-2: the Shadow Archer's stand-still stealth and its +50 % first shot**, schema first, test data only (QA full).
4. **Requests 54 / 55** (from D32): "base defended" counts only a real raid; a `train` step may spend through an
   earlier step's reserve. Each changes Normal AI behaviour (rules 43 / 45): plan with a data task that re-measures.
5. **Request 53, part two:** read the Frenzy report on `main` (D32's opening is the shipped column now), re-print
   docs/03's table, look at why "army pop 15:00 (sum)" is 0; a cast rule only if a fielded Shaman standing with the
   army does not cast (today's figures say it does).
6. A hardening with no opening measurement beside it: BUG-1149 / 1151 / 1152, BUG-1161's names.

---

## View track

### Where we are
M6-V21 on `main`. Counter **2 / 4**. 105 scenes, all green. New: **BUG-1412 S3** (the build warning in `QaM6V21Test`,
filed by the Producer), **BUG-1410 S3** (`QaV5Test`'s flash check reads a node a model never draws on), **BUG-1411 S4**
(look nits: the sword clip's wind-up reads as a T-pose up close; Ochre / Bone players tint the Whirlwind figures one
tone; soldier types carry unused worker clips). Older: BUG-1381 S4, BUG-1177 item 2 (the owner's taste), BUG-1115
item 4. What reads badly on the pictures (the developer, QA and the Producer agree): the Camp Follower in a player's
colour is nearly the Laborer; the Heavy Infantry's box helmet is a pale block and the shield is edge-on from the
side; the Archer's bow hangs level at the hip; the Raider's blade is a thin line at the default zoom; player 1's
slate is close to the grey Malazan outfits.

### Next task candidates
1. **The owner's request: the Teblor humanoids first** (inbox 2026-11-14; feature, QA standard). **First, small:
   BUG-1412** (`string? why` at `QaM6V21Test.cs` line 257; the build is 0 / 0 again), **BUG-1410** (`QaV5Test` reads
   `ModelMeshOf(slot) ?? view`; add it to docs/03's list), and docs/03 "Implementation (M6-V21)"'s bench table replaced
   by the Producer's alone figures (above). Then the Gatherer, Warrior, Javelin Thrower, Shaman and Breaker: the
   Universal Base Character at the data's `scale` 1.5 on a 0.75 m radius, the two outfits recoloured to the Teblor
   palette (docs/04 "Style guide"), props as boxes (a javelin, a great club or blade, the Shaman's staff), team colour
   on one `team` surface, clip maps (the Gatherer the worker clips, the Javelin Thrower a throw if the libraries hold
   one). If five is too many for one slice, the Gatherer, Warrior and Javelin Thrower first. The Destrier Rider and the
   War-dog stay capsules (no horse or dog pack on disk: the owner's download, already under Waiting on you).
   `ModelViewTest` per type; pictures on the shipped data beside a Malazan soldier (rule 56: the real 0.75 m giants);
   the 400- and 1,000-figure bench alone. `tools/asset-manifest.json`, `tools/import-assets.ps1` and `tools/art/**`
   are the view's. Rules 62 / 64 / 65.
2. **Merged surfaces** (rule 61 as amended: the task right after the Teblor humanoids): one material and one atlas a
   model in the import tooling, every model re-generated; target 1,000 figures measured alone, reported against 16.7 ms.
3. **The owner's request: the Shift-queue made visible** (a line or markers per queued order for the selection),
   after the sim's audit says what queues; check first what the view already draws.
4. Small: figures at a finished replay keep moving in place (`AnimationRate` is 0 only under a paused tree); BUG-1411
   item 1 (try UAL2 `Sword_Regular_A/B/C`).
5. The view's part of M8 criterion 1 (stealth visuals for a forest-hidden unit) once a roster ships the block.
6. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Where we are
D32 on `main`: the Teblor AI's second pass. Counter **2 / 4**. Fixed: BUG-1390, BUG-1391 items 1, 2, 4. New:
**BUG-1420 S3** (Breakers, Riders and War-dogs off the field), **BUG-1421 S4** (five small items), and **BUG-1400 S3**
(a Stress report row red since D31, found by the sim's QA: the data's file). Open: BUG-1360 S4.

### Next task candidates
1. **BUG-1400 first, whatever the task** (rule 57): `Content/TeblorLandingOpeningReportTests.TheRows_ChangeTheMatch_FromTheFirstFrenzyOn_Report`
   compares "without the rows" against "with the rows", and the shipped data carries the rows since D31: re-think it
   against a copy with the rows removed, or retire it with the reason.
2. **If the sim ships the gatherer-overlap value next session (a hash move and a changed economy): a light task with
   no hash move.** BUG-1421 items 3-4 (pin or date-label the page's measured figures; the two unwrapped lines; the
   192 KB fixture's size and name), BUG-1360; or **the Shadow page made pin-ready** (as D-R36 did for the Teblor:
   `docs/factions/shadow.md` cells checked against docs/02 and the schemas that exist, the open questions listed for
   the Producer). No opening is measured beside a sim task that moves the economy (rule 41).
3. **D33: Breakers, Destrier Riders and War-dogs back in the Teblor army** (BUG-1420; BUG-1421 items 1, 2, 5), the
   start of the balance pass against Malazan and Whirlwind, **on the economy as it is after the sim's gather change**.
   Measure Easy, Normal and Hard this time (the D32 brief measured Normal only: the Producer's miss), the mirror too,
   and count distinct matches (the named maps give 1-3 outcomes over 8 seeds). Keep the Shamans (27) and the second
   Longhouse (18); the tooltip follows what most matches do. One hash move. The For-your-review table as always.
4. The owner's answers if any land ("teblor ai: early raid", the Teblor table, "teblor buildings: 1.5", "opening
   text: ...", "Frenzy text: ...", the counters, `goldShare`, BUG-0780, `counterStrength`).
