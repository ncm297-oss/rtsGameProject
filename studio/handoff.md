# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-14-0616** (session 1 / 8 of 2026-11-14, started 06:16, about 6 hours).
Sim **M7-H2** ACCEPT (hardening, 0 fix rounds, QA standard PASS_WITH_ISSUES), view **M7-V4** ACCEPT (1 fix round, QA
standard FAIL → PASS), data **D31** ACCEPT (0, QA standard PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only),
M6 5 / 10, M7 3 / 4 (criterion 3 ticked).** Counters: **sim 0 / 4, view 1 / 4, data 1 / 4**. Bug ids next session:
**sim 1400+, view 1410+, data 1420+**. Open S1 / S2: none. The next session is 2 / 8 of 2026-11-14.

**Next session in one line (proposal):** sim **M8-1, the first Shadow rules on test data** (BUG-1370 and the M7-7
table first; feature, QA full) · view **the art pass's next slice** (feature, QA standard) · data **D32, the Teblor
AI's second pass** (BUG-1390 first; Shamans and the expansion; one hash move; feature, QA standard).

## Where things stand

- **`main` after this session = sim 9bdfb98 + view ae360b3 + data fab2ad4** (plus this ACCEPT's studio files). The
  Producer's scratch clone of that tree (`acc0616` in the session scratchpad): build `--no-incremental` 0 / 0; the
  default loop **9,261 / 0 failed / 19 skipped of 9,280 in 21 m 19 s (beside the scene loop)**; smoke **PASS**; the scene loop **104 of 104, first pass**.
- **The Teblor's finished numbers shipped (D31).** Data hash and golden header **27036D3A2F2BA1CB** (was
  67269D955D18D885); the golden's `k` lines unchanged; save format 10. Every save and recording from before is refused
  `DataMismatch` (fixtures in `QA/Content/Fixtures/pre_d31/`). Shipped: `teblor_frenzied` (`damageBonus` 0.2, `onEnd`
  Slowed 0.2 for 4 s; the last row of `common/statuses.json`), `blood_oil_frenzy` applying it, the Anointing's
  `statusDuration` +4 s, `scale` 1.25 on ten buildings, `teblor_gatherer` `carry` 15, V24 as `standard` with
  `attack.reserveArmyShare` 0.25. `Content/Fixtures/teblor_standard_d29.json` stays as V24's record,
  `teblor_standard_d28.json` as the old opening's.
- **The shipped Teblor AI, measured.** 64 matches (four maps, `Content/TeblorLandedShareReportTests`): the target on 8
  of 8 seeds; Age II by 10:00 in 55, a wave in 62; **no second Longhouse by 15:00 in any**; the Anointing in 35; a
  Shaman in 4 (4 casts); defeated by 15:00 in 14 (12 v the Whirlwind). The sim's report on the merged tree (generated
  map, 32 matches): Age II by 15:00 in 25, Shamans' Circle 11, **a Shaman in 0, 0 casts**, defeated 13; the diagnostic
  (two Shamans at 0:00) 69 casts landed, 2.16 a match. The old opening on the same report: Age II 4, defeated 24.
- **`Stress/FrenzyCastReportTests` measures an opening from a file:** `FRENZY_REPORT_OPENING=<path>` (absolute, or
  relative to `sim/Rts.Sim.Tests` or the repo root; one `openings[]` object; it goes in as `standard`). On `main` its
  "shipped" and "landing" columns are the same opening now, and **docs/03 "Implementation (M7-7)"'s table was printed
  before the landing** (it shows the old opening as "shipped"): the sim re-prints it first thing next task.
- **A building's `scale` is drawn as height** (`BuildingViews.DrawnHeight` / `BarHeightOf`; the hit test stays the lower
  3 m). On the picture 1.25× reads "slightly taller" (`studio/review/2026-11-14-0616/`): a question to the owner in
  STATE, default keep.
- **The scene loop: 104 scenes, no red row on purpose, no known ERROR flake** (BUG-1350 fixed by cause; an ERROR line in
  `QaM6V20Test` is a finding again: reopen BUG-1350 on sight). `QaBuildReachRepro` is not in the loop (its name).
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's list).

