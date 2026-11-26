# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-26-0207** (session 1 / 8 of 2026-11-26, started 02:07).
Sim **M7-9** ACCEPT (feature, 0 fix rounds, QA full PASS_WITH_ISSUES), view **M6-V29** ACCEPT (feature, 0, QA
standard PASS_WITH_ISSUES), data **D37** ACCEPT (feature, 0, QA standard PASS; nothing shipped). **M5 8 / 9
(criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters: **sim 2 / 4, view 3 / 4, data 2 / 4**. Bug ids
next session: **sim 1810-1829, view 1830-1839, data 1840-1849** (1772-1789, 1793-1799, 1800-1809 unused). Open
S1 / S2: none. The next session is **2 / 8 of 2026-11-26**.

**Next session in one line (proposal):** sim **M8-4: BUG-1771's shared test helper first (D38 needs it), then
Request 56 G4, the Hounds' 7-alive limit, on test data** (feature, QA full; no AI-play change, no hash move, no pin
move) · view **M6-V30, the next building models (Whirlwind or Teblor, the same pack)** (feature, QA standard; the
view's clean-up follows at 4 / 4 with BUG-1790 / 1792) · data **D38, ship the three Teblor orders through
`buildOrders`** (feature, QA standard; **the session's one hash move**; the owner's review table).

**The default loop on `main` is green.** Expect **0 failed / 20 skipped** (10,569 passed / 0 failed / 20 skipped of 10,589 in 22 m 1 s on the Producer's scratch clone
`prod0207-accept` of sim e2193da + view 3c4cd4f + data a206dd0). The skips: last session's 21 less the BUG-1760
row (un-skipped by D37). Scene loop **128 of 128, first pass**. Any red row is a finding. Stress did not run as a category this
session (rule 53: no tick change); sim QA ran the eight pin classes (125 / 0 / 2, no value moved) and Perf alone
(214 / 0 / 3).

**At the PLAN, first:** (1) the inbox (waiting, none blocking: D33's and D34's tables, "buildings ok", "whirlwind
colours ok", the draw rule, the four buildings, and now the draw notice's look; tweaks come before roadmap work);
(2) the default loop on `main`; (3) **the bug index:** `studio/bugs/README.md` must hold exactly one row each for
BUG-1760 and BUG-1761, both "fixed (D37...)" (the merge doubled them: an "open" row and a "fixed" row; drop the
"open" one if the conductor did not), and one row each for BUG-1770, 1771, 1790, 1791, 1792; (4) **BUG-1791: restore
the heading yourself** (docs drift, one line): in `docs/03-technical-design.md`, put `## Vision, detection, fog` and
a blank line back before the paragraph that starts "Fog of war and the high-ground rule are in since M4-3a" (it now
runs on at the end of "Implementation (M6-V29)"), check `grep -n "^## Vision, detection, fog"`, and set the bug
fixed; (5) `CLAUDE.md`'s "Current milestone" line is still stale (on "Waiting on you"); (6) **rule 54 / 55's
rehearsal for D38, because it is the first shipped file to carry `buildOrders` and the first to carry
`throughReserve`:** on a scratch clone write the Teblor `standard` exactly as D38 will (`buildOrder` = the Normal
fixture's steps, `buildOrders.easy` = the Easy fixture's, `buildOrders.hard` = the Hard fixture's; the fixtures are
`sim/Rts.Sim.Tests/Content/Fixtures/teblor_standard_d37_<difficulty>.json`), run the default loop and the Stress
classes that forge or read the Teblor `standard` (`Stress/ReserveSwitchReportTests`, QA's
`M78ReserveSwitchQaStressTests`, the M7-9 rehearsal row
`M79PerDifficultyAiQaTests.TheDataTracksRehearsal_*`, `FarExpandProbeQaStressTests`), and sort the red list into
"D38 re-pins" (data-owned or handed by name), "the sim's helper fix (BUG-1771)" and "other" before writing the two
briefs. Sim QA's rehearsal this session had `buildOrder` unchanged and no shipped switch, so its list of 8 is a
floor, not the list.

## Proposed briefs

### Sim: M8-4, BUG-1771 first, then Request 56 G4 (feature, QA full)
(0) **BUG-1771 (S3; first, and D38 depends on it):** one shared helper (for example in `AiScenes`) that sets an
opening's build order on a JSON copy **and removes `buildOrders`**, used by every test site that writes
`o["buildOrder"] = ...` (about 100; grep `\["buildOrder"\]\s*=`), `WorkerCarryTests.s_teblorQuiet` among them; and
the helpers that forge the Teblor `standard` (`Ai/TeblorSwitchOrders`, `Ai/PerDifficultyOrders`,
`Stress/ReserveSwitchReportTests`) tolerate a shipped file that already carries `buildOrders` and `throughReserve`
(rule 58). Prove it as rule 59 asks: run the touched classes on a copy of the shipped data written as D38 will
write it (the three D37 fixtures), not only on today's data. No expected value changes. (1) **BUG-1770 (S4, a few
lines in the same loader):** a `null` or `[]` entry does not count toward ruling (a). (2) **Request 56 G4:** the
Hounds' 7-alive limit as a generic data hook, on test data only (docs/factions/shadow.md, docs/03 "For the data
track (M8-3)" for the shape of G2 / G3). Out: any AI-play change (the AI-play session is the one after, so D38's
orders ship on the `main` they were measured on), any byte under `game/data/**`, existing classes under `Content/`
and `QA/Content/`. The data developer edits the hash pins and fixture pins of D38's list; the sim developer does not
touch those rows.

