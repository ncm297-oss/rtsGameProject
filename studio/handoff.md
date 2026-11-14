# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-13-2306** (session 2 / 8 of 2026-11-13; it ended on the 14th).
Sim **M7-7** ACCEPT (0 fix rounds, QA full PASS_WITH_ISSUES), view **M6-VH7** ACCEPT (0, QA standard PASS_WITH_ISSUES),
data **D-H8** ACCEPT (0, QA standard PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 2 / 4.**
Counters: **sim 4 / 4 (hardening next), view 0 / 4, data 0 / 4**. Bug ids next session: **sim 1370+, view 1380+,
data 1390+**. Open S1 / S2: none. The next session is 1 / 8 of 2026-11-14.

**Next session in one line (proposal):** sim **M7-H2, the sim's hardening** (BUG-1340 first, BUG-1341, BUG-1220 / 1221,
BUG-1161's names, BUG-1149 / 1151 / 1152; hardening, QA full if an AI rule is touched, else standard) · view **M7-V4,
the buildings' drawn `scale`** (BUG-1350 first; feature, QA standard) · data **D31, the one landing** (feature, QA
standard; rule 54's rehearsal at the PLAN).

## Where things stand

- **`main` after this session = sim a34df18 + view d6cbf74 + data 289ec52.** The Producer's scratch clone of that tree
  (`acc` in the session scratchpad): build 0 / 0; the default loop **9,177 / 0 failed / 21 skipped of 9,198 in 20 m 32 s (beside the scene loop)**; smoke **PASS**; the scene loop
  **102 of 102, first pass**.
- **No shipped data moved this session.** The golden header is still **67269D955D18D885**; save format 10; every pin as
  it was. `game/assets/animations/universal_humanoid.glb` changed (the finger tracks are gone: 234,948 bytes).
- **Animated figures are cheap enough for the art pass to widen (BUG-1321 closed for 400; rule 60 lifted).** Alone at
  ACCEPT: 400 models 10.1 ms a frame while 90%+ alive (worst 1% 15.9), capsules 0.75 ms. **1,000 models are still
  23-27 ms** (10,500 draw calls): see rule 61.
- **The scene loop has no red row on purpose** (102 scenes: 101 + QA's `QaM6VH7Test`). `QaM6V20Test` can log one ERROR
  line about a GC handle under load (BUG-1350, 1 run in 20): rerun it once; red twice is a finding.
- **Why the AI never casts Frenzy (Request 53): it trains no Shaman before 15:00** (3 of 32 matches with the shipped
  opening, 0 of 32 with V24). That is `ai.json`'s. Whether a fielded Shaman stands with the army and whether "2 enemies
  within 8 m" suits a buff is **not measured** (BUG-1340: the diagnostic counted the second Shaman as the army; docs/03
  carries a correction paragraph).
- **`units.json` takes `carry` on a worker** (Request 48's schema; absent = `rules.json`'s 10; in the data hash only when
  present). The Gatherer's row is `"carry": 15`, to land with D31.
- **The landing copy is final but for one number.** `Content/TeblorLanding` writes six files in place (48 lines added,
  28 removed; `D30_WRITE_LANDING=1` on a scratch clone). The ability tooltip and the status row's text are quoted in the
  session log for the owner. Open: the opening's own `reserveArmyShare`, 0.25 or 0.5 (table in the session log).
- **With D31's rows + V24 at 0.5 + `carry` 15 on this session's sim tree, 37 default-loop rows go red** (QA): the two
  golden data-hash pins and 35 in `Content/**` / `QA/Content/**` (the 24 known + `AiContentTests` x2,
  `AiSpendingReportTests`, `TeblorRosterPinTests.AnAiCellChanged_IsCaught` x5, `TeblorOpeningQaTests` x3,
  `AiHouseRuleReportTests`, `D20PinQaTests.HouseNote_*`). Any other red row at the landing is a finding.
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's list).

## Standing rules (1-62)


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
- **The scene loop is all green: 102 scenes.** `QaM6V20Test` samples after a forced collection (objects flat at 3,251);
  BUG-1350 is its one known flake (an ERROR line, exit 0, 1 run in 20 under load). Rows that flake under load are listed
  below; any other red row is a finding.
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
- **The Teblor rows may land; expect 37 red rows on the rehearsal, all listed under "Where things stand".** The two
  golden data-hash pins `FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders` and
  `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders` are sim-owned: the D31 brief hands them to the data track
  (rule 51). A Frenzy status id must sort after `stealthed` (docs/03 "For the data track (M7-4)"; QA's
  `D30LandingQaTests.ShippedData_TheM4StatusesKeepTheirStateHashNumbers` now names a wrong id).
  `StatusLoaderTests.Shipped_Regenerating_*` assumes the same of any later status: a brief that adds one says so.
- **M7-4's fields and M7-7's `carry` are in the data hash only when used:** the first shipped `damageBonus` / `onEnd` /
  `statusDuration` / building `scale` / `carry` moves the golden header (rule 51 applies to that data brief).
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
- **The third faction's order of work now:** data **D31, the one landing** (the Frenzy row, the ability's effect and
  tooltip, the Anointing's +4 s, the buildings' `scale` 1.25, V24 with its own `reserveArmyShare`, the Gatherer's
  `carry` 15; one golden header move; M7 criterion 3 ticked there) -> the view draws the buildings' `scale` (built next
  session on a forged row; rule 56's screenshot on the shipped Teblor once D31 is on `main`) -> Shamans in the opening and
  Request 53 re-measured -> the balance pass (Requests 46 / 51 as measured) -> models (rule 60 lifted, rule 61).

