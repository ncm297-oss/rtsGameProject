# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-25-1437** (session 2 / 8 of 2026-11-25, started 14:37,
ended past midnight: about 9.5 h; the sim builder 5 h 11 m, sim QA 3 h 53 m). Sim **M6-S13** ACCEPT (feature, 0 fix
rounds, QA full PASS_WITH_ISSUES), view **M6-V28** ACCEPT (feature, 0, QA standard PASS_WITH_ISSUES), data **D36**
ACCEPT (feature, 0, QA standard PASS_WITH_ISSUES; nothing shipped). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10,
M7 3 / 4, M8 0 / 3.** Counters: **sim 1 / 4, view 2 / 4, data 1 / 4**. Bug ids next session: **sim 1770-1789, view
1790-1799, data 1800-1809**. Open S1 / S2: none. The next session is **1 / 8 of 2026-11-26**.

**Next session in one line (proposal):** sim **M7-9, Request 59: a build order per difficulty inside an opening**
(feature, QA full; schema + executor on test data, **no hash move, no pin move**: left out = today) · view **M6-V29,
the draw banner and the countdown notice** (feature, QA standard) · data **D37, the Normal order searched alone, the
Easy and Hard orders re-measured on the draw rule's `main`, BUG-1760 / 1761** (feature, QA standard; **measured and
reported, no game data byte**: the three orders ship in D38, the session after Request 59 is on `main`).

**The default loop on `main` is green.** Expect **0 failed / 21 skipped** (10,447 passed / 0 failed / 21 skipped of 10,468 in 22 m 12 s on the Producer's scratch
clone `prod1437-accept` of sim f4e0553 + view 4aa99ca + data 995eba7). The skips: the standing ones, minus the two
BUG-1720 rows (un-skipped), plus QA's BUG-1730 row and BUG-1760 row. Scene loop **126 of 126** (QA, the view branch;
124 of 124 on the sim branch). Any red row is a finding. Stress ran as a whole category on the sim branch this
session (979 / 0 / 9 of 988 in 2 h 51 m); Perf alone 214 / 0 / 3.

**At the PLAN, first:** (1) the inbox (waiting, none blocking: D33's and D34's tables, "buildings ok", "whirlwind
colours ok", and now the draw rule and the four buildings; tweaks come before roadmap work); (2) the default loop on
`main`; (3) the bug index: `studio/bugs/README.md` must hold one row each for BUG-1730, 1731 (open, S4), BUG-1750
(fixed), BUG-1760 (open, S3), BUG-1761 (open, S4), with BUG-1720 / 1721 "fixed (D36)" and BUG-1536 "closed": three
branches edited the file's last lines, so check for a lost or doubled row after the conductor's merge; (4)
`CLAUDE.md`'s "Current milestone" line is still stale (on "Waiting on you"); (5) **the data hash is
3D93D45345A3B4EA** since the draw rule (`rules.json` `draw`); content classes that rebuild D33's / D34's data take
the block back out through `Draw.DrawScenes.TakeOutDraw` / `Off` / `TeblorOpeningBeforeDraw` (sim-owned helpers the
data track may call, not edit); (6) Request 59's shape is the PLAN's to fix (sketch below), with rule 54's
rehearsal on a scratch clone if it adds a default every test reads; (7) M6-V29 needs a text for the countdown: check
at the PLAN which `ui.json` key the banner's "Draw" uses and who may add a key (M6-V27 added `orders.queue_full`
from the view track; if that file is in the data hash, the text is a hash move and must be named as the session's
one).

## Proposed briefs (for the next PLAN; nothing here is planned yet)

### Sim: M7-9, Request 59, a build order per difficulty (feature, QA full)
Why: docs/01 2026-11-26 ACCEPT (3). Two searches (28 full-row Teblor orders) found no single order that meets the
bar on Easy, Normal and Hard; one meets every Easy figure (K9KC10S), one every Hard figure (QRD1).
Sketch (the PLAN decides the exact names): an opening in `ai.json` may carry, beside `buildOrder`, an optional
per-difficulty list (for example `"buildOrders": { "easy": [...], "hard": [...] }`); a seat of a difficulty with an
entry plays that list from its first think, any other plays `buildOrder`; each list is validated exactly as
`buildOrder` is (ids, the two switches, the expand step); left out = today's behaviour, today's content hash and
today's saves (hashed only when present); a JSON `null` policy row; the executor's saved step state is per seat, so
check a save taken mid-order loads on the same list. Schema, loader, `DataValidationTests`, the executor, test data
only plus the minimum entries the tests need (**no byte in `game/data/factions/**`**: the data track ships the
content in D38). "Off is identical": every pinned AI match unchanged (no pin class may move; rules 43 / 71 / 74).
docs/02 "AI opponent", docs/03 "Implementation (M7-9)" with a "For the data track" row. Out: any other AI-play
change (BUG-1661, 1660, 1555, 1554 / 1556, 1558 stay the session after), BUG-1730, BUG-1731.

