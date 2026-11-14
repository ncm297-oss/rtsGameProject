# Handoff: brief for the next session

> **PLAN of session 2026-11-14-0616 (1 / 8 of 2026-11-14) is written into this file:** each track's section below
> opens with its "Current session plan". Sim **M7-H2** (hardening, QA standard) · view **M7-V4** (feature, QA standard) ·
> data **D31** (feature, QA standard). Two Producer decisions made at this PLAN: the Teblor opening's own reserve share
> is **0.25** (confirmed by D31's measurement with the carry, 0.5 the fallback), and a building's `scale` is drawn as
> **height only**. No AI rule is touched this session (rule 41: D31 measures the opening). Everything under this note
> is the 2306 ACCEPT's handoff, still true.

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

### Current session plan (2026-11-14-0616, session 1 / 8 of 2026-11-14): M7-H2, the sim's hardening
Hardening (counter 4 / 4), QA **standard**. Bug ids 1370+.

Goal: pay the sim's small debts around the Teblor landing, without touching an AI rule or a shipped file. First make
the Frenzy report honest and reusable (BUG-1340), so the data track can measure "Shamans fielded by the opening" with no
sim edit; then the carry leftovers (BUG-1341), two forged-input gaps (BUG-1220 / 1221) and the tests that find shipped
rows by file position.

Scope:
- **BUG-1340.** `Stress/FrenzyCastReportTests.Measure` leaves units of the caster's type out of "the army" (QA's
  `Stress/FrenzyCastReportQaStressTests` is the model) and prints the diagnostic by phase (before / after the seat's
  first non-Shaman army unit). Re-run it; docs/03 "Implementation (M7-7)": the table, the "cause" bullet, the correction
  paragraph and "For the data track (M7-7)" become one consistent text (no sentence left that the numbers contradict).
- **An opening from a file.** A report configuration that reads one opening (the JSON object of an `ai.json`
  `openings[]` entry) from a path in an environment variable, puts it in as the Teblor's `standard` and prints the same
  columns, so the data track can run it read-only. Unset = today's behaviour.
- **Rule 58 / 59 (the landing arrives beside you).** D31 lands this session in the data track: `teblor_frenzied` in
  `common/statuses.json`, the Frenzy applying it, the Anointing's `statusDuration`, `scale` 1.25 on Teblor buildings,
  V24 as the shipped `standard` opening with `attack.reserveArmyShare` **0.25** (0.5 is the fallback), `"carry": 15` on
  `teblor_gatherer`. Every test you add or edit must be green on the shipped data **and** on a scratch clone that
  carries those rows (recipe: on a clone outside `.claude/worktrees`, `D30_WRITE_LANDING=1 dotnet test
  sim/Rts.Sim.Tests --filter FullyQualifiedName~TeblorLandingWriteTests`, then replace `ai.json`'s `standard` opening
  with `Content/Fixtures/teblor_standard_d29.json` plus the share, and add the carry). `FrenzyCastReportTests`' landing
  column and its `Assert.Equal(0.5f, ...ReserveArmyShare)` must not depend on a literal share or on the shipped opening
  being the pre-landing one: look the row up, then add or edit.
- **BUG-1341** items 1-3: the docs/03 sentence; `WorkerCarryTests.AGatherOrder_ToAWorkerHoldingItsOwnFullLoad_ReturnsIt`
  derives its load from `Rules.WorkerCarry`; a focused test of the AI's `EconomyManager.FullLoad` through
  `NoteDroppedGather` (an AI worker with carry 15 holding 10 is not sent home; at 15 it is), on a forged row that
  tolerates the Gatherer already carrying 15.
- **BUG-1220:** a save whose free unit slot holds status entries is `SaveError.Malformed`; the stealth / blind /
  `UnitsWithStatuses` totals are checked against the entries as `ReadRegenFrom` checks its own. No format change: every
  honest save still loads (the existing save suites and 50 mid-match saves). Un-skip QA's row.
- **BUG-1221:** a case-only folder listed twice reads "listed twice". Un-skip QA's row.
- **Position lookups:** `StatusLoaderTests` (the four rows that expect `statuses[0]`) and `Abilities/BloodOilScenes`
  (`techs[0]`, `abilities[0]`) find their rows by id.
- **The 600 s child timeout** in `Stress/RebuildLookMatchQaStressTests.PaleHillsSeed2_*`: make it hold beside a long
  loop (a longer limit or no child process), saying which.
