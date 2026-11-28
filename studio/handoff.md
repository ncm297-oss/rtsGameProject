# Handoff: brief for the next session

**Amended at the PLAN of session 2026-11-27-1857 (the session plan below replaces the proposed briefs: sim
M6-S15 the computer-player task with the draw clause, view M6-V32, data STOP; the teleport and the data's
re-measure move to the next session).** Written by the Producer at the ACCEPT of session **2026-11-27-0817** (session 1 / 8 of 2026-11-27; about nine
hours). Sim **M6-SH3** ACCEPT (hardening, 0 fix rounds, QA full PASS_WITH_ISSUES), view **M6-V31** ACCEPT
(feature, 0, QA standard PASS_WITH_ISSUES), data **D-H11** ACCEPT (hardening, 0, QA light PASS_WITH_ISSUES).
**M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters: **sim 0 / 4, view 1 / 4, data
0 / 4**. Bug ids next session: **sim 1930-1949, view 1950-1959, data 1960-1969** (1895-1909, 1913-1919,
1922-1929 unused). Open S1 / S2: none. The next session is **2 / 8 of 2026-11-27** (or 1 / 8 of the day it starts).

**Next session in one line (proposal):** sim **M8-5, the draw rule's drop-off clause (BUG-1893) first, then the
Shadow's teleport (Request 56 G5) on test data** (feature, QA full; moves no AI play) · view **M6-V32, the
last six box buildings and the Stone Cairn as kit models, with BUG-1911 / BUG-1912** (feature, QA standard) ·
data **D40, the re-measure: the Teblor plans and the Malazan "keep 18" rule on today's computer player**
(feature, QA standard, "measure and report"; BUG-1851 / 1853 / 1856, BUG-1840's last file, BUG-1920 / 1921).
The session after: the sim's computer-player task (BUG-1890 / 1891 / 1855; a pin move, the data track static).

**The default loop on `main` is green.** Expect **11,103 passed / 0 failed / 21 skipped of 11,124 in 24 m 5 s alone** (the Producer's scratch clone `prod0817-accept` of
sim f4c6ba3a + view 26c94844 + data 139b99bb). The 21 skips: last session's 24, less
`M6S14AiPlayQaTests.Floor_RoomForTwoWorkers_*` (BUG-1857 fixed), `DrawRuleQaTests.AForgedClock_OnAWonMatch_IsRefused`
(BUG-1730 fixed) and D39's two (BUG-1880 fixed), plus `DH11LimitReaderQaTests.ATargetCountFollowedByAnAliveWord_ReadsNone`
(BUG-1920). Scene loop **135 of 135, first pass, alone (29 m)** (135 scenes: `WhirlwindBuildingsTest`, QA's `QaM6V31Test` are new). The
whole Stress category ran on the sim's branch with QA's files (the default loop with Stress: 12,071 / 2 / 34 of
12,107, the two red rows QA's own serial-collection miss, moved and re-run green); it has **not** run on the
three-way tree (rule 74; the view and data branches change no sim code and no Stress class). Perf alone on the
sim's branch: 216 / 0 / 3. Skipped in Stress with ids: as before (BUG-1851 x 2, BUG-1853 x 2, the others in the
index). Any red row is a finding.

**At the PLAN, first:** (1) the inbox (waiting, none blocking: "whirlwind buildings ok", "cairn ok", "teblor
buildings ok", "buildings ok", "whirlwind colours ok", the draw notice, D33 / D34 / D38's tables, "review:
archive before <date>"; tweaks come before roadmap work); (2) the default loop on `main`; (3) **the bug index
after the three-way merge:** rows BUG-1890 to 1894 (sim), 1910 to 1912 (view), 1920 / 1921 (data) each once,
BUG-1880 "fixed" once; (4) **rule 84 for the sim's brief** (below): the clause for BUG-1893 is written against
the shipped drop-off prices (every small drop-off 0 gold / 75 wood; the halls 275 / 275), and its QA focus asks
for the state "bank pays for a drop-off but for no unit, no worker" (still spent) and "pays, worker dead the
tick before"; (5) **rule 41 for the data brief:** M8-5 must move no AI play (the clause only makes fewer states
spent; teleport is test data), so the data track may measure; if the sim's brief grows an AI item, the data
track goes static instead; (6) the data re-measure runs hour-long report classes: tell QA which run by name and
that the sim's QA full runs beside it (session length).

## Current session plan (2026-11-27-1857, session 2 / 8; written at the PLAN)

**Sim GO, view GO, data STOP.** Base `1df37150`. Bug ids: **sim 1930-1949** (a pin move: rule 71), **view
1950-1959**, data 1960-1969 unused. **The order of the sim's next two tasks is swapped** (docs/01, the
2026-11-27-1857 row, (1)): the computer-player task runs now and carries the draw rule's drop-off clause; the
Shadow's teleport is the sim task after it. So the data track's re-measure (D40) is made once, next session, on
the final computer player; this session it cannot measure (rule 41) and has no other unblocked work. No track
moves the data hash or the save format this session. The Producer's list of test files that seat an AI (rules
74 / 83), 308 files, 58 under `Stress/`:
`C:\Users\Nick\AppData\Local\Temp\claude\162c01fae57881b0\d9c61ef4-693f-4440-b4d4-9132119945e3\scratchpad\prod1857-plan\prod1857-ai-match-grep.txt`
(remake it with `grep -rlE "MapMatch|AiDifficulty|AddAi|AiScenes|AiPlayer" sim/Rts.Sim.Tests --include=*.cs`).

### Sim track: M6-S15, the computer player spends what it has and finishes its hunt; the draw rule's drop-off clause (feature, QA full)

**Goal.** Three AI-against-AI test matches never end: Pale Hills four-seat free-for-all seeds 2 and 4, and
Pale Hills two-seat seed 7 (seed 8 is the same match). M6-SH3 showed the draw rule is right and the computer
player is at fault: it sits for ever on money that pays for a unit, and its hunt starts over after every raid
on its base. Cure both, add the draw rule's decided missing clause (BUG-1893) with the computer player's side
of it, and re-measure every pinned AI match once. Decisions: docs/01, the 2026-11-27-1857 row, (2)-(5).

