# Handoff: brief for the next session

**PLAN of session 2026-11-15-2136 (session 3 / 8, started 21:36): sim GO M6-SH2 (hardening, QA full) · view GO
M6-V23 (feature, QA standard) · data STOP (cheap; rule 41).** The briefs are under each track's "Current session
plan" below. Everything else in this file is the 1536 ACCEPT's handoff, unchanged.

Written by the Producer at the ACCEPT of session **2026-11-15-1536** (session 2 / 8 of 2026-11-15, started 15:36; the
ACCEPT ran from about 20:00). Sim **M6-S11** ACCEPT (feature, 0 fix rounds, QA full PASS_WITH_ISSUES), view
**M6-VH8** ACCEPT (hardening, 0, QA standard PASS_WITH_ISSUES), data **D-R39** ACCEPT (feature, 0, QA light
PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters: **sim 4 / 4 (its
next session is a hardening), view 0 / 4, data 1 / 4**. Bug ids next session: **sim 1520+, view 1530+, data 1540+**.
Open S1 / S2: none. The next session is **3 / 8 of 2026-11-15** (or 1 / 8 of 2026-11-16 if it starts after midnight).

**Next session in one line (proposal):** sim **the hardening (M6-SH2): the Stress rows red by ruling first (two
sessions red: rule 57 makes them the task), with BUG-1465 / 1460 / 1463 behind most of them; one pin move** (QA
full) · view **M6-V23: the first Shift-placed building queues behind a gatherer's trip, BUG-1500 / 1501, the
Casting clip** (feature, QA standard) · data **a cheap STOP** (the sim's hardening changes the AI under every
opening measurement: rule 41), unless an owner answer is in the inbox; **D33** the session after.

**The default loop on `main` is green.** Expect **0 failed** (9,665 passed / 0 failed / 19 skipped of 9,684 in 21 m 49 s (beside the scene loop) on the Producer's scratch clone of the
three-way tree). Any red row is a finding.

**At the PLAN, first:** (1) the inbox; (2) the default loop on `main` is green; (3) **rule 68 for the sim's
hardening:** rehearse nothing new, but sort the rows its AI changes will move into "re-pin" (the match pins, rules
43 / 45), "the pages' cells and the prose pins" (hand `docs/factions/**` cells, the sentences
`QA/Content/DR35ProsePinQaTests` reads, `Content/AiOpeningPairsReportTests`' Age II cells and its `DefeatedBeforeDue`
entry, `Content/HolyWarFirstWaveReportTests`' table and `QA/Content/TeblorOpeningQaTests`' opponent sentence to the
sim developer **by name**, since the data track is stopped that session) and "another track's"; (4) if the data
track runs anyway (an owner answer), it must not touch a faction page the sim re-prints.

## Where things stand

- **`main` after this session = sim e4bdaaa + view 81d0325 + data 49d650f** (plus this ACCEPT's studio and docs
  files). The Producer's scratch clone of that tree (`acc1536` in the session scratchpad; conflicts only in
  `studio/bugs/README.md` and `studio/qa/coverage.md`, both sides kept): build `--no-incremental` **0 errors / 0
  warnings**; the default loop **9,665 passed / 0 failed / 19 skipped of 9,684 in 21 m 49 s (beside the scene loop)**; smoke **PASS**; the scene loop **111 of 111, first pass**.
- **No data byte moved.** Data hash **B74DC7AE61B4862D**; formats unchanged (save 10, replay 9 / 6); the golden
  unchanged. Saves and recordings from the morning of 2026-11-15 on still load.
- **Request 57 is built (M6-S11):** `EconomySystem.Step` asks `EndsForQueue` for a unit on a loop with
  `QueueCount > 0`: the loop ends at once when nothing is carried and the worker is not in reach of its node, or no
  own drop-off stands (cargo kept); otherwise the trip runs and `Deposit` ends the loop; phase 7 starts the queue the
  same tick. No saved state. Waits measured: Laborer 14.9-17.1 s at most, Gatherer 21.0-23.9 s; 200 on one mine mean
  7.0 s, longest 22.8 s. **BUG-1490 (S4): a popped Gather with more orders behind it is skipped out of reach and
  makes one trip in reach. Ruling (docs/01 2026-11-15 (c) item 2): exactly one trip, by a saved per-unit flag, built
  by the first sim task that changes the save format anyway** (M8-2 is the likely one); docs/02 says what happens today.