- OUT of scope: `game/data/**` (the data track writes `common/statuses.json` this once), the golden replay
  (`Replays/cross_map_seed1.replay`), `FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`,
  `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders` and `QA/GatherWedgeQaTests`' comment (the data track's this
  session, rule 51), `Content/**`, `QA/Content/**`, any save or replay format, **every AI rule** (BUG-1149 / 1150 /
  1151 / 1152 and BUG-1161's three items wait for a session with no opening measurement beside them, rule 41), the cast
  rule (Request 53's second half), `sim/Rts.Sim/ViewApi/**`, `game/**`.

Acceptance criteria:
1. `FrenzyCastReportTests` counts no unit of the caster's type as army and prints both phases; on the shipped data its
   shipped-opening figures equal QA's (3 of 32 matches field a Shaman before 15:00; 2+ non-Shaman army units within 8 m
   in 250 of 14,996 ready thinks on the diagnostic, 2+ attacking in 0) or the report says why a figure differs.
2. With the environment variable set to `Content/Fixtures/teblor_standard_d29.json`, the report runs that opening and
   prints its row; unset, the output is as before. A bad path or a malformed opening fails with a message naming it.
3. docs/03 "Implementation (M7-7)" has no correction paragraph and no figure the fixed report does not print.
4. BUG-1341: the three items, each with its test or sentence.
5. BUG-1220: QA's skipped row green; a forged stealth count and a forged blind count refused, each by a new row that
   fails on the base; the save suites unchanged and green.
6. BUG-1221: QA's skipped row green.
7. No `[0]` position lookup left in the named classes; they pass with a status row, a tech and an ability forged in
   front of the shipped ones.
8. Every test added or edited is green on the landed scratch clone too (criterion run reported: the filters, the counts).
9. The shipped data hash 67269D955D18D885, the golden and save format 10 are untouched; `git diff <base>...HEAD --
   game/ sim/Rts.Sim.Tests/Replays sim/Rts.Sim.Tests/Content sim/Rts.Sim.Tests/QA/Content` is empty; three 9,000-tick
   matches are bit-identical base v head.
10. Build 0 warnings / 0 errors; **the full default loop after the last edit** (rule 62; or the report's first lines
    say it was not run); no test process left running (rule 52).

Design references: docs/03 "Implementation (M7-7)", "For the data track (M7-4)" and "(M7-7)", the save format's
"Known limits" under "Implementation (M7-H1)"; `studio/bugs/BUG-1340`, `1341`, `1220`, `1221`.

Tests required: the rows named above, each bug fix with a row that fails on the base; determinism (criterion 9).

Constraints: no AI behaviour changes (a moved M5-5 row or pinned match is a finding, not a re-pin); nothing in a
per-tick path; the load checks run at load only; stats and ids come from the data, never a literal; the developer
cannot write `studio/**` (bug updates as a patch in the scratchpad).

QA focus (standard: the default loop, the Stress rows touched, no Soak, no Perf, rule 53): forge saves at the new
checks' edges (a free slot with a stealth flag but no entry, a live slot whose counts disagree by one, totals off by
one) and prove no honest save from the existing suites is refused; run the changed classes on a landed clone made by
the recipe with the share at 0.25 and at 0.5; feed the opening-from-file configuration a missing file, an empty
object, an opening naming a Malazan unit and one with a different id; re-derive two figures of the fixed report from
the world.

### Next task candidates (after this session)
1. ~~**M7-H2, the sim's hardening.**~~ planned above. The notes below are the previous handoff's.
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

### Current session plan (2026-11-14-0616): M7-V4, the buildings' drawn `scale`
Feature (counter 0 / 4 → 1 / 4), QA **standard**. Bug ids 1380+.