**Scope, in this order, each item its own commit, each measured apart from the others (rule 70):**

- **A. The draw rule's drop-off clause (BUG-1893).** In `World.CanStillTrain` (`sim/Rts.Sim/World.Draw.cs`),
  clause (b): a seat with a live worker and **no** finished drop-off of its own also counts carried and map
  resources when, for some drop-off building type of its faction (`BuildingDef.DropOff`; every drop-off type
  for a seat with no faction, test data), the bank pays that building's gold and wood, **and** the bank less
  that price, plus what its units carry and what is left on the map, pays a unit type not at its `maxAlive`
  in gold and in wood. A drop-off's `requires` is not asked (no shipped drop-off has any: the three halls cost
  275 / 275, the three small ones 0 gold / 75 wood). Nothing else of the rule changes: a site is still no
  drop-off, the refund of a waiting item or of a site is still not counted, population room is still not
  asked. Counters only (the drop-off types per seat are listed once at construction, as `_drawUnits` is);
  nothing allocated per tick, nothing saved or hashed. docs/02 "Game flow" 4 gains the clause in plain words.
- **A2. The computer player's side of the clause.** First say what an AI seat does today with a live worker,
  no finished drop-off, no drop-off site and a bank that pays for its faction's small drop-off but not for the
  hall rebuild (`BuildOrderExecutor`'s rebuild, `HallReserved`). If it never builds the small drop-off: it now
  does. Rule: a seat with a live worker, no finished drop-off and none being built, which cannot pay for the
  hall rebuild now, builds the cheapest drop-off type of its faction it can pay for (gold, then wood, then
  data order), placed by the executor's existing placement near the nearest node it knows and may gather
  (else round the rebuild centre); the hall rebuild's reserve does not hold this one build. With a bank that
  pays for the hall it rebuilds the hall as today. If this takes more than about 60 lines or a new saved
  field, stop the item, land A alone and report (A is the rule a human player sees; an AI seat that then
  holds a match open is a finding with a bug id).
- **B. BUG-1891, trace first.** Pale Hills four-seat FFA seed 4 from 48,000 to 72,000
  (`Stress/DrawReachPreTraceTests` is the start): why seat 0 (26 workers, four drop-offs) mines none of the
  726 gold left, and why seat 3's workers never deliver the 30 gold they carry: is the node known, may the
  seat gather it (`KnowsNode`, `ai.gatherReach`, the defended-point filter, a dropped mark), what state are
  the loaded workers in. Write the answer in docs/03. Cure it only if the cure is about 30 lines or fewer
  inside an existing rule; else leave the trace and the finding.
- **C. BUG-1890, "spends what it has".** `ProductionManager` (and whatever it needs of `AiPlayer.Starved`'s
  parts (i) and (ii): `ShortForPick`, `CanEverPay`). When production's pick cannot be paid for now **and
  income can never close the gap for it** (part (ii) as `Starved` reads it; **not** part (iii), the army
  size, and say whether a defended base should switch it off: my default is that it does not), production
  trains instead, one Train a think as today:
  1. the most under-weighted unit of the current composition (the same share order, the same producer test,
     the unit limit respected) that the seat can pay for now; with none,
  2. the first unit type of its faction in data order, the worker included, that it can pay for now and that
     a finished building of its own with an empty queue may train (population room and the unit limit as the
     train check has them).
  A reserve (a build-order step's or the hall rebuild's) holds these buys only while the seat could ever pay
  the reserved cost; a reserve it can never fill does not. A seat that is short for its pick but knows a node
  it may gather of the short kind waits exactly as today. Nothing new is saved (think scratch only).
  Why part 2 (rule 84, checked on the shipped prices): a seat can hold the price of a worker and of no unit
  of its mix (Laborer 50 gold against Heavy Infantry 54; Camp Follower 40 against Raider 48; Gatherer 75
  against Warrior 120), the draw rule rightly says it can still train, and without part 2 it never does.
- **D. BUG-1855, "finishes its hunt".** `MilitaryManager` (`BeginHunt`, `StartSweep`, `NextSweep`, the
  defense's interruption). Today a hunt cut short by a defense begins again as a new hunt: marks cleared, and
  from the match's second hunt on the free starts first, so the same far first leg is re-sent after every
  raid (the trace: 14 times to sweep point 200). Rule: a hunt cut short by a defense is **resumed** when the
  next hunt begins within `ai.huntMemory` of the defense ending: the visited marks and the pass are kept, and
  it sweeps nearest-first from where the army stands, the free starts not first. A fresh enemy sighting still
  comes first; a hunt that begins later than that forgets as today. **Use only what the save already holds**
  (`HuntVisited`, `HuntPass`, `HuntFindTick`, `HuntFindPoint`, or think scratch rebuilt on load); if the rule
  needs a new saved field, stop the item, write the design into docs/03 and report (no save-format step this
  session). Measure on `Stress/PaleHillsStallTraceTests`.
  **Cut rule:** if D does not fit, drop it whole before the pin move and say so in the first lines of the
  report; A-C and the pin move then stand alone.
- **E. The pin move, once, after D** (rules 43 / 45 / 71 / 74 / 83): criteria 10-12.
- **F. Small, no behaviour:** BUG-1892 (QA's four-seat table calls its 0 / 1 / 0 / 1 rows "2v2": name them
  1v1v2, or add true 2v2 rows with teams 1 / 2 / 1 / 2 beside them; no row deleted).

**Out of scope:** any save-format step (BUG-1490 and Request 60 wait; `SaveFormat.CurrentVersion` stays 11);
every byte under `game/data/**` (no new `ai` knob: if an item needs a number, use one the data already has or
stop the item and report); the Shadow's teleport and Request 56; movement and pathfinding; "press the attack
while the draw clock runs"; BUG-1894; `game/**` except the hand-over in criterion 13; the data track's files
except the hand-over below.

**Acceptance criteria.**
1. A: unit tests in `Draw/`. Red on the base: the BUG-1893 scene on shipped data (a Malazan seat with a
   Laborer, a finished unit-producing building, no drop-off, a gold mine on the map, bank 35 gold / 100 wood)
   is **not** spent; the same for a Whirlwind and a Teblor seat with their small drop-off's price; a forged
   seat whose only drop-off type is hall-priced and whose bank pays it. Guards (green on the base, say so):
   the same scene with 74 wood is spent; with the worker dead it is spent from that tick; with exactly the
   drop-off's price, no wood left on the map and no unit free of wood (forged) it is spent; a paid drop-off
   site with an empty bank is spent until the tick it is finished.
2. A: the property rows. Over the draw fuzz's states: "M6-SH3's rule says can" implies "the new rule says
   can"; "the new rule says can and M6-SH3's says cannot" implies the clause's own condition, scanned
   test-side (`Draw/DrawReachScan` keeps M6-SH3's scan under its own name); the sim's counter answer equals
   the scan on every state. M6-SH3's row "M6-S13-spent implies spent" is re-stated to what now holds, with the
   reason written beside it; no row is deleted.
3. A: `QA/M6Sh3DrawQaTests.Bug1893_*` is re-pinned to the new rule (the clock does not run), a save in a
   state that uses the clause loads to the same hash and answer, and `Draw/DrawCostTests` holds at 0 bytes on
   the new path; the check's cost at 8 seats / 2,000 units is reported.
4. A2: what the AI does today is written in docs/03. If the rule is built: a test, red on the base, for each
   shipped faction: an AI seat with a worker, no drop-off, no hall, a known gold node and a bank that pays
   the small drop-off but not the hall sends that Build and its worker delivers there; a guard that a bank
   which pays for the hall rebuilds the hall as today. If it is not built: the report says so in its first
   lines and the four-seat table of criterion 9 shows what A alone does.
5. B: docs/03 "Implementation (M6-S15)" says in plain words why the 726 gold is not mined and the 30 gold not
   delivered. Measure and report; a cure is optional under the 30-line rule, with a test red on the base.
6. C: tests in `Ai/`, red on the base: a staged seat whose pick can never be paid for and whose bank pays for
   another composition unit sends that Train within two thinks; one whose bank pays for no composition unit
   but for its worker trains the worker; one held only by a reserve it can never fill trains. Guards: a seat
   that knows a node it may gather of the short kind waits as today; a reserve it can fill still holds. A
   determinism twin and a save between two such thinks (same hash 1,000 ticks on).
7. D (if not cut): tests in `Ai/`, red on the base: a staged hunt cut by a defense and begun again within
   `ai.huntMemory` keeps its visited marks and its next target is the nearest unvisited point from the army,
   not the leg it was on before; begun again later than that it forgets as today (guard). A save between the
   cut and the resume loads to the same hash and plays the same 1,000 ticks. `M6-S14`'s `Ai/HuntResumeTests`
   stay green unchanged.
8. No new saved field: `SaveFormat.CurrentVersion` is 11 and a save made on `main` loads on the branch.
9. **Measure and report** (rule 48), a table in docs/03 with a column for the base and one after each of A,
   A2, C and D: Pale Hills four-seat FFA seeds 1-8 and the 0 / 1 / 0 / 1 rows
   (`Stress/M6S14FourSeatQaStressTests`, by name: it is Soak-tagged), `Stress/DrawStalledRowsReportTests`,
   Pale Hills, Raraku and Vathar Crossing two-seat seeds 1-8 (`Stress/MatchRowsM6S10Report`): end tick and
   result. That seeds 2 and 4 (four seats) and seed 7 (two seats) end is the hope, not a pass / fail line. A
   row that ended on the base and no longer ends is a finding with a bug id, and the item that caused it is
   named.
10. The pin list is the one in docs/03 "Implementation (M6-SH3)" ("The pin list"), copied into
    "Implementation (M6-S15)" one class a line with "re-measured: yes / moved to ... / unchanged" beside
    each, the handed content classes included, old values kept in "was" comments, and docs/03's M5-5 table
    re-printed.
11. Every Stress class in the Producer's list file (above) is run on the final tree and the report names any
    not run. A pinned criterion row that stops ending moves to the nearest seed 1-8 that ends inside its
    bound, with a bug id; no bound is raised. If a map has no ending seed, revert the item that caused it
    (they are measured apart) and report.
12. Which rows can move, said in the report per item (rule 43). My expectation, to be checked: A moves only a
    match that is drawn or comes near one; A2 only a seat that has lost every drop-off; C every match in
    which a seat's pick becomes unpayable for good (the four-seat rows, the Hard v Normal tally's long rows,
    the FFA and 2v2 criterion rows at 56,664 and 42,301); D every match with a hunt cut by a defense
    (`HuntStressTests`, `AiDeterminismTests`' hunt row, Pale Hills rows); the four two-seat criterion rows
    that end before 28,000 should not move, and if one does the report says which item moved it.
13. The view's scene loop (`game/tests`, 135 scenes) is run once on the final branch. A scene red only
    because a staged AI seat now buys or builds what this task makes it buy or build is handed to you for a
    staging-only edit, named in the report, **unless** it is `BuildingModelTest`, `QaM6V26Test`,
    `ViewHardenVh9Test`, `TeblorBuildingsTest`, `WhirlwindBuildingsTest`, `QaV4Test` or `TeblorViewTest`
    (the view track edits those this session: report the row instead). No other file under `game/**` changes.
14. No byte under `game/data/**` changes; the golden replay's body is unchanged (say so, or say why not).
15. `dotnet build RtsGame.sln --no-incremental` 0 errors / 0 warnings after the last edit; the default loop
    (`Category!=Perf&Category!=Stress&Category!=Soak`) green after the last edit with the skip list given by
    name (base: 21 skipped); smoke PASS.

**Handed over from the data track (rules 40 / 68), because your pin move re-prints their cells:**
`Content/AiOpeningPairsReportTests`, `Content/HolyWarFirstWaveReportTests`, `Content/AiSpendingReportTests`,
`Content/AiOpeningPairsVerdictTests`, `QA/Content/DR35ProsePinQaTests`, `QA/Content/TeblorOpeningQaTests`,
`QA/Content/D21OpeningsQaTests`, `QA/Content/D20PinQaTests`, `QA/Content/AiHouseRuleReportTests`, and the
cells they read on `docs/factions/malazan.md`, `whirlwind.md`, `teblor.md` (cells only, no prose). The six
rows skipped with BUG-1851 / BUG-1853 / BUG-1920 stay skipped: the data track's re-measure is next session.
The data track is stopped this session and edits nothing.

**Design references.** docs/02 "Game flow" 4 and "AI opponent"; docs/01 2026-11-25 (the owner's draw
decision), the three 2026-11-27 rows; docs/03 "Implementation (M6-SH3)" (A's trace, B's rule, the pre-trace,
the pin list), "Implementation (M6-S14)" A (the hunt's resume from a find), "Implementation (M5-9a)" (the
starved seat), "Implementation (M5-4d slice 2)" (the rebuild); BUG-1893, 1890, 1891, 1855, 1854, 1860, 1892.

**Tests required.** As the criteria name; a determinism twin for A, C and D; the draw rule's invariant fuzz;
every bug fix a regression test that failed first.

**Constraints.** The AI reads the world only through its own view and sends commands (no `World` read from
`Ai/`, and no AI code reads `CanStillTrain`); no allocation in per-tick or per-think code; no Dictionary /
HashSet order; no wall clock; no stat in C#; docs in the same commit as behaviour. Write your questions for
the Producer, the per-item measurements and the rule 71 list into docs/03 "Implementation (M6-S15)" (rules
67 / 80). Record the PID of every `dotnet test` / Godot you start and end only those (rules 52 / 69). Scratch
folders are new and named `m6s15-...` (rule 63); absolute paths inside your worktree only. Run the full
default loop after your last edit or say in the first lines that you did not (rule 62). You cannot write
`studio/**`: bug-file updates come as a patch in your scratch folder.

**QA focus (full).** (1) The clause from both sides: a state it calls not spent in which the seat can in fact
never field a unit (the bank pays the drop-off but what is left pays no unit and the map holds nothing it
needs; the worker dies the tick before; the drop-off's price paid by wood the unit also needs), and a state
still called spent in which a seat with a worker can plainly still field one. A wrongly early draw is S2.
(2) The fuzz: M6-SH3-can implies new-can, and the difference is exactly the clause. (3) **A seat that sits:**
an oracle over the four-seat tables, the two-seat seeds 1-8 on three maps and the Hard v Normal tally: at the
end tick (or the bound) is there an AI seat that the draw rule says can still train, with a building free to
train, that has sent no Train for 6,000 ticks? Report every one with its bank and why (measure and report,
against the base). Try the shapes C names and the ones it does not: a seat at its population cap with a small
bank, a seat whose only payable unit is at its unit limit, a seat with no building left but a worker, a
Teblor seat with 75-119 gold and under 50 wood. (4) C must not change a healthy seat: a seat that is only
briefly short (a node it may gather is known) plays tick for tick as on the base for 12,000 ticks on three
seeds. (5) D: a hunt cut twice in a row, cut on the tick it reaches a point, cut then the match saved and
loaded in another process; a hunt that resumes must not walk into an ally's base (`TeamAiSoakStressTests`'
oracle) and must still end. (6) A2: the depot is built where the worker can reach it, on each of the three
maps' bases; the seat with wood for the depot and no node known; two seats of one team. (7) The rule 71 list
against the grep file: a Stress class that seats an AI and was not run. (8) Think cost and allocation of an AI
think with the new rules (0 bytes), and the draw check's cost. Suites: the default loop with Stress on the
commit that holds your files, then Perf alone (rule 42); after a tests-only fix round the touched classes and
the default loop only (rule 72).

### View track: M6-V32, the last six box buildings and the Stone Cairn as models (feature, QA standard)

**Goal.** Six building types are still drawn as boxes: the Malazan Wickan Corral, Armory, Cadre Tower and
Engineers' Yard and the Teblor Stables & Kennels and Breaker's Hall. Make them models, so that every finished
building in the game is one, and rebuild the Stone Cairn as stacked rocks with the player's colour on cloth
(its pale wall hides the Jade and Ochre players' colour: BUG-1870).

**Scope.**
- **First, two small fixes in their own commits,** since the task changes both files: **BUG-1911**
  (`tools/art/build_building.py` `check_kit`: a size / at / rotate number that is not finite is refused; an
  unknown key of a part is refused; an entry with no `colours` fails with its id and a plain sentence; an
  unused colour on an entry with no source says "no kit part has role ..."; then
  `tools/qa/kit-errors-extra.ps1` exits 0 and `tools/art/test-kit-errors.ps1` gains the rows) and
  **BUG-1912** (`game/tests/WhirlwindBuildingsTest.cs`: a manifest type drawn as a box fails its own rows and
  the scene goes on to (S), (F) and (3)).
- **The six types,** through `tools/asset-manifest.json` `buildings` and the existing builder (a pack
  `source`, a `kit`, or both; `keep` to crop). Your pick per building, said in the report and in docs/04. Each
  must read as what it is and be unlike every other building of its faction at zoom 40. Starting points, not
  orders: the Malazan four in the Second Age set's grey stone with the roof in the player's colour (Wickan
  Corral: a fenced paddock with a hide tent or an open stable, `Farm_SecondAge_*` or kit rails; Armory:
  `Storage_SecondAge_Level3` or `Market_SecondAge_*` with racks; Cadre Tower: `TowerHouse_SecondAge` or
  `Temple_SecondAge_*`, plainly not the Watchtower; Engineers' Yard: a shed with a kit timber frame, a log
  pile and a half-built engine); the Teblor two in the First Age set's timber at the data `scale` 1.25
  (Stables & Kennels: `Farm_FirstAge_*` with a kit rail and kennel boxes; Breaker's Hall: a heavy hall,
  `Barracks_FirstAge_Level3` or `Temple_FirstAge_*`, with kit boulders). No pack file that another building
  of the same faction already uses with the same palette.
- **The Stone Cairn** (`teblor_stone_cairn`, 2 x 2, `scale` 1.25): stacked rocks (the pack's `Rock*` /
  `Resource_Rock_*` files, kit parts, or both) with a team part of cloth (a banner on a pole, a wrap) that
  shows in all eight colours. It replaces the `WatchTower_FirstAge_Level1` model.
- Pictures (windowed, `ModelShots` or a scene's `-- --shots <dir>`), into a new scratch folder
  `m6v32-pictures`: every new or changed type at zoom 40 and at zoom 20, a whole Malazan base and a whole
  Teblor base at zoom 40, the Cairn and one Malazan building in all eight player colours. Look at them before
  reporting.
- docs/04 "Building models, as built (M6-V32)" (sources or parts, triangles, fitted sizes, palette; a line in
  the licensing log for every pack file newly used and "studio-made" for kit-only models) and docs/03
  "Implementation (M6-V32)", with your questions for the Producer (rule 67).

**Out of scope:** `sim/**` (no `ViewApi` change is expected), `game/data/**` (a building's model is found by
id), unit models, BUG-1910 (the Bone camp's trim, the owner's to ask), any file under `game/scripts/` unless
a criterion cannot be met without it (say why), stored save fixtures. `tools/**` other than `tools/art/**`,
`tools/asset-manifest.json`, `tools/import-assets.ps1` and `tools/qa/kit-errors-extra.ps1` is the sim's.

**Acceptance criteria.**
1. BUG-1911: each of its four inputs (NaN in a size, Infinity in an `at`, no `colours`, an unused colour on a
   source-less entry) and an unknown part key gives a `FAIL` line that names the entry and the part, and no
   model is written; `tools/qa/kit-errors-extra.ps1` exits 0; `tools/art/test-kit-errors.ps1` passes with
   the new rows. Its own commit.
2. BUG-1912: with one manifest model replaced by its LFS pointer text, `WhirlwindBuildingsTest` prints its
   FAIL lines for that type, throws nothing and still runs (S), (F) and (3). Its own commit.
3. All 30 building types are models (the manifest lists 30; the report lists each of the seven new or changed
   ones with its source or parts). Each new model: at most 5,000 triangles, one surface, one `team` material,
   an atlas of 64 x 64 or 128 x 128. If a type cannot be made to read as what it is, it stays a box and the
   report says why (then "30" above is the number reached).
4. The import is deterministic: `tools/import-assets.ps1` run twice gives byte-identical new files, and every
   pre-existing model, atlas, library and `.import` file other than the Stone Cairn's is byte for byte
   unchanged (SHA-256 before and after); `git status` clean after the second run.
5. A new headless scene in the loop, on the pattern of `WhirlwindBuildingsTest`, with the types read from the
   manifest: each new model drawn at the M6-V26 fit inside its footprint by the fit margin, base on the
   ground (the Teblor ones at `scale` 1.25); at a terrace's foot and edge; a worker in front of each is
   selected by a click at zoom 20, 40 and 60 and model pixels select the building; the bar sits over the
   drawn top; models off or a file missing gives the box, an LFS pointer gives the box and exactly one
   warning; a three-faction match draws all 30 types as models. A site is still its box until finished.
6. The pictures show it: in the two base pictures no two buildings of a faction look alike, and the report
   says in one line per type what a player sees at zoom 40.
7. Team colour, **measure and report:** for the Cairn and for one Malazan building of the six, in all eight
   colours at zoom 40, the luminance gap (Rec. 709) between the team part and the largest non-team part next
   to it, as a table in docs/04. A gap under 30 levels is named, not hidden. BUG-1870 is closed if the
   Cairn's table has no colour under 30; otherwise it is re-stated with the new table.
8. Frame time, on a quiet window (rule 77: after the sim's suites if you can, and say what else ran):
   `ModelShots --bench 30 --vsync off` at 1600 x 900: 400 figures + 60 building models (`--bench-buildings 60
   --bench-zoom 60`), 400 capsules from the same window, 1,000 figures, read in the "while 90 %+ alive" column
   (`main`: 6.54 / 0.82 / 16.42 ms); and `--bench-building-types` for each new type against its box. More
   than +1 ms for a type is a finding. Reported; judged by the Producer alone at ACCEPT.
9. Scenes that pinned one of the six as a box, or the Cairn's tower shape, are adapted with no row deleted: a
   site (always a box), `BuildingViews.UseModels = false`, or `ModelOf` / `BarY`; counts are read from the
   manifest. Known: `BuildingModelTest`'s box-type row, `QaM6V26Test`, `ViewHardenVh9Test` P2 ("the
   Stables"), `TeblorBuildingsTest`, `WhirlwindBuildingsTest`, `QaV4Test` and `TeblorViewTest` (they name the
   six ids), `ViewHardenVh10Test` (the Cairn's colours); find the rest with the scene loop. The scene loop is
   green, first pass, alone.
10. If `tools/import-assets.ps1` changes: `tools/art/test-import-exit.ps1` and
    `tools/qa/import-exit-extra.ps1` pass.
11. `dotnet build RtsGame.sln --no-incremental` 0 / 0; the default loop
    (`Category!=Perf&Category!=Stress&Category!=Soak`) green with 21 skipped; `tools/qa/smoke.ps1` PASS; new
    binaries are Git LFS files of types already in `.gitattributes`; the report gives "the art pass's
    binaries" size (`main`: 41,273,999 bytes).

**Design references.** docs/04 "Humanoid base", "Building models, as built (M6-V26 / M6-V28 / M6-V30 /
M6-V31)" (the kit's parts, roles and refusals), the style guide (grounded, not cartoonish), the licensing
log; docs/03 "Implementation (M6-V26)", "(M6-V30)", "(M6-V31)", "(M6-VH9)" (picking follows the drawn shape),
"(M6-VH10)" (the Cairn's colours); `docs/factions/malazan.md` and `teblor.md` for what each building is and
trains; BUG-1870, BUG-1911, BUG-1912.

**Tests required.** The new scene as criterion 5; the adapted scenes; the two-run import proof; the kit
refusal rows.

**Constraints.** Views hold no gameplay state; a click, a bar and a shot height follow the drawn model
through the existing fit, with nothing computed per frame; no player-facing text in C#; never commit anything
from `asset-sources/` (read it from the owner's main checkout, read-only; Blender 5.2) and log every pack
file you use in docs/04 in the same commit. Record the PID of every Godot / Blender / `dotnet test` you start
and end only those; a Godot `--import` can hang under load: time it out and re-run alone; after an import
check `git status` and the file hashes (BUG-1831). Scratch folders are new and named `m6v32-...`; absolute
paths inside your worktree only. The sim track may hand itself a staging-only edit of a scene under
`game/tests` this session, never one of the seven scenes named in criterion 9. You cannot write `studio/**`.

**QA focus (standard).** (1) Look at the pictures: does each of the seven read as what it is at zoom 40 and
20, on grass and on sand; can the Cadre Tower be told from the Watchtower and the Corral from the Stables at
a glance; is any part floating, sunken on a slope or outside its footprint at `scale` 1.25. (2) The Cairn in
all eight colours beside a Jade and an Ochre player's other buildings; the luminance table re-measured from
your own pictures. (3) Picking: a click between a fence rail and the building, through an open stable, on
the rocks' gaps; a unit standing inside a paddock or behind the tower (`HiddenByBuilding`); drag-select
round a yard. (4) The bar, the selection ring and the rally line on each new type at the closest zoom.
(5) The kit refusals with inputs the developer did not list (a negative size, a string for a number, a
`rotate` of NaN, two parts with one bad); the import twice on a fresh clone. (6) The fallback rows with one
new file deleted and with an LFS pointer. (7) The bench alone, last, after the sim's suites end (rule 77);
`BenchTest` and `QaV3Test` pass alone (`QaV3Test -TimeoutSec 1200` under load). Suites: the default loop,
the scene loop, the `--no-incremental` build on the commit holding QA's files (rule 65).

### Data track: STOP this session

No brief. The data track's next task is the re-measure (D40), and it cannot be made while the sim changes
how the computer player plays (rule 41); the pages and content classes it would touch are handed to the sim
for the pin move (rule 68); no owner review tweak is waiting; the Shadow roster waits for Request 56 G5-G8.
**Next session: D40 on the final computer player** (the three Teblor plans against the bar, the Malazan
"keep 18" rule, BUG-1856, the last page sentences, BUG-1840's last file; a data change, if the numbers ask
for one, is that session's one hash move, with the sim's pin classes handed by rules 51 / 76). Rule 57's
clock for the five skipped rows ends with that task.

## Where things stand

- **`main` after this session = sim f4c6ba3a + view 26c94844 + data 139b99bb** (plus this ACCEPT's studio and
  docs files). Scratch clone `prod0817-accept`: build `--no-incremental` 0 / 0, default loop **11,103 passed / 0 failed / 21 skipped of 11,124 in 24 m 5 s alone**, smoke
  **PASS**, scene loop **135 of 135, first pass, alone (29 m)**; bench 2026-11-27 17:40 to 17:54, `ModelShots --bench 30 --vsync off`, 1600 x 900, no test run beside it: 400 figures + 60 building models (`--bench-buildings 60 --bench-zoom 60`) **6.54 ms** while 90 %+ alive (worst 1 % 10.23; `main` 6.54; the same with boxes 6.45), 400 capsules **0.82** (`main` 0.82), 1,000 figures **16.42** (`main` 16.53); 60 buildings of the ten Whirlwind types: models 6.42, boxes 6.49; per type, models less boxes: Holy Camp -0.05, Tent +0.01, Raider Camp 0.00, Archer Camp +0.15, Horse Lines -0.02, Ram Yard 0.00, Smithy +0.14, Shrine -0.05, Supply Cache -0.05, Lookout Tower -0.04 ms (logs in the scratchpad's `prod0817-bench`).
- **No data hash move, no save-format step, no golden change this session.** Data hash 3BB926FD74795B9E, save
  format 11: saves and recordings made since 2026-11-27 morning still load.
- **The draw rule after M6-SH3** (docs/02 "Game flow" 4, docs/03 "Implementation (M6-SH3)" B): `World.CanStillTrain`
  = a unit item in training, or one waiting with population room; or can produce and can pay, in gold and wood,
  for a unit type not at its `maxAlive`; reach = bank, plus carried and map resources only with a live worker
  and a finished drop-off of its own. New derived counters, recounted on load, never saved or hashed:
  `PlayerLedger.TrainingUnits`, `WaitingUnits(p, type)`, `CarriedWood`, `FinishedDropOffs`,
  `ResourceStore.WoodLeft`; `BuildingStore.StartHead` starts a head. `Draw/DrawReachScan` is the test-side scan
  of the rule (old and new). **To come (docs/01 2026-11-27 ACCEPT row (1), BUG-1893):** a live worker and a bank
  that pays for one of the faction's drop-off buildings counts as a finished drop-off.
  `QA/M6Sh3DrawQaTests.Bug1893_*` pins the rule as built and is re-pinned by that task.
- **The AI after M6-SH3:** the worker floor's room test adds `AiPlayer.WaitingWorkerHalfPop`; `NoteDroppedGather`
  judges a full-load worker when a drop-off stood at the last think too (`AiPlayer.HadDropOff`); the kept rally
  point is dropped when the hall point changes. All think scratch, nothing saved.
- **Pinned matches after M6-SH3** (docs/03 "Implementation (M6-SH3)", the pin list): `MatchCriterionTests`
  unchanged (Vathar seed 1 22,840, Raraku seed 2 27,400, Pale Hills seed 1 26,594, generated seed 3 23,806, FFA
  56,664, 2v2 42,301; `HalfWay` 11,420); Hard v Normal 32 / 4 / 0 / 6. Moved: Raraku seeds 3 / 4 / 6 end at
  35,551 (out of the sweep's `Known`); Pale Hills four-seat FFA seed 7 draws at 59,184; Vathar four-seat FFA
  seed 3 ends at 46,671; `StandardPresetPinQaTests` save scene `defeated_seat` hash. **Not ending:** Pale Hills
  two-seat 7 / 8 (BUG-1855, traced: `Stress/PaleHillsStallTraceTests`), Pale Hills four-seat FFA 2 and 4
  (BUG-1890 / 1891, traced: `Stress/DrawReachPreTraceTests`); generated seed 1 inside 24,000.
- **QA's four-seat "2v2" rows are 1v1v2** (teams 0 / 1 / 0 / 1; BUG-1892): `M6S14FourSeatQaStressTests`. A real
  2v2 passes 1 / 2 / 1 / 2. Vathar 2v2 seed 6 ends at 79,351 (past `M5H5HuntAfterDefeatQaTests`' 72,000).
- **The view after M6-V31:** all ten Whirlwind buildings are models (24 of 30 types; boxes left: Malazan Wickan
  Corral, Armory, Cadre Tower, Engineers' Yard; Teblor Stables & Kennels, Breaker's Hall). The builder
  (`tools/art/build_building.py`) takes a `kit` (parts: box, ridge, pyramid, cone, cylinder, pole, awning; a
  role each) with or without a pack `source`, and `keep` (a crop of the source); a kit with no source is drawn
  in meters inside the data footprint less 0.25 m. `tools/art/test-kit-errors.ps1` proves the refusals (about a
  minute, Blender). **`tools/qa/kit-errors-extra.ps1` exits 1 until BUG-1911 is fixed; it is in no gate**
  (checked: nothing calls it). No file under `game/scripts/` changed: a model is found by id. **135 scenes.**
  Binaries 41,273,999 bytes.
- **The data track after D-H11:** `UnitLimitText.Stated(description, unitName)` reads a number as a limit only
  when it counts the unit (an alive word, "of them", the unit's name); `UnitLimitPagePin.RowProblems` is in
  `PagePinReadinessTests.Problems`; `AiContentQaTests.CompositionProblems` names a list with no Age II research.
- **Rule 68's handed list is eight classes** (unchanged). The sim re-measured all of them this session: no cell
  moved, no faction page changed.
- **Requests:** 56: G1-G4 built, G5-G8 open; 60 open (the draw's seats; with the next save-format step, with
  BUG-1490).
- **STATE.md keeps its short form** (Now table one line a field, queues as numbered lists). For your review is
  whole and grows (about 245 entries); the owner is asked for "review: archive before <date>".
- **Session length:** about nine hours. The sim's QA full took about 4.5 h beside the view's loop and benches
  (default loop + Stress 3 h 57 m; 23 m to 25 m for the default loop alone). The lock went a few minutes past its
  three-hour window once (12:21 -> 15:24, the conductor waiting on agents); a background refresher kept it
  after that; no second session started (rule 81: noted, not an incident).
- **Scratch folders left** (the owner's housekeeping list): in this session's scratchpad `prod0817-plan`,
  `prod0817-accept`, `prod0817-bench`, `m6sh3-*`, `m6v31-*`, `dh11-*`, `qa0817`. The stray empty
  `C:\Dev\rtsGameProject\ModelShots.cs` and the file named with a yellow-circle emoji are still untracked in the
  owner's main checkout.
- **Process notes:** the data developer read (did not write) one file in the owner's checkout through a relative
  path; the conductor confirmed the checkout unchanged. Briefs keep saying "absolute paths inside your worktree".
  QA applied the sim developer's bug-file patch itself (it can write `studio/**`): fine, keep it.
- **Usage at the ACCEPT:** weekly 43 % at the conductor's last check (stop at 95 %).

## Standing rules (1-84)


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


**84 (new, 2026-11-27-0817):** a Producer decision that leaves a case out "because it nearly never matters" is
checked at PLAN against the shipped data it rests on, and the brief's QA focus names the case (M6-SH3: "a bank that
buys a drop-off nearly always buys a unit" was false, every small drop-off costs 0 gold / 75 wood: BUG-1893, found
by QA). And a criterion that names the staged state of a regression test ("a staged Moving worker") adds "or the
state the trace finds", since the brief is written before the trace (M6-SH3 criterion 7: rule 78's family).


## Watch-outs (all tracks)
- **New this session (0817): the draw rule asks for a finished drop-off.** A scene or test that stages a seat
  "able to train" through gold or wood on the map gives it a live worker **and** a finished drop-off of its own
  (`DrawViewTest.DropOffFor0` is the model; a Town Hall is one). Wood counts: a seat with gold for a unit and no
  wood for it is spent. A test that writes store arrays directly calls `World.RecountDraw()` (it rebuilds the
  new counters too). The next sim task loosens this once (BUG-1893: a bank that pays for a drop-off).
- **New this session: all ten Whirlwind buildings are models.** A scene that reads a Whirlwind building's box
  sets `BuildingViews.UseModels = false` or uses a type that is still a box (six left; none after M6-V32: use a
  site). Model counts are read from `tools/asset-manifest.json`, never a literal. The scene loop is **135 scenes**.
- **New this session: `tools/qa/kit-errors-extra.ps1` exits 1 on purpose until BUG-1911 is fixed**; it and
  `tools/art/test-kit-errors.ps1` are in no loop (Blender, about a minute each).
- **"Pinned matches" bullets below are stale:** the current list is under "Where things stand" and in docs/03
  "Implementation (M6-SH3)".
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

### Current session plan
**M6-S15** (2026-11-27-1857): see "Current session plan" above. The candidates below are as written at the
last ACCEPT; candidates 1 and 2 are swapped (docs/01, the 2026-11-27-1857 row).

### Where we are
M6-SH3 accepted: the draw rule's "can still train" is exact, BUG-1857 / 1852 / 1730 / 1862 / 1731 / 1153 are
closed, Q7 is built, every pin re-measured once. Counter **0 / 4**. Open sim debt that changes AI play or match
ends: BUG-1890, 1891, 1855 (matches that do not end), BUG-1893 (the draw rule's missing clause, decided),
BUG-1894 (later-draw edges); that does not: BUG-1490, Request 60, BUG-1892 (QA's table name), BUG-1149's strict
row.

### Next task candidates
1. **M8-5 (proposal): BUG-1893's clause first, its own commit** (a live worker and a bank that pays for one of
   the faction's drop-off buildings counts as a finished drop-off; counters only: the cheapest drop-off per
   faction is data, read at construction; old-rule-spent no longer implies new-spent for this clause, so the
   property row becomes "M6-SH3-spent implies spent unless the clause holds"; re-pin
   `M6Sh3DrawQaTests.Bug1893_*`; the four-seat tables measured before and after, "measure and report"), **then
   Request 56 G5** (a `teleport` effect with `exceptSlots`, test data only; G6 if it fits). QA full. No AI code,
   no pin move expected beyond `Draw/*`.
2. **The computer-player task, the session after** (QA full, a pin move, the data track static): BUG-1890 (a
   starved seat buys the most under-weighted unit it can pay for), BUG-1891 (trace the unmined 726 gold first),
   BUG-1855 (a hunt interrupted by a defense resumes its leg or sweeps from where the army stands). Measured on
   `Stress/DrawReachPreTraceTests` and `Stress/PaleHillsStallTraceTests`.
3. The save riders (BUG-1490, Request 60): one format step, rules 50 / 82.
4. BUG-1894 item 1 (a finished head with no free cell), "press the attack while the draw clock runs" (needs a
   design in docs/02 first).

### Watch-outs
- BUG-1893's clause loosens the rule: the fuzz's "old spent implies new spent" row must be re-stated, not
  deleted (rule: no row deleted to pass).
- A scene or test that stages a seat with a worker and no drop-off to be "spent" will stop being spent when its
  bank holds 75 wood (`DrawViewTest`'s `DropOffFor0` staging is unaffected; check `Draw/DrawReachTests` rows
  "ignored without a finished drop-off").
- `Stress/M6S14FourSeatQaStressTests` and `Stress/M6Sh3RarakuQaReportTests` are Soak-tagged: by name only.

## View track

### Current session plan
**M6-V32** (2026-11-27-1857): see "Current session plan" above.

### Where we are
M6-V31 accepted: the Whirlwind's ten buildings are models, the kit path exists. Counter **1 / 4**. 15 of 21 unit
types and 24 of 30 building types are models. 135 scenes. Bench on a quiet window: see "Where things stand".

### Next task candidates
1. **M6-V32 (proposal): the last six boxes as kit models** (Malazan Wickan Corral, Armory, Cadre Tower,
   Engineers' Yard; Teblor Stables & Kennels, Breaker's Hall; a pack source where one fits, the kit where none
   does, in each faction's palette) **and the Stone Cairn as stacked rocks** (closes BUG-1870), with BUG-1911
   (non-finite numbers, the unclear FAIL lines; then `tools/qa/kit-errors-extra.ps1` passes) and BUG-1912
   (`WhirlwindBuildingsTest.ScreenRect` on a box) folded in, since the task touches both files. Pictures at zoom
   40 and 20, all eight colours for one building per faction, the bench alone.
2. BUG-1910 (a dark trim on the Bone camp's cloth) only if the owner asks.
3. Mounted units and machines once the owner's packs are on disk; the limit count on a button with the Shadow
   roster; BUG-1790's three-sided half once Request 60 is on `main`.

### Watch-outs
- Scenes that pin a box type by name (`BuildingModelTest`'s box-type row, `QaM6V26Test`, `ViewHardenVh9Test` P2
  "the Stables", `TeblorBuildingsTest`) lose their box when the last six are models: the brief hands them, and a
  site (always a box) or `UseModels = false` is the replacement; no row deleted.
- `BenchTest`, `QaV3Test`, `QaM6V28Test`, `QaM6VH9FixTest` fail on wall clock or the 600 s timeout under load and
  pass alone.
- A Godot `--import` can hang under load; time it out and re-run alone.

## Data track

### Current session plan
**STOP this session** (2026-11-27-1857): the sim moves computer-player behaviour (rule 41). D40, the
re-measure below, is next session's task, on the final computer player.

### Where we are
D-H11 accepted: BUG-1880 fixed, the limit pin is in the readiness check, no game data changed. Counter **0 / 4**.
The Teblor plans and three Malazan balance rows are un-measured on the computer player since M6-S14 (five rows
skipped: BUG-1851 x 3, BUG-1853 x 2). **Rule 57's clock started at this ACCEPT:** the re-measure is next.

### Next task candidates
1. **D40 (proposal), the re-measure** (QA standard, "measure and report"): the three Teblor orders on the
   shipped file (BUG-1853; re-point the D38 rows at the new table), the BUG-0982 "keep 18" rule against
   `cadre_rush` (BUG-1851: either the rule's pick or the opening's wave size changes; a data change moves the
   hash and the golden header, allowed next session because the sim's proposal ships no data), BUG-1856,
   malazan.md's Age II paragraph (line 141, noted by D-H11's developer, unchecked), teblor.md; BUG-1840's last
   file (`QA/Content/D21OpeningsQaTests`); BUG-1920 / 1921 if they fit. The handed classes return to the data
   track for this task. Owner review table for any data byte.
2. The Shadow roster once G5-G8 are on `main`.

### Watch-outs
- Owner review tweaks from the inbox come first, always; none is waiting as of this ACCEPT.
- If D40 changes a data byte it is the session's one hash move: the sim's brief must ship no value under
  `game/data/common/**`, and every save and recording from before is refused again (say so in the entry).
- The hour-long Teblor report classes run by name; say the filter in the brief (a light or standard QA never
  runs the full suite: rule 53).