- **Session length:** 2026-11-13-1118 ran 9.5 hours, 2026-11-13-2306 about 6 (the sim's full QA 2 h 39 m beside a 1 h 26 m rehearsal). Two tracks on
  light or standard QA beside one on full is the shape that fits a day; avoid Soak or all-category runs in a QA pass
  (rule 53) and say so in the sim brief.

---

## Sim track

### Where we are
M7-7 on `main`: the Frenzy report and `units.json` `carry`. Counter **4 / 4: the next session is the sim's hardening.**
Debt: **BUG-1340 S3, BUG-1341 S4** (new), BUG-1220 / 1221 S4, BUG-1161's three alias names S4, BUG-1149 / 1151 / 1152
S3, BUG-1150 S4, BUG-1010; unfiled notes: `StatusLoaderTests` x4 find shipped rows by file position;
`BloodOilScenes.Edit` addresses the Anointing as `techs[0]` and the Frenzy as `abilities[0]`;
`RebuildLookMatchQaStressTests.PaleHillsSeed2_*`'s 600 s child timeout trips beside a long loop.

### Next task candidates
1. **M7-H2, the sim's hardening.** QA standard unless an AI rule changes (then full).
   - **BUG-1340 first:** `Stress/FrenzyCastReportTests` counts an army without Shamans (QA's
     `FrenzyCastReportQaStressTests` is the model), prints the diagnostic by phase (before / after the first army unit),
     and docs/03's M7-7 table and its correction paragraph are made one consistent text. Add a configuration that reads
     an opening from a file path so the data track can measure "Shamans fielded by the opening" without sim edits.
   - **BUG-1341:** the test literal reads `workerCarry` from the data; a focused test of `EconomyManager.FullLoad` on a
     carry-15 worker (an AI worker holding 10 of 15 is not sent home; at 15 it is).
   - **Rule 58 for D31, the same session:** D31 lands in the data track beside this task. Name in the brief every
     sim-owned class the landing turns red (the two golden hash pins only, by QA's rehearsal) and keep the sim off
     `game/data/common/statuses.json` (the data track writes it this once).
   - Then, as size allows: BUG-1220 / 1221, BUG-1161's names, the position lookups in `StatusLoaderTests` /
     `BloodOilScenes`, the 600 s child timeout, BUG-1149 / 1151 / 1152 (each only with a measured repro; rules 43 / 45
     if an AI rule moves).
   - Out of scope: `game/data/**`, the golden, any format, `Content/**`.
2. **Request 53, part two** (after D31 and after an opening fields Shamans): re-run the fixed report; a cast rule for a
   buff aura (own fighters within the radius, the optional knob from this session's brief) only if it shows a fielded
   Shaman with the army and still not casting; a "casters follow the army" rule if it shows the Shaman left behind.
3. Requests 46 / 51's remaining parts, each only when the balance pass's numbers ask for it.

---

## View track

### Where we are
M6-VH7 on `main`. Counter **0 / 4** (the hardening just ran). 102 scenes, all green. Fixed: BUG-1323, BUG-1321 (400
figures), BUG-1320, BUG-1322 items 1-2, BUG-1230. New: **BUG-1350 S3** (a rare GC-handle ERROR line in `QaM6V20Test`).
Older: BUG-1177 item 2 (the owner's taste), BUG-1115 item 4, BUG-1331 item 1 (closed by the buildings' `scale`).

### Next task candidates
1. **M7-V4: the buildings' drawn `scale`.** Feature, QA standard.
   - **BUG-1350 first (small):** find why `GD.Load<Shader>` can log `Handle is not initialized` right after the scene's
     forced collection (a cached C# wrapper finalized while the engine object lives?). If the cause is the test's own
     `WaitForPendingFinalizers` racing a load, order the test so no load follows within a frame, or sample 12 cycles
     and compare halves without forcing; if the cause is in game code (a static holding a freed wrapper), fix that.
     Criterion: 40 runs with the scene loop running beside it, no ERROR line.
   - The placeholder box at `BuildingDef.Scale` about the footprint's centre; footprint, outline, selection ring, hit
     test, ghost and rally unchanged; health bar and label heights follow the drawn height. A forged row for the scene
     (the shipped Teblor carries `scale` only after D31); rule 56's screenshot on the shipped data is taken at the
     first ACCEPT after D31 is on `main` (the report says "forged row" until then).
   - Closes BUG-1331 item 1. Also commit `game/tests/QaM6VH7Test.cs.uid` (QA left it out; Godot writes it on import, so every clone shows it untracked).
2. **The art pass's next slice** (rule 60 lifted; rule 61): Heavy Infantry and the Whirlwind line from the two outfits
   recoloured, per the default in STATE's Waiting item; split tooling from view code; figures follow game speed; a
   per-weapon attack clip. Bench reported per rule 61.
3. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Where we are
D-H8 on `main` as tests; nothing shipped moved. Counter **0 / 4**. Fixed: BUG-1330, BUG-1331 items 2-3. New:
**BUG-1360 S4**. Open: BUG-1241 S3 (until the opening lands), BUG-1331 item 1 (the view's).

### Next task candidates
1. **D31: the one landing.** Feature, QA standard. **Rule 54: the Producer rehearses it on a scratch clone at the PLAN.**
   - Files: `game/data/common/statuses.json` (`teblor_frenzied`; the sim's folder, named here this once),
     `factions/teblor/abilities.json`, `techs.json`, `buildings.json`, `units.json` (the Gatherer's `"carry": 15`),
     `ai.json` (V24 as the standard opening with its own `attack.reserveArmyShare`), docs/02 (the Status effects row,
     the Frenzy Effect cell, the carry row), `docs/factions/teblor.md` (the landing's page + the Gatherer sentence + the
     AI section for V24), the pins both ways, the golden header once (rule 51: the two sim-owned hash pins, the Stress
     pin classes, the reason in `GatherWedgeQaTests`' comment).
   - **The share: 0.25 or 0.5, decided at the PLAN** (session log table). Whichever is chosen is re-run with the
     Gatherer's carry on the landed tree (64 matches, about 10 minutes) and reported beside this session's row; if the
     carry moves "seeds met" below 6 of 8, stop and report rather than re-tune.
   - **Shamans:** only if a one-line change does it (a larger `teblor_shaman` share once in Age II) and the same
     64-match row holds; otherwise the task after. Measure with the sim's `Stress/FrenzyCastReportTests` (read-only).
   - Expected reds before the pins are rewritten: the 37 named under "Where things stand". M7 criterion 3 is ticked at
     that ACCEPT. New player-facing text quoted for the owner (two texts are already in this session's review entry).
   - The old saves and recordings stop loading once (the data hash moves): say so in the report.
2. **Shamans in the opening + the full balance pass** (several seeds, both seat orders; inputs: the 0.75 m crowd
   figures, the aftermath-slow stacking note, Request 53's result).
3. BUG-1360 (S4) at the next hardening.
4. The owner's answers if any land ("teblor ai: early raid", the Teblor table, the counters, `goldShare`, BUG-0780,
   `counterStrength`, "Frenzy text: ...").