- **The Hard-counter row** (`CounterCompositionTests.AHardSeat_*`) runs `RunWide` (two barracks, two crossbow
  ranges, twice the purse): Normal 11 / 6 seen v 10 / 8 unseen (0.092), Hard 12 / 5 (0.150); strength-0 controls 0
  (`CounterRestageQaTests`). One unit on one seed: honest, thin. BUG-1156 has the figures.
- **Stress rows red on `main` by ruling (BUG-1462), confirmed exactly by M6-S11's QA on a47318f (16 rows), now two
  sessions red:** `MapSlice2StressTests` Raraku 3 / 4 / 6 (BUG-1460), six `KeepCornerCureQaStressTests` rows
  (BUG-1463: HardShapes cliff / depot / edge, TwoByTwoDepot Gatherer 12, FuzzSeeds57To120, KeepOnTheMapEdge),
  `FarExpandProbeQaStressTests` Vathar 2 with four seats, `StarvedSaveTwoProcessQaStressTests.PaleHillsSeed1_*`,
  `StartingResourcesAiStressTests` openings 1 and 2, `ScoutCapQaTests.OneScoutKilled(seed: 5)`: **the sim's
  hardening**; and `Bug0982_CadreRushVersusRaiderRush_*` + `DH6ThresholdQaTests.Bug0982_*` (the rule picks 14,
  `cadre_rush` ships 18): **the data's D33**.
- **The view (M6-VH8):** a queued Stop / Hold / no-target cast is a torus in its own multimesh (`OrderPathView`
  `RingsNode`, priorities line 1 / ring 2 / marker 3); markers grow above 30 m zoom, lines above 20 m;
  `UnitViews.OverlayLines` = (`UnitModels.TopBone` + 0.18 m) x scale for a model type, the capsule's top otherwise
  (`CombatViews` / `AbilityViews` read it; a giant's line 2.94 m); `SelectionController.OrderBuild` refuses a queued
  Build onto a full queue (`RefusedBuilds`, the Refused sound); `AnimationRate` 0 at a replay's end. **111 scenes.**
  `ViewApi/OrderPath.QueuesBehind` is unchanged: the first Shift-placed building still replaces a gather loop.
  On the sim's branch QA saw `OrderQueueViewTest`'s `BehindAGatherLoop` row print "still queued: False"; the scene
  passes on the merged tree.
- **The data (D-R39):** `AiOpeningPairsReportTests.DefeatedBeforeDue = {(0, 2, 3, player 1)}` (reported, not failed;
  fails if that side ever waves), `HallLost` checked last in `WhyWaiting`, `WaveDue` (earliest possible tick);
  `Content/HolyWarFirstWaveReportTests` pins whirlwind.md's new table; `DR35ProsePinQaTests` re-targeted;
  `TeblorOpeningQaTests.TheOpponentSentence_QuotesTheReportRows`; the fixture is `save_teblor_frenzy.b64`.
  malazan.md has "Questions for the Producer (D-R39)": answered in docs/01 2026-11-15 (c) item 6 (18 stays until
  D33; the table re-print accepted; `KnownBeatenAlive` seed 19 out in the next data task).
- **docs/02 "Queued orders (Shift)"** was edited by the Producer at the ACCEPT (the full-queue refusal, the
  mid-queue gather as decided and as it is today, nested rings, zoom, replays). The "Buildings" sentence is still
  M6-V22's (replaces a gather loop): M6-V23 rewrites it.
- **Requests 58 / 59 (new, for the sim):** a per-tick list of queued orders dropped at their turn behind a read-only
  ViewApi accessor (BUG-1471 item 3; with it a `ui.json` text for a full queue); `tools/qa/smoke.ps1` stops on LFS
  pointer files (BUG-1440).
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's list).


## Standing rules (1-69)


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



## Watch-outs (all tracks)
- **The default loop is green on `main` again** (the five rows ruled red at 0506 are fixed). A "default loop with
  Stress" run still expects BUG-1462's Stress rows red (the list under "Where things stand"), until the sim's hardening
  lands. `main` builds **0 errors / 0 warnings**.
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
- **The view's scene loop is 111 scenes** (`OverlayLineTest`, QA's `QaM6VH8Test` are new). `BenchTest` (7 s),
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

