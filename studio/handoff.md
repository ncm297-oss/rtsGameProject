# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-25-0924** (session 1 / 8 of 2026-11-25, started 09:24,
QA ended about 12:40). Sim **M7-H3** ACCEPT (hardening, 0 fix rounds, QA standard PASS_WITH_ISSUES), view
**M6-V27** ACCEPT (feature, 0, QA standard PASS_WITH_ISSUES), data **D-H10** ACCEPT (hardening, 0, QA standard
PASS_WITH_ISSUES; nothing shipped). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters:
**sim 0 / 4, view 1 / 4, data 0 / 4**. Bug ids next session: **sim 1730-1749 (20: a pin move), view 1750-1759,
data 1760-1769**. Open S1 / S2: none. The next session is **2 / 8 of 2026-11-25**.

**Next session in one line (proposal):** sim **M6-S13, the owner's draw rule** (feature, QA full; the session's one
hash move and pin check) · view **M6-V28, the next Malazan building models** (feature, QA standard) · data **D36,
the Teblor opening's second search** around one Shaman (feature, QA standard; **measured and reported, no game data
byte**: it ships the session after, re-measured on the draw rule's `main`), with BUG-1721's page fix and BUG-1720.

**The default loop on `main` is green.** Expect **0 failed / 21 skipped** (10,301 passed / 0 failed / 21 skipped of 10,322 in 22 m 19 s on the Producer's scratch clone
of sim 68755b7 + view b6683e8 + data c31d8ef). The skips are the standing 20, minus the lifted BUG-1620 row, plus
QA's two BUG-1720 rows. Scene loop **124 of 124, first pass**. Any red row is a finding. Stress was not run as a category this
session (rule 53; nothing in the tick moved, four shipped matches hash-equal on base and head).

