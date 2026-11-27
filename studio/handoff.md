# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-26-1847** (session 3 / 8 of 2026-11-26; ended about
07:00 on 2026-11-27). Sim **M6-S14** ACCEPT (feature, 1 fix round, QA full FAIL -> PASS_WITH_ISSUES), view
**M6-VH10** ACCEPT (hardening, 0, QA standard PASS_WITH_ISSUES), data **D39** ACCEPT (feature, 0, QA light
PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters: **sim 4 / 4
(hardening next), view 0 / 4, data 4 / 4 (hardening next)**. Bug ids next session: **sim 1890-1909, view
1910-1919, data 1920-1929** (1863-1869, 1871-1879, 1881-1889 unused). Open S1 / S2: none. The next session is
**1 / 8 of 2026-11-27** (or of the day it starts).

**Next session in one line (proposal):** sim **M6-SH3, the sim's hardening: the matches that stopped ending
(BUG-1855 traced first; BUG-1854 / 1860 with BUG-1731), BUG-1857, BUG-1852, Q7, BUG-1862; one pin re-measure**
(hardening, QA full) · view **M6-V31, the Whirlwind's buildings as kit-bashed tents, with BUG-1870's sentence**
(feature, QA standard) · data **D-H11, the data's hardening: BUG-1880, BUG-1840's last file, the readiness line**
(hardening, QA light; nothing measured, no byte under `game/data/`).

**The default loop on `main` is green.** Expect **11,017 passed / 0 failed / 24 skipped of 11,041 (23 m 19 s alone)** (the Producer's scratch clone `prod1847-accept`
of sim c13c5fcb + view ac83a4dd + data 458ac8cf). The 24 skips: last session's 20, plus
`M6S14AiPlayQaTests.Floor_RoomForTwoWorkers_*` (BUG-1857), `DR35ProsePinQaTests.Malazan_Bug0982Prose_*`
(BUG-1851), and D39's two (BUG-1880: `ATargetCountAtOnce_IsNotAUnitLimit`, `AHardEntryWithoutTheAgeResearch_*`);
check the list against `recheck1-skips.txt`'s form if the count differs. Scene loop **133 of 133, first pass**. The whole
Stress category ran on the sim's branch before the fix round (996 / 2 / 11 of 1,009); after it only the touched
classes ran (rule 72); it has **not** run on the three-way tree (rule 74). Skipped in Stress with ids:
`DH6ThresholdQaTests.Bug0982_KeepEighteen_*` and `AiOpeningPairsReportTests.Bug0982_*` (BUG-1851), the two D38
shipped-order rows (BUG-1853). Any red row is a finding.

**At the PLAN, first:** (1) the inbox (waiting, none blocking: "cairn ok", "teblor buildings ok", "buildings
ok", "whirlwind colours ok", the draw notice, D33 / D34 / D38's tables, a tent pack, "review: archive before
<date>"; tweaks come before roadmap work); (2) the default loop on `main`; (3) **the bug index after the
three-way merge:** rows for BUG-1870 and BUG-1880 came from the view and data branches; check that the
conflict kept both sides and that BUG-1710 / 1792 / 1830 / 1831 read "fixed" once; (4) **rule 82 for the sim's
brief:** if the save riders (BUG-1490, Request 60) are planned, grep `sim/Rts.Sim.Tests/Content/` and
`QA/Content/` for stored save fixtures and hand every row that loads one (this session's three:
`D32PreFixtureTests` x 2, `D31LandingQaTests.ASaveMadeBeforeD31_*`, which now relabel to
`SaveFormat.CurrentVersion` and should follow a format step without an edit: prove it on a scratch clone); and
name the view scenes that pin save bytes (rule 50: this session's eleven); (5) **rule 41 for the data brief:**
the sim's hardening moves AI play again, so the data track measures nothing this session; (6) rule 66 on every
"equals the base" criterion: can it hold on the final tree, or only at one commit (M6-S14 criterion 3)?

## Proposed briefs (for the next PLAN to complete)

### Sim: M6-SH3, the sim's hardening (hardening, QA full, one pin re-measure)
In this order, each its own commit, measured apart (rule 70):
- **A. BUG-1855, trace only first** (Pale Hills Normal v Normal seed 7 / 8: p0 hunts twice, p1 waves starved at
  69,240, not spent). A `Stress/` trace class like `VatharStallTraceTests`; the cure only if it is a few lines and
  clearly inside an existing rule, else a finding with the trace for the next AI task.
- **B. The draw clock's "can still train" (BUG-1860 / 1854, with BUG-1731 and the unit limit).** Producer's
  reading (docs/01 2026-11-27 (3) Q11): a seat can still train when it could pay for and place a unit now: the
  cheapest trainable unit's gold **and wood** from the bank plus what its own gatherers can still bring to a
  finished drop-off; population room for it; not at its unit limit; a paid item waiting at the cap does not
  count for ever. Read QA's trace in BUG-1860 first; if "what gatherers can still bring" needs a new rule, do the
  bank-and-room part and report. The draw rows are re-measured (`Draw/*`, `DrawStalledRowsReportTests`,
  `HuntStressTests`, `M6S14FourSeatQaStressTests` by name).
- **C. BUG-1857** (the floor's room test counts queued, not-started workers), **BUG-1852** (a Gather re-sent to
  a gone node: forget the node; BUG-1151's family), **Q7** (the kept rally point is forgotten when the hall
  point changes). Each changes AI play: the pin move, once, after C (rules 43 / 45 / 71 / 74 / 83).
- **D. Small:** BUG-1862, BUG-1153 (close or re-state), BUG-1149's test branch (narrow it as BUG-1861's
  allowance was), the Vathar 2v2 seed 6 difference between `M5H5HuntAfterDefeatQaTests` (no end by 72,000) and
  QA's four-seat table (ends): one look, reported.
- **E. Only if A-D fit the size budget; otherwise the session after:** BUG-1490, Request 60, BUG-1730 (one
  save-format step, 11 -> 12; rule 82's hand-over; the view re-checks its save scenes, rule 50).
Out: Request 56 G5-G8; any byte under `game/data/factions/**`; tuning data to make a row end (rule 48).

### View: M6-V31, the Whirlwind's buildings as kit-bashed tents (feature, QA standard)
A builder feature in `tools/art/` (box-and-prism tents with team-coloured cloth; the pack's market stalls for
the Supply Cache and its tower for the Lookout Tower; docs/01, the fourth 2026-11-26 row, (2)); if the owner's
"tent pack: <folder>" note is in the inbox, that pack instead. As many of the Whirlwind's types as fit, pictures
at zoom 40 and 20 in all eight colours for one type, the bench alone (rules 61 / 77). **First, a rider:** the
wrong sentence in docs/03 "Implementation (M6-VH10)" ("a paler wall moves every other colour's contrast the
right way or keeps it": Jade 31 -> 0, Ochre 43 -> 12, Bone 97 -> 67; BUG-1870 stays open for the owner's "cairn
ok"). A brief that touches `tools/import-assets.ps1` names `tools/art/test-import-exit.ps1` and
`tools/qa/import-exit-extra.ps1`. Out: sim code, save formats (the sim may step the format: read
`SaveFormat.CurrentVersion`, add no stored save fixture).

### Data: D-H11, the data's hardening (hardening, QA light; nothing measured)
(1) BUG-1880, both edges (the limit reader reads "at most / no more than N ... at once" as a limit only on a
unit-count sentence, not a target count; `CompositionProblems` on a list with no Age II research step); un-skip
QA's rows. (2) BUG-1840's last file: `QA/Content/D21OpeningsQaTests` reads every list a seat can play (the sim
re-pinned its cells this session; it is the data track's again). (3) `UnitLimitPagePin.RowProblems` wired into
`PagePinReadinessTests.Problems` (D39 Q2). (4) Stale sentences on the faction pages found on the way. Filter:
`Category!=Perf&Category!=Stress&Category!=Soak`. Out: `game/data/**`; any measurement of AI play (rule 41);
the five skipped rows (BUG-1851 / 1853: the re-measure, the task after the sim's hardening).

## Where things stand

- **`main` after this session = sim c13c5fcb + view ac83a4dd + data 458ac8cf** (plus this ACCEPT's studio and
  docs files). Scratch clone `prod1847-accept`: build `--no-incremental` 0 / 0, default loop **11,017 passed / 0 failed / 24 skipped of 11,041 in 23 m 19 s**, smoke
  **PASS**, scene loop **133 of 133, first pass**; bench 2026-11-27 07:12 to 07:16, `ModelShots --bench 30 --vsync off`, 1600 x 900, no test run beside it: 400 figures + 60 building models (`--bench-buildings 60 --bench-zoom 60`) **6.54 ms** while 90 %+ alive (worst 1 % 9.87; was 6.43), 400 capsules **0.82** twice (was 0.82), 1,000 figures **16.53** (was 16.57).
- **The data hash moved once: B7947DC602B1E11B -> 3BB926FD74795B9E** (`rules.json` `ai.minWorkers` 4). **The
  save format is 11** (format 10 plus 14 bytes at the end of each AI seat's `AIST` block and 13 at the end of
  `HUNT`; a save with no AI seat differs only in the number). Saves and recordings from before are refused.
  `ai.minWorkers` enters the hash only when it is not 1 (marker -14), so a test that rebuilds pre-M6-S14 data
  writes 1 (`Draw/DrawScenes.BeforeWorkerFloor`).
- **The AI after M6-S14** (docs/03 "Implementation (M6-S14)"): a hunt resumes from a razed find within
  `ai.huntMemory` (`Blackboard.WaveFromHunt` / `HuntFindTick` / `HuntFindPoint`); the worker floor
  (`BuildOrderExecutor.Retrain`, live + queued below `ai.minWorkers`, one Train a think, room needed above zero
  workers); `PlayerView.IsKnownClosed` / `KnownClosedWindow` read ghosts and remembered nodes; the blind
  re-scout counts its losses (`BlindScoutsLost`, cap `ai.scoutReplacements`) and may take a gatherer of the
  known kind (`AiPlayer.PickGatherer`); the rally point is kept while acceptable and within `ai.rallyReach` of
  the nominal point (`RallyKept`).
- **Pinned matches after M6-S14** (the full table and the rule 71 list are in docs/03 "Implementation
  (M6-S14)" F): `MatchCriterionTests` Vathar seed 1 22,840 (p1), Raraku **seed 2** 27,400, Pale Hills seed 1
  26,594, generated **seed 3** 23,806 (194 ticks under its bound), FFA Vathar seed 1 56,664, 2v2 Vathar seed 1
  42,301; `HalfWay` 11,420. Vathar four-seat seed 2: 47,412 (`FarExpandProbeQaStressTests` row un-skipped).
  Hard v Normal 32 / 4 / 6. **Not ending:** Pale Hills two-seat 7 / 8 (BUG-1855), Pale Hills four-seat FFA 2
  (BUG-1854) and 4 (BUG-1860); Raraku 3 / 4 / 6 stop on check (e) at 34,181 (BUG-1852, in the sweep's `Known`);
  generated seed 1 does not end inside 24,000.
- **The view after M6-VH10:** `Match.ResultFor` reads Draw for a drawn match with exactly two sides
  (`SidesThatBuilt`); `AbilityViews.CuesEnabled` is false in playback; `DrawNotice` wraps inside 800 px;
  `tools/import-assets.ps1` fails on a non-zero Godot exit or an empty `.tmp` left in `.godot/imported`; the
  Cairn's wall is `#938e83` / `#aca79a`. **133 scenes** (`ViewHardenVh10Test`, QA's `QaM6VH10DrawTest`,
  `QaM6VH10NoticeTest`).
- **The data track after D39:** shadow.md G4 built; `Content/UnitLimitText`, `UnitLimitPagePin`,
  `UnitLimitPinTests` (forged data only); `AiContentQaTests.Lists` feeds (a)-(d), (g).
- **Rule 68's handed list is now eight classes:** the seven of the 2026-11-16-2036 amendment plus
  `Content/AiOpeningPairsVerdictTests` (BUG-1858's ruling).
- **Requests:** 56: G1-G4 built, G5-G8 open; 60 open (the draw's seats; with the next save-format step).
- **STATE.md was trimmed at this ACCEPT** (1.43 MB -> about 0.86 MB): every section but For your review is
  rewritten short; For your review is whole (the rule forbids shortening it) and the owner is asked for "review:
  archive before <date>". The pre-trim file is `git show f40cb16e:studio/STATE.md`. **Keep the short form:** the
  Now table one line a field, queues as numbered lists, no "Current order (after ...)" history paragraphs.
- **Session length:** about 12 hours. QA's default loop took 2 h 25 m beside five to seven other test hosts
  (22 to 28 m alone); the whole Stress category 3 h 43 m.
- **Scratch folders left** (the owner's housekeeping list): in this session's scratchpad `prod1847-plan`,
  `prod1847-accept`, `prod1847-accept-files`, `m6s14*`, `m6vh10*` (QA could not remove `m6vh10-qa\clone`),
  `d39*`, `mut1`, `mut2`. The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` and a file named with a
  yellow-circle emoji are still untracked in the owner's main checkout.
- **Usage at the ACCEPT:** weekly 38 % (stop at 95 %).

## Standing rules (1-83)


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

**80 (new, 2026-11-26-0717):** a brief's rehearsal or "second shape" is loaded on a scratch clone at PLAN before
it is written into the QA focus (the sim's QA focus asked for a Teblor `standard` with entries for all three
difficulties, which the loader's ruling (a) refuses; QA chose a shape that loads, `buildOrder` = Easy's with
`normal` and `hard` entries: rule 78's family). And whatever a criterion asks for "in the report" (a rule 71
class list, the owner's review table) is also written where QA can read it: the page or docs section the task
owns, or a scratchpad file the brief names (D38's criteria 7 and 9 were "partly verified" and "unverifiable" by
QA because they lived only in the developer's returned text: rule 67's family).
**81 (new, 2026-11-26-0717):** the conductor refreshes `studio/.session.lock` at every phase change and at least
every two hours while a suite runs, and says so in its notes to the Producer (this session ran about ten hours
against the lock's three-hour window; three refreshes kept the hourly fallback from starting a second session).
A session that could not refresh its lock is logged as an incident.

**82 (new, 2026-11-26-1847):** a sim task that steps the save format hands over, by name, every test in another
track's folders that loads a stored save fixture (at PLAN: grep the content test folders for the fixture files and
`Simulation.Load`), as rule 68 does for pins; "the default loop green" is never written beside rows the format
step must turn red (M6-S14: three data-track rows, BUG-1859, S2, one fix round; the Producer's own miss). A
fixture row written since then relabels its bytes to `SaveFormat.CurrentVersion` before its data-hash proof.
**83 (new, 2026-11-26-1847):** the developer of a pin-move task runs every Stress class in the Producer's grep
file, not only the classes on the pin list, and the report names any it did not run (M6-S14:
`M5H5HuntAfterDefeatQaTests` was in the grep, not on the list and not run: BUG-1861, found by QA). And a
criterion of the form "equals the base" says at which commit it must hold when later items of the same task
move the thing compared (M6-S14 criterion 3: rule 66's family).


## Watch-outs (all tracks)
- **New this session (1847): the save format is 11 and `rules.json` carries `ai.minWorkers` 4.** A test that
  writes a format literal reads `SaveFormat.CurrentVersion`; a helper that forges an `ai` block tolerates the
  field (rule 58); "the data before M6-S14" is the shipped data with `ai.minWorkers` 1
  (`Draw/DrawScenes.BeforeWorkerFloor`), which hashes as the old data.
- **New this session: a seat below four workers trains workers before anything else it can afford.** A scene
  that stages an AI seat with fewer than four workers and counts its Trains, its bank or its queue sets
  `ai.minWorkers` 1 on a copy or expects the floor's Trains (three thinks, one each).
- **New this session: the AI's rally point is sticky.** It moves only when the nominal point is farther than
  `ai.rallyReach` from it or it stops being acceptable; a scene that expects the point to follow a new target
  at once moves the target farther than that.
- **New this session: a blind re-scout may take a gatherer**, at most `ai.scoutReplacements` lost in a match.
  Bounds on "scouts sent / lost" are `2 x scoutReplacements + 1` (the first, the replacements, the blind ones).
- **New this session: `M5H5HuntAfterDefeatQaTests` has a named allowance** (`HarmlessToAlliesWithAnEnemyInside`:
  a positional cast of an enemy-only ability with no friendly-fire damage and an enemy within its radius, onto an
  ally's building, is counted and printed, not red). It is private to that class; `TeamAiSoakStressTests` and
  `AiTeamTests` keep their own strict copies.
- **Five rows are skipped waiting for the data track's re-measure** (BUG-1851 x 3, BUG-1853 x 2). They stay
  skipped through the sim's hardening (it moves AI play again); the re-measure is the data task after it. Rule
  57's "two sessions" clock starts at the sim's hardening ACCEPT.
- **"Pinned matches" below is stale since this session:** the current list is under "Where things stand" and in
  docs/03 "Implementation (M6-S14)" F.
- **STATE.md's short form is deliberate** (see "Where things stand"); do not grow the Now table or the queues
  back into history.
- **New this session (0717): the Teblor `standard` ships `buildOrders` and `throughReserve`.** Its `buildOrder` is
  the Normal seat's list only; Easy and Hard play their entries. A test or report that means "what a seat plays"
  reads `AiPlayer.Steps` or `AiOpening.BuildOrderFor(difficulty)`; one that replaces a list calls
  `AiScenes.SetBuildOrder` (sim folders: enforced by `Ai/BuildOrderHelperSourceTests`) or removes `buildOrders`
  itself (content folders: BUG-1840 for what is left). A shipped opening with an entry for all three difficulties
  does not load (ruling (a)). The golden-header rows are four (`MaxAliveLoaderTests.TheHash_*` is new): a data
  brief that moves the hash regenerates the header and all four follow without an edit.
- **New this session: a unit type may carry `maxAlive`.** No shipped unit does. A test that forges it uses
  `Economy/UnitLimitTests.s_capped` / `s_pack` (they strip any shipped `maxAlive` first: rule 58). A test that
  writes store arrays directly calls `World.RecountDraw()` (it rebuilds the limit counts too). `TrainError` has a
  new last value, and `ui.json` `train` must hold a text for every value or the game does not boot.
- **New this session: eight Teblor building types are models.** A scene that reads a Teblor building's box sets
  `BuildingViews.UseModels = false` or uses a type that is still a box (Stables & Kennels, Breaker's Hall:
  `ViewHardenVh9Test` P2 uses the Stables). A model's drawn height is the fitted height x the data `scale`. The
  scene loop is **130 scenes**; under load `BenchTest`, `QaM27Test`, `QaH1Test` and `QaV3Test` fail on time and
  pass alone (`QaV3Test` took 899 s under load: run it with `-TimeoutSec 1200`).
- **`tools/import-assets.ps1`'s PASS line is not proof until BUG-1831 is fixed:** after an import check `git
  status` and the file hashes. A Godot `--import` hung 53 minutes under load in QA's run; alone it took 21 s.
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
M6-S14 accepted: the five AI cures are on `main`, every pin re-measured, the data hash and the save format moved
once each. Counter **4 / 4**: the next sim session is its hardening. Open sim debt that changes AI play or match
ends: BUG-1855, 1854, 1860, 1857, 1852, Q7 (docs/01 2026-11-27 (3)); that does not: BUG-1862, 1730, 1490,
Request 60, BUG-1153 (stale), BUG-1149's test branch.

### Next task candidates (after this session)
1. **M6-SH3** (proposal above): the matches that do not end, then BUG-1857 / 1852 / Q7, one pin re-measure.
2. The save riders (BUG-1490, Request 60, BUG-1730) if they did not fit M6-SH3: one format step, rules 50 / 82.
3. Request 56 G5-G8 (teleport first), test data only.
4. "Press the attack while the draw clock runs": write the design into docs/02 at a PLAN before any brief.

### Watch-outs
- A pin move two sessions running: keep rule 70's "measure each item apart" and rule 83's "run the whole grep".
- The draw clock's cure moves when matches end, not how seats play before that: say in the brief which rows it
  can move (the draw rows, the four-seat rows) and which it cannot (rule 76).
- `Stress/M6S14FourSeatQaStressTests` is Soak-tagged: it runs by name only (8 classes x 4 rows, prints
  `QA4ROW`); it is the before / after table for item B.

## View track

### Where we are
M6-VH10 accepted: the hardening is done, counter **0 / 4**. 15 of 21 unit types and 14 of 30 building types are
models; the Whirlwind has no building model yet. 133 scenes. Bench on a quiet window: see "Where things stand".

### Next task candidates (after this session)
1. **M6-V31**, the Whirlwind's tents (proposal above), with BUG-1870's docs sentence.
2. The Stone Cairn as stacked rocks (closes BUG-1870), the remaining box buildings as sources allow.
3. Mounted units and machines once the owner's packs are on disk; the limit count on a button with the Shadow
   roster; BUG-1790's three-sided half once Request 60 is on `main`.

### Watch-outs
- If the sim's session steps the save format, the view's eleven save scenes are re-run by the sim's developer
  on its branch (rule 50); the view adds no stored save fixture.
- A kit-bashed model is new geometry: the bench runs alone and reports the 400- and 1,000-figure rows with the
  capsule figure from the same window (rules 61 / 77).
- `BenchTest` and `QaV3Test` fail on wall clock under load and pass alone.

## Data track

### Where we are
D39 accepted: shadow.md is in step with the rules built (G1-G4), no game data changed. Counter **4 / 4**: the
next data session is its hardening. The Teblor plans and three Malazan balance rows are un-measured on the new
computer player (five rows skipped: BUG-1851, BUG-1853).

### Next task candidates (after this session)
1. **D-H11** (proposal above): static debt only.
2. **The re-measure, the session after the sim's hardening** (QA standard, "measure and report"): the three
   Teblor orders on the shipped file (BUG-1853; re-point the D38 rows at the new table), the BUG-0982 rule
   against `cadre_rush` (BUG-1851: either the rule's pick or the opening's wave size changes; a data change
   moves the hash, so not in a session where the sim moves it), BUG-1856, malazan.md's Age II paragraph,
   teblor.md; the hour-long Teblor report classes.
3. The Shadow roster once G5-G8 are on `main`.

### Watch-outs
- Owner review tweaks from the inbox come first, always; none is waiting as of this ACCEPT.
- A content test that loads a stored save relabels it to `SaveFormat.CurrentVersion` (rule 82).
