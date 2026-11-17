# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-16-2036** (session 1 / 8 of 2026-11-16, started 20:36; it
ran about 11 hours: QA full with one 4 h 17 m Stress run, one sim fix round; the ACCEPT on 2026-11-17 about 06:10).
Sim **M6-S12** ACCEPT (feature, 1 fix round, QA full FAIL → PASS_WITH_ISSUES), view **M6-V24** ACCEPT (feature, 0,
QA standard PASS), data STOP at the PLAN. **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.**
Counters: **sim 1 / 4, view 2 / 4, data 1 / 4**. Bug ids next session: **sim 1580+ (20 ids), view 1600+, data
1610+**. Open S1 / S2: none. The next session is **1 / 8 of 2026-11-17**.

**Next session in one line (proposal):** sim **M8-2: the Shadow Archer's stand-still stealth** (feature, QA full; no
AI behaviour change) · view **M6-V25: the next art slice** (feature, QA standard) · data **D33, the start of the
balance pass** (QA standard, the one hash move of the session).

**The default loop on `main` is green.** Expect **0 failed, 20 skipped** (9,763 passed / 0 failed / 20 skipped of 9,783 in 42 m 20 s on the
Producer's scratch clone of sim 30c23d6 + view d6bbbe9). Any red row is a finding.

**At the PLAN, first:** (1) the inbox; (2) the default loop on `main`; (3) **D33 moves the data hash**: the golden's
header and the two sim-owned data-hash pins are handed to the data developer by name (rule 51), and **the sim's M8-2
must ship test data only** (no byte under `game/data/common/**`; two tracks never move the hash in one session);
(4) **M8-2 must not change Normal AI behaviour** (rule 41: D33 measures openings on `main`'s AI); if its trace says a
shipped rule must move, stop and ask; (5) the view's art slice adds LFS binaries only of types already in
`.gitattributes` and no new third-party pack; (6) session length: no pin move next session, so QA full on the sim
runs Stress once and should not need four hours beside a balance task (tell the data's QA to run its scenario rows
first and its Stress rows alone).

## Current session plan

None yet (written at the next PLAN).

## Where things stand

- **`main` after this session = sim 30c23d6 + view d6bbbe9** (plus this ACCEPT's studio and docs files). The
  Producer's scratch clone `prod2036-accept` (merged without conflict): build `--no-incremental` 0 / 0, default loop
  **9,763 passed / 0 failed / 20 skipped of 9,783 in 42 m 20 s**, smoke PASS, scene loop **115 of 115, first pass**.
- **No data byte moved.** Data hash **B74DC7AE61B4862D**; formats unchanged (save 10, replay 9 / 6); the golden
  `cross_map_seed1.replay` unchanged. Saves and recordings from the morning of 2026-11-15 on still load. The premap
  AI replay and the `save_ai_seed2` digest were re-recorded.
- **The AI (M6-S12):** `MilitaryManager.RallyPoint` → `OffThePassage` / `Acceptable` / `NarrowWithin` (a one-cell
  lane within `ai.rallyReach` of the nominal point moves it along the same line, back first, then out, at most
  `ai.rallyReach`); `PlayerView.IsKnownClosed` (terrain, known buildings, live nodes on explored ground; **BUG-1555:
  it reads the cell's truth for a razed ghost and a felled node**); `EconomyManager.Rebalance` measures from
  `NearestDropOff` (idle picks unchanged); `ScoutManager.Rescout` runs after `EconomyManager` in the think
  (`BlindRescoutDue` on the first think of each `ai.scoutAfter` window, `BlindTo`, `AiPlayer.PickShortOf`,
  `PickBuilder(idleOnly: true)`). No new saved state.
- **Decided at this ACCEPT, not built (docs/01 2026-11-17):** the re-scout counts blind scouts lost and stops at
  `ai.scoutReplacements`, after which it may take a gatherer (BUG-1554 / 1556; a save-format change and a pin move);
  the rally search reads the seat's memory (BUG-1555). Both in the sim's next AI task, never beside a data balance
  task.
- **Stress rows red on `main`, 2:** `Bug0982_*` x2 (**the data's D33**). Everything else in Stress is green or
  skipped with an id. Perf alone: `Perf_UnitsConvergingOnOnePoint (500)` is flaky on its worst tick (BUG-1539).
- **Skipped with an id since this session:** five `KeepCornerCureQaStressTests` rows at 0.5 (BUG-1463; bodies and
  bounds unchanged; a movement task's). Still skipped: `DR35ProsePinQaTests.Malazan_Bug0982Prose_*` (BUG-1524 item 3;
  D33 un-skips it), `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall` (BUG-1068 / 1116 / 1559).
  `ReachRuleQaTests` (2, 80) is un-skipped. `MatchCriterionSweepQaStressTests.Known` lost Raraku 1, Raraku 2 and
  Vathar 2. Known cases printed, not failed: `ScoutCapQaTests.KnownWorkerLosses` seeds 5 / 6 (BUG-1557; the label
  in the test says BUG-1462), `AiOpeningPairsReportTests.AliveWithoutAWave` (0, 2) seed 3 (BUG-1553).
- **The view (M6-V24):** a unit `.glb` is one surface with a material named `team` and one `<id>_atlas.png` (512
  tiles; alpha 0.5 team, 0.75 prop, 1 other; the layout in the file's extras, `UnitModels.AtlasOf`);
  `team_colour.gdshader` draws the whole figure (`use_atlas`, `atlas_grid`; Burley diffuse; a team texel culled as
  the team surface was; a file without the layout is drawn the old way); `UnitViews.CastProgress` / `CastClipTime`
  seek the cast clip from the sim's `CastTicks` every frame. **115 scenes** (`AtlasModelTest`, QA's `QaM6V24Test`;
  `QaM6V24Shot` and `ModelShots --compare` are windowed tools). Bench alone-ish: 400 figures 6.2-6.6 ms, 1,000
  figures 16.5-17.5 ms (while 90 %+ alive); draw calls 1,003 / 2,660.
- **Requests:** 58 open (dropped queued orders for the view; small).
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's
  list; nobody touches the owner's checkout).
- **The conductor's lock heartbeat** (every 25 minutes during QA) is in use: a QA full pass is longer than the
  3-hour stale window.

## Standing rules (1-71)


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
**66 (new, 2026-11-14-2156):** at PLAN every invariant criterion ("no worker ...", "every pair ...", "within N s") is
checked two ways before it is written as pass / fail: **does it hold today with the feature off** (if not, or if the
out-of-scope list forbids what would make it hold, it is written "measure and report, against off": rule 48), and
**can it hold together with every other criterion** (M6-S9: criterion 5's Stop clause against criterion 4, criterion
7's 60 s window against "off is identical"; both cost a fix round and an ACCEPT by ruling). A criterion QA shows to be
the brief's error is corrected in the session log by name, never passed silently.
**67 (new):** a developer's open questions for the Producer are written where QA can read them (a "Questions for the
Producer" section of the page or docs section the task owns, each with the recommendation); the Producer's answers
go to docs/01 and the next task writes them in (D-R38: Q1-Q11 lived only in the returned report).
**61 (amended again 2026-11-14-2156):** the order after the Teblor humanoids is the owner's Shift-queue request, the
view's hardening (4 / 4), then merged surfaces; 400 mixed figures of eleven types are at 11.1-11.4 ms alone (worst 1 % 15.4-15.7).
**68 (new, 2026-11-15-0506):** a brief never writes "the default loop green" beside rows it forbids the developer to
fix. When a task re-prints cells that another track's prose pins read, the brief hands those pins and sentences over
by name, or writes the criterion as "red only in these named rows, ruled at ACCEPT". At PLAN the rehearsal's red list
is sorted into "re-pin", "re-stage" and "another track's" before the criteria are written (M6-S10: criterion 14
against 10 and 12; five default-loop rows reached `main` red by ruling). A fix round with nothing to act on but a
Producer ruling may be skipped by the conductor; the session log says so.
**69 (new, 2026-11-15-1536):** a developer or QA agent ends only the processes it started itself, by the process ids it
recorded when it started them; never by matching command lines or names (M6-S11's developer force-stopped 34 processes
whose command line matched "Total tests" / "Passed!|Failed!", some possibly another track's; nothing was lost, by luck).
A brief that runs long suites says "record the PID of every `dotnet test` / Godot you start".
**70 (new, 2026-11-15-2136):** when a QA FAIL leaves nothing a developer can act on without a Producer ruling, the
conductor calls the Producer, who answers with rulings and a fix brief instead of a verdict; the last fix round applies
them (tests and docs only when possible, so no Stress re-run is needed). `main` takes no red default-loop row by ruling:
a row the ruling cannot make green is skipped with its bug id in the repo's `Skip = "BUG-..."` form, its cure first in
the next brief. An owner bug that changes gather or AI behaviour moves every AI pin through divergence (BUG-1520 did,
alone): plan it only where a pin move is planned, and tell the developer to measure its effect apart from the rest.

**68 (amended 2026-11-16-2036, BUG-1552):** the content classes handed to a sim developer whose task moves Normal AI
play while the data track is stopped are seven: `Content/AiOpeningPairsReportTests`,
`Content/HolyWarFirstWaveReportTests`, `Content/AiSpendingReportTests`, `QA/Content/DR35ProsePinQaTests`,
`QA/Content/TeblorOpeningQaTests`, `QA/Content/D21OpeningsQaTests`, `QA/Content/D20PinQaTests`, with the pages' cells
they read (Age II tables, "Houses at the cap", "Spending on the menu's map", the holy war's first wave, the Teblor
opponent sentence).
**71 (new, 2026-11-16-2036):** a pin-move brief lists the pin classes one a line, and the developer's report carries
the same list with "re-measured: yes / moved to ... / unchanged" beside each, Stress pin classes included (M6-S12:
the four-seat FFA pin was on the list and never run: BUG-1559, S2, one fix round). Such a task gets 20 bug ids
(M6-S12 used all ten). When rule 45 moves a row onto a seed another row already plays, the report says so.


## Watch-outs (all tracks)
- **The default loop is green on `main`** (20 skipped on the merged tree). A
  "default loop with Stress" run expects only `Bug0982_*` x2 red, until D33 lands. `main` builds **0 errors /
  0 warnings**.
- **New this session: the AI's rally point may differ from `hall + rallyDistance`.** A test that stages an AI seat
  and expects its army at the nominal point must keep one-cell lanes (between its own buildings too) more than
  `ai.rallyReach` away, or read `Board.Rally`. A rebalance now goes to the node nearest the worker's nearest finished
  drop-off; a blind seat (phase 2, short of a resource, no live node known, an idle worker) sends a scout on the
  first think of an `ai.scoutAfter` window: a staged seat that must sit still needs a known node of both kinds or no
  idle worker.
- **A construction site is not a drop-off** (M6-SH2). A test or scene that places a drop-off building and expects
  deliveries there must finish it first; a loaded worker with only a site standing waits in state `Returning`. The AI
  retrains a worker when it has none: a test that keeps a seat worker-less must also keep it without the gold
  (`HuntTests.TheRingLeg_*` is the model).
- **Unit models are one surface now.** A scene that pinned a surface count or a team surface index reads
  `UnitModels.AtlasOf` / the one `team` material (`ModelViewTest`, `TeblorModelTest`, `QaM7V5Test`,
  `OverlayLineTest` were updated). A new model type goes through `tools/art/build_unit.py` `build_atlas`; the role
  PNGs no longer exist. Team texels are lit Burley (1-4 levels brighter than before M6-V24): a pixel pin from
  before is stale.
- **Session length:** 2036 ran about 11 hours (Stress 4 h 17 m beside the default loop; the view's default loop
  2 h 43 m under that load). The Perf category was not evidence (the PC's slow state: `ASweepRetarget` 78 us).
- **Session length:** 2136 ran about 22 hours (the Stress category 3.8 h and 4.0 h beside the default loop; the
  default loop 43 m to 2.5 h under load). M6-S12 is QA full with a pin move again: see "At the PLAN" item 4.
- **New this session: a gather loop ends for a queue** (`EconomySystem.EndsForQueue`, only when `QueueCount > 0`). A
  test that parks a worker on a loop with a queued order must put it in reach of its node with a drop-off standing
  (`ViewApi/OrderPathTests.QueuesBehind_*` is the model), or the queue starts at once. The AI never queues.
- **`EndGameHardeningTests.PaleHillsSeed2_FourSeats_*_TwoProcesses_*` has a 1,200 s wall limit** and failed under a
  contended Stress run (3 m 11 s alone): a Stress red there counts only when it fails alone.
- **Scratch clones left behind (permission prompt denied):** `qa1536\m6vh8-qa-clone` and `m6s11-qa-clone` in session
  e9f70028's scratchpad; harmless, on the owner's housekeeping list.
- **`gatherOverlap` 0.5 is in the shipped `rules.json`.** A test that means "the rule off" removes the field from a
  copy (`GatherOverlapScenes` writes or removes it itself; rule 58); "the base's data" in a fixture means the field
  taken out (`D32PreFixtureTests`). Any value above 0 turns the gather floor on.
- **The data hash moved (B74DC7AE61B4862D):** every save and recording fixture from before is refused. The next
  hash move is the data's (D-R39 only if a number must move, else D33) or the sim's hardening: never both in one
  session.
- **Session length:** 0506 ran about 9.5 hours (the sim's QA full: default loop 1 h 42 m and Stress 2 h 3 m beside
  the other tracks). Keep the data task light next session and tell the view's QA to run timing rows alone.
- **The view's scene loop is 115 scenes** (`AtlasModelTest`, QA's `QaM6V24Test` are new). `BenchTest` (7 s),
  `MinimapTest` (0.5 ms), `DebugOverlayTest`, `QaM25Test`, `QaM27Test` and `QaH1Test` flake under load and pass alone.
- **`ViewApi` source may not name `World` or `UnitStore`** (`TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls`):
  `OrderPath` is built from the store's arrays, handed in by the view (`OrderPathView.NewReader`).
- **Eleven unit types are models** (Laborer, Crossbowman, Heavy Infantry; Camp Follower, Raider, Desert Archer; the
  Teblor Gatherer, Warrior, Javelin Thrower, Shaman, Breaker); ten are capsules (`ModelViewTest` prints "11 models,
  10 capsules; teblor capsules: [teblor_destrier_rider teblor_war_dog]"). A count of model types is read from
  `tools/asset-manifest.json`, never a literal (`QaM6V21Test` row 1). A model file holds the 1.84 m base figure; the
  data `scale` is applied by the view. Bars and marks sit over the figure as drawn since M6-VH8 (`UnitViews.OverlayLines`).
- **Session length:** 2026-11-14-2156 ran about 7 hours (builders and QA about 6; one sim fix round; the lock was
  refreshed four times). QA full on the sim beside a standard and a light pass still gave default loops of 1 h 4 m
  to 1 h 15 m against 22 m alone. Next session the sim is on QA full again with pin re-measures: keep the data task
  light and tell the view's QA to run its bench last.
- **The third faction's order of work now:** M6-S10 (the wedge + the gather value; every opening measurement is
  redone after it) -> data **D33** (BUG-1420) as the start of the balance pass -> the sim reads the Frenzy report
  again (Request 53 part two). The Teblor humanoids are done; the Rider and the dog wait on the owner's packs. M7
  criterion 4 is ticked after the balance pass; the end-of-M7 hardening follows it.
- **Three factions ship now.** A fixture that means "the two original factions" uses `TestSim.TwoFactionData`,
  `TestDataDir.CopyOfShippedTwoFactions()` / `CutToFirstFactions(2)` (sim) or `ThirdFactionForge` (view, cut to two).
  A pin about "every shipped unit" must be true for the Teblor too (a bite, a pack, a scale, a self-cast aura, radius
  0.75, regeneration).
- **Radius 0.75 is a fourth radius class** (+131,072 bytes on a 1024 map; the shipped-data memory row allows it by
  class count). A single-type 0.75 m crowd arrives about 40% less often than a 0.4 m one on the crowd sweep's maps
  (`Stress/WideCrowdStressTests`, a report with no bound): 500 to 4 points 172.6 v 288.1 mean arrivals, 128 to 64 goals
  58.4 v 112.1. If a sim task changes movement, re-run it and print both columns.
- **The scene loop is all green: 111 scenes** (`OverlayLineTest` and QA's `QaM6VH8Test` are new; `QaM6V21Shots` and `ModelShots` are windowed tools, not in the loop). `QaM6V20Test`
  samples after a forced collection (objects flat at 3,252); BUG-1350 is fixed by cause, so an ERROR line there is a
  finding. Rows that flake under load are listed below; any other red row is a finding.
- **Model animation is manual:** every model's `AnimationPlayer` is in manual mode and `UnitViews._Process` advances it
  through `CrowdAnimator` (48 nearest every frame, the rest 1 frame in 3). A scene that calls `Sync` directly and wants
  motion calls `AdvanceAnimations(delta, eye)` too. A model shows no worker tint (`ShownTint` still records the state).
  Since M6-V21 the figures follow the game speed (`UnitViews.AnimationRate`: the runner's speed, 0 while the tree is
  paused); a replay that has reached its end is not paused, so its figures keep moving in place (a small view item).
  Each model type has its own `AnimationLibrary` (state names -> the shared clips, from the clip map in the `.glb`'s
  glTF extras); a slot's type and model are set before the fog check.
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

- **Pinned matches on `main` after M6-S12 (every one re-measured once, old values in "was" comments; docs/03
  "Implementation (M6-S12)" item 6 lists old -> new):** the Hard v Normal tally **34 / 7 / 1** (Raraku 13 / 1 / 0,
  Vathar 12 / 2 / 0, Pale Hills 9 / 4 / 1); `MatchCriterionTests` (Vathar **seed 1** 23,244 / Raraku seed 3 27,394 /
  Pale Hills seed 1 33,230 / generated **seed 1** 21,876 / FFA 38,690 / 2v2 61,686; `HalfWay` 11,622);
  `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, the eight `CounterCompositionTests.TheNonHardPins_*`,
  `ReserveTwinTests`, `SharedVisionFfaPinTests`, `OutcomePinM73bQaTests`, the premap AI replay, `save_ai_seed2`,
  `CliTests.Match_TheLiteralRow_*`; unchanged: `HardKnobsPinTests`, `seed21_playable.replay`, the golden. Stress:
  `AiDeterminismTests` (`HuntSeed` 2: ends 51,218, 2 hunts; its comments still carry M6-SH2's figures),
  `HuntStressTests` (`MidHuntSeed` 2; **the four-seat FFA oracle row seed 2**, 59,951, the same match as
  `FourAiSeats_OnPaleHills_Seed2_KnownStall`: drop that twin at the next pin move), `M6Sh2DeterminismStressTests`
  (the retrain twin on **Raraku seed 3**), `M6Sh2QaStressTests` (the save sweep on **Raraku seed 2**),
  `StarvedSaveTwoProcessQaStressTests` (Raraku seed 1), `ReservePairingsStressTests` (`KnownNoAgeTwo` (2, 0, 1, 1)),
  `MatchRowsM6S10Report` (seeds 1-8 on the three maps: all end). The next sim task that changes Normal AI behaviour
  or gather movement re-measures all of them (rules 43 / 45 / 71).
- **Known stalls / skips with ids on `main`:** the four-seat Pale Hills FFA seed 1 (BUG-1068 / 1116 / 1559: the
  map's gold is gone by 30,000), seeds 3 and 4 (BUG-1536, the owner's question), seeds 6 and 7 (no end by 144,000,
  measured in M6-S12's fix round, same shape); the five `KeepCornerCureQaStressTests` rows (BUG-1463);
  `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*`
  (BUG-1156), BUG-1161's three rows, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the view's `QaM7V1Test`
  zoom-40 1.5 head row; `DebugOverlayTest` and `QaM25Test` 1 ms rows flake under load.
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.

---

## Sim track

### Where we are
M6-S12 on `main`. Counter **1 / 4.** Fixed: BUG-1460, 1461, 1531; in-session BUG-1559 (S2), 1550, 1551, 1552; closed
by measurement BUG-1462, 1521, 1522, 1523. Ruled for the next AI task: BUG-1555 (fog leak in the rally search),
BUG-1554 / 1556 (the re-scout's idle-only gap and missing cap: a saved count, cap `ai.scoutReplacements`). Debt:
BUG-1557 (S3), BUG-1558 (S4), BUG-1463 (five rows skipped; a movement task's), BUG-1536 (the owner's question),
BUG-1537 (S4 now), BUG-1539 (a flaky Perf row). Older: BUG-1490 (with the next save change), BUG-1434, 1433, 1161,
1149, 1152, 1150, 1010. The Producer's answers to Questions 1-9 are in docs/01 (2026-11-17 row) and the session log.

### Next task candidates
1. **M8-2: the Shadow Archer's stand-still stealth** (Request 56 G1; M8 criterion 1; feature, QA full). A generic
   rule with data hooks, schema first, **test data only** (D33 moves the data hash next session: no byte under
   `game/data/**`). **No change to Normal AI behaviour** (rule 41), so no pin moves: the criteria say "every AI pin
   class green as it stands; the golden and the data hash unchanged". If it adds saved state (a stand-still timer),
   the save format changes once and **BUG-1490's one-trip flag rides with it** (docs/01 2026-11-15 (c) item 2); the
   brief then names the view scenes that pin save bytes (rule 50). Read shadow.md's Decisions and docs/02 "Stealth"
   for the numbers; M8-1's `ForestStealthSystem` is the model.
2. **The AI follow-up (QA full, one pin move, rules 43 / 45 / 68 / 71; only in a session where the data track does
   not measure openings):** BUG-1555 first (`IsKnownClosed` reads a remembered ghost as closed and a node as the
   seat last saw it; QA's `IsKnownClosed_*_Bug1555` row flips); the saved count of blind re-scouts lost, capped by
   `ai.scoutReplacements`, after which a gatherer of the plentiful resource may be taken (BUG-1554 / 1556; QA's
   `KnowsGold_NeverSawATree_*_Bug1554` row flips, `EveryScoutKilledAtOnce_TheBlindSeatsDrain_*` gets a bound);
   BUG-1558's three edges (a minimum distance from the hall, never fall back onto a closed cell, no flap under a
   flickering target); `ScoutCapQaTests.KnownWorkerLosses`' label BUG-1462 -> BUG-1557 and one trace of why the
   woodcutters go to the 147-154 m forests; the seed-2 twin row of `HuntStressTests` dropped; `AiDeterminismTests`'
   comments.
3. **Request 58** (dropped queued orders for the view; small).
4. Requests 54 / 55, 53 part two, 56 G3-G8. A movement task for BUG-1463 only with a design first (a worker at work
   yields to a loaded mate in a one-cell lane), never as a rider.

---

## View track

### Where we are
M6-V24 on `main`. Counter **2 / 4.** 115 scenes. Fixed: BUG-1530. No new bug. Eleven unit types are one-surface
models; ten are capsules. 400 figures about 6.5 ms a frame, 1,000 about 16.5-17.5 ms. The Producer's answers to
Questions 1-4 (all accepted) are in docs/01 (2026-11-17 row).

### Next task candidates
1. **M6-V25, the next art slice (feature, QA standard; rule 61's bench in the report):** at the PLAN read which of
   the ten capsule unit types are humanoids on foot (no horse, dog or machine) and plan those as figures from the
   packs on disk (the two outfits recoloured, props shaped in Blender, as docs/04 and the owner's default say); the
   design is clear, so a whole slice (up to about five types) fits. If fewer than two are left, plan instead **the
   first building models** as an uncertain slice (about 800 lines): the import path for a static building from
   `quaternius-ultimate-fantasy-rts-2022-08` (one surface, team colour by the same mask) and two Malazan buildings,
   pictures for the owner; the licensing log row already exists. `tools/asset-manifest.json`,
   `tools/import-assets.ps1` and `tools/art/**` are the view's. No scene pins an AI match's outcome.
2. The view's half of Request 58 once the sim exposes it (the Refused sound + the `placement` text over the unit).
3. The Destrier Rider and the War-dog once a horse / dog pack is on disk (owner); Teblor buildings; the javelin prop.
4. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Where we are
D-R39 on `main`; stopped two sessions (rule 41). Counter **1 / 4**. The sim's M6-S12 re-printed cells on all three
pages (both Age II tables, the holy war's first wave, the house notes, the spending sentences, the Teblor opponent
sentence), each old figure kept as history. Open: BUG-1420 S3, BUG-1524 items 3-4, BUG-1553 S3 (new), BUG-1421 S4
(items 1, 2, 5), BUG-1510 S4. BUG-1522 is closed by measurement (Hard 34 / 7 / 1).

### Next task candidates
1. **D33, next session (QA standard; one hash move; the sim's M8-2 does not touch the AI):** first **BUG-1524
   item 3** (re-measure malazan.md's BUG-0982 threshold table on `main`, rewrite its prose, un-skip
   `DR35ProsePinQaTests.Malazan_Bug0982Prose_*`) with the two `Bug0982_*` Stress rows (settle 14 v 18 pop, or
   re-state the rule), item 4 (a stale comment in `AiOpeningPairsReportTests`); then BUG-1420 (Breakers, Riders,
   War-dogs in the Teblor army), `holy_war`'s 20-pop wave (**BUG-1553**: pair (0, 2) seed 3 stands at 20:00 without
   a wave, peak 16 pop; take it out of `AliveWithoutAWave` if a number cures it), `KnownBeatenAlive` seed 19 with
   its `DR33QaTests` pin, BUG-1510 item 1, BUG-1421 items 1, 2, 5. Print the Hard v Normal tally, tune nothing for
   it. The golden's header and the two sim-owned data-hash pins handed over by name (rule 51); the Stress pin
   classes run (rule 51). The owner's review table (old -> new, why) in the report. If the budget is short, the
   Malazan table and BUG-1420 first, the rest in D34.
2. The Shadow roster as Request 56's schemas reach `main`; the "Orders full" text once Request 58's key exists.