## Standing rules (1-64)


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
**56 (new):** a "is drawn right" claim about new content is checked on a screenshot of that content in the shipped data,
not on a forged copy with other numbers (M7-V1's 1.5× looked right on a 0.4 m unit; the real giants are 0.75 m).
**57 (new):** a QA regression scene or test that is red by an open S3 reaches `main` only by an explicit ruling at ACCEPT
(named in the session log and STATE's Tests row), with the fix as the first item of that track's next brief; until then
every loop's expected result names that one row. Two sessions red = the fix becomes the whole task.
**58 (new):** a test helper that forges a data row must tolerate the shipped data already carrying it (look the id up,
then add or edit), and when docs/03 tells the data track which rows to write, the sim's QA applies them on a scratch
clone and runs the task's classes (BUG-1280).
**59 (new):** a developer who writes a fix for "the data track's rows break our tests" runs its own new tests on a copy
carrying those rows before reporting (M7-6's fix broke on exactly the rows it was written for: BUG-1310 / 1311, one fix
round). A sim test about "every shipped row" reads the count from the shipped file, never a literal.
**60 (lifted 2026-11-13-2306):** BUG-1321 is closed for 400 figures (10.1 ms alone); model types may be imported again.
**61 (new):** before more than ten unit types are models, a view task measures a 1,000-figure crowd of mixed types alone
(rule 42) and reports what 16.7 ms would take (merged surfaces, a shared skeleton or baked animation); every art brief
reports the 400- and 1,000-figure bench with the capsule figure from the same window and reads the "while 90%+ alive"
column. An LFS size criterion names "the art pass's binaries", never "total LFS".
**62 (new):** a developer runs the full default loop after its last edit, or its report says in its first lines that
it did not (two sessions running a developer fixed one row and re-ran only the affected classes).
**63 (new):** a scratch folder's name starts with its track and task (`m7v4-...`, `d31-...`), is created new, and no
agent writes into, cleans or resets a folder it did not create (2026-11-14-0616: the view developer reset another
agent's clone named `clone`; nothing committed was lost). A brief that asks for a scratch clone says so.
**64 (new):** "build 0 / 0" in a report means `dotnet build RtsGame.sln --no-incremental` after the last edit (an
incremental build hid two warnings in a new test file: BUG-1380, one fix round).
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
- **The scene loop is all green: 104 scenes** (`BuildingScaleViewTest` and QA's `QaM7V4Test` are new). `QaM6V20Test`
  samples after a forced collection (objects flat at 3,252); BUG-1350 is fixed by cause, so an ERROR line there is a
  finding. Rows that flake under load are listed below; any other red row is a finding.
- **Model animation is manual:** every model's `AnimationPlayer` is in manual mode and `UnitViews._Process` advances it
  through `CrowdAnimator` (48 nearest every frame, the rest 1 frame in 3). A scene that calls `Sync` directly and wants
  motion calls `AdvanceAnimations(delta, eye)` too. A model shows no worker tint (`ShownTint` still records the state).
  Figures do not follow game speed (2x plays clips at 1x): unchanged, a candidate for the next art slice.
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
- **The third faction's order of work now:** data **D32** (BUG-1390's tooltip, Shamans fielded, the expansion; one
  hash move) -> the sim reads the Frenzy report again (Request 53's second half; a rule only if it asks for one) ->
  the balance pass (Requests 46 / 51 as measured) -> models (rule 61). M7 criterion 4 is ticked after the balance
  pass and the Teblor models; the end-of-M7 hardening follows it.

- **Session length:** 2026-11-13-1118 ran 9.5 hours, 2026-11-13-2306 about 6, 2026-11-14-0616 about 6 (three
  standard QA passes, each with landed-clone runs, and one fix round). Two tracks on light or standard QA beside one
  on full is the shape that fits a day; avoid Soak or all-category runs in a QA pass (rule 53) and say so in the sim
  brief.

---

## Sim track

### Current session plan (2026-11-14-1246, session 2 / 8): M8-1, forest stealth as a faction bonus, on test data only (feature, QA full)

**Goal.** M8's first sim slice. Build the Shadow page's "Children of Shadow" as a generic faction bonus: units of a
faction whose data carries the bonus are Stealthed while they stand beside a standing tree and have gone the bonus's
delay without landing an attack or losing hit points. Schema first, on forged test data only: no shipped file changes,
so the data hash 27036D3A2F2BA1CB, the golden, every pin and every save stay byte for byte. Three small M7 leftovers
first. Producer readings of the page are in docs/01's change log row "2026-11-14 (d)".

**Scope.**
- A. Leftovers, first: (1) **BUG-1370**: `Stress/FrenzyCastReportTests` "landed" reads the ready tick at the Frenzy's
  own slot in the Shaman's ability list; a regression row that failed first (a forged Shaman with a second ability
  listed before the Frenzy). (2) docs/03 "Implementation (M7-7)": re-print the table and its sentences from the report
  run on this branch (the shipped opening is V24 at share 0.25 now). (3) BUG-1391 item 3: the two stale docs/03 lines
  ("nothing ships them", `carry`'s "no shipped unit sets it yet").
- B. Schema (sim-owned): `faction.json` optional block
  `"forestStealth": { "status": "stealthed", "delay": 3, "exceptSlots": ["siege"] }`. `status` must name a status of
  kind `stealthed`; `delay` in seconds, at least one tick, with an upper limit in `DataLimits`; `exceptSlots` known unit
  slot names, no duplicates. Loader validation with one named error each, `DataValidationTests` rows. The block enters
  `ContentHash` only when present.
- C. Rule: a live unit whose owner's faction has the block, whose slot is not excepted and which is not summoned
  qualifies when (i) its own cell or one of the 8 cells round it holds a standing tree, (ii) at least `delay` has passed
  since its last attack landed or cast resolved (never = qualifies), (iii) at least `delay` since it last lost hit
  points (any source). While it qualifies it is Stealthed through the existing status store and the one predicate
  (`World.IsStealthed` / `HiddenFrom`), so detection, Revealed, targeting, the fog and the AI's `PlayerView` work as in
  M4-5 with no second path. The tick it stops qualifying that stealth ends (no linger). A timed Stealthed from another
  source (an ability) is never shortened or removed by this rule. Runs in phase 5 before `StealthSystem.Run`.
- D. State: the new timers are hashed and saved only when the loaded data has a faction with the block (a save part
  written exactly then, as `HUNT` is with an AI seat; a save whose part disagrees with the data is refused, not
  crashed). `SaveFormat.CurrentVersion` stays 10. `LastDamagedTick`'s gate (regen factions only) may widen to factions
  with the block.
- **Out of scope:** any file under `game/` (no Shadow roster, no `rules.json` / `statuses.json` edit); the Shadow
  Archer's stand-still stealth and its +50% first shot (M8-2); any view or ViewApi change; any AI rule or behaviour
  (rules 41 / 43: the data track measures the Teblor opening beside this); Hounds, Shadow-step, Wraiths; re-pinning any
  digest. Size: about 800 changed lines (QA's suites don't count).

**Acceptance criteria.**
1. BUG-1370 fixed with its regression row; docs/03 M7-7's table equals the report's printed lines on this branch (the
   lines pasted in the report); the two stale lines corrected.
2. Schema: the block loads; every validation error has a named loader test; with the block absent `ContentHash` is
   unchanged (the existing hash pins green, no new literal: rule 49); with it present the hash moves, and moves again
   for each field changed.
3. Rule, each a named test on forged data (rule 58): (a) a never-hit unit stepping beside a tree is Stealthed that
   tick; (b) it is not the tick it is no longer beside one; (c) its attack landing from stealth gives Revealed, and it
   is hidden again only when Revealed has ended and `delay` has passed (shown with `delay` 3 s and 5 s); (d) a unit that
   attacked in the open and walks to a tree inside `delay` waits it out; (e) hp loss while hidden (a melee hit, a
   projectile, a splash, a zone's tick) ends the stealth that tick and it returns after `delay`; (f) an excepted slot, a
   summoned unit and another faction's unit beside the same tree never; (g) the tree felled ends it that tick; (h) an
   ability's timed Stealthed on the same unit keeps its own duration in and out of the forest; (i) proximity, a unit
   detector and a tower detect it as in M4-5; no scan acquires it, an explicit Attack on it is dropped, the enemy AI's
   `PlayerView` does not hold it.
4. Determinism and saves: two runs of a 3,000-tick forged match with a forest-stealth faction are hash-equal every
   tick; saved while units are hidden and mid-wait, loaded and continued, it matches the unsaved run for 1,000 ticks;
   its recording replays; changing a timer changes the state hash when the block is in the data; a forged save (part
   present without the block, absent with it) is refused with a `SaveError`.
5. Nothing shipped moves: `git diff <base> -- game/ sim/Rts.Sim.Tests/Replays sim/Rts.Sim.Tests/Content
   sim/Rts.Sim.Tests/QA/Content` is empty; no pinned digest literal is edited in the diff; `CurrentVersion` is 10; the
   shipped-data memory row (`World_1024Map_CacheStays32_MemoryBounded`) is green without a new bound.
6. Cost: 0 bytes a tick on a forged 500-unit brawl with 250 forest-stealth units among trees; a match whose data has no
   block does no per-unit work for the rule (one early return). Measure and report, alone (rule 42): that brawl's ms a
   tick, `EmptyTick(2500)` and the M4 stealth brawl row against their figures on the base. Tree adjacency is 9 cell
   reads a unit, no spatial query.
7. Docs in the same commit: docs/02 "Stealth and detection" gains readings (a)-(c) of docs/01's "2026-11-14 (d)" row;
   docs/03 "Implementation (M8-1)" (schema, phase, state / hash / save, known limits, **"For the data track (M8-1)"**
   with the exact block the Shadow `faction.json` will carry, **"For the view (M8-1)"** saying what a view reads to draw
   its own hidden units, or that nothing new is needed).
8. `dotnet build RtsGame.sln --no-incremental` 0 / 0 and the full default loop green after the last edit (rules 62 /
   64); every test process the developer started is ended (rule 52).

**Design references.** `docs/factions/shadow.md` "Faction bonus" and "Balance risk"; docs/02 "Stealth and detection",
status table rows Stealthed / Revealed; docs/03 "Implementation (M4-5)" (the predicate, detection, the one hook, Reveal,
state and hashing, cost, memory), "Implementation (M7-1)" (`faction.json` `regen`, `LastDamagedTick`, `REGN`).

**Tests required.** Loader and validation rows; the scene rows of criterion 3; the twin-run, save / load, replay and
hash rows of criterion 4; the allocation row; the regression row for BUG-1370. Golden replay tests re-run, unchanged.

**Constraints.** No Godot in `Rts.Sim`; no hard-coded stat (the delay, the status and the excepted slots are data); no
allocation in per-tick code; no dictionary iteration; no wall clock. Bug ids 1400+. Scratch folders named `m81-...`
(rule 63). If the status store's 8-entry cap or the one-entry-per-id merge gets in the way, say so in the report's
first lines rather than widening the store.

**QA focus (full; rule 53: default loop with Stress, then Perf alone; no Soak).** Off-by-one at every boundary (the
tick the delay ends, the tick Revealed ends, the tick a tree falls); phase order (stealthed in phase 5, scanned in
the same tick); a unit walking a ragged forest edge (stealth flickering every tick: stale detection bits, target
keep / drop churn); a unit already holding 8 statuses; a dormant Stealthed under Revealed; the unit inside an enemy
sandstorm, below a cliff, seen by an ally of the enemy (shared vision); own workers felling the tree it hides by;
forged saves both ways; a 4-seat 9,000-tick forged match with two forest-stealth factions twice (hash-equal), and the
shipped pins on three maps unmoved; 2,000 hidden units' tick cost; the AI seat's view (no leak, and its own hidden
units still obey orders).

### Where we are
M7-H2 on `main` (the hardening): BUG-1340 / 1341 / 1220 / 1221 fixed, the report reads an opening from a file, lookups
by id. Counter **0 / 4**. The sim's M7 part is done until the data fields Shamans (Request 53's second half).
Debt: **BUG-1370 S4** (new), BUG-1391 item 3 (docs/03, from the data's QA), BUG-1161's three alias names S4, BUG-1149 /
1151 / 1152 S3, BUG-1150 S4, BUG-1010; notes: `[0]` lookups left in `FrenzyRulesLoaderTests`,
`StatusDurationTechTests`, `DataContentHashTests`, QA's `M7H1QaTests`.

### Next task candidates
1. **M8-1: the first Shadow rules, schema first, on test data only** (M8 criterion 1's sim half: forest stealth, and
   the Shadow Archer's stand-still stealth). Feature, QA **full** (fog / stealth). At the PLAN:
   - Read `docs/factions/shadow.md`, docs/02's stealth rules and docs/03's M4 stealth sections first; anything the
     page leaves [OPEN] is resolved by the documented recommendation (`open_decisions: producer_default`) and
     recorded. If the design is not clear enough for one slice, plan only "forest stealth" (a unit of a tagged type
     standing in a forest cell is stealthed; detectors reveal it) and leave stand-still stealth for M8-2.
   - Schema, loader validation and `DataValidationTests` rows; the fields in the data hash only when used, so the
     shipped hash 27036D3A2F2BA1CB, the golden and every pin stay (criterion: the diff of `game/`, `Replays`,
     `Content`, `QA/Content` empty, three 9,000-tick matches bit-identical). No AI rule (rule 41: the data measures an
     opening beside it). Save format: say at the PLAN whether stealth state needs a new part; if it does, rule 50
     (name the view scenes that pin save bytes).
   - **Folded in first (small):** BUG-1370 (the report's "landed" reads the Frenzy's own slot); docs/03
     "Implementation (M7-7)"'s table and sentences re-printed from the report on `main` (the shipped opening is V24 at
     0.25 now); BUG-1391 item 3's two docs/03 lines ("nothing ships them", "no shipped unit sets it yet").
   - Rules 54 / 55 do not apply (no shipped row); rule 58 does for any forged row.
2. **Request 53, part two**, after D32 fields Shamans: run the report on `main`; a buff cast rule (own fighters within
   the radius) only if a fielded Shaman stands with the army and still does not cast; "casters follow the army" only
   if it shows the Shaman left behind.
3. A hardening with no opening measurement beside it: BUG-1149 / 1151 / 1152, BUG-1161's names (rules 43 / 45).

---

## View track

### Current session plan (2026-11-14-1246, session 2 / 8): M6-V21, the art pass's second slice (feature, QA standard)

**Goal.** Four more unit types become animated, team-coloured figures from the two outfits on disk (the owner's
default: recolour, props shaped as boxes): the Malazan Heavy Infantry and the Whirlwind Camp Follower, Raider and
Desert Archer. With them: melee and bow attack clips, and figures that follow the game speed. Six model types after
this; rule 61's crowd bench reported.

**Scope.**
- `tools/asset-manifest.json` (four new `models` entries; new clips in `library`), `tools/import-assets.ps1` and
  `tools/art/**` only as the entries need (these three are the view track's; the rest of `tools/**` is not). Run the
  import with Blender 5.2 against the main checkout's `asset-sources/`; commit its outputs under
  `game/assets/models/<faction>/<id>/` and `game/assets/animations/` (Git LFS, already in `.gitattributes`).
- Heavy Infantry: an outfit recoloured to the Malazan palette (steel grey, oxblood), a big rectangular shield and a
  short sword as props. Camp Follower: the Peasant in the Whirlwind palette (ochre, sand, bleached white) with the
  worker clips. Raider: Whirlwind palette, a curved blade. Desert Archer: the Ranger in the Whirlwind palette, a bow.
  Team colour on the `team` surface of each, as the first two.
- Attack clips: a one-handed melee swing for the Heavy Infantry and the Raider and a bow draw for the Archer, chosen
  from UAL1 / UAL2 (list the candidates considered in the report; if the libraries hold no credible bow clip keep
  `Pistol_Shoot` and say so). The library keeps its bone trimming.
- Figures follow the game speed: `CrowdAnimator`'s advance is the frame's delta times the runner's game speed; a
  paused match advances nothing (Producer decision, docs/01 "2026-11-14 (d)" (f)).
- Scenes: any scene that reads one of these four types' slot mesh or material either sets `UnitViews.UseModels =
  false` (as the five listed in docs/03 "Implementation (M6-V20)") or reads `ModelMeshOf`; no pin value changes.
- **Out of scope:** any other unit type (horses, siege, mages, the Zealot, the Sapper, the Teblor); Mixamo retargeting;
  death animations and bodies; building models; merged surfaces, a shared skeleton or baked animation (report what the
  bench says they would be worth, do not build them); `game/data/**`; the sim outside read-only `ViewApi`; the "models:
  bigger" question (the owner's).

**Acceptance criteria.**
1. Exactly six types draw a model on the shipped data, each at the path its `model` field names; the other types
   draw their capsule with no `ERROR` line (`ModelViewTest`, extended per type: at most 3,000 triangles, textures at
   most 512 x 512, 65 bones, one `team` surface, feet at the origin, facing -Z, height within 5% of 1.8 m x the type's
   `scale`).
2. A fighting Heavy Infantry and Raider play their melee clip and a shooting Desert Archer its clip, from real sim
   states; a Camp Follower plays build, gather_wood and gather_gold; a clip the library lacks still falls back to idle.
3. Game speed: at 2x a walking figure's clip advances twice the 1x amount over the same frames, at 0.75x three
   quarters, paused nothing (a headless row in `ModelViewTest`); 0 bytes over 120 syncs of 400 mixed models.
4. A re-run of `tools/import-assets.ps1` reproduces every committed output byte for byte (state it with the hashes).
5. Bench, alone (rules 42 / 61), 1600 x 900, vsync off, default zoom, mixed evenly over the six types, with the
   capsule figure from the same window: **400 figures at most 16.7 ms a frame while 90%+ are alive** (pass / fail);
   1,000 figures: measure and report, with what merged surfaces or a shared skeleton would be worth.
6. Pictures (rule 56, on the shipped data, windowed): each new type at the default zoom and close up, in two player
   colours, beside the Laborer and a capsule type; a Malazan line fighting a Whirlwind line. Saved under a scratch
   folder `m6v21-shots/` and listed in the report (the Producer copies them to `studio/review/`); the developer has
   looked at each and says what reads badly.
7. docs/04 ("Unit models, as built", the licensing log if any new source file is used) and docs/03 "Implementation
   (M6-V21)" (the entries, the clips chosen, the game-speed rule, the bench table, the scenes that turn models off).
8. `dotnet build RtsGame.sln --no-incremental` 0 / 0; the default loop green; `tools/qa/smoke.ps1` PASS; the scene
   loop green, every scene (rules 62 / 64); every Godot and test process the developer started is ended (rule 52).

**Design references.** docs/04 "Style guide" (scale, team colour, faction palettes and silhouettes), "Coverage plan",
"Import process" / "Unit models, as built", "Animation setup", "Licensing log"; docs/03 "Implementation (M6-V20)" and
"(M6-VH7)" (the fallbacks, `CrowdAnimator`, the bench method and its "while 90% live" column).

**Tests required.** `ModelViewTest` rows per criterion 1-3; the scene loop; the bench runs of criterion 5.

**Constraints.** Rule 7 (one view node per slot, no gameplay state in the view); no unit id named in C#; no
player-facing text in C#; never commit anything from `asset-sources/`; the model files are LFS. Bug ids 1410+. Scratch
folders named `m6v21-...` (rule 63); never touch another agent's folder or processes.

**QA focus (standard: default loop + scene loop).** The four types in every scene that used their capsules (pins
silently reading a hidden mesh); slot reuse across model and capsule types; an LFS-pointer clone and a missing file per
new model (capsule, one warning, no `ERROR`); game speed changed mid-clip and pause / resume (no jump, no drift between
the 48 nearest and the staggered rest); the prop sitting in the hand through walk and attack on the pictures; team
colour legible on both palettes for all eight player colours; the bench re-measured alone.

### Where we are
M7-V4 on `main`. Counter **1 / 4**. 104 scenes, all green. Fixed: BUG-1350 (by cause), BUG-1331 item 1, BUG-1380 (in
the round). New: **BUG-1381 S4** items 1-2 (scale limits no building ships). Older: BUG-1177 item 2 (the owner's
taste), BUG-1115 item 4. Notes: `Sfx` loads its clips per match (BUG-1350's pattern; no error seen); `SpawnUnit` puts
a unit inside an impassable cell without a check (a test trap, QA's `QaBuildReachRepro`).

### Next task candidates
1. **The art pass's next slice** (rule 60 lifted; rule 61): Heavy Infantry and the Whirlwind line from the two outfits
   recoloured, per the default in STATE's Waiting item; split tooling from view code; figures follow game speed; a
   per-weapon attack clip. Bench reported per rule 61 (400 and 1,000 figures with the capsule figure from the same
   window, alone). Feature, QA standard. `tools/asset-manifest.json`, `tools/import-assets.ps1` and `tools/art/**` are
   the view's. **Rule 64: the report's "build 0 / 0" is `--no-incremental`.**
2. **An owner note if one lands:** "teblor buildings: 1.5" is the data's (one hash move, with D32 if it arrives in
   time); "buildings: wider too" or "click the whole building" are the view's (small).
3. The view's part of M8 criterion 1 (stealth visuals for owner and enemies) only once the sim's rules are on `main`.
4. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Current session plan (2026-11-14-1246, session 2 / 8): D32, the Teblor AI's second pass, one hash move (feature, QA standard)

**Goal.** The shipped Teblor AI plays thin: it almost never trains a Shaman (4 of 64 matches), never builds a second
Longhouse by 15:00 (0 of 64) and its opening's tooltip misdescribes its first raid (BUG-1390). Fix the tooltip, and
get Shamans and the expansion into the opening through `ai.json` alone, measured before anything ships. One data hash
move for the whole task.

**Scope (files).** `game/data/factions/teblor/ai.json`; `game/data/factions/teblor/buildings.json` (the Stone Cairn's
`scale` field order only); `docs/factions/teblor.md`; `sim/Rts.Sim.Tests/Content/**` and `QA/Content/**`; and, handed
over by name for the hash move (rule 51): the golden replays' header and checksum, the two sim-owned hash pins
(`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`)
and the hash list and comment in `QA/GatherWedgeQaTests` (old hash, new hash, why). No other file.
- **BUG-1390 first.** The `standard` opening's `description` becomes: "War Lodge and Javelin Ground first, then Age
  II; four Warriors raid at 8 army pop, then Javelin Throwers, Destrier Riders, Shamans, Breakers and War-dogs join in
  Age II." If the opening you ship changes what that sentence says, change the sentence to match and quote it in the
  report. The fixture and the two page rows that quote it move with it.
- **Shamans fielded.** The composition weight alone does nothing (10 → 25 played 64 matches identically). Try the
  Shamans' Circle earlier and / or a `train` step for one or two Shamans after it. Measure read-only with
  `FRENZY_REPORT_OPENING=<file>` on `Stress/FrenzyCastReportTests` and with the 64-match row
  (`Content/TeblorLandedShareReportTests`).
- **The expansion.** The `expand` step (`time: 480`) sits after two `time: 600` steps in the list; find out whether
  list order, gold or the reserve share holds it back, and fix it in `ai.json` only.
- BUG-1391 items 1, 2 and 4 (the misplaced `<summary>` twice and the duplicated `Strip`; the Cairn's field order;
  "army pop 42" said as a 64-match sum on the page).
- **Out of scope:** any unit, building, tech, ability or status number; `game/data/common/**`; other factions' files;
  any C# outside the two content test folders and the named pins; the balance pass against Malazan and Whirlwind; a new
  AI rule (if `ai.json` cannot express what is needed, write it as a Request for the sim track in the report).

**Acceptance criteria.**
1. BUG-1390: the shipped description is the sentence above (or its matching variant), equal in `ai.json`, the fixture
   and both page rows; the pins catch a one-sided edit (show one mutant red).
2. The shipped opening, on the 64-match row: "Age II and a wave by 10:00 in 6 of 8" on at least 6 of 8 seeds;
   defeated by 15:00 in at most 14 of 64; more than 4 matches with a Shaman; more than 0 with a second Longhouse by
   15:00. **Terminal rule:** if no variant measured meets all four, ship the description fix alone (plus BUG-1391),
   report the best variant's row beside today's, and say what `ai.json` cannot express.
3. Measure and report, not pass / fail (the cast rule is the sim's): Frenzy casts landed and matches with a cast for
   the shipped opening, from the report run with `FRENZY_REPORT_OPENING`.
4. The variants table in the report: every variant tried, one row each, with the four figures of criterion 2 and what
   was changed; the cause of the missing expansion stated in one sentence with the evidence.
5. The hash moves once: the golden's header and checksum only (its `k` lines identical), the two hash pins and the
   `GatherWedgeQaTests` list and comment updated; the Stress pin classes run and no other digest moved (list them with
   their counts); a save and a recording from before are refused `DataMismatch` (fixtures as `pre_d31`'s pattern).
6. `docs/factions/teblor.md` says what the shipped opening does and carries the re-measured rows (per-match figures
   named as such); every content pin both ways green; `DataValidationTests` green.
7. The For-your-review table in the report: each changed step or field (which, old → new, one-line why), the new
   description quoted, and the sentence that saves and recordings from before this session stop loading.
8. `dotnet build RtsGame.sln --no-incremental` 0 / 0 and the full default loop green after the last edit (rules 62 /
   64); test processes ended (rule 52).

**Design references.** `docs/factions/teblor.md` ("AI build order", the openings table, "The reserve share,
measured", the Owner review rows); docs/02 "AI" (the build-order steps, `expand`, the difficulty table, compositions,
the reserve); docs/03 "Implementation (M7-6)" (an opening's own reserve share) and "(M7-7)" (the Frenzy report; its
table is being re-printed by the sim this session, so read the report's own output, not the doc).

**Tests required.** The fixture and page pins for the shipped opening; the 64-match report row re-run on the shipped
file; the pre-D32 refusal fixtures; one mutant per new pin.

**Constraints.** Data only; schemas are the sim's. The sim's M8-1 beside this touches no AI rule and no shipped file
(rule 41), so measurements on `main` hold. Batch everything into the one hash move. Bug ids 1420+. Scratch folders
named `d32-...` (rule 63). The 64-match rows are long: run them alone where you can, never beside your own second
test host.

**QA focus (standard: default loop + the Stress pin classes + the task's report rows).** Re-run the shipped row in
QA's own harness column for column; the opening on Easy and Hard and against each of the three factions (a Shaman
`train` step that starves the first wave, an earlier Circle that delays Age II); the expansion on all four maps
(Raraku has little room); the description against what the opening measurably does; mutants on every new pin; the
refusal text for an old save; the hash pins and that no Stress digest moved.

### Where we are
D31 on `main`: the landing. Counter **1 / 4**. Fixed: BUG-1241. New: **BUG-1390 S3** (the opening's tooltip),
**BUG-1391 S4**. Open: BUG-1360 S4.

### Next task candidates
1. **D32: the Teblor AI's second pass, one hash move.** Feature, QA standard. Files: `factions/teblor/ai.json`,
   `factions/teblor/buildings.json` (the Cairn's field order only), `docs/factions/teblor.md`, `Content/**`,
   `QA/Content/**`, and the golden header + the two sim-owned hash pins + `GatherWedgeQaTests`' comment (rule 51; run
   the Stress pin classes).
   - **BUG-1390 first:** the `standard` opening's description says what the AI does. Default wording (the owner may
     send other words): "War Lodge and Javelin Ground first, then Age II; four Warriors raid at 8 army pop, then
     Javelin Throwers, Destrier Riders, Shamans, Breakers and War-dogs join in Age II." Fixture and page rows with it.
   - **Shamans fielded** (Request 53's data half): the composition weight alone changed nothing (10 → 25 played 64
     matches identically), so move the Shamans' Circle earlier and / or add a `train` step for one or two Shamans
     after it. Measure read-only with `FRENZY_REPORT_OPENING` (matches with a Shaman, casts landed) and with the
     64-match row.
   - **The expansion:** 0 of 64 by 15:00 with the carry (5 without). The `expand` step sits after two `time: 600`
     steps in the list; find out whether order, gold or the reserve share holds it back, and fix it in `ai.json` only.
   - **Acceptance rule to write into the brief:** the shipped opening still meets "Age II and a wave by 10:00 in 6 of
     8" on at least 6 of 8 seeds, defeats by 15:00 not above 14 of 64, and more matches with a Shaman and with a
     second Longhouse than today's row (4 and 0); if no variant does all of it, ship BUG-1390's text alone with the
     best measured variant reported, and say what `ai.json` cannot express (a Request for the sim track). Rule 48:
     check each of these against the brief's out-of-scope list before calling it pass / fail.
   - BUG-1391 items 1, 2, 4. Rule 41: the sim's M8-1 touches no AI rule.
   - The For-your-review table as always; the saves sentence (the hash moves again).
2. **The balance pass** against Malazan and Whirlwind (several seeds, both seat orders; inputs: the 14 defeats, 12 by
   the Whirlwind; the 0.75 m crowd figures; the aftermath-slow stacking note; Request 53's result).
3. BUG-1360 (S4) at the next hardening.
4. The owner's answers if any land ("teblor ai: early raid", the Teblor table, "teblor buildings: 1.5", "opening
   text: ...", "Frenzy text: ...", the counters, `goldShare`, BUG-0780, `counterStrength`).