- **Pinned matches on `main` after M6-S10 (every one re-measured once, old values in comments; docs/03
  "Implementation (M6-S10)" lists old → new):** the Hard v Normal tally **36 / 3 / 3** (Raraku 14 / 0 / 0, Pale Hills
  12 / 2 / 0, Vathar 10 / 1 / 3); `MatchCriterionTests` (Vathar seed 7 23,412 / Raraku seed 2 27,936 / Pale Hills seed 1
  42,384 / generated seed 2 21,822 / FFA 52,098 / 2v2 57,154; `HalfWay` 11,706); `StandardPresetPinQaTests`,
  `NonHardMatchPinQaTests`, the eight `CounterCompositionTests.TheNonHardPins_*`, `ReserveTwinTests`,
  `SharedVisionFfaPinTests`, `HardKnobsPinTests`, `OutcomePinM73bQaTests` (its fingerprints moved: a behaviour
  change, said in its comment), the premap AI replay, `seed21_playable.replay` (re-recorded; `Seed21ResearchTests`
  re-stated), `CliTests.Match_TheLiteralRow_*`. The next sim task that changes Normal AI behaviour or gather
  movement re-measures all of them again (rules 43 / 45): batch such changes into the hardening.
- **Known stalls / skips with ids on `main`:** **Raraku Normal v Normal seeds 1, 3, 4, 6 (BUG-1460; `MatchCriterionSweepQaStressTests.Known`)**, Pale Hills FFA seed 1 (BUG-1068 / 1116), `FarExpandProbeQaStressTests` Pale Hills FFA
  seed 2 (BUG-1151), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (BUG-1156), BUG-1161's
  three rows, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the view's
  `QaM7V1Test` zoom-40 1.5 head row; `DebugOverlayTest`'s 1 ms frame row flakes under three-track load (passes on a rerun).
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.



---

## Sim track

### Current session plan (2026-11-15-2136, session 3 / 8): M6-SH2, hardening, QA full