### View: M6-V30, the next building models (feature, QA standard)
The Whirlwind's or the Teblor's buildings from the same pack, through M6-V26 / V28's import path (the data `scale`
on a model's height when a Teblor building follows); pictures at zoom 40 and 20; the bench alone (rules 61 / 77).
`tools/asset-manifest.json`, `tools/import-assets.ps1`, `tools/art/**` are the view's. Out: `Match.ResultFor`,
`DrawNotice` (BUG-1790's two-sided fix, BUG-1792 and the wrong sentence in docs/03 "Implementation (M6-V29)"
Question 1 are the first items of the view's clean-up, the session after: counter 4 / 4). Write the Producer's
answers to M6-V29's Questions 1-3 (docs/01, the second 2026-11-26 row, (2)) into docs/03 "Implementation (M6-V29)"
(rule 67).

### Data: D38, ship the three Teblor orders (feature, QA standard; the session's one hash move)
`game/data/factions/teblor/ai.json`, the `standard` opening only: `buildOrder` = M1RS (the Normal fixture),
`buildOrders` = `{ "easy": K9KC10S, "hard": QRD1 }` (the Easy and Hard fixtures), each step for step as measured;
description, compositions and attack unchanged. docs/03 "For the data track (M7-9)" is the schema's page. **Measure
on the shipped file, all three difficulties' 64 seats (rule 75): the rows must equal D37's** (Easy 1 / 0 / 38 / 28 /
34 / 42 / 22, Normal 9 / 23 / 23 / 18 / 30 / 40 / 18, Hard 11 / 23 / 23 / 40 / 42 / 47 / 25); if a cell differs, stop
and say so. Pins handed by name (rule 51; the PLAN's rehearsal completes the list): the golden's header and the two
golden-hash rows (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`,
`QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`),
`StillStealthLoaderTests.TheHash_IsUnchangedWithoutTheField_MovesWithIt_AndWithEachField`,
`Content/D34HashMoveTests`, `QA/Content/D33QaTests`, `QA/Content/D34QaTests`,
`Content/TeblorOpeningFixtureTests.TheShippedOpening_IsTheD34Fixture`; **not** `WorkerCarryTests` (the sim fixes
its helper beside you: that one row, named, may be red on your branch and must be green on the merged tree: rule
68). `QA/Content/D32DescriptionQaTests.Shamans_HaveTheirOwnTrainStep_AfterTheCircleStep` reads every list by
position in the file, not by due time (docs/01, the second 2026-11-26 row, (3)). teblor.md: "AI build order
(Normal)" = M1RS, a short "Easy" and "Hard" note, BUG-1612 closed with the three rows, the Producer's answers to
D37's Questions 1-3 written in (rule 67). The owner's review table: per difficulty, before (D34's order) and after,
the seven figures, in plain words. Older saves and recordings stop loading: say so. Then BUG-1421 item 1.

## Where things stand

- **`main` after this session = sim e2193da + view 3c4cd4f + data a206dd0** (plus this ACCEPT's studio and docs
  files). Scratch clone `prod0207-accept` (code merged clean; only `studio/bugs/README.md` conflicted): build
  `--no-incremental` 0 / 0, default loop **10,569 passed / 0 failed / 20 skipped of 10,589 in 22 m 1 s**, smoke **PASS**, scene loop **128 of 128, first pass**.
- **No hash move: the data hash is still 3D93D45345A3B4EA.** Saves and recordings made since 2026-11-26 load.
  `game/data/common/ui.json` gained `hud.draw_countdown` (view data, not in the hash).
- **A build order per difficulty (M7-9):** `AiOpening.BuildOrders` (indexed by `AiDifficulty`; empty array = the
  field left out; an empty entry = no entry) and `BuildOrderFor(difficulty)`; `AiPlayer.Steps` is the seat's list,
  resolved once; nothing under `sim/Rts.Sim/Ai/` reads `Opening.BuildOrder` (a source row pins it). Loader:
  `DataLoader.BuildAiBuildOrders`, rulings (a)-(e) in docs/03 "Implementation (M7-9)". Hash: marker 2 + mask + each
  entry, only when present. **No shipped opening carries it.** Easy always plays opening 0, so an `easy` entry on
  another opening is never played (allowed). Test orders: `Ai/PerDifficultyOrders` (A, B, C).
- **A test that replaces an opening's `buildOrder` must remove `buildOrders` too** (BUG-1771, open until the sim's
  next task): harmless today, wrong the moment D38 lands.
- **The draw on screen (M6-V29):** `Match.ResultFor` reads `World.MatchDrawn` first; `DrawNotice` (a node `Match`
  adds beside `Alerts`; label at top centre, 800 x 30 px from 86 px down; `Sync(World)`; `ShownSeconds`,
  `TextWrites` for tests). **128 scenes.** `DrawViewTest` runs dev matches on a data copy with `draw.afterSeconds`
  20 and no starting gold; `QaM6V29Test` prints `KNOWN BUG-1790` and stays green. Known: BUG-1790 (a save taken
  after a side fell past the draw loads to "Defeat"), BUG-1792 (long text), BUG-1791 (the docs heading).
- **The Teblor orders (D37; nothing shipped, BUG-1612 open until D38):** Normal **M1RS** 9 / 23 / 23 / 18 / 30 /
  40 / 18 (also meeting the bar: M1SC 9 / 23 / 27 / 14 / 23 / 37 / 21, RSCR 9 / 23 / 26 / 18 / 30 / 37 / 18); Easy
  K9KC10S 1 / 0 / 38 / 28 / 34 / 42 / 22; Hard QRD1 11 / 23 / 23 / 40 / 42 / 47 / 25. Orders in
  `Content/D37TeblorNormalOrderReportTests.AllVariants` (39) and D36's class; fixtures
  `Content/Fixtures/teblor_standard_d37_{easy,normal,hard}.json` with `Content/D37TeblorFixtureTests`. QA's three
  own Normal orders (QR1-QR3) miss the bar. The War-dog step's `throughReserve` is what costs Normal its Anointing.
- **Description checks (D37):** "first-shot" with a hyphen, any Unicode dash or several spaces reads as first-shot
  words (BUG-1760 fixed); U+2212 and a soft hyphen do not (not named by the ruling).
- **Requests:** 55, 58, **59 done**; 56: G1-G3 built, G4-G8 open; **60 new** (which seats stood at the draw, for
  BUG-1790 with three or more sides; small, with the sim's next save-format change).
- **Stalled rows, pinned matches, building models:** unchanged from 2026-11-25-1437 (Pale Hills four-seat seeds
  1 / 2 / 4 / 6 / 7 draw at 61,779 / 58,378 / 63,124 / 51,216 / 80,167; seed 3 p1 at 40,674; Hard v Normal
  34 / 5 / 0 / 3; 6 of 30 building types are models; still skipped: the Vathar four-seat seed 2 row (BUG-1621 /
  1610), `ALoneIdleWorker_*` (BUG-1116), the Pale Hills 2v2 seed 2 row (BUG-1149)). Draw test helpers:
  `Draw/DrawScenes` (`Off`, `TakeOutDraw`, `TeblorOpeningBeforeDraw`), QA's `QA/DrawQaOracle`; a test that writes
  store arrays directly calls `World.RecountDraw()`.
- **Queues after the proposals above.** Sim: the AI-play session (BUG-1661 first and alone, then BUG-1660
  `ai.minWorkers` from 4, 1555, 1554 / 1556 with 1490 and Request 60 (one save-format change), 1558; one pin move;
  un-skip the Vathar four-seat row with the cure; candidate: press the attack while the draw clock runs; **the data
  track then re-measures the three Teblor orders, "measure and report": rule 41**), then Request 56 G5-G8, the sim's
  clean-up at 4 / 4 (BUG-1730, 1731's decision). View: its clean-up at 4 / 4 (BUG-1790's two-sided fix,
  BUG-1792, BUG-1710, the docs sentence), then more models, the mounted units and machines once packs are on disk.
  Data: after D38, the hour-long Teblor Stress report classes, then the Shadow roster as its rules land; owner
  tweaks first whenever the inbox has them.
- **Session length:** the sim's QA full without the whole Stress category (rule 53) kept the suites short: default
  loops of 30-43 m beside each other.
- **Scratch folders left** (harmless; the owner's housekeeping list): in this session's scratchpad `prod0207-plan`,
  `prod0207-accept`, `m79-*`, `m6v29*`, `d37-*`; outside it `C:\Users\Nick\AppData\Local\Temp\claude\m79-scratch`
  (one test log; deletion denied). The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` and a file named with a
  yellow-circle emoji are still untracked in the owner's main checkout.
- **Usage at the ACCEPT:** weekly 24 % / 25 % (stop at 95 %).

## Standing rules (1-79)


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
**72 (new, 2026-11-17-0816):** after a fix round that changed tests or docs only, QA's re-check runs the touched
classes, the default loop and the `--no-incremental` build; it does not re-run Stress or Perf (M8-2's re-check spent
4 h 46 m on a full loop for three test files). Rule 70's "no Stress re-run" holds for every tests-only round.
**73 (new, 2026-11-17-0816):** no new test pins the shipped data hash, or the bytes / digest of a save made on
shipped data, as a literal (rule 49 widened: BUG-1580, S2, one fix round). The hash is read from the golden header;
"byte for byte what `main` writes" is proven in-session against the base and kept as a differential row
(`StillStealthSaveBytesTests` is the model). A brief never states the shipped hash as a pass / fail literal in a
session where another track moves it.


**74 (new, 2026-11-18-0926):** "the Stress category has no expected red row" is said only after the Stress
category ran on the merged tree. `FarExpandProbeQaStressTests` was not on rule 71's list and went red with D33
unseen for a session (BUG-1621). Rule 71's list gains `Stress: FarExpandProbeQaStressTests`, and at PLAN the list
is checked against a grep for every test class that plays a full AI match.
**75 (new):** a bar in a data brief ("ship only if ...") names every figure the bug is about on every
difficulty it is about. D34's bar guarded Easy's upgrade and Normal's / Hard's unit kinds, so the order that met it
cost Easy its Riders (36 -> 4) with the bar green. And when a developer reports "none meets the bar", QA tries
an order of its own before agreeing (it found one: BUG-1641).
**76 (new):** a pin-move list is cut to what can move. A change to one faction's `ai.json` lists the classes that
seat that faction plus the hash pins and the golden's header; the other classes are named as "cannot move: no
seat of this faction" and are not run (D34 ran nineteen classes that seat only factions 0 and 1).


**77 (new, 2026-11-24-1431):** an art or view brief's frame-time criterion is judged on a quiet window. When the
developer's and QA's bench both ran beside another track's test process (M6-VH9: capsules x1.45-2.0 slower than
the last session's, so every figure row was "within the window's factor"), the Producer runs the pinned rows alone
on the merged tree at the ACCEPT and the log carries that figure; a brief tells the view's QA to run the bench
after the other tracks' suites have ended when it can. And a criterion of the form "only these files change" is
checked at PLAN against every other criterion that asks for a new mark in a file (M6-VH9's 11 against 6: rule 66).


**78 (new, 2026-11-25-0924):** a brief that asks for a search ("find the order that meets the bar") names the levers to vary
(which steps carry a switch, each step's count, time and position) and the developer's report says which levers were not
varied; "none meets the bar" is read as "none of these" until then (D-H10: all eleven candidates kept the Shaman step's
count of 2; QA's one-Shaman orders halved the shortfall: BUG-1721). And a ruling about how existing code behaves is
checked against that code at PLAN when the brief forbids changing it (M7-H3 item 10: the `null` switches).

**79 (new, 2026-11-26-0207):** a ruling of the form "the same answer live, after a load and in playback" is checked
at PLAN against what a save actually keeps (M6-V29: the view latches its result live, a save keeps no order of "draw"
and "defeat", and the brief forbade the sim change that would: BUG-1790; rule 66's family). And rule 58 widened: a
helper that *replaces* a data field also removes every field that can override it (`buildOrder` and `buildOrders`:
BUG-1771), and sim QA's rehearsal of "what the data track will ship" uses the data track's real fixture when one
exists, whole (D37's Normal fixture carries `throughReserve`; the M7-9 rehearsal kept the shipped `buildOrder`).


## Watch-outs (all tracks)
- **New this session (0207): an opening may carry `buildOrders`** (no shipped opening does until D38). A test
  that sets an opening's `buildOrder` on a JSON copy must remove `buildOrders` (BUG-1771); a seat's list is
  `AiPlayer.Steps`, never `Opening.BuildOrder`. A scene that plays to a draw reads the notice through
  `SimRunner.Ticked` (`DrawViewTest` is the model); `QaM6V29Test` prints `KNOWN BUG-1790` on purpose.
- **New this session: a two-process row's limit is processor time.** `MapFileTests.RunChild(..., seconds)` ends its child when
  the child and its test host have used `seconds` of processor time (wall ceiling 3x). A child on several cores can
  use more than one second a second: read "used more than N s of processor time" on a small limit as that before
  calling it a hang. A queued-order notice is view-only: `World.DroppedOrders` is emptied every tick, so a scene
  must read it through `SimRunner.Ticked`, never once a frame.
- **New this session: a click is tested against what is drawn.** `SelectionController.BuildingOnRay` /
  `GhostOnRay` replace the box-only picks in `EnemyAt`, `ContextTarget`, `PickResource`, `PickBuilding`; a unit
  wholly behind a model is not click-picked (`HiddenByBuilding`). A scene that clicks beside a Keep or Barracks
  model must pick a pixel on open ground or select the unit directly (`M3PlayableTest`'s seed-6 builder,
  `ReplayPlaybackTest`). Older scenes that pinned the 3 m box were adapted: `BuildingScaleViewTest`, `QaM7V4Test`,
  `QaV3Test`.
- **New this session: `ai.json` steps may carry `throughReserve` / `noReserve`** (no shipped step does). A test
  that forges the Teblor `standard` (`Ai/TeblorSwitchOrders`, `Stress/ReserveSwitchReportTests`, QA's
  `M78ReserveSwitchQaStressTests`) reads the shipped file: when the data track ships switches there, those
  helpers must tolerate them (rule 58). `noReserve` on an expand step = never expands (BUG-1663, by ruling).
- **New this session: `World.DroppedOrders`** is output like `Deaths`: emptied each tick, not hashed, not saved;
  an entry's unit may be dead when read. `SaveRestoreQaTests` lists `_droppedOrders` as not saved.
- **Session length:** 1431 ran about 10 hours with no pin move (sim QA full: default loop 2 h 56 m, Stress
  3 h 35 m under load; the data's QA first ran the wrong filter and lost 2 h: say the filter in a light brief).
- **New this session: a unit type may carry `attack.fromStealthBonus` and a `nearbyOwn` passive.** No shipped
  unit does. A test that forges them uses `Vision/HitBonusScenes` (rule 58). With either anywhere in the data,
  `World.HitBonusInData` is true and every hit by a field type reads `HitFactor.Of` (one status scan; for a pack
  type one grid query into `World.ShoveNeighbors`, so never call it inside movement's phases 8-9).
  `PassiveLoaderTests` pins four triggers; `DataContentHashTests` pins `UnitPassive`'s nine properties.
- **New this session: two building types are models.** A scene that reads a finished Garrison Keep's or
  Barracks' box mesh, material or bar height sets `BuildingViews.UseModels = false` before binding or reads
  `ModelOf` / `BarY` (`BuildingScaleViewTest` and `QaM7V4Test` are the models). Picking follows the drawn shape since M6-VH9
  (BUG-1630). A building's model is found by its id under `game/assets/models/<faction>/<id>/`; the data has no
  `model` field for buildings. `QaM6V26Test` passes with its known-bug rows printed; `--strict` makes them fail.
- **New this session: the Teblor `standard` has the Carver and the Anointing at steps 19 / 20 (10:00).** In
  `ai.json`, research and build steps ignore the reserve while train steps obey it, and a step behind a waiting
  expand step spends only above the reserve (Request 55): **file order is behaviour**, and it changes with the
  sim's next task.
- **The data hash moved (A732F387C2BB7E4D, D34):** every save and recording fixture from before is refused. No
  hash move is planned next session (the sim's AI follow-up moves pins, not the hash, unless it adds an `ai`
  knob to `game/data/common/**`: then it is the session's one hash move and the data track ships no data).
- **Session length:** 0926 ran about 21 hours. The sim developer's last full loop took 6 h 34 m beside the other
  tracks; the view's and data's QA were done hours before the sim's. Perf was evidence this time (214 / 0 / 3
  alone).
- **New this session: a unit type may carry `stillStealth`.** No shipped unit does. A test that forges it uses
  `Vision/StillScenes` (rule 58). With the field anywhere in the data, every world allocates `UnitStore.Still`, the
  save gains a `STIL` part and a held unit's Stealthed entry shows `TicksRemaining` = timed part +
  `StatusSystem.Holders`: a test that reads a Stealthed entry's ticks on forest or stand-still data must subtract the
  holders.
- **New this session: the AI's openings changed (D33).** `cadre_rush` waves at 16 pop, `holy_war` at 14, the Teblor
  `standard` has 29 steps and trains a Breaker, a Rider and a War-dog pack before its expand step. In `ai.json` a
  step after a waiting expand step spends only above the expand's reserve (Request 55): **file order is behaviour**.
  The FFA criterion row is Vathar seed 1; the retrain twin is Raraku seed 5.
- **`ModelViewTest` pins the capsule types as an exact set of six** (the criterion's own list). A new unit type
  without a model (the Shadow roster, a test faction on shipped data) turns it red: the task that lands one hands the
  view that list (rules 54 / 55 rehearsal).
- **Session length:** 0816 ran about 26 hours (three default loops of 1-2.7 h, two Stress runs of 2.6 h and 4.8 h,
  the data's pin runs; QA started per track as builds finished). Rule 72 removes the second Stress run. Perf was
  evidence only in the first inspection (the re-check hit the PC's slow state; base failed the same rows).
- **Smoke can fail once on stale import state** (`Can't find file 'uid://...'` / `Unrecognized UID` lines in a
  worktree's `.godot` cache after another branch's assets were merged): the re-run passes. Two developers saw it this
  session; it is not a finding unless the second run fails.
- **Pinned matches after D33** are listed under "Where things stand"; the older "Pinned matches on `main` after
  M6-S12" bullet below is right except for those rows.
- **The default loop is green on `main`**; the Stress category has no expected red row since 1431 (the BUG-1621 row is skipped with its ids).
  `main` builds **0 errors / 0 warnings**.
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
- **Session length:** 0506 ran about 9.5 hours (the sim's QA full: default loop 1 h 42 m and Stress 2 h 3 m beside
  the other tracks). Keep the data task light next session and tell the view's QA to run timing rows alone.
- **The view's scene loop is 122 scenes** (`ViewHardenVh9Test`, QA's `QaM6VH9Test` and `QaM6VH9FixTest` are new). `BenchTest` (7 s),
  `MinimapTest` (0.5 ms), `DebugOverlayTest`, `QaM25Test`, `QaM27Test` and `QaH1Test` flake under load and pass alone.
- **`ViewApi` source may not name `World` or `UnitStore`** (`TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls`):
  `OrderPath` is built from the store's arrays, handed in by the view (`OrderPathView.NewReader`).
- **Fifteen unit types are models** (the eleven before plus Cadre Mage, Sapper, Priest, Zealot); six are capsules
  (`ModelViewTest` prints "15 models, 6 capsules" and names them: Wickan Lancer, Horse Raider, Catapult, Battering
  Ram, Destrier Rider, War-dog). A count of model types is read from `tools/asset-manifest.json`, never a literal. A
  model file holds the 1.84 m base figure; the data `scale` is applied by the view. Bars and marks sit over the figure
  as drawn (`UnitViews.OverlayLines`); robes, veils and satchels are rigid boxes on a bone (a leg shows through).
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
M7-9 (Request 59, a build order per difficulty) on `main`; no hash, pin or format moved. Counter **2 / 4.** New
debt: BUG-1771 (S3; test helpers and `buildOrders`: **first item of the next task**), BUG-1770 (S4; a second
misleading loader error). Carried: BUG-1730, 1731, 1700 (S4), BUG-1660, 1661 (S3; behind BUG-1621 / BUG-1610, whose
Stress row stays skipped), 1555, 1554 / 1556, 1557 (S3), 1558 (S4), 1463, 1537, 1490, 1434, 1433, 1161, 1149, 1152,
1150, 1068, 1116, 1010. The answers to M7-9's Questions 1-4 are in docs/01 (the second 2026-11-26 row, (1)): keep
all four.

### Next task candidates
1. **M8-4: BUG-1771 first, BUG-1770, then Request 56 G4** ("Proposed briefs" above; feature, QA full; no AI-play
   change, no hash move, no pin move; 20 bug ids 1810-1829). Write M7-9's answers into docs/03 "Implementation
   (M7-9)" (rule 67).
2. **The AI-play session (feature, QA full, one pin move; the data track ships no opening beside it and re-measures
   the Teblor orders after it):** BUG-1661, then BUG-1660, BUG-1555, BUG-1554 / 1556 with BUG-1490 and Request 60 (a
   save format change: rule 50), BUG-1558. Each cure measured apart (rule 70). BUG-1700's comment with it.
   Candidate: a seat with an army presses its attack while the draw clock runs.
3. Request 56 G5-G8, on test data only.
4. The sim's hardening (at 4 / 4): BUG-1730, BUG-1731's decision, older S3 / S4.
5. Requests 54, 53 part two. A movement task for BUG-1463 only with a design first.

---

## View track

### Where we are
M6-V29 (the draw on screen) on `main`. Counter **3 / 4.** 128 scenes. New debt: BUG-1790 (S3; a save taken after a
side fell past the draw loads to "Defeat"), BUG-1791 (S3; a docs/03 heading deleted: the Producer restores it at
the next PLAN), BUG-1792 (S4; the notice's long text). Carried: BUG-1710 (S4). The Producer's answers to M6-V29's
Questions 1-3 are in docs/01 (the second 2026-11-26 row, (2)): the notice's look and silence kept; Question 1 is a
bug to fix, not a limit to keep.

### Next task candidates
1. **M6-V30, the next building models** ("Proposed briefs" above; feature, QA standard). Write M6-V29's answers
   into docs/03 (rule 67).
2. **The view's clean-up (at 4 / 4, the session after):** BUG-1790's two-sided fix in `Match.ResultFor` (a drawn
   match with exactly two sides reads Draw for both, whoever is out now), the wrong sentence in docs/03
   "Implementation (M6-V29)" Question 1, BUG-1792 (grow both ways or wrap; the stale 640 px comment), BUG-1710,
   older S3 / S4.
3. The two cavalry, the Destrier Rider, the War-dog, the Catapult and the Ram once packs are on disk (owner).
4. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...", "priest veil: ...",
   "buildings ok", "roof: also the tower tops", "depot: taller", "box towers: shoot from the top", "whirlwind
   colours ok" / "priest: white robe", "outline hidden units", "order notice: ...", "announce every dropped order",
   "draw notice: ...").

---

## Data track

### Where we are
D37 on `main`; no game data changed. Counter **2 / 4.** Closed: BUG-1760 (S3), BUG-1761 (S4). Open: BUG-1612 (the
unit mix; the cure is D38, measured and waiting in three fixtures), BUG-1421 item 1, BUG-1611 (S4). The Producer's
answers to D37's Questions 1-3 are in docs/01 (the second 2026-11-26 row, (3)): Normal = M1RS; `buildOrder` is
Normal's, Easy and Hard are entries; the Shaman / Circle check reads position, per list; no new first-shot words.

### Next task candidates
1. **D38** ("Proposed briefs" above; QA standard; the session's one hash move; the owner's review table).
2. The hour-long Teblor Stress report classes D34 did not re-run, malazan.md's "Age II against the Whirlwind"
   paragraph, BUG-1421 item 1.
3. After the sim's AI-play session: the three Teblor orders re-measured on that `main` (measure and report).
4. The Shadow roster as Request 56's schemas reach `main` (G1-G3 are there; G4-G8 are not).