Goal: `buildings.json` has carried an optional `scale` since M7-4 and the view ignores it; the Teblor's buildings get
1.25 this session (the data track's D31), so the view must draw it. First a small defect: the rare ERROR line in
`QaM6V20Test` (BUG-1350).

**Producer decision (owner may revisit): a building's `scale` multiplies its drawn height only.** The box keeps its
footprint's width and depth, as a unit's body keeps its collision radius since M7-V3c (BUG-1260): a box drawn wider than
its footprint would stand inside its neighbours and inside the units walking past. The alternative (scaled about the
footprint's centre in all three axes) is one inbox note away once real building models exist.

Scope:
- **BUG-1350 first.** Find why `GD.Load<Shader>` in `FogOfWar.TerrainMaterial()` can log `Handle is not initialized`
  right after the scene's forced collection. If the cause is the test's own `WaitForPendingFinalizers` racing a load,
  re-order the test (no load within a frame of the collection, or sample 12+ cycles and compare the halves' minimums
  with no forced collection); if game code holds a freed wrapper (a static cache), fix that. Say which, with evidence.
- A finished building's placeholder box is `BuildingViews.BoxHeight × BuildingDef.Scale` tall on the same footprint,
  its base on the ground; a construction site rises to that height; the placement ghost and the remembered-building
  ghost show the same height; the health bar (and anything else placed from `BarHeight`) sits above the drawn top.
- Footprint, outline, selection ring, click hit test, rally point and minimap unchanged.
- A new scene on a forged row (a data copy with one building type at 1.25 and one at 2.0; the shipped Teblor carries
  `scale` only when D31 lands). **Rule 59:** D31 lands beside you, so run the scene loop also on a scratch clone that
  carries D31's rows (outside `.claude/worktrees`: `D30_WRITE_LANDING=1 dotnet test sim/Rts.Sim.Tests --filter
  FullyQualifiedName~TeblorLandingWriteTests`, build, import, `tools/qa/scene-loop.ps1`); `TeblorViewTest` and every
  scene that reads a Teblor building's bar or box must be green there as well as on the shipped data.
- Commit nothing under `tools/**` (none needed). `tools/asset-manifest.json`, `tools/import-assets.ps1` and
  `tools/art/**` are yours if a need appears; the rest of `tools/**` is the sim's.
- OUT of scope: `game/data/**`, any sim file outside `sim/Rts.Sim/ViewApi/**` (none should be needed:
  `BuildingDef.Scale` is loaded already), unit models and the art pass, building models, the hit test, a wider box.

Acceptance criteria:
1. BUG-1350: the cause named with evidence; `QaM6V20Test` 40 runs with the scene loop running beside it, no `ERROR`
   line, objects still flat across restarts (BUG-1323 not undone).
2. On the forged data a 1.25 building's box top is at 1.25 × `BoxHeight` and a 2.0 one's at 2 ×, both with the
   footprint's width and depth; the site's rise ends at that height; the placement ghost and the remembered ghost match.
3. The health bar sits `BarHeight - BoxHeight` above the drawn top for every scale; no bar inside a box.
4. A scale-1 building is drawn exactly as before: screenshots of a Malazan base at the base commit and at the head are
   byte-identical (`--placeholder-art`, same seed, same frame), or the report says which pixels differ and why.
5. The footprint, outline, ring, hit test and rally rows of the existing scenes pass unchanged; a click just above a
   scale-1 box's old top that now lands on a taller box selects what it selected before (the hit test does not follow
   the height) and the report says so in a sentence for the owner.
6. No allocation added to the per-frame sync (the existing 0-bytes rows stay green; a new row covers a scaled type).
7. On the landed scratch clone: the scene loop green (every scene, first pass or a named load flake rerun once) and a
   screenshot of a Teblor base beside a Malazan base at the default zoom, **looked at**, with one sentence on whether
   1.25 × in height reads as "bigger" (rule 56: this is the landing's data, say so; the Producer re-takes it on the
   merged tree at ACCEPT). Pictures go to the scratchpad; the report names the paths.
8. docs/03 "Implementation (M7-V4)" (the rule, the decision above, BUG-1281's sentence made true); the patch that closes
   BUG-1331 item 1 and BUG-1350 in the scratchpad.
9. Build 0 / 0; `tools/qa/smoke.ps1` PASS; the scene loop green on the shipped data; **the full default loop after the
   last edit** (rule 62); no Godot or `testhost` left running (rule 52).

Design references: docs/03 "Implementation (M7-4)" (the building `scale` field, 0.5-3.0), "Implementation (M7-V3c)"
(the unit rule this follows), `docs/factions/teblor.md` "Buildings" ("1.25× scale visually"), `studio/bugs/BUG-1350`,
`BUG-1331` item 1, `BUG-1281`.

Tests required: the new scene (criteria 2, 3, 6), the existing scenes unchanged (4, 5), the 40-run row (1).

Constraints: views hold no gameplay state; the height comes from the data, never a literal per faction; no text in C#;
nothing allocates per frame; the scene loop's known load flakes are in the handoff's watch-outs, any other red row is a
finding.

QA focus (standard: the default loop, the scene loop, no Perf): scales at the limits (0.5, 3.0) on a 1×1 and the
largest footprint, on a ramp and beside a cliff; a site cancelled half-built, a building destroyed and remembered
under fog, a ghost over blocked ground; bars of a damaged scaled building at the nearest and farthest zoom; the
selection ring and box-select of a tall building behind a short one; 40 more `QaM6V20Test` runs under a different load;
the same pictures on the landed clone with the real Teblor.

### Next task candidates (after this session)
1. ~~**M7-V4: the buildings' drawn `scale`.**~~ planned above. The notes below are the previous handoff's.
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

### Current session plan (2026-11-14-0616): D31, the one landing
Feature (counter 0 / 4 → 1 / 4), QA **standard**. Bug ids 1390+.

Goal: ship the Teblor's finished numbers in one move, so the data hash (and with it every old save and recording)
changes once: Blood-oil Frenzy's extra damage and the slow afterwards, the Anointing's longer Frenzy, buildings 1.25×,
the measured AI opening (V24) with its own reserve share, and the Gatherer's load of 15. This is M7 criterion 3.

**Rehearsed at the PLAN (rule 54 / 55), scratch clone of e13aac2 with the rows below, V24 at 0.25 and the carry:** build
0 / 0; default loop **18 failed / 9,158 passed / 21 skipped of 9,197** (the write helper had already written the two
docs, so fewer than QA's 37): the two sim-owned golden hash pins, and 16 rows in `Content/**` / `QA/Content/**`
(`AiContentTests` A and F, `AiSpendingReportTests.GeneratedNormal_PagesAndDocs03_QuoteTheReport`,
`TeblorLandingPinTests.OnTheCopy_*`, `TeblorRosterPinTests` x7 (`AnAiCellChanged_IsCaught` x5,
`TheLandingsStatusRow_IsHeldBothWays`, `TheOwnerReviewTable_*`), `AiHouseRuleReportTests` (teblor),
`D20PinQaTests.HouseNote_*` (teblor), `TeblorOpeningQaTests` x3). **Any other red row on your tree is a finding.**
Smoke on that clone **PASS**; the scene loop **102 of 102, first pass** (no view scene pins the data hash or a Teblor
building's height today).

**Producer decision: the opening's own `attack.reserveArmyShare` is 0.25** (D-H8's table: 0.25 and 0.5 both meet 6 of 8
seeds; 0.25 leaves the larger army at 15:00, 56 v 36 pop, and builds every building kind in 8 matches v 0). It is
confirmed or replaced by the measurement in scope item 3, by the rule written there, not by taste.

Scope (files; no other track edits them this session):
1. `game/data/common/statuses.json` (the sim's folder, yours this once): the `teblor_frenzied` row exactly as docs/03
   "For the data track (M7-4)" gives it (id sorts after `stealthed`). `factions/teblor/abilities.json`: the Frenzy's
   effect and its tooltip. `factions/teblor/techs.json`: the Anointing's `statusDuration` +4 s and its description.
   `factions/teblor/buildings.json`: `"scale": 1.25` on all ten. Use `Content/TeblorLanding`'s write helper on a
   scratch clone and copy the six files, or make the same edits by hand; the diff must be the landing's lines only.
2. `factions/teblor/units.json`: `"carry": 15` on `teblor_gatherer`, on no other unit, never `null`.
3. `factions/teblor/ai.json`: the `standard` opening becomes V24 (`Content/Fixtures/teblor_standard_d29.json`, which
   stays in place as the record; the sim's report reads it) with `attack.reserveArmyShare`. **Measure first, on the
   landed tree with the carry:** `TeblorReserveShareReportTests`' harness, own 0.25 and own 0.5, seeds 1-8, 64 matches
   a row, printed beside D-H8's two rows. **Ship 0.25 if it meets 6 of 8 seeds; else 0.5 if that does; if neither
   does, land everything except the `carry` (V24 at 0.25, D-H8's measured row), leave the carry for the balance pass
   and say so in the report's first lines.** No other re-tune of the opening.
4. **Shamans, only if one line does it:** a larger `teblor_shaman` share in the Age II composition, kept only if the
   same 64-match row still meets 6 of 8 and `Stress/FrenzyCastReportTests` (read-only; the sim is fixing its
   diagnostic column this session, so read only "matches that field a Shaman before 15:00" and "casts landed") shows
   more Shamans. Otherwise leave the composition and say what you measured; it is the next data task.
5. Docs: docs/02 (the Status effects row, the Frenzy Effect cell, the carry row: "none ships one yet" becomes the
   Gatherer's 15), `docs/factions/teblor.md` (the landing's page text, the Gatherer sentence, "AI build order" /
   "Opening 1" for V24 and its share, the Owner review table). **The buildings' sentence says the box is drawn 1.25×
   taller on the same footprint** (the view builds exactly that this session, M7-V4; a Producer decision). In
   docs/03 only the lines a content pin quotes (`AiSpendingReportTests.*_PagesAndDocs03_QuoteTheReport`'s rows); the
   sim rewrites "Implementation (M7-7)" this session, so stay out of that section.
6. Pins both ways in `Content/**` and `QA/Content/**` for every new number and word (a wrong status id, a wrong share,
   a carry on a second unit, a building without `scale`, each caught by name), and one combat scene **on the shipped
   data**: a Shaman casts, a Warrior inside 8 m strikes 20% harder and 40% faster for 10 s (14 s with the Anointing),
   then is Slowed 20% for 4 s; a unit outside is untouched.
7. The golden, once (rule 51; the sim's files, yours this session): regenerate `sim/Rts.Sim.Tests/Replays/
   cross_map_seed1.replay`'s header as D28-L did, write the old → new hash and the reason in `QA/GatherWedgeQaTests`'
   comment, and see `FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders` and
   `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders` green. Its `k` lines must be byte for byte as before
   (a Malazan v Whirlwind match does not move); if one moves, stop and report. **Run the Stress pin classes** and report
   each: `OutcomePinM73bQaTests`, `FactionOrderQaTests`, the four `FactionOrderTests` copy pins, `StandardPresetPinQaTests`,
   `NonHardMatchPinQaTests`, `HardKnobsPinTests`. A Teblor-copy digest that moves because the copy now carries the new
   rows is re-pinned with the old value in a comment; a Malazan or Whirlwind one that moves is a finding.
- OUT of scope: any C# outside `Content/**` and `QA/Content/**` except the three named sim-owned test files' pin and
  comment; the schema; `rules.json`; every other faction's files; a balance change to any unit's stats or costs; the
  Teblor AI's raid size (the owner's open question); BUG-1360 (next hardening); `Stress/FrenzyCastReportTests` (the
  sim edits it this session: do not touch it).

Acceptance criteria:
1. The six data files carry exactly the rows above; `git diff` shows the landing's lines only (no re-indent, line
   endings kept); `DataValidationTests` green.
2. The share shipped is the one item 3's rule picks, with both measured rows (and D-H8's two) in the report and on
   the page.
3. The shipped Frenzy, on the shipped data, in a combat scene: +40% attack speed and +20% damage for 10 s, 14 s with
   the Anointing, Slowed 20% for 4 s after, nothing outside 8 m (scope item 6's test, named).
4. Every number and word on `docs/factions/teblor.md` and docs/02 that the landing touches equals the files, pinned
   both ways; the 16 `Content/**` / `QA/Content/**` rows the rehearsal turned red are green by a rewritten pin or a
   re-measured report, each listed old → new in the report.
5. The golden header moved once, the reason is in `GatherWedgeQaTests`' comment, the two hash pins are green, the `k`
   lines unchanged; the Stress pin classes' results listed.
6. Shamans: the one-line change with its two measurements, or "not changed" with the measurement that says why.
7. The For-your-review table for the owner in the report: unit / building / tech / status / opening, field, old →
   new, one line why; every new or changed player-facing name and description quoted; one sentence that saves and
   recordings from before this session stop loading.
8. BUG-1241: closed if the shipped opening meets "Age II and a wave by 10:00 in 6 of 8", else left open with the
   figures (a patch in the scratchpad either way).
9. Build 0 / 0; **the full default loop after the last edit**, green (rule 62); no test process left running.

Design references: `docs/factions/teblor.md` ("Faction upgrade", "Unit notes" Gatherer, "Abilities", "Buildings",
"AI build order", "Owner review"); docs/02 "Combat / Stats" status effects and the economy table's carry row; docs/03
"For the data track (M7-4)" and "(M7-7)", "Implementation (M7-6)" (the opening's own share); last session's log (the
share table, the two texts).

Tests required: scope items 6 and 7; `DataValidationTests`; the replay golden tests.

Constraints: no stat in C#; player-facing text only in the data; the status id's sort order; one hash move; rule 41
(the measurement is made on the tree that ships, carry included); the developer cannot write `studio/**`.

QA focus (standard: the default loop, the Stress pin classes named, the 64-match rows; no Soak, no Perf): re-run the
shipped share's row independently and compare all columns; play the shipped Frenzy at the edges (a unit entering the
aura after the cast, a second Shaman's cast refreshing it: does the aftermath fire once; death while frenzied; the
Anointing researched mid-Frenzy; a Slowed unit bitten by a War-dog at the same time); a carry-15 Gatherer on a nearly
empty mine and on the last tree; mutate each new row (id, share, carry, scale) and name the pin that catches it; load
a save and a recording made on the base commit and read the refusal text; check the diff for stray formatting.

### Next task candidates (after this session)
1. ~~**D31: the one landing.**~~ planned above. The notes below are the previous handoff's.
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