**M6-SH2: the Stress rows red by ruling, by cause (the AI that never retrains or rebuilds, Raraku's wood, the
give-up loop), one pin move.** Bug ids 1520+. About 800 changed production lines (rule 39: pins, re-printed cells
and QA's suites do not count).

Goal: sixteen Stress rows have been red on `main` by ruling for two sessions (BUG-1462; rule 57 makes them the whole
task). Most come from three causes: a beaten AI seat never retrains a worker or rebuilds a hall (BUG-1465), a winning
AI on Raraku runs out of wood in reach and neither fetches far wood nor attacks (BUG-1460), and workers are held by
a give-up loop against standing own units (BUG-1463 / 1461). Fix the causes, leave every row green or honestly
re-stated, re-measure the match pins once.

Scope, in this order:
- **A. BUG-1465.** Trace first (`Stress/RarakuStallProbeQaStressTests`, Raraku seed 1, seat 0 from tick 30,000), then
  fix generically: (a) a seat with a finished worker-training building, no worker, free population and the gold for
  one trains one, also while it defends and after its opening's worker steps are done; (b) a seat with at least one
  worker, no town hall standing or under construction and the hall's cost fillable rebuilds one
  (`AiOpeningPairsReportTests` pair (2, 0) seed 13: 4 workers, 1,072 gold, 257 wood, never rebuilds).
- **B. BUG-1460.** Re-run Raraku seeds 1-8 after A. If a winner still cannot finish (seed 1: 17 workers idle from
  36,000, wood 14, the nearest wood 41.8 m from its drop-offs, 2 soldiers, no hunt), trace why, then build **the wood
  rule (Producer decision, owner may revisit), in this order of preference, existing knobs only:** (1) a seat that for
  `ai.starvedWaveAfter` seconds can neither train any unit of its army mix nor pay its due step, because a resource
  it lacks has no known node inside its gather reach, counts as starved: the existing starved wave and hunt, with
  `starvedMinArmy`; (2) only if (1) does not end the rows: its idle workers go for the nearest known node of the
  missing resource up to `ai.farExpandReach` (the far expansion's path, a Camp by it when it can pay). If the trace
  shows another cause (a hunt that never starts with an idle army), fix that and say so.
- **C. BUG-1463 / 1461 + BUG-1464 item 1.** Trace the give-up loop (a walker on a gather loop gives up after 11-19
  stuck ticks against standing own units, `Wait` re-walks into the same spot; why `PushAfterStuckTicks` does not free
  it), fix it generically (any worker, any faction; armies' separation unchanged). `Movement/HeldWorkerProbe` checks
  on every tick past the clock with the Producer's definition of "held" (BUG-1464, the answer to item 1). If the
  cause is not found by a bounded trace, report the traces: no speculative movement change (it moves every pin).
- **D. The other red rows, re-run after A-C:** `FarExpandProbeQaStressTests` (vathar_crossing, 2, 4 seats),
  `StarvedSaveTwoProcessQaStressTests.PaleHillsSeed1_*`, `StartingResourcesAiStressTests.EachOpening_AtHigh_*`
  openings 1 / 2, `QA/ScoutCapQaTests.OneScoutKilled_*(seed: 5)`. Each: green as it is, fixed by cause (bug id, a test
  that failed first), or re-staged (old → new, the reason in a comment).
- **E. Every match pin re-measured once, after the last behaviour change** (list under criterion 8).
- **F. If the budget allows:** BUG-1434 (run `GatherOverlapPaleHillsProbeQaStressTests`, report the longest wait),
  Request 59 (`tools/qa/smoke.ps1` stops on LFS pointer files, BUG-1440).
- **Handed to you by name this session (the data track is stopped; rules 40 / 68):** the re-printed cells on
  `docs/factions/**` and the sentences that quote them; `sim/Rts.Sim.Tests/Content/AiOpeningPairsReportTests` (its
  Age II cells and `DefeatedBeforeDue`), `Content/HolyWarFirstWaveReportTests` (whirlwind.md's table),
  `QA/Content/DR35ProsePinQaTests` (the sentences it reads), `QA/Content/TeblorOpeningQaTests` (the opponent
  sentence), `QA/Content/D21OpeningsQaTests` (cells). Re-print numbers and the sentence that quotes them; remove no
  assertion; list old → new. A row whose meaning changes (not only its numbers) is listed "for ruling", not edited.
- **OUT:** any byte under `game/data/**` (no data-hash move: the view's fixtures would refuse); any save or replay
  format change or new saved field (BUG-1490 stays open); a new knob (if a number is unavoidable, stop that part and
  write the proposed knob under "Questions for the Producer" in docs/03's section; never a C# literal); M8-2;
  Request 58; `Bug0982_*` x2 (D33's: they stay red); `game/**`; `sim/Rts.Sim/ViewApi/**`,
  `sim/Rts.Sim.Tests/ViewApi/**` and `sim/Rts.Sim.Tests/QA/OrderPathQaTests.cs` (the view's this session); any Perf
  threshold.
- **Cut order if the budget runs out:** F, then D's re-stagings (a diagnosis line per row instead); never E.

Acceptance criteria:
1. A scene (failing on the base): an AI seat with a finished worker-trainer, 0 workers, free population and gold for
   a worker has one in training within 200 ticks, with enemy soldiers razing its buildings and with its opening's
   worker steps done. With no gold for one, nothing is sent.
2. A scene (failing on the base): a seat with a worker, no hall and the hall's cost fillable places a hall within
   600 ticks where a site exists; pair (2, 0) seed 13 on the shipped data rebuilds, or the report line says why not.
3. Raraku Normal v Normal seeds 1-8, every check on, 72,000 ticks: a table old → new (tick, winner). Target: all
   end, `MapSlice2StressTests` Raraku 3 / 4 / 6 green, `MatchCriterionSweepQaStressTests.Known` loses Raraku 1, 3, 4, 6.
   A seed that still does not end stays in `Known` with its id and a probe (a line per seat every 6,000 ticks) and is
   listed for ruling; no bound is raised.
4. The wood rule as built has scenes that failed first (a seat with gold, no wood in reach and a small army attacks
   or fetches; a seat with wood in reach is unchanged), reads only what the seat has seen (`PlayerView`), uses only
   existing knobs, and is in docs/02's AI section.
5. The six `KeepCornerCureQaStressTests` rows red today are green with no assertion edited; a BUG-1461 scene (a
   worker's way back crossed by six own Idle soldiers) is not held; the fuzz gate (seeds 2-56) stays green at 0 and
   0.5. Measure and report: seeds 57-120 at 0.5 (seed 72), BUG-1463's table re-printed (0 / 0.5, base / now; a cell
   worse than the base is a finding), `Stress/WideCrowdStressTests` both columns old → new. The M1 movement and
   arrival tests pass unedited.
6. Each row of D has one line: green as is / fixed (id, test) / re-staged (old → new, why) / for ruling (diagnosis).
7. Determinism: twins, a save mid-match continued, and a replay for one match on each changed path (retrain,
   rebuild, the wood rule, a held shape). Data hash **B74DC7AE61B4862D** and the formats (save 10, replay 9 / 6)
   unchanged; no `game/data/**` byte in the diff. Say whether the golden moves; if it does, regenerate it in the same
   commit with the reason in the `GatherWedgeQaTests` comment.
8. Pins, once, old values in comments, docs/03's M5-5 table and "Implementation (M6-SH2)" old → new (rules 43 / 45):
   the Hard v Normal tally (36 / 3 / 3; measure and report, a map Hard no longer leads is a finding),
   `MatchCriterionTests` (six rows + `HalfWay`), `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, the eight
   `CounterCompositionTests.TheNonHardPins_*`, `ReserveTwinTests`, `SharedVisionFfaPinTests`, `HardKnobsPinTests`,
   `OutcomePinM73bQaTests`, the premap AI replay, `seed21_playable.replay` (`Seed21ResearchTests`),
   `CliTests.Match_TheLiteralRow_*`. A criterion row that stops ending moves to the smallest ending seed and is
   listed as a finding with an id, never re-bound. `AHardSeat_ShiftsItsLineShareFurther_*` stays green unedited, or
   is listed for ruling.
9. New per-tick code allocates 0 bytes (a test); Perf run alone (rule 42) green with no threshold in the diff.
10. The handed-over cells, sentences and content pins are re-printed as listed in scope; old → new in the report.
11. `dotnet build RtsGame.sln --no-incremental` 0 errors / 0 warnings after the last edit; the default loop
    (`Category!=Perf&Category!=Stress&Category!=Soak`) 0 failed, or red only in rows the report names "for ruling"
    under criterion 10; Stress red only in `Bug0982_*` x2 and rows named for ruling under 3 / 5 / 6; smoke PASS; the
    scene loop run once at the end, a red scene listed as "the view's", not fixed.
12. docs/02 (the AI retrains, rebuilds, the wood rule), docs/03 "Implementation (M6-SH2)" (causes, old → new tables,
    "Questions for the Producer"), bug-file updates as a patch in the scratchpad.

Design references: docs/02 "AI" (the starved charge, the far expansion, the gather reach, the defense), docs/03
"Implementation (M6-S10)", "(M5-9a)", "(M5-9b)", "(M5-4d)", "(M6-S7)" (the gather reach), "(M5-5)" (the pin table);
`rules.json` `ai` block (`starvedWaveAfter` 60, `starvedMinArmy` 1, `farExpandReach` 384, `gatherReach` 64, `expandHallReach` 40);
BUG-1460 / 1461 / 1462 / 1463 / 1464 / 1465 / 1510 item 2.

Tests required: the failing-first scenes of criteria 1, 2, 4, 5; determinism rows (7); an allocation row (9); the
re-measured pins (8); the Stress rows green or re-stated.

Constraints: the AI acts only through commands and its fog-filtered view; no number in C# (existing knobs); no
allocation in per-tick code; no `Dictionary` / `HashSet` order in results; `SimMath` only. Rules 52 / 69: record the
PID of every `dotnet test` / Godot you start and end only those. Rule 62: the full default loop after the last edit.
Rule 63: scratch folders named `m6sh2-...`. Rule 64. You cannot write `studio/**`.

**QA focus (full):** (1) the retrain rule under a camping enemy: does a seat feed workers to two soldiers at its door
for ever (count workers trained and lost over 20 minutes; compare gold spent), and does a retrained worker actually
gather; zero workers with a full queue, at the population cap, with the trainer under construction or destroyed
mid-training. (2) The rebuild: no site, an enemy on the old site, two halls lost in one tick, gold arriving a little
at a time. (3) The wood rule: a seat that has wood in reach must behave exactly as on the base (twin a base match
where it never triggers); a seat that never scouted any far wood; FFA and 2v2; Easy and Hard. (4) Raraku seeds 1-16
and Pale Hills / Vathar seeds 1-8, every check on: any match that stops ending. (5) Held workers: BUG-1463's five
shapes x 8 shuffles at 0, 0.5 and 0.75, Laborers and Gatherers, fuzz seeds 57-200; 200 workers on one mine; armies
(no gather loop) must separate exactly as before (twin a no-worker battle against the base). (6) Determinism: twins,
save on every 500th tick of a Raraku match, replay. (7) Every pin edit against a fresh run; no assertion weakened in
the handed-over content pins. Default loop with Stress, then Perf alone (rule 42). Rule 65.

### Where we are
M6-S11 on `main`: the Hard-counter row honest, Request 57 built. Counter **4 / 4: the next session is the sim's
hardening.** New: **BUG-1490** S4 (ruling: one trip, with the next save-format change). Open and first: BUG-1462's
Stress rows (two sessions red), **BUG-1460** S3 (Raraku seeds 1, 3, 4, 6 do not end), **BUG-1465** S3 (a seat with a
Keep and gold never retrains a worker; and, from D-R39, a seat with workers and gold never rebuilds its hall: opening
pair (2, 0) seed 13), **BUG-1463** S3 (held workers at 0.5 on some shapes), **BUG-1461** S3, BUG-1464 S4 item 1.
Older debt: BUG-1434 (re-measure the 267 s wait), BUG-1433, BUG-1161's three alias names, BUG-1149 / 1151 / 1152,
BUG-1150, BUG-1010; notes: `[0]` lookups left in `FrenzyRulesLoaderTests`, `StatusDurationTechTests`,
`DataContentHashTests`, QA's `M7H1QaTests`; the JSON-`null` policy.

### Next task candidates
1. **M6-SH2, the sim's hardening (QA full; one pin move; rules 43 / 45 / 57 / 68):** in this order:
   - **The Stress rows red by ruling, each green or re-stated with its id:** Raraku 3 / 4 / 6 with **BUG-1465**
     (a seat with a production building, gold for a worker and no worker trains one; a seat with workers, gold and
     no hall rebuilds one) and **BUG-1460** (Raraku ends again on seeds 1, 3, 4, 6, or the row says why not: wood on
     Raraku is finite, so an AI with gold and no wood needs a rule: attack with what it has / gather the far wood);
     the six `KeepCornerCureQaStressTests` rows with **BUG-1463** (the give-up loop at 0.5: `QueueArrival` against
     `ArrivalDistance`, the 2 x 2 depot with Gatherers first); `FarExpandProbe` Vathar 2; `StarvedSaveTwoProcess`
     Pale Hills 1; `StartingResourcesAi` 1 / 2; `ScoutCap` seed 5.
   - Then as far as the budget allows: BUG-1461, BUG-1464 item 1, BUG-1434's re-measure, Request 59 (a few lines).
   - Every match pin re-measured once more (the list is in Watch-outs), old values in comments; a row that stops
     ending is a finding, not a re-bound. The faction pages' cells **and** the prose and report pins that read them
     are handed over by name (see "At the PLAN"), so the default loop stays green: no ruling this time.
   - OUT: any data hash move (`game/data/**`), BUG-1490 (needs a save flag), M8-2, Request 58, `Bug0982_*` (D33's).
   - If this is too much for one session, the Stress rows and their causes are the task; the rest waits.
2. **M8-2: the Shadow Archer's stand-still stealth** (Request 56 G1; then G2), schema first, test data only (QA
   full). If it changes the save format, BUG-1490's flag ("this loop was started from the queue": one trip, then the
   next order) rides with it.
3. **Request 58** (dropped queued orders for the view; small).
4. **Requests 54 / 55** (each changes Normal AI behaviour: with a pin move), **Request 53 part two**, **56 G3-G8**.

---

## View track

### Current session plan (2026-11-15-2136, session 3 / 8): M6-V23, feature, QA standard

**M6-V23: the first Shift-placed building waits for a gatherer's trip; ring levels per point; the same-tick
full-queue check; the cast gesture.** Bug ids 1530+. Up to about 700 changed lines.

Goal: the last part of the owner's Shift-queue request (inbox 2026-11-14). Since M6-S11 the sim lets a gathering
worker with queued orders finish the trip in hand and then start its queue; the view still makes the first
Shift-placed building interrupt a gatherer at once. Make it queue, and close three small leftovers.

Scope:
- **The first Shift-placed building queues behind a gather loop.** `sim/Rts.Sim/ViewApi/OrderPath.QueuesBehind`
  returns true for a worker on a gather loop (its summary rewritten); an idle worker still starts at once.
  `CommandCard.GhostClick` / `SelectionController.OrderBuild` comments and behaviour follow. What the player gets is
  the sim's rule: walking out empty-handed the worker starts the building at once; working or loaded it delivers
  first; with no drop-off standing it starts at once and keeps its load.
- **Handed to you by name (the sim track does not touch them this session):**
  `sim/Rts.Sim.Tests/ViewApi/OrderPathTests.cs` (`QueuesBehind_*`: the two "gather loop: replace" rows become
  "queue") and `sim/Rts.Sim.Tests/QA/OrderPathQaTests.cs` (the rows asserting `QueuesBehind` false on a loop, lines
  about 60 and 216). Re-state them old → new; remove no other assertion.
- **BUG-1500:** item 1, ring levels counted per point across the selection's units, so one unit's Stop and
  another's Hold at one point nest; items 2-3, the `MarkerFullZoom` comment (2 m wide) and docs/03's 0.16 m →
  0.21 m (man) / 0.32 m (giant).
- **BUG-1501:** the full-queue Build check also counts the selection's own queued commands sent since the last
  tick, so two Shift placements inside one tick onto a queue of 7 send one and refuse one.
- **BUG-1441 item 4, the cast gesture:** a Casting unit plays `attack_cast` when its model's library has that
  clip, `idle` otherwise. You may re-pin `QaM6VH7Test` row 1, `QaM6V20Test` row 9 and `TeblorModelTest` for the
  extra clip, old → new listed. No re-import: if a model lacks the clip in its `.glb`, it keeps `idle` and the
  report says which.
- docs/02's "Buildings" sentence under "Queued orders (Shift)" rewritten (queued behind a gatherer's trip); docs/03
  "Implementation (M6-V23)".
- **OUT:** merged surfaces; any re-import or change under `tools/**` or `game/assets/**`; Request 58 (no text over
  the unit, no dropped-order feedback); the javelin prop; `game/data/**`; any `sim/**` file outside
  `sim/Rts.Sim/ViewApi/**` and the two test files named above (plus `sim/Rts.Sim.Tests/ViewApi/**`); any sim rule
  (BUG-1490 is the sim's). The sim track changes gather movement and the AI this session: a new scene must not pin
  an AI match's outcome or a gatherer's exact position.

Acceptance criteria:
1. `OrderQueueViewTest`'s `BehindAGatherLoop` is a pass row on this branch: a loaded worker on a loop, one Shift
   placement of a house: the Build is sent queued, the worker delivers its full load (the bank rises by it), then
   walks to the site and builds; nothing carried is lost.
2. Scene rows: a worker walking out empty-handed and out of reach of its node starts the building in the tick the
   command applies; an idle worker starts at once; a second Shift placement is queued behind the first; a plain
   (no Shift) placement still interrupts a gatherer at once; with no drop-off standing the worker starts at once
   with its cargo kept.
3. `OrderPathTests` and `QA/OrderPathQaTests` green with the re-stated rows listed old → new;
   `OrderPathAllocationTests` still 0 bytes; the read-only proof (the twin) green;
   `TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls` green.
4. BUG-1500 item 1: two units, a queued Stop and a queued Hold at one shared point: two rings at different radii,
   both colours visible on a picture you looked at (`QaM6VH8Test` row R re-stated, old → new); one unit's nesting
   and the three-ring cap unchanged.
5. BUG-1501: `QaM6VH8Test` row F's last line reads one sent, one refused (the Refused sound once); a Shift-queued
   Move and a Build inside one tick onto 7 also refuse the Build; after a tick the count resets.
6. A Casting Teblor Shaman plays `attack_cast` (a scene row reads the playing clip; a picture mid-cast); a unit
   type whose library lacks the clip plays `idle` with no warning or `ERROR`; capsule types unchanged; pins edited
   listed old → new.
7. Measure and report (not pass / fail): the 400-figure bench alone, last (rule 42), with the capsule figure from
   the same window.
8. The scene loop all green (111 + new scenes; timing rows that flake under load re-run alone and named);
   `dotnet build RtsGame.sln --no-incremental` 0 errors / 0 warnings; the default loop 0 failed; smoke PASS.
9. docs/02 and docs/03 as in scope; bug-file updates as a patch in the scratchpad.

Design references: docs/02 "Queued orders (Shift)" (the sim's rule for a queue behind a gathering worker; the
"Buildings" sentence you rewrite); docs/03 "Implementation (M6-S11)", "(M6-V22)", "(M6-VH8)"; docs/01 change log
2026-11-15 (c); BUG-1500, BUG-1501, BUG-1441 item 4.

Tests required: the scene rows of criteria 1, 2, 4, 5, 6; the re-stated `OrderPathTests` / `OrderPathQaTests`
rows.

Constraints: ViewApi stays read-only (no sim state, tick or hash change; its source may not name `World` or
`UnitStore`); views hold no gameplay state (the same-tick count is input bookkeeping, reset each tick); no
player-facing text in C#; a scene that parks a worker on a loop with a queued order puts it in reach of its node
with a drop-off standing, or the queue starts at once. Rules 52 / 69 (record PIDs, end only your own), 62, 63
(`m6v23-...`), 64. You cannot write `studio/**`.

**QA focus (standard):** the queued Build across the trip: a worker at every phase of its loop (walking out in and
out of reach, working, returning, depositing), gold and wood, Laborer and Teblor Gatherer, 30 workers selected
with mixed phases, the node running dry or the drop-off razed during the wait, the building's cost no longer
payable when the worker arrives, the ghost cancelled; Shift held for five placements. Rings: three units with
Stop / Hold / a no-target cast at one point, deselecting one. The same-tick check at Fast speed and while paused
(placements made during a pause must not leak the count). The cast clip at 0.5x and 2x speed and at a replay's end.
Run the default loop, the Stress rows that touch `OrderPath`, the scene loop; timing rows and the bench alone and
last. Rule 65.

### Where we are
M6-VH8 (the hardening) on `main`. Counter **0 / 4.** 111 scenes. Fixed: BUG-1470. Part-fixed: BUG-1471 (item 3 is
Request 58), BUG-1441 (item 2), BUG-1440 / 1411 / 1381 (docs). New: BUG-1500 S4, BUG-1501 S4. Answers to the
developer's questions (docs/01 2026-11-15 (c)): the overlay line keeps counting every clip a figure holds; "Orders
full" words are a later data row; the ring colours stay.

### Next task candidates
1. **M6-V23 (feature, QA standard; the owner's Shift-queue request, last part):**
   - The first Shift-placed building for a worker on a gather loop is **queued** (the sim then lets the worker
     finish its trip: empty-handed walking out it starts at once, loaded it delivers first); an idle worker still
     starts at once. `ViewApi/OrderPath.QueuesBehind` + its rows in `sim/Rts.Sim.Tests/ViewApi/OrderPathTests`
     (name that file in the brief: the sim track does not touch it that session), `CommandCard.GhostClick` /
     `SelectionController.OrderBuild`, docs/02's "Buildings" sentence, `OrderQueueViewTest`'s `BehindAGatherLoop`
     as a pass row.
   - BUG-1500 item 1 (ring levels counted per point across units), items 2-3 (a comment, docs/03's 0.16 m →
     0.21 / 0.32 m); BUG-1501 (count the selection's own queued commands not yet applied).
   - The Casting clip (BUG-1441 item 4): a Casting unit plays `attack_cast` when its library has one; the brief
     gives leave to re-pin `QaM6VH7Test` row 1 or `QaM6V20Test` row 9 / `TeblorModelTest`, old → new listed.
   - OUT: merged surfaces, re-imports, anything needing Request 58.
2. **Merged surfaces** (rule 61): one material and one atlas a model; re-measure the 400- and 1,000-figure bench
   **alone** (this session's 11.0-13.1 ms / 31-34 ms were under other tracks' load; M7-V5: 11.00 / 29.22).
3. The view's half of Request 58 once the sim exposes it (the Refused sound + the `placement` text over the unit).
4. The Destrier Rider and the War-dog once a horse / dog pack is on disk (owner); Teblor buildings; the javelin
   prop (a Blender re-import with a byte-identical check of the other models).
5. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Current session plan (2026-11-15-2136, session 3 / 8): STOP (cheap)

No owner answer in the inbox, and the sim's hardening (M6-SH2) changes the AI and gather movement under every
opening measurement D33 rests on (rule 41) and re-prints cells on all three faction pages. D33 is the data's task
the session after M6-SH2 lands. Counter stays 1 / 4.

### Where we are
D-R39 on `main`: no data byte moved. Counter **1 / 4**. Fixed: BUG-1480; BUG-1421 items 3-4; the four content rows.
New: BUG-1510 S4. Open: BUG-1420 S3, BUG-1421 S4 (items 1, 2, 5). **The economy and the AI change once more in the
sim's hardening: D33 comes after it, not beside it.**

### Next task candidates
1. **Next session: STOP (cheap), unless the inbox holds an owner answer** ("Shadow names ok", "gather overlap ...",
   "teblor ai: early raid", "teblor buildings: 1.5", "opening text: ...", "holy war: ...", the counters,
   `goldShare`, BUG-0780, `counterStrength`). An owner tweak that moves a data number is planned only if the sim
   moves no hash that session (it does not) and the brief keeps it off the pages the sim re-prints.
2. **D33 (the session after the sim's hardening; QA standard; one hash move):** its first item is the two
   `Bug0982_*` Stress rows red by ruling (settle 14 v 18 army pop for `cadre_rush`: at 14 seed 8 dies at 19:28
   where at 18 it stands; seeds 14, 18, 23 die without a wave at any threshold; or re-state the rule, which no
   longer answers BUG-0982's question). Then BUG-1420 (Breakers, Destrier Riders and War-dogs back in the Teblor
   army), `holy_war`'s 20-pop first wave against Malazan first waves at 5:42-6:08 (whirlwind.md's table),
   `D21OpeningsQaTests.KnownBeatenAlive` seed 19 out with its `DR33QaTests` pin, BUG-1510 item 1, BUG-1421 items
   1, 2, 5. Measure Easy, Normal and Hard and the mirror; count distinct matches. The golden's header and the two
   sim-owned data-hash pins handed over by name (rule 51).
3. The Shadow roster, file by file, as Request 56's schemas reach `main`; the "Orders full" text once Request 58's
   `ui.json` key exists.
