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