### View: M6-V29, the draw on screen (feature, QA standard)
The sim's rule is on `main`: `World.MatchDrawn` (read it **before** looking for a winner: a draw is final even if a
side falls later, docs/01 2026-11-26 (1)), `World.DrawTicksLeft` (-1 = not running; else the ticks to the draw),
`World.DrawWaitTicks`. Today a drawn match shows "Victory" to a seat still standing. Build: the banner and the
stats screen read "Draw" for every seat of a drawn match (replays and loaded saves too); a countdown notice while
the clock runs (for example "No side can train: draw in 4:59", shown from the first spent tick or from the last
N seconds, the developer proposes, pictures at zoom 40); it disappears the tick the clock restarts. Scenes that play
to a result (rule 50's list) checked. Text from data. Out: any sim change; an alert sound unless it is one line.

### Data: D37, the Teblor orders per difficulty, measured (QA standard; no byte under `game/data/`)
(0) The owner's answers, if any. (1) **Normal alone:** a bounded search (rule 78's levers named again; at most eight
full rows of 64 Normal seats) for an order that meets every Normal figure of the bar (RSC misses only the
Anointing, 13 of 17; QSH and QSR are QA's nearest); (2) **re-measure K9KC10S on Easy and QRD1 on Hard** on this
`main` (the draw rule moved the hash; a 15-minute match is never spent, so the figures must be D36's exactly: if not,
stop and say so); (3) the three orders as fixtures (`Content/Fixtures/teblor_standard_d37_<difficulty>.json`, each
loading as a plain opening on a copy of the shipped data) and teblor.md's table; (4) BUG-1760 (hyphenated
"first-shot" reads as first-shot words; QA's row un-skipped) and BUG-1761 ("below" -> "above"); the Producer's
answers to D36's Questions 1-3 written into teblor.md (docs/01 2026-11-26 (3) (4); rule 67). Files: teblor.md, new
`D37*` classes, the fixtures, `UnitHitBonusText*`, `D36Bug1720QaTests`. The sim builds Request 59 beside it and
touches no content class. D38 (the session after): ship the three orders through Request 59's schema, one hash
move, the owner's review table.

## Where things stand

- **`main` after this session = sim f4e0553 + view 4aa99ca + data 995eba7** (plus this ACCEPT's studio and docs
  files). Scratch clone `prod1437-accept` (code merged clean; only `studio/bugs/README.md` conflicted): build
  `--no-incremental` 0 / 0, default loop **10,447 passed / 0 failed / 21 skipped of 10,468 in 22 m 12 s**, smoke **PASS**, scene loop **126 of 126, first pass**. The Producer also
  played `match --map pale_hills --players 4 --seed 6` on the merged tree: `result draw at tick 51216 (42:40)`.
- **The data hash moved: A732F387C2BB7E4D -> 3D93D45345A3B4EA** (`rules.json` `"draw": { "afterSeconds": 300 }`).
  Saves and recordings from before this session no longer load. Save format 10 kept; a `DRAW` part sits inside
  `WRLD` after the result flag only when the clock runs or the match is drawn.
- **The draw rule (M6-S13):** `sim/Rts.Sim/World.Draw.cs` (phase 11c, after the defeat check): `CanStillTrain`,
  `MatchSpent`, `DrawClock`, `MatchDrawn`, `DrawTicksLeft`, `DrawWaitTicks`; counters in
  `Economy/PlayerLedger.Draw.cs` (`LiveWorkers`, `QueuedUnits`, `CarriedGold`) and `ResourceStore.GoldLeft`, derived,
  not hashed or saved, recounted at a load; **a test that writes store arrays directly must call
  `World.RecountDraw()`**. Hash bit 37 of every player's word plus two words, only when the state is set ("zero is
  free": QA compared every tick of three never-spent matches with the base). Test helpers: `Draw/DrawScenes`
  (`Off` = shipped data without the block, `TakeOutDraw(dir)`, `TeblorOpeningBeforeDraw(opening)`), QA's
  `QA/DrawQaOracle` (a scan-based second opinion).
- **Stalled rows now:** Pale Hills four-seat seed 1 draw 61,779; seed 2 draw 58,378 (was p1 at 59,951); seed 3 never
  spent, p1 at 40,674; seed 4 draw 63,124; seed 6 draw 51,216; seed 7 draw 80,167. Still skipped: the Vathar
  four-seat seed 2 row (BUG-1621 / 1610, never spent), `ALoneIdleWorker_*` (BUG-1116), the Pale Hills 2v2 seed 2 row
  (BUG-1149). Hard v Normal tally unchanged, printed as hard / normal / draw / none: **34 / 5 / 0 / 3** (one Pale
  Hills match is spent from 71,281 and not drawn by the 72,000 bound: a longer bound would turn one "none" into a
  draw). No M5 criterion row is ever spent.
- **Known limits of the rule (docs/01 2026-11-26 (1)):** BUG-1731 (a paid queue item at the pop cap keeps a seat
  "able to train" for ever); a draw can fall shortly before a slow win (seed 2 above); BUG-1730 (S4: a forged `DRAW`
  part with a running clock on a won match loads; one skipped QA row).
- **Building models (M6-V28):** 6 of 30 types (Keep, Barracks, Billet, Depot, Crossbow Range, Watchtower), all
  Malazan. `BuildingViews.ShotHeights` / `ModelShotShare` 0.85; `ProjectileViews.BuildingShotHeights`; box towers
  shoot from 1.2 m. **126 scenes** (`MalazanBuildingsTest`, QA's `QaM6V28Test`). Art binaries 39,864,445 bytes.
  Quiet bench: 400 figures + 60 models 6.34 ms (boxes 6.34); the bench's 60 buildings are fought over from frame 1
  (the "3 of 60 gone" is the bench's own fight, not the view). Left as boxes for the Malazan: Wickan Corral, Armory,
  Cadre Tower, Engineers' Yard (no plain match in the pack).
- **The Teblor `standard` (unchanged since D34; BUG-1612 open):** shipped M 5 figures missed / 50 seats short; QRD1
  4 / 16 (every Hard figure met); K9KC10S 7 / 47 (every Easy figure met); RSC 5 / 23 (Normal: only the Anointing
  short, 13 of 17); QA's QSH 5 / 18, QSR 6 / 20, QSRS 6 / 24. Orders live in
  `Content/D36TeblorOrderSearchReportTests.AllVariants` (58) and `QA/Content/D36TeblorOrderQaStressTests`.
  `noReserve` on `armor_1` is inert before 15:00: leave it out.
- **Description checks (D36):** `UnitHitBonusText`: "other ... within" is pack words only outside an on-death
  sentence ("dies", "death", "dying"); the first-shot role needs first-shot words, or the unit's field with the
  field's own number. Open: BUG-1760 (hyphenated "first-shot").
- **Requests:** 55, 58 done; 56: G1-G3 built, G4-G8 open; **59 new** (a build order per difficulty).
- **Queues after the proposals above.** Sim: the AI-play session (BUG-1661 first and alone, then BUG-1660
  `ai.minWorkers` from 4, 1555, 1554 / 1556 with 1490, 1558; one pin move; un-skip the Vathar four-seat row with the
  cure; candidate: the computer player presses its attack while the draw clock runs), then Request 56 G4-G8.
  View: more building models (Whirlwind, Teblor; the four Malazan left need another source), the mounted units and
  machines once packs are on disk. Data: D38 ships the three Teblor orders; then the Shadow roster as its rules
  land; owner tweaks first whenever the inbox has them.
- **Session length:** 9.5 h, most of it the sim's two long suites (the whole Stress category twice: developer and
  QA). For a task with no tick change (Request 59 is "off is identical"), rule 53 applies: no whole-category Stress
  run unless a pin class is touched.
- **Scratch clones left in the session's scratchpad** (harmless; the owner's housekeeping list): `prod1437-plan`,
  `prod1437-accept` (the Producer's), `m6s13-*`, `m6v28-*`, `d36-*`. The stray empty
  `C:\Dev\rtsGameProject\ModelShots.cs` and a file named with a yellow-circle emoji are untracked in the owner's
  main checkout.
- **Usage at the ACCEPT:** weekly 20 % / 21 % at 20:15 (stop at 95 %).

## Standing rules (1-78)


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


## Watch-outs (all tracks)
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
M6-S13 (the owner's draw rule) on `main`. Counter **1 / 4.** Closed: BUG-1536 (by measurement). New debt: BUG-1730
(S4; a forged `DRAW` part on a won match loads), BUG-1731 (S4; a paid queue item at the pop cap). Carried:
BUG-1700 (S4), BUG-1660, 1661 (S3; behind BUG-1621 / BUG-1610, whose Stress row stays skipped), 1555, 1554 / 1556,
1557 (S3), 1558 (S4), 1463, 1537, 1490, 1434, 1433, 1161, 1149, 1152, 1150, 1068 (narrowed: the stalls end in a
draw), 1116 (the lone-idle-worker row), 1010. The answers to M6-S13's Questions 1-5 are in docs/01 (2026-11-26,
the ACCEPT row (1)): keep all five.

### Next task candidates
1. **M7-9, Request 59: a build order per difficulty** ("Proposed briefs" above; feature, QA full, no hash move, no
   pin move; 20 bug ids 1770-1789). Write M6-S13's answers into docs/03 "Implementation (M6-S13)" (rule 67).
2. **The AI-play session (feature, QA full, one pin move; the data track measures no opening beside it):**
   BUG-1661, then BUG-1660, BUG-1555, BUG-1554 / 1556 with BUG-1490 (a save format change: rule 50), BUG-1558. Each
   cure measured apart (rule 70). BUG-1700's comment with it. Candidate to design there: a seat with an army presses
   its attack while the draw clock runs (it would have won Pale Hills four-seat seed 2).
3. **M8-4:** Request 56 G4 (the Hounds' 7-alive limit) and the next of G5-G8, on test data only.
4. The sim's hardening (at 4 / 4): BUG-1730, BUG-1731's decision, older S3 / S4.
5. Requests 54, 53 part two. A movement task for BUG-1463 only with a design first.

---

## View track

### Where we are
M6-V28 on `main`. Counter **2 / 4.** 126 scenes. BUG-1750 (S3, a test row) found and fixed in the task. Debt:
BUG-1710 (S4). The Producer's answers to M6-V28's Questions 1-5 are in docs/01 (2026-11-26, the ACCEPT row (2)):
accept the four picks, the roof as the team part, the low Depot, the 0.85 shot share (box towers stay 1.2 m); Iron's
weak roof is the owner's.

### Next task candidates
1. **M6-V29, the draw on screen** ("Proposed briefs" above; feature, QA standard). Write M6-V28's answers into
   docs/03 "Implementation (M6-V28)" (rule 67).
2. More building models: the Whirlwind's or the Teblor's from the same pack (the data `scale` on a model's height
   when a Teblor building follows); the four Malazan left need another source.
3. The two cavalry, the Destrier Rider, the War-dog, the Catapult and the Ram once packs are on disk (owner).
4. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...", "priest veil: ...",
   "buildings ok", "roof: also the tower tops", "depot: taller", "box towers: shoot from the top", "whirlwind
   colours ok" / "priest: white robe", "outline hidden units", "order notice: ...", "announce every dropped order").

---

## Data track

### Where we are
D36 on `main`; no game data changed (the hash moved by the sim's `rules.json` only). Counter **1 / 4.** Closed:
BUG-1720, BUG-1721. New debt: BUG-1760 (S3; hyphenated "first-shot", one QA row skipped), BUG-1761 (S4; "below" for
a section above). Open: BUG-1612 (the unit mix; the cure is Request 59 + D38), BUG-1421 item 1, BUG-1611 (S4). The
Producer's answers to D36's Questions 1-3 are in docs/01 (2026-11-26, the ACCEPT row (3), (4)): an opening per
difficulty; keep "the field's own number"; drop `noReserve` on `armor_1` from searches.

### Next task candidates
1. **D37** ("Proposed briefs" above; QA standard; no byte under `game/data/`).
2. **D38:** ship the three Teblor orders through Request 59's schema once it is on `main` (one hash move; the pin
   list of D-H10's brief; the owner's review table with the Easy / Normal / Hard rows before and after).
3. The hour-long Teblor Stress report classes D34 did not re-run, malazan.md's "Age II against the Whirlwind"
   paragraph.
4. The Shadow roster as Request 56's schemas reach `main` (G1-G3 are there; G4-G8 are not).