**At the PLAN, first:** (1) the inbox (waiting, none blocking: D33's and D34's tables, "buildings ok", "whirlwind
colours ok"; tweaks come before roadmap work); (2) the default loop on `main`; (3) the bug index: check that
`studio/bugs/README.md` holds one row each for BUG-1682 (fixed, M6-V27), BUG-1690 / 1691 (fixed, D-H10), BUG-1700,
1710, 1720, 1721 (open) after the three-way merge, and fix duplicates; (4) `CLAUDE.md`'s "Current milestone" line is
still stale (on "Waiting on you"); (5) **one hash move, the sim's:** the draw rule adds a `rules.json` key, so the
data track ships no byte under `game/data/` next session and the golden header is the sim's; (6) the draw rule can
end pinned matches early: rules 43 / 45 / 71 / 74 in the sim brief (list every class that plays a full AI match,
Stress pin classes included; name which rows are expected to move: the stalled four-seat rows BUG-1621 / BUG-1610,
`HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`); (7) the two-process rows' limits are processor time
now (`ChildProcessLimit`): a child that uses several cores can pass a small limit faster than the clock; QA saw
0.5-1.2x wall, so a 180 s row has room, but read a new "used more than N s of processor time" failure as that
first; (8) the view's draw screen: check at PLAN whether the result screen and `ui.json` already have a draw text
(the sim brief's "for the view track" note must say what the view reads).

## Current session plan (2026-11-25-1437, session 2 / 8 of 2026-11-25; written at the PLAN)

All three tracks GO, all feature sessions (counters before: sim 0 / 4, view 1 / 4, data 0 / 4). Bug ids: sim
1730-1749, view 1750-1759, data 1760-1769. **One hash move, the sim's** (`rules.json` gains `draw`); the data track
ships no byte under `game/data/`; the view touches no data file. The draw rule's design is docs/01's
"2026-11-25-1437 PLAN" row (it replaces the one-line design of the owner row above it: every unit can fight and
costs gold, and BUG-1536's stuck seat still has five soldiers).

PLAN checks on scratch clone `prod1437-plan` at base 6b6363f: build `--no-incremental` 0 errors / 0 warnings; the
default loop **10,301 passed / 0 failed / 21 skipped of 10,322 in 22 m 11 s**; smoke **PASS**. Rule 54's rehearsal
could not be run (a `draw` key has no reader on the base): criterion 13 of the sim brief carries that risk (staged
two-sided worlds without gold that now draw). The view's banner reads "nobody stands" for Draw
(`Match.ResultFor`), so a drawn match shows "Victory" until M6-V29 (next session, the view's first item); the
`match` command's result line is fixed in the sim task.

### Sim: M6-S13, a spent match ends in a draw (feature, QA full)

Goal: Build the owner's draw rule (inbox 2026-11-25, BUG-1536). A match in which no side can ever train another
unit ends in a draw after a wait kept in the data, as a match-end rule on the sim's tick, so a mined-out match ends
honestly for a human player and in the AI test matches instead of running for ever.

Design (docs/01, the 2026-11-25-1437 PLAN row; do not widen or narrow it, ask through "Questions for the
Producer"):
- **A seat can still train** when it has a unit in a production queue, or when `banked gold + gold carried by its
  own units + (it has a live worker ? the gold left in every gold node on the map : 0)` is at least the gold cost of
  its faction's cheapest unit (any unit of the faction, whether or not it has the building for it; a faction with a
  0-gold unit can always train) **and** it has a finished unit-producing building or a live worker. Wood is ignored.
- **The match is spent** on a tick when it is not over, at least two sides that ever built still stand, and no
  undefeated seat can still train.
- **The clock** counts the ticks the match has been spent without a break. It goes back to 0 on a tick the match is
  not spent, on a tick a non-summoned unit died (any cause; a summon's death or expiry does not count), and on a tick
  a seat is defeated. When it reaches the wait, the match is **drawn** on that tick: `MatchOver` is true for good,
  a new read-only `MatchDrawn` (name yours) is true for good, **no seat is defeated by it, nothing collapses, no
  defeat event**; ticks go on as after any result. A win that falls on the same tick stands (the defeat check runs
  first; the draw is only looked at when there is no result).
- **The wait** is `game/data/common/rules.json` `"draw": { "afterSeconds": 300 }` (the loader converts to ticks).
  The block left out = no draw rule (the clock stays 0). No number of this rule in C#.

Scope:
- The schema, its validation and the shipped value (`rules.json` only; this is the session's one data-hash move).
- The rule in the tick (after the defeat check of phase 11b), its state (the clock, the drawn flag) in the state
  hash and in the save, and read-only accessors for the next view task: drawn or not, and the ticks left on the
  clock (or "not running").
- **"Zero is free":** a world whose clock is 0 and which is not drawn hashes and saves exactly as on the base, so a
  match that is never spent keeps every tick hash and every pin. Save format 10 is kept with the new bytes present
  exactly when the data has `draw` and they are not zero (the FRST / STIL pattern), or say in docs/03 why not.
- `tools/Rts.Cli` `match`: a drawn match prints `result draw at <tick> (<m:ss>)` and exits as a result does (today
  the line would name a standing seat the winner).
- One re-measure of the pinned AI matches (list below) and of the stalled rows.
- docs/02 "Game flow" 4 (the draw paragraph, in plain words), docs/03 "Implementation (M6-S13)".
- OUT of scope: any change to how the AI plays (no AI rule reads the clock; BUG-1661, 1660, 1555, 1554 / 1556, 1558
  are the next sim session); the two options the owner did not take (an armyless seat counting as starved, a
  wood-only unit); the defeat rule itself; anything under `game/` except `game/data/common/rules.json` (the banner
  and a countdown are the view's M6-V29, next session); `sim/Rts.Sim/ViewApi/**`; faction data; `docs/factions/**`;
  loosening any Perf threshold; BUG-1700.
- Content tests: `sim/Rts.Sim.Tests/Content/**` and `QA/Content/**` are the data track's and it is working there
  this session. You may edit a row there **only** to re-pin a literal the hash move or a draw forces, and never in
  `UnitHitBonusText*`, `DH10TextPinQaTests`, `DH10TeblorOrderQaStressTests` or any file whose name starts `D36`;
  list every such edit in the report. Anything else red there: name it, do not edit it.

Acceptance criteria:
1. **Schema.** `draw.afterSeconds` 300 loads as 6,000 ticks; the block left out loads as "no rule"; zero, a
   negative, a non-number and a value over the cap you choose (at least 86,400 s, so a test can put the wait out of
   reach) are load errors that name the field; what a JSON `null` means for the block and for the number is stated
   in docs/03 "Data format" and pinned in `Data/DataNullPolicyTests`. `DataValidationTests` covers it. The shipped
   `rules.json` carries 300 and the field is part of the data hash.
2. **"Can still train", clause by clause** (staged worlds on test data, each clause alone flips the answer): a unit
   in a queue; the bank at the cheapest unit's gold cost exactly, and one below; gold carried by an own unit that
   closes the gap; gold left in a node with a live worker, and the same node with no worker; a seat with the gold but
   neither a finished unit-producing building nor a worker; a site does not count as a building; a defeated seat is
   ignored; a forged faction with a 0-gold unit is never spent (rule 58: the forge tolerates shipped rows).
3. **Spent needs a live match:** never spent with fewer than two standing sides that ever built, in a sandbox (no
   seat ever built), or after the match is over. Two allied seats alone are one side: never spent.
4. **The clock and the draw.** With the wait at N ticks the draw falls on the first tick the clock reaches N (rows at
   N - 1 and N); a non-summoned unit's death at clock N - 1 restarts it; a summon's expiry or death does not; a
   seat's defeat restarts it (three sides); a cancel that refunds enough gold makes the match not spent and restarts
   it; a win on the tick the wait ends is a win, not a draw. At the draw: `MatchOver` and the drawn flag true and
   still true 1,000 ticks later, no seat's defeated flag changed, no building freed, no defeat event, the tick goes on.
5. **It always ends while spent (invariant fuzz, at least 200 seeds):** in a staged spent match with two to four
   seats, units on every side and seeded random move / attack / stop / hold orders (no cancel, so it stays spent),
   the match is over within `wait x (non-summoned units alive at the first spent tick + seats + 1)` ticks, and the
   clock never restarts without a death or a defeat on that tick.
6. **Off and never-spent are the base.** (a) On a copy of the shipped data with `draw` removed the clock stays 0
   and the match never draws (one stalled row, 144,000 ticks or its old bound). (b) A twin with the wait out of
   reach is hash-equal, checkpoint by checkpoint, to the shipped-data run up to that run's draw tick, on at least
   three matches that draw. (c) No pin literal (end tick, winner, digest) of a match that is never spent before its
   old end is edited; the golden replay's tick hashes are unchanged and only its header is regenerated, with the
   reason in the `GatherWedgeQaTests` comment (if a tick hash does move, stop and say why). No new test pins the
   data hash or a save's bytes as a literal (rules 49 / 73).
7. **Save and replay.** A save taken mid-clock loads and draws on the same tick with the same hashes as the
   unsaved run (in process, and once through the two-process path `MapFileTests.RunChild`); a save taken after the
   draw loads as over and drawn; a save whose clock is negative or above the wait, or that is drawn without being
   over, is refused; `SaveRestoreQaTests`' field list knows the new state. A recorded match that draws replays to
   the same draw tick with every checkpoint equal. docs/03 says which bytes were added and when they are present.
8. **Determinism.** A drawn AI match run twice (and once in two processes) gives the same draw tick and state hash.
9. **The command line.** `match` on a drawn match prints `result draw at ...` and exits 0 (`CliTests`); the progress
   lines are unchanged for a match that is never spent.
10. **The stalled rows, measured and reported (not pass / fail: no AI cure is in scope, rule 48).** For each of
    the four-seat Pale Hills free-for-all seeds 1, 3, 4, 6, 7, the skipped Vathar four-seat row (BUG-1621 /
    BUG-1610), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116) and `M5H5HuntAfterDefeatQaTests.A2v2_*`
    (BUG-1149): the first spent tick, how often the clock restarted, and the end (winner or draw, tick). Expected by
    the design: seed 3 (BUG-1536: gold gone from 30,000, banks 2-16) ends in a draw or a late win well inside
    144,000. A row that now ends is un-skipped and pinned with its end, old value in a "was" comment
    (`HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall` and its seed-2 twin are re-staged or dropped as the
    handoff notes). A row that still does not end stays skipped with its id, and the report says which clause kept
    it un-spent (a seat sitting on gold it never spends is an AI-play finding with a new bug id, not cured here).
11. **The pins (rules 43 / 45 / 71 / 74).** Run the default loop and the whole Stress category on the head. The
    report carries the list below one class a line with "unchanged / moved to ... (spent at tick ..., drew or won
    at ...) / not run because ...". A pinned match may move only because it became spent before its old end. If a
    criterion row of M5 (`MatchCriterionTests`) turns from a winner into a draw or stops ending inside its bound,
    do not re-pin it as a draw: move it to the smallest seed that ends with a winner inside the bound, file it as a
    finding with an id, and say if that seed is already another row's (rule 45). If any of the Hard v Normal
    tally's 42 matches is spent before the tally's bound, re-measure the tally and print it as wins / losses /
    draws / unfinished, with docs/03's M5-5 table and the bound comments in the same commit (rule 43).
12. **Cost.** The per-tick check allocates nothing (an allocation row in a `[Collection(SerialCollection.Name)]`
    class) and scans no store every tick that it could count instead; the Perf category, run alone (rule 42), has
    no new failure and no threshold is touched.
13. **Other tests staged without gold.** Every row of the default loop, the Stress category and the scene loop that
    goes red only because its staged two-sided world now draws is sorted (rule 68): sim-owned rows are re-staged
    (the test's own data copy without `draw`, or a mine) with a one-line why; content rows and scenes are named in
    the report, not edited (the content exception above aside). If more than about twenty sim rows need it, say so
    and propose one helper instead.
14. **Green.** `dotnet build RtsGame.sln --no-incremental` 0 errors / 0 warnings after the last edit (rule 64); the
    full default loop after the last edit: 0 failed, the skip count stated with each id (21 on the base; rule 62);
    the Stress category: no red row but the skipped ones, named; `tools/qa/smoke.ps1` PASS; the scene loop run once
    on your branch with its result (you cannot edit `game/tests`; the scenes that play to a result or write a save
    are `MatchOverViewTest`, `QaM6V4Test`, `QaM6V14Test`, `QaM6V9Test`, `AiSeatTest`, `ReplayPlaybackTest`,
    `TeamsTest`, `FreeForAllTest`, `TeamFilesTest`: rule 50).
15. **Docs.** docs/02 "Game flow" 4: the draw rule in plain words (what "spent" means, the 5 minutes, what restarts
    the clock, nobody is defeated, the banner until M6-V29). docs/03 "Implementation (M6-S13)": the formula, where
    it runs in the tick, the hash and save layout, a "For the view track" note naming the accessors M6-V29 reads, a
    "Questions for the Producer" section with your recommendation on each (rule 67), and the answer to M7-H3's
    Question 1 written into "Data format" (docs/01 2026-11-25 ACCEPT (1): keep). Bug-file updates (BUG-1536, 1523's
    kin, 1068 / 1116 / 1559, any new id) as a patch in your scratch folder: you cannot write `studio/**`.

Pin classes (rule 71; add any class a grep for full AI matches finds that is missing here, rule 74):
- Default loop: `Ai/MatchCriterionTests` (with `Ai/CriterionMatch`)
- `QA/StandardPresetPinQaTests`
- `QA/NonHardMatchPinQaTests`
- `Ai/CounterCompositionTests` (`TheNonHardPins_*`)
- `Ai/ReserveTwinTests`
- `Vision/SharedVisionFfaPinTests`
- `QA/OutcomePinM73bQaTests`
- `HardKnobsPinTests`, `Ai/HardKnobsMatchTests`, `QA/HardKnobsQaTests`, `QA/HardKnobsFightsOnQaTests`
- `Ai/AiVersusAiTests`, `Ai/AiDeterminismTests`
- `Ai/EndGameHardeningTests` (the two-process Pale Hills seed 2 four-seat rows; `ALoneIdleWorker_*`)
- `Ai/FarExpandTests`, `Ai/StarvedWaveTests`, `QA/StarvedWaveQaTests`, `Ai/RebuildTests`, `QA/RetrainQaTests`
- `Ai/OpeningReserveShareTests`, `QA/SpendDownQaTests`, `Economy/StartingResourcesTests`, `QA/StartingResourcesQaTests`
- `MatchOverTests`, `TeamTests`, `QA/TeamQaTests`, `QA/WinConditionQaTests`, `QA/DefeatRuleQaTests`
- `Save/SaveRoundTripTests`, `Save/SaveRefusalTests`, `QA/AiSaveQaTests`, `QA/SaveRestoreQaTests` (`save_ai_seed2`)
- `ReplayGoldenTests`, the premap AI replay, `seed21_playable.replay`, `cross_map_seed1.replay`
- `CliTests.Match_TheLiteralRow_*`
- `Data/FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`
- Stress: `HuntStressTests` (the four-seat oracle rows, `MidHuntSeed`, the known-stall rows)
- Stress: `HuntSweepQaStressTests`, `HuntSweepRound2QaStressTests`, `M5H5HuntAfterDefeatQaTests`
- Stress: `M6Sh2DeterminismStressTests`, `M6Sh2QaStressTests`, `M6S12AiRulesQaStressTests`, `M6S12DeterminismStressTests`
- Stress: `StarvedSaveTwoProcessQaStressTests`, `StarvedPickOracleQaStressTests`, `StarvedWaveProbeQaStressTests`
- Stress: `ReservePairingsStressTests`, `OpeningReserveShareStressTests`, `M78ReserveSwitchQaStressTests`, `M78ShippedDigestQaStressTests`, `ShippedDigestReportTests`
- Stress: `MatchRowsM6S10Report` (seeds 1-8 on the three maps)
- Stress: `FarExpandProbeQaStressTests`, `VatharStallTraceTests`, `EndGameH6ProbeQaStressTests`
- Stress: `HardVersusNormalTallyQaStressTests`
- Stress: `DefeatAiStressTests`, `RarakuTwinStressTests`, `RebuildLookMatchQaStressTests`, `TeamAiSoakStressTests`, `MapSlice2StressTests`, `MatchStatsFuzzStressTests`, `RegenQaStressTests`
- The data track's, read and reported (edited only under the content exception): `Content/D33BalanceReportTests`,
  `Content/D34HashMoveTests`, `Content/TeblorOpeningVariantsReportTests`, `Content/AiOpeningPairsReportTests`,
  `Content/HolyWarFirstWaveReportTests`, `Content/AiSpendingReportTests`, `QA/Content/DR35ProsePinQaTests`,
  `QA/Content/TeblorOpeningQaTests`, `QA/Content/D21OpeningsQaTests`, `QA/Content/D20PinQaTests`,
  `QA/Content/D33QaTests`, `QA/Content/D27HardMirrorQaStressTests`, `QA/Content/TeblorAiMatchQaStressTests`

Design references: docs/01 change log (the 2026-11-25 owner row and the 2026-11-25-1437 PLAN row); docs/02 "Game
flow" 4 (the defeat rule this sits beside); docs/03 "Save/load and replays", "Data format" ("A JSON `null`"),
"Implementation (M5-4e)" (the defeat check), "Implementation (M8-1)" / "(M8-2)" (a save part present only when the
data asks for it); `sim/Rts.Sim/World.Defeat.cs`; `studio/bugs/BUG-1536-*.md`.

Tests required: unit rows for criteria 1-4 and 9; the invariant fuzz of 5; the twins of 6; save, refusal and
replay rows of 7; the determinism rows of 8 (the two-process ones may be Stress); the allocation row of 12; a
report class for 10 (Stress). Every bug fix in passing gets a test that failed first.

Constraints: no Godot in `Rts.Sim`; the wait only from data; no wall clock; seats walked in player order, no
`Dictionary` / `HashSet` iteration, no LINQ or `new` in the per-tick check; no player-facing text in C#; docs in the
same commit. Process: record the PID of every `dotnet test` / Godot you start and end only those (rules 52 / 69);
scratch folders named `m6s13-...`, created new (rule 63); run the full default loop after your last edit or say in
the report's first lines that you did not (rule 62); the Perf category only alone (rule 42).

QA focus (full): attack the rule's edges before the matches: every clause of "can still train" at its boundary
(bank exactly at the price, a carried load that closes the gap, the last gold leaving a node on the tick a worker
dies); what else adds gold (every refund path: a cancelled unit, site, research; anything that pays gold on a kill
or a collapse) and whether each makes the match un-spent on that tick; teams (2v2 with one side spent, a defeated
ally); a draw and a defeat on one tick; a seat that places its first building after the draw (the latch); summons
and packs (a Teblor war-dog pack's deaths, a summoned unit's expiry); regenerating giants in a stand-off. Fuzz the
invariant of criterion 5 with 1,000 seeds and with cancels allowed (then only "the clock never restarts without a
death, a defeat or a tick un-spent"). Save at every 500th tick of a clock run and compare draw ticks; tamper the
new bytes. Prove "zero is free" yourself: every tick hash of the golden and of two never-spent pinned matches
against the base. Check the per-tick cost at 2,000 units and four seats (Perf alone, rule 42). Re-run two of the
developer's stalled rows and one moved pin. Suites: the default loop with Stress, then Perf alone; the scene loop
once (rule 53). Re-check after a tests-only fix round: touched classes + the default loop + the build (rule 72).

### View: M6-V28, four more Malazan building models (feature, QA standard)

Goal: Continue the art pass on buildings. Two of 30 building types are models (the Garrison Keep and the Legion
Barracks, M6-V26); this task adds the four Malazan buildings the pack covers most plainly, through the same import
path, so a Malazan base starts to read as a base instead of grey boxes.

Scope:
- Models for **`malazan_billet`** (2 x 2 cells; the pack's Houses line), **`malazan_depot`** (2 x 2; the Storage
  line), **`malazan_crossbow_range`** (3 x 3; the Archery line) and **`malazan_watchtower`** (2 x 2; the WatchTower
  line), from `asset-sources/quaternius-ultimate-fantasy-rts-2022-08/` (read it in the owner's main checkout; never
  copy the pack into the repo). The pick of the exact model (age, level) is yours, said in docs/04 with the reason,
  by M6-V26's reasoning: a large team-coloured part that faces the camera, the Malazan tint (steel grey, oxblood,
  dull bronze), and each building told apart from the others at the default zoom.
- `tools/asset-manifest.json` `buildings` (four entries), `tools/art/build_building.py` and
  `tools/import-assets.ps1` only if a model needs something the builder lacks (those three are the view track's;
  the rest of `tools/**` is not). Blender 5.2 is installed.
- `BuildingModels` / `BuildingViews` only as far as the four need; the Watchtower shoots: check where its shot is
  drawn from and make it leave from the drawn tower, not from the old box's height, if it does not already.
- One scene (or `BuildingModelTest` extended) and the existing scenes that read one of these four as a box adapted
  (`BuildingViews.UseModels = false` before binding, or `ModelOf` / `BarY`: `BuildingScaleViewTest` and
  `QaM7V4Test` are the models).
- docs/04 "Building models, as built" (the table, the picks, the tints, the sizes, the licensing log row in the
  same commit), docs/03 "Implementation (M6-V28)", and the Producer's answers to M6-V27's Questions 1-4 written
  into docs/03 "Implementation (M6-V27)" (docs/01 2026-11-25 ACCEPT (3): keep, keep, keep, BUG-1710 next hardening).
- OUT of scope: the other four Malazan buildings (Wickan Corral, Armory, Cadre Tower, Engineers' Yard: no plain
  match in the pack, a later task); Whirlwind and Teblor buildings; construction-stage models (a site stays its
  box); resource and nature models; the data `scale` on a model; unit models; the draw banner and countdown
  (M6-V29, next session, after the sim's rule is on `main`); BUG-1710; anything under `sim/` or `game/data/`.

Acceptance criteria:
1. Four manifest entries and four built files `game/assets/models/malazan/<id>/<id>.glb` with their atlases; a
   re-run of the import is byte-identical for all six buildings and every unit model (`git status` clean after it).
2. Each model: one surface, one material named `team`, one atlas, at most 5,000 triangles (docs/04), origin on the
   ground at the plan's centre, entrance toward +Z.
3. Fit: each stands at least 0.25 m inside every edge of its footprint (4 x 4 m or 6 x 6 m) and on the ground, on
   flat ground and on a terrace edge; the rows read the building list from the manifest, not a literal count.
4. Life cycle for each of the four: a site is the box, the finished building the model, a remembered one darkened,
   a destroyed one gone; with `--placeholder-art` and with an LFS pointer file in place of the `.glb` it is the box
   and the log has no `ERROR` line.
5. Picking and the health bar follow the drawn shape (M6-VH9's path): a click on a pixel of the model selects it, a
   unit in front is picked first, the bar sits above the drawn top and stays on screen at the closest zoom.
6. The Watchtower's shot leaves from the drawn tower (a row that reads the shot's start height against the model's
   top); a tower site still shoots nothing.
7. Team colour: for four player colours (Bone, Crimson, Iron, Jade) the team part of each building reads as that
   colour at the default zoom (the measured rows M6-V26 used for the Keep, on all four).
8. Pictures, looked at before you report, saved to your scratch folder and listed in the report (the Producer
   copies them to `studio/review/`): each building at zoom 40 and 20; one Malazan base with all six models and the
   four boxes left; the Billet beside the Depot and the Crossbow Range beside the Barracks at zoom 40 (told apart
   or not: say what you see); the four in four player colours; a site and a remembered one.
9. Frame time (rules 61 / 77), the bench alone after the other suites have ended when you can: 400 figures with 60
   building models of the six types mixed, and 1,000 figures, each beside the boxes / capsules figure from the same
   window; 400 + 60 stays under 16.7 ms and the report says what the models cost over boxes. Judged on a quiet
   window: the Producer re-runs the pinned rows at the ACCEPT.
10. Sizes: each new `.glb` and atlas in bytes and the art pass's binaries' new total (Git LFS; `.glb` and `.png`
    are already in `.gitattributes`); the licensing log names the four source files.
11. Nothing under `sim/` or `game/data/` changes; under `tools/` only the three files named above.
12. Green: build `--no-incremental` 0 / 0 after the last edit; the default loop 0 failed (21 skipped on the base);
    `tools/qa/smoke.ps1` PASS; the scene loop green (124 on the base plus yours; `BenchTest`, `MinimapTest`,
    `DebugOverlayTest`, `QaM25Test`, `QaM27Test`, `QaH1Test` may flake under load and must pass alone).
13. Docs as in the scope, with a "Questions for the Producer" section in docs/03 "Implementation (M6-V28)"
    (rule 67): each pick that is a taste call, with your recommendation.

Design references: docs/04 "Building models, as built (M6-V26)", "Scale and dimensions", the style guide and the
Malazan palette; docs/03 "Implementation (M6-V26)" and "(M6-VH9)"; docs/01 2026-11-18 (b) (e) and 2026-11-20 (2);
`game/data/factions/malazan/buildings.json` (read only: ids and footprints).

Tests required: the scene rows for criteria 2-7 (headless where the base's are); the bench rows of 9; every bug fix
in passing gets a row that failed first.

Constraints: dumb views (no gameplay state in a view; picking reads what is drawn); no player-facing text in C#;
binaries through Git LFS only, no raw pack file committed, the licensing log in the same commit; visuals changed, so
run windowed, take the pictures and look at them before claiming it works. Process: PIDs recorded, end only your own
Godot / `testhost` (rules 52 / 69); scratch folders named `m6v28-...` (rule 63); the full default loop after the
last edit (rule 62). A smoke failure on stale import state (`Unrecognized UID`) counts only if the re-run fails.

QA focus (standard): the pictures first: does each building read as what it is, in every player colour, at zoom 40
and 60, and are the two 2 x 2 and the two 3 x 3 buildings told apart; clicks on every part of each model and beside
it (a unit in front, a unit behind, an enemy's building in front of an own one: BUG-1682's shape); the bar at the
closest zoom; the Watchtower's shot start on flat and high ground; the life cycle with fog (a remembered model that
is razed unseen, a site cancelled); a fresh-clone import for byte identity; the pointer-file and
`--placeholder-art` paths. Run the bench last, alone (rule 77), and say what else was running. Suites: the default
loop and the scene loop (rule 53); no Stress, no Perf category.

### Data: D36, the Teblor opening's second search, and BUG-1720 (feature, QA standard)

Goal: Find the Teblor `standard` opening that gives the Easy, Normal and Hard computer player its unit mix back
(BUG-1612), by a bounded search around the one-Shaman orders QA found (BUG-1721), and close the description
check's two gaps (BUG-1720). **Measured and reported only: no byte under `game/data/` this session** (the sim moves
the data hash); the order found ships next session (D37) after one re-measure on the draw rule's `main`.

Scope:
- (0) Nothing from the owner is waiting (inbox New is empty).
- (1) **The search.** Start from QA's table in `studio/bugs/BUG-1721-*.md` and `studio/qa/2026-11-25-0924-D-H10.md`
  (QA1, QRD1, QA1T); the tool is `QA/Content/DH10TeblorOrderQaStressTests` (D34's row through
  `D34TeblorStepOrderReportTests.Play`: 64 seats a difficulty, to 15:00). Write your orders in a **new** report
  class whose name starts `D36` (Stress category), not into the existing classes. **The levers (rule 78), each
  varied or said not to be:** the Shaman step's count (1 or 2) and its position; which of the three train steps
  (Breaker, Destrier Rider, War-dog) carry `throughReserve`; the Shamans' Circle's time; the Anointing step's time
  or position; the Carver's time; `noReserve` on the `armor_1` research step. Never `noReserve` on the expand step
  (docs/01 2026-11-24 ACCEPT (1)(e)). **At most twelve full-row orders** (a full row = Easy + Normal + Hard, 64
  seats each); reduced rows to choose candidates are free but are not evidence. Use at most 6 threads
  (`QA_DH10_THREADS` or your class's equal): the sim track's long suites run beside you.
- (2) **The bar** (docs/01 2026-11-24 ACCEPT (3)(c), unchanged; 64 matches a difficulty to 15:00): Easy: Anointing
  at least 20, Destrier Rider 20, War-dog 40, Shaman 25, Breaker 20, defeated at most 2; Normal: Anointing 17,
  Rider 22, War-dog 37, Shaman 20, Breaker 12, second Longhouse 18, defeated at most 10; Hard: Anointing 20, Rider
  40, War-dog 46, Shaman 22, Breaker 35, second Longhouse 18, defeated at most 12.
- (3) **The result.** If an order meets every figure: the whole opening as it would sit in `ai.json` goes to
  `sim/Rts.Sim.Tests/Content/Fixtures/teblor_standard_d36.json`, with a default-loop row that loads it through the
  shipped loader on a copy of the shipped data (valid against today's schema, the tooltip still true) and its full
  row in the page. If none does: no fixture; the table, and the nearest order named **by figures missed, then by
  seats short**.
- (4) `docs/factions/teblor.md` "The unit mix": the D36 table added; BUG-1721's corrections (the nearest order,
  the "No candidate brings Hard's Riders past 37" sentence, Question 1's premise); the Producer's answers to
  D-H10's Questions 1-2 written in (docs/01 2026-11-25 ACCEPT (4); rule 67); a "Questions for the Producer" section
  for anything new, each with your recommendation.
- (5) **BUG-1720** by docs/01 2026-11-25 ACCEPT (5): in `Content/UnitHitBonusText`, "other ... within" counts as
  pack words only in a sentence that is not an on-death sentence ("dies", "death", "dying"); a percentage beside
  "hidden" / "stealthed" is the first-shot bonus only with first-shot words ("first shot", "first hit", "from
  hiding") or when the unit has the field. The two skipped rows in `QA/Content/DH10TextPinQaTests` are un-skipped.
- Files you may change: `docs/factions/teblor.md`; under `sim/Rts.Sim.Tests/Content/` and `QA/Content/`: new files
  named `D36*`, the fixture, `UnitHitBonusText*` and its tests, `DH10TextPinQaTests`. **Do not edit any other
  existing content class this session** (the sim developer may re-pin literals in them for its hash move).
- OUT of scope: any file under `game/data/`; C# outside the two content folders; the reserve rules themselves; an
  opening per difficulty (a schema request, the Producer's call if the search fails); a restated bar; the other
  factions' openings; the Shadow roster; BUG-1612's closing (D37).

Acceptance criteria:
1. The report lists every lever of (1) as varied (with the values tried) or not varied (with the reason), and at
   most twelve full-row orders, each with its `ai.json` steps or its difference from QRD1 / QA1.
2. A table of every full row: order | Easy | Normal | Hard (defeated / 2nd Longhouse / Shaman / Breaker / Rider /
   War-dog / Anointing) | figures missed | seats short, with the shipped order's row re-measured on this base as
   the first line (it must equal D-H10's: Easy 1 / 0 / 16 / 30 / 4 / 27 / 28, Normal 9 / 23 / 15 / 14 / 22 / 37 /
   17, Hard 11 / 23 / 22 / 39 / 33 / 46 / 23; if it does not, stop and say so).
3. Either the fixture of (3) with its load row and a statement "meets every figure" backed by its row, or "none of
   these twelve meets the bar" with the nearest named by figures missed, then seats short. Neither outcome fails
   the task; a wrong "nearest" or a fixture whose row misses a figure does.
4. `git diff <base> -- game/` is empty; the shipped data hash still equals the golden header's (the existing rows).
5. teblor.md says what (4) asks and its prose pins are green; no sentence on the page contradicts the table.
6. BUG-1720: the two QA rows un-skipped and green; new rows for each half of the ruling in both directions (an
   on-death "other Zealots within 6 m" is not the pack's reach, a true pack sentence still is; a status percentage
   beside "hidden" without first-shot words is not the first-shot bonus, with the words or the field it is); every
   shipped description of the three factions and the Shadow page's still passes.
7. Green: build `--no-incremental` 0 / 0 after the last edit; the default loop
   (`--filter "Category!=Perf&Category!=Stress&Category!=Soak"`) 0 failed, 19 skipped (21 on the base less the two
   BUG-1720 rows), run in full after the last edit (rule 62); your `D36` report class is Stress and not in it.

Design references: docs/01 2026-11-24 ACCEPT (1)(e) and (3)(b)(c), 2026-11-25 ACCEPT (4)(5);
`docs/factions/teblor.md` "The unit mix (BUG-1612, D-H10)" and the opening's tooltip; docs/03 "Implementation
(M7-8)" (the two switches: research and build steps ignore the reserve, train steps obey it, a step behind a
waiting expand step spends only above the reserve: file order is behaviour); `studio/bugs/BUG-1721-*.md`,
`BUG-1720-*.md`, `BUG-1612-*.md`.

Tests required: the `D36` report class (Stress); the fixture's load row if there is a fixture; BUG-1720's rows
(failing first).

Constraints: player-facing text only in data (none changes); no C# outside the two content folders; no data byte.
Process: PIDs recorded, end only your own test hosts (rules 52 / 69); scratch folders named `d36-...` (rule 63);
bug-file updates as a patch in your scratch folder.

QA focus (standard): rule 75: before agreeing with "none meets the bar" (or with the order found), try up to four
full-row orders of your own on levers the report says were not varied, and re-run the developer's best row and
the shipped row to confirm the figures; check the fixture is the order the table measured (load it, diff its
steps against the report class's order) and that it carries no `noReserve` on the expand step; check "nearest" by
the stated rule; read teblor.md against the table line by line. BUG-1720: adversarial sentences for both halves
(an on-death sentence with "other" and "within" in different clauses, "death" inside another word, a pack sentence
that mentions a death; "hidden" with a first-shot bonus on a unit without the field). Suites: the task's filters,
the default loop with the filter of criterion 7, and only the `D36` / `DH10` Stress rows you re-run (rule 53); cap
your threads at 6.

## Proposed briefs (superseded by the plan above; kept for the record)

### Sim: M6-S13, a match with the gold gone ends in a draw (owner decision, inbox 2026-11-25; BUG-1536)
Design: docs/01 2026-11-25 owner-decision row. A match-end rule on the sim's tick, for the whole match: no side has
a unit that can fight and none can afford or train one (no gold banked, none left to mine or no worker to mine it),
held for a wait kept in `game/data/common/rules.json`; the result is a draw. Feature, QA full. One hash move (golden
header regenerated with the reason in `GatherWedgeQaTests`' comment; no hash literal: rule 73). Save format only if
the wait's counter must be saved (then rule 50: name the view scenes that pin save bytes). Expected to move: the
four-seat stalled rows (un-skip those that now end in a draw, with their new end ticks); every other pinned match
must be hash-equal up to its old end (a twin with the rule's wait set out of reach proves "off is identical").
Out: any AI-play cure (BUG-1661, 1660, 1555, 1554 / 1556, 1558: the session after), the view's result screen.

### View: M6-V28, the next Malazan building models
The import path exists since M6-V26 (`tools/art/build_building.py`, `BuildingModels`); two of 30 building types are
models. Next: the other Malazan buildings the Ultimate Fantasy RTS pack covers (the brief names them from docs/04's
table), pictures at zoom 40 and 20, the bench alone (rules 61 / 77), pick boxes by drawn height (M6-VH9's path).
Fold in nothing else; BUG-1710 waits for the view's hardening. If the sim's draw rule needs a result text, that is
the view's task after this one (M6-V29), not beside it.

### Data: D36, the Teblor `standard` opening, second search (BUG-1612, BUG-1721), then BUG-1720
No byte under `game/data/` (the sim moves the hash). Start from QA's table (`studio/bugs/BUG-1721-*.md`,
`studio/qa/2026-11-25-0924-D-H10.md`; `QA/Content/DH10TeblorOrderQaStressTests` is the tool): QA1 (A with Shaman
count 1; misses Easy Anointing 10 / 20, Normal Breaker 11 / 12 and Anointing 13 / 17, Hard Rider 38 / 40), QRD1
(`throughReserve` on Rider and War-dog only, Shaman count 1; misses Easy Shaman 17 / 25 and Anointing 18 / 20,
Normal Shaman 19 / 20 and Anointing 12 / 17; every Hard figure met), QA1T (Carver and Anointing at 9:30). **Rule 78:
the brief names the levers** (the Shaman step's count 1 / 2 and position, which of the three train steps carry
`throughReserve`, the Circle's time, the Anointing step's time or position, `noReserve` on the armor_1 research step;
never on the expand step) and the report says which were not varied. Up to twelve full-row orders. Same bar (docs/01
2026-11-24 ACCEPT (3)(c)). Result: the order that meets every figure is written to
`Content/Fixtures/teblor_standard_d36.json` with its table, to ship next session after one re-measure on the draw
rule's `main` (rule 41); if none does, the table and the nearest order **by figures missed and seats short**, and
the Producer then chooses between an opening per difficulty (a sim schema request) and a restated bar. Also:
teblor.md's "The unit mix" corrected (the nearest order, the "Riders past 37" sentence, Question 1's premise);
BUG-1720 by the ruling in docs/01 2026-11-25 ACCEPT (5), the two QA rows un-skipped (skips 21 -> 19).

## Where things stand

- **`main` after this session = sim 68755b7 + view b6683e8 + data c31d8ef** (plus this ACCEPT's studio and docs
  files). Scratch clone `prod0924-accept`: build `--no-incremental` 0 / 0, default loop **10,301 passed / 0 failed / 21 skipped of 10,322 in 22 m 19 s**, smoke
  **PASS**, scene loop **124 of 124, first pass**.
- **The data hash did not move: A732F387C2BB7E4D.** Saves and recordings keep loading. Formats unchanged.
- **Test helpers (M7-H3):** `HangGuard.Run(work, limit, out error)` (own thread); `ChildProcessLimit.Wait`
  (processor-time limit through a Windows job object, wall ceiling `WallCeilingFactor` = 3x), behind
  `MapFileTests.RunChild`. `Ai/TeblorSwitchOrders` orders: "none" (switches removed), "shipped", "A", "B".
  docs/03 "Data format", "A JSON `null`": most optional fields and the two switches read `null` as left out; only
  `gatherOverlap`, a unit's `carry` and `attack.reserveArmyShare` refuse it (`Data/DataNullPolicyTests`).
- **Dropped-order notices (M6-V27):** `ViewApi/OrderNotices.Collect` (spans in, `OrderNotice` out),
  `ViewApi/NoticeLines` (64 lines, 2.5 s, 0.5 s fade), `game/scripts/OrderNoticeView.cs` (reads after every tick the
  runner runs; off in replay), `OrdersText`, `ui.json` `orders.queue_full`. Only a refused Build and a full queue are
  announced. **124 scenes** (`OrderNoticeTest`, QA's `QaM6V27Test`). `SelectionController.OwnBuildingAt` returns
  nothing when another player's building or ghost is drawn in front (BUG-1682).
- **The Teblor `standard` (unchanged since D34):** 64-seat rows (defeated / 2nd Longhouse / Shaman / Breaker /
  Rider / War-dog / Anointing): Easy 1 / 0 / 16 / 30 / 4 / 27 / 28; Normal 9 / 23 / 15 / 14 / 22 / 37 / 17; Hard
  11 / 23 / 22 / 39 / 33 / 46 / 23 (misses 5 figures of the bar, 50 seats short). Best known: QA1 and QRD1 miss 4
  (17 and 16 seats short). The figures swing a lot for small edits (the Circle and Shaman ahead of the Breaker:
  Normal Breakers 13 -> 0).
- **Description checks (D-H10):** `PassiveContentTests` reads a stand-still stealth sentence as `stillStealth`;
  `UnitHitBonusText` reads a percentage as a hit bonus only with the field or first-shot / pack words; open:
  BUG-1720 (on-death "other ... within", a status percentage beside "hidden").
- **Pins:** unchanged this session (no pin class, fixture or golden in any diff).
- **Skipped / known rows:** default loop 21 (above). Stress: the BUG-1621 row,
  `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, the five `KeepCornerCureQaStressTests` rows.
- **Requests:** 55 done; 58 done (both halves); 56: G1-G3 built, G4-G8 open.
- **Queues after the proposals above.** Sim: the AI-play session (BUG-1661 first and alone, then BUG-1660
  `ai.minWorkers` from 4, 1555, 1554 / 1556 with 1490, 1558; one pin move; un-skip the Vathar four-seat row with the
  cure), then Request 56 G4-G8 (Shadow rules). View: the draw result text if needed, more building models, the
  mounted units and machines once packs are on disk. Data: D37 ships D36's order; then the Shadow roster as its rules
  land; owner tweaks first whenever the inbox has them.
- **Scratch clones left in the session's scratchpad** (harmless; the owner's housekeeping list): `prod0924-plan`,
  `prod0924-accept` (the Producer's), `m7h3-*`, `m6v27-*`, `dh10*`. The stray empty
  `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout.
- **Usage at the ACCEPT:** weekly 14 % / 15 % (stop at 95 %).

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
M7-H3 (the hardening) on `main`. Counter **0 / 4.** Closed: BUG-1582, BUG-1539, BUG-1620 (by ruling); BUG-1662
item 1, BUG-1557's label, BUG-1433 item 3. New debt: BUG-1700 (S4, a stale comment). Carried: BUG-1660, 1661
(S3; behind BUG-1621 / BUG-1610, whose Stress row stays skipped), 1555, 1554 / 1556, 1557 (S3), 1558 (S4), 1463,
1536 (the owner's draw rule cures it), 1537, 1490, 1434, 1433, 1161, 1149, 1152, 1150, 1010. The answer to
M7-H3's Question 1 is in docs/01 (2026-11-25, the ACCEPT row (1)): keep.

### Next task candidates
1. **M6-S13, the owner's draw rule (inbox 2026-11-25, docs/01; feature, QA full, one pin move, one hash move so
   the data track ships no data beside it; 20 bug ids):** a match-end rule in the tick: no side has a unit that can
   fight and none can afford or train one (no gold banked, none reachable to mine or no worker), held for
   `rules.json`'s wait (propose 3 game minutes, measured); the existing Draw outcome; save format and replay
   checkpoints checked (rule 50); un-skip / re-measure BUG-1536, BUG-1523's kin and the Pale Hills four-seat stalls
   (seeds 1, 3, 4, 6, 7); docs/02 win condition. "Off is identical": a twin with the wait out of reach is
   hash-equal on every pinned match. At PLAN: check the view's Draw banner text and the `match` CLI's result line
   need nothing new, else a view rider (M6-V29). Write M7-H3's answer into docs/03 (rule 67).
2. **The AI-play session (feature, QA full, one pin move; 20 bug ids; the data track measures no opening beside
   it):** BUG-1661, then BUG-1660, BUG-1555, BUG-1554 / 1556 with BUG-1490 (a save format change: rule 50),
   BUG-1558. Each cure measured apart (rule 70). BUG-1700's comment with it.
3. **M8-4:** Request 56 G4 (the Hounds' 7-alive limit) and the next of G5-G8, on test data only.
4. Requests 54, 53 part two. A movement task for BUG-1463 only with a design first.

---

## View track

### Where we are
M6-V27 on `main`. Counter **1 / 4.** 124 scenes. Closed: BUG-1682. New debt: BUG-1710 (S4; the lost-cast cue in
replay playback). Request 58 is complete. The Producer's answers to M6-V27's Questions 1-4 are in docs/01
(2026-11-25, the ACCEPT row (3)): keep, keep, keep, BUG-1710 in the next hardening.

### Next task candidates
1. **M6-V28, more building models** (the other eight Malazan buildings; about 1,900 lines with its scene: split
   in two if the PLAN's size check says so; the bench alone: rule 77). Write M6-V27's answers into docs/03.
2. **M6-V29, the draw result on screen**, only if M6-S13's "for the view track" note says the existing banner or
   text does not cover it.
3. The two cavalry, the Destrier Rider, the War-dog, the Catapult and the Ram once packs are on disk (owner).
4. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...", "priest veil: ...",
   "buildings ok", "whirlwind colours ok" / "priest: white robe", "outline hidden units", "order notice: ...",
   "announce every dropped order").

---

## Data track

### Where we are
D-H10 (the hardening) on `main`; no game data changed. Counter **0 / 4.** Closed: BUG-1691, BUG-1690, BUG-1602.
New debt: BUG-1721 (S3; the search's missing lever and the page's wrong "nearest"), BUG-1720 (S3; two QA rows
skipped). Open: BUG-1612 (the unit mix), BUG-1421 item 1, BUG-1611 (S4). The Producer's answers to D-H10's
Questions 1-2 and BUG-1720's wording are in docs/01 (2026-11-25, the ACCEPT row (4), (5)).

### Next task candidates
1. **D36, the Teblor opening's second search (QA standard; no byte under `game/data/`; files:
   `docs/factions/teblor.md`, the two content test folders):** (0) the owner's answers, if any. (1) the search as
   in "Proposed briefs" above (rule 78's levers; QA1 / QRD1 / QA1T as the start; up to twelve full-row orders; the
   order that meets the bar to `Content/Fixtures/teblor_standard_d36.json`). (2) teblor.md corrected (BUG-1721)
   and the answers written in (rule 67). (3) BUG-1720, the two QA rows un-skipped.
2. **D37:** ship D36's order after one re-measure on the draw rule's `main` (the pin list of D-H10's brief, one
   hash move, the owner's review table), or the Producer's ruling if none met the bar.
3. The hour-long Teblor Stress report classes D34 did not re-run, malazan.md's "Age II against the Whirlwind"
   paragraph.
4. The Shadow roster as Request 56's schemas reach `main` (G1-G3 are there; G4-G8 are not).
