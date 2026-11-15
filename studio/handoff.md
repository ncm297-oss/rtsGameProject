# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-15-0506** (session 1 / 8 of 2026-11-15, started 05:06; the
ACCEPT ran from 14:15, about 9 hours in). Sim **M6-S10** ACCEPT **by ruling** (feature, 1 fix round of 2, QA full FAIL
on criterion 14 only: five default-loop rows red, ruled under rule 57), view **M6-V22** ACCEPT (0, QA standard
PASS_WITH_ISSUES), data **D-H9** ACCEPT (hardening, 0, QA light PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6
owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters: **sim 3 / 4, view 4 / 4 (its next session is a hardening),
data 0 / 4**. Bug ids next session: **sim 1490+, view 1500+, data 1510+**. Open S1 / S2: none. The next session is
**2 / 8 of 2026-11-15**.

**Next session in one line (proposal):** sim **M6-S11: the red `AHardSeat_*` row, then Request 57 (a worker with
queued orders finishes its trip and starts the queue)** (feature, QA full; must move no AI match) · view **M6-VH8,
the hardening** (BUG-1470 first, then bars above the giants, marker size by zoom; QA standard) · data **D-R39: the
four red rows first** (the prose pins and the two opening-pair rows), then the Teblor sentences (QA light, or
standard if an `ai.json` number moves).

**`main` is red in exactly five default-loop rows, by ruling (BUG-1462, rule 57).** Every default loop next session
expects **5 failed**, these and no other:
1. `CounterCompositionTests.AHardSeat_ShiftsItsLineShareFurther_ThanANormalSeat` (the sim's first item)
2. `Content/AiOpeningPairsReportTests.NormalAiVersusAi_EveryOpeningPair_BothWave_NoDeadEndStep(malazan: 0, whirlwind: 2)`
3. the same row `(malazan: 2, whirlwind: 0)`
4. `QA/Content/DR35ProsePinQaTests.AgeTwoProse_MatchesTheAgeTwoTables`
5. `QA/Content/DR35ProsePinQaTests.Malazan_Bug0982Prose_MatchesTheThresholdTable` (2-5: the data's first item)

**A row still red after next session becomes that track's whole task.** Write each brief's "default loop" criterion
as "red only in the rows of this list that the task does not own" (rule 68).

**At the PLAN, first:** (1) the inbox; (2) the default loop on `main` reproduces exactly those five; (3) for the
sim brief, check on a scratch clone whether any AI code issues a queued command to a worker on a gather loop (if it
does, Request 57 moves AI matches and rules 43 / 45 apply; then it is planned with the sim's hardening and its pin
move instead, and M8-2 takes its place beside the red row); (4) rule 68 on every brief.

## Where things stand

- **`main` after this session = sim be2cbd5 + view 7dd3b35 + data e0227ca** (plus this ACCEPT's studio and docs
  files). The Producer's scratch clone of that tree (`acc0506` in the session scratchpad; conflicts only in
  `studio/bugs/README.md` and `studio/qa/coverage.md`, both sides kept): build `--no-incremental` **0 errors / 0
  warnings**; the default loop **9,589 / 5 failed / 19 skipped of 9,613 in 21 m 30 s (beside the scene loop)**; smoke **PASS**; the scene loop **109 of 109, first pass**.
- **The gather overlap is shipped: `rules.json` `"gatherOverlap": 0.5`.** Data hash **B74DC7AE61B4862D** (was
  5353DF8A1473FC6D): saves and recordings from before are refused with `DataMismatch`; formats unchanged (save 10,
  replay 9 / 6). The golden `seed21_playable.replay` was re-recorded.
- **BUG-1431's cure:** `MovementSystem.QueuesBehind`: a footprint walker stops on a touching groupmate nearer its
  stand point only while it is itself within `EconomyConstants.QueueArrival` (Reach - GoalInset x sqrt 2 = 1.073 m)
  of that point. No data field, no saved state. **QA's note:** that is 7 cm above `ArrivalDistance` (1.0 m), so in
  practice a footprint walker no longer stops on a mate: it presses, gives up and re-walks. Stuck ticks and give-ups
  with the knob **off** rose 2-3x (24 Gatherers: 18,133 / 618 against 8,613 / 185) while income rose; at 0.5 they are
  low (24 Laborers 18 give-ups, 24 Gatherers 82). This is BUG-1463's mechanism.
- **Held workers:** seeds 2-56: none at 0 / 0.25 / 0.5 / 0.75 (base 5 / 4 / 2 / 1). QA's 19 staged shapes: 16 held on
  the base at 0, 2 on the new tree at 0.5 (a map-edge Keep, a 2 x 2 depot). Still held at 0.5 (BUG-1463): a 2 x 2
  depot with a mine on its face worked by 12 Teblor Gatherers (8 of 8 shuffles; 0 of 8 on the base at 0.5), fuzz
  seed 72, the map-edge Keep 4 of 8. **"Held" on an every-tick check excludes a worker that was in reach of its node
  or a drop-off in the last 600 ticks** (BUG-1464 item 1; the probe still looks once until the hardening).
- **Income (table (a), docs/03 "Implementation (M6-S10)"):** mine, 5 minutes, off / 0.5: Laborers 16: 2,520 / 2,710,
  24: 3,630 / 4,020; Gatherers 16: 2,235 / 2,865, 24: 2,985 / 4,155 (M6-S9's off: 2,440 / 3,430 / 2,040 / 2,790).
- **The match rows:** Vathar Crossing seeds 1-8: 24,497 / 30,569 / 27,240 / 27,240 / 27,361 / 27,240 / 23,412 /
  23,412: **the literal 20-minute row is seed 7** (`MatchCriterionTests`; the method names still say "Seed1";
  `HalfWay` 11,706). **Raraku seeds 1-8: none / 27,936 / none / none / 29,569 / none / 28,547 / 28,547: the row is
  seed 2; seeds 1, 3, 4, 6 do not end in 72,000 (BUG-1460)**: the winner has 17 idle workers, wood 14, the nearest
  tree 41.8 m from its drop-offs; the loser a finished Keep, 4,058 gold, 0 units and never trains a Laborer
  (**BUG-1465**). With the knob off (the cure alone) seed 1 ends at 35,736. Pale Hills seed 1 42,384; generated
  seed 2 21,822; FFA 52,098; 2v2 57,154. Hard v Normal **36 / 3 / 3** (Raraku 14 / 0 / 0, Pale Hills 12 / 2 / 0,
  Vathar 10 / 1 / 3).
- **Stress rows red on `main` by the same ruling (ids in BUG-1462):** `Bug0982_*` x2 (the rule picks 14 pop,
  cadre_rush ships 18: D33's), `MapSlice2StressTests` Raraku 3 / 4 / 6 (BUG-1460), `FarExpandProbeQaStressTests`
  Vathar 2 with four seats (BUG-1152's family), `StarvedSaveTwoProcessQaStressTests.PaleHillsSeed1_*` (the match ends
  before a starved wave), `StartingResourcesAiStressTests` openings 1 and 2, `ScoutCapQaTests.OneScoutKilled(seed: 5)`,
  and QA's six `KeepCornerCureQaStressTests` rows (BUG-1463). The five BUG-1430 rows are green. **These are the sim's
  hardening (counter 4 / 4 after the next feature session): by then they are two sessions red.**
- **`CounterCompositionTests.ANormalSeat_ThatSawShock_*` runs on a test-supplied purse now** (wood topped up, the
  delivered gold replaced by a trickle derived from the data; seen 6 Line / 3 Ranged v unseen 5 / 4). Accepted as a
  re-staging; QA's `CounterRestageQaTests` control (strength 0: 5 / 4 both ways) shows the weights decide. The margin
  is one unit, as before.
- **Queued orders are drawn (M6-V22).** `sim/Rts.Sim/ViewApi/OrderPath` (a class built once per match from the unit
  store's arrays: ViewApi source may not name `World` or `UnitStore`) + `OrderPoint`; `game/scripts/OrderPathView`
  (two MultiMeshes, no depth test, own units only, Attack markers fog-gated); `CommandCard.GhostClick` +
  `SelectionController.OrderBuild` (the first Shift-placed building queues behind an order that ends and replaces a
  gather loop or idleness: `OrderPath.QueuesBehind`). 108 scenes + QA's `QaM6V22Test` = **109**. Cost: shared +0.45 ms,
  distinct +0.74 ms on 200 selected, 0 bytes a frame. **An order queued behind a gather loop never starts**
  (`OrderSystem.Run` starts the queue only for an Idle unit; `OrderQueueViewTest`'s `BehindAGatherLoop` row prints it
  as a known gap): **Request 57**, decided (docs/01 2026-11-15 (b) item 5).
- **D-H9:** shadow.md has "Decisions (D-R38)"; `Content/OwnNames.cs` (a foreign name counts only outside every own
  name); BUG-1450 / 1451 / 1360 fixed; 19 skips in the default loop (was 21).
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's list).

## Standing rules (1-68)


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

## Watch-outs (all tracks)
- **New this session: `main` is red in five default-loop rows by ruling (BUG-1462; the list is at the top).** A
  default loop expects exactly those five; a "default loop with Stress" run also expects the Stress rows listed under
  "Where things stand". `main` builds **0 errors / 0 warnings**.
- **`gatherOverlap` 0.5 is in the shipped `rules.json`.** A test that means "the rule off" removes the field from a
  copy (`GatherOverlapScenes` writes or removes it itself; rule 58); "the base's data" in a fixture means the field
  taken out (`D32PreFixtureTests`). Any value above 0 turns the gather floor on.
- **The data hash moved (B74DC7AE61B4862D):** every save and recording fixture from before is refused. The next
  hash move is the data's (D-R39 only if a number must move, else D33) or the sim's hardening: never both in one
  session.
- **Session length:** 0506 ran about 9.5 hours (the sim's QA full: default loop 1 h 42 m and Stress 2 h 3 m beside
  the other tracks). Keep the data task light next session and tell the view's QA to run timing rows alone.
- **The view's scene loop is 109 scenes** (`OrderQueueViewTest`, QA's `QaM6V22Test` are new). `BenchTest` (7 s) and
  `MinimapTest` (0.5 ms) flake under load and pass alone.
- **`ViewApi` source may not name `World` or `UnitStore`** (`TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls`):
  `OrderPath` is built from the store's arrays, handed in by the view (`OrderPathView.NewReader`).
- **Eleven unit types are models** (Laborer, Crossbowman, Heavy Infantry; Camp Follower, Raider, Desert Archer; the
  Teblor Gatherer, Warrior, Javelin Thrower, Shaman, Breaker); ten are capsules (`ModelViewTest` prints "11 models,
  10 capsules; teblor capsules: [teblor_destrier_rider teblor_war_dog]"). A count of model types is read from
  `tools/asset-manifest.json`, never a literal (`QaM6V21Test` row 1). A model file holds the 1.84 m base figure; the
  data `scale` is applied by the view. Bars and marks still use the capsule's height (BUG-1441).
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
- **The scene loop is all green: 109 scenes** (`OrderQueueViewTest` and QA's `QaM6V22Test` are new; `QaM6V21Shots` and `ModelShots` are windowed tools, not in the loop). `QaM6V20Test`
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

### Where we are
M6-S10 on `main`: BUG-1431's cure + `gatherOverlap` 0.5, every pin re-measured once. Counter **3 / 4**. Fixed:
BUG-1430, BUG-1431. New: **BUG-1460** S3 (Raraku seeds 1, 3, 4, 6 do not end), **BUG-1465** S3 (a seat with a Keep
and gold never retrains a worker), **BUG-1463** S3 (held workers at 0.5 on some shapes), **BUG-1461** S3 (a worker
held by its own parked army), **BUG-1462** S3 (the rows red by ruling), BUG-1464 S4 (item 1: the every-tick probe
with the Producer's definition). Older debt: BUG-1434 (its "does not end" half is gone: Pale Hills seed 1 ends at
42,384; re-measure the 267 s wait), BUG-1433, BUG-1161's three alias names, BUG-1149 / 1151 / 1152, BUG-1150,
BUG-1010; notes: `[0]` lookups left in `FrenzyRulesLoaderTests`, `StatusDurationTechTests`, `DataContentHashTests`,
QA's `M7H1QaTests`; the JSON-`null` policy.

### Next task candidates
1. **M6-S11 (feature, QA full; about 300 production lines), in this order:**
   - **The red row first (rule 57):** `CounterCompositionTests.AHardSeat_ShiftsItsLineShareFurther_ThanANormalSeat`
     (Normal and Hard both shift 0.111 on nine units in the 180 s window). Either a staging that can tell Line x 1.5
     from x 2 honestly (more units inside the memory window: a longer window from the data, two producers of each
     kind, or more seeds summed; the same purse both ways, `CounterRestageQaTests`' strength-0 control extended to
     Hard), or the finding, with numbers, that Hard's `counterStrength` 1.0 does not out-shift Normal's 0.5 at any
     army size the AI reaches by its first three waves (then the row is re-stated as that measurement, BUG-1156 gets
     the figures, and the data track is told: a Request in reverse). No assertion loosened to pass.
   - **Request 57 (owner request follow-on; Producer decision, docs/01 2026-11-15 (b) item 5):** a unit on a gather
     loop that has queued orders finishes its current trip (it delivers what it carries; a worker with no cargo that
     has not reached its node goes at once) and then starts the queue. Generic (any worker, any node). No new saved
     state if the existing queue and cargo state can say it; else outcome "needs a format bump", reported, not
     built. Proof that **no AI match moves** (every pin unedited; the golden may move only if the recording itself
     holds a queued order behind a gather loop: say so and regenerate with the reason). The view's
     `OrderQueueViewTest` `BehindAGatherLoop` row is named in the report's first lines for the view track (the sim
     cannot edit `game/tests`); the first-Shift-building rule stays as it is until the view follows.
   - OUT: BUG-1460 / 1463 / 1465 / 1461 (the hardening, next), any pin move, `game/**`.
2. **The sim's hardening (counter 4 / 4 after M6-S11), one pin move:** **BUG-1465** (a seat with a production
   building, gold for a worker and no worker trains one) and **BUG-1460** (Raraku ends again on seeds 1, 3, 4, 6, or
   the row says why not: wood on Raraku is finite, so an AI with gold and no wood needs a rule: trade nothing, but
   attack with what it has / gather the far wood), **BUG-1463** (the give-up loop at 0.5: look at `QueueArrival`
   against `ArrivalDistance`, the 2 x 2 depot with Gatherers first), **BUG-1461**, BUG-1464 item 1, the Stress rows
   red by ruling (each green or re-stated with its id), BUG-1434's re-measure. Rules 43 / 45 apply; the data track
   measures no opening that session.
3. **M8-2: the Shadow Archer's stand-still stealth** (Request 56 G1; then G2), schema first, test data only (QA full).
4. **Requests 54 / 55** (each changes Normal AI behaviour: with the hardening's pin move, or later).
5. **Request 53, part two** (the Frenzy report re-read), **Request 56 G3-G8**.

---

## View track

### Where we are
M6-V22 on `main`. Counter **4 / 4: the next session is the view's hardening.** 109 scenes (check the count in the
loop's last line). New: **BUG-1470** S3 (a queued Stop / Hold / no-target cast disc covers the marker before it),
BUG-1471 S4 (the mixed picture's Gather marker off frame; a worker with 8 queued silently drops the first
Shift-placed building; no feedback when a queued Build is refused at its turn). Older: BUG-1441 S4, BUG-1440 S4,
BUG-1411 S4, BUG-1381 S4, BUG-1177 item 2 (the owner's taste), BUG-1115 item 4. Answers to the developer's questions
(docs/01 2026-11-15 (b) item 6): markers scale with zoom: yes, in the hardening; the current order has a line and no
marker: keep; no depth test: keep; the one-tick-old state: accepted.

### Next task candidates
1. **M6-VH8, the view's hardening (QA standard):** BUG-1470 first (a ring round the previous marker, as docs/02
   says); queued markers and lines sized by the camera's zoom (readable at 60 m); **BUG-1441 item 1** (bars, status
   and cast marks and the cargo marker follow the drawn figure's height: every Teblor match shows it); BUG-1471 items
   2-3 (a refused or dropped queued Build gives the existing refusal feedback; the text, if any, from `ui.json`); a
   scene row for queued orders while watching a replay (QA checked it by reading only); a Casting clip
   (`attack_cast`) for a type that has one; BUG-1411; BUG-1440; BUG-1381; figures at a finished replay. Timing rows
   that flake under load (`BenchTest` 7 s, `MinimapTest` 0.5 ms) are re-run alone, not loosened.
2. **After Request 57 lands in the sim:** `OrderQueueViewTest`'s `BehindAGatherLoop` row becomes a pass row, and the
   first Shift-placed building queues behind the trip instead of replacing (docs/02 "Queued orders" follows).
3. **Merged surfaces** (rule 61): one material and one atlas a model; 400 figures 11.1-11.4 ms of 16.7.
4. The Destrier Rider and the War-dog once a horse / dog pack is on disk (owner); Teblor buildings.
5. The view's part of M8 criterion 1 once a roster ships the block.
6. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Where we are
D-H9 on `main`: no data byte moved. Counter **0 / 4** (D-H9 was the hardening). Fixed: BUG-1450, BUG-1451, BUG-1360.
New: BUG-1480 S4 (the Decisions list's numbering). Open: BUG-1420 S3, BUG-1421 S4. **Four of the five rows red on
`main` are this track's** (BUG-1462). The economy changed under every opening measurement (M6-S10), and it will
change once more in the sim's hardening (BUG-1465 / 1460): **D33 comes after that hardening, not before.**

### Next task candidates
1. **D-R39: the pages and the opening report after the gather value (the four red rows first; QA light, standard
   if a data number moves).** In order:
   - `QA/Content/DR35ProsePinQaTests.AgeTwoProse_MatchesTheAgeTwoTables` and
     `Malazan_Bug0982Prose_MatchesTheThresholdTable`: rewrite the prose on `docs/factions/malazan.md` and
     `whirlwind.md` to the cells M6-S10 re-printed (malazan seeds 32 / 45, cadre_rush v raider_rush seeds 8 / 14,
     whirlwind seeds 9 / 19); the pins pass unedited.
   - `Content/AiOpeningPairsReportTests` (0, 2), seed 3: the Malazan standard opening's first wave (5:47) wipes the
     holy_war army and the Whirlwind are defeated at 12:29 with no wave sent. Measure holy_war against all three
     Malazan openings over the row's seeds; if holy_war loses before its first wave in most, that is a balance
     finding for D33 (write it on the page and re-state the row as "a side defeated before its first wave is due is
     reported, not failed", with the reason); if it is one seed, say so. (2, 0), seed 13: a beaten Whirlwind seat
     (Town Hall razed, four steps due) is reported "DEAD END": the report gets a "beaten: hall lost" reason for a
     seat whose hall is razed (test logic in `Content/`), not a data change.
   - The Teblor sentences M6-S10 listed (teblor.md: the Hut steps 15 / 16; the first waves 323 s / 316 s, Age II
     448 / 429 s, beaten at 1,022 s) and BUG-1421 items 3-4; BUG-1480.
   - No `ai.json` number moves unless the measurement demands it (then one hash move, the golden's header and the
     two sim-owned data-hash pins handed over by name: rule 51; the sim moves no hash that session).
2. **D33: Breakers, Destrier Riders and War-dogs back in the Teblor army** (BUG-1420; BUG-1421 items 1, 2, 5; BUG-0982's
   14 v 18 pop), the start of the balance pass, **after the sim's hardening**. Measure Easy, Normal and Hard and the
   mirror; count distinct matches. One hash move.
3. The owner's answers if any land ("Shadow names ok", "gather overlap ...", "teblor ai: early raid", "teblor
   buildings: 1.5", "opening text: ...", the counters, `goldShare`, BUG-0780, `counterStrength`).
4. The Shadow roster, file by file, as Request 56's schemas reach `main`.
