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

## Current session plan (2026-11-26-0207, session 1 / 8 of 2026-11-26; written at the PLAN)

All three tracks GO, all feature sessions (counters before: sim 1 / 4, view 2 / 4, data 1 / 4). Bug ids: sim
1770-1789, view 1790-1799, data 1800-1809. **No hash move this session:** the sim and the data track ship no byte
under `game/data/`; the view adds text keys to `game/data/common/ui.json` only (view data: the sim never loads it,
so it is not in the data hash; handed to the view track by name this session, the sim does not touch it). Inbox New
was empty. Rule 54's rehearsal is not needed (Request 59 adds an optional field with no shipped entry; no default
any test reads).

PLAN checks on scratch clone `prod0207-plan` at base 1e8652c: build `--no-incremental` 0 errors / 0 warnings; the
default loop **10,447 passed / 0 failed / 21 skipped of 10,468 in 23 m 12 s**; smoke **PASS**. Verified claims:
`match --map pale_hills --players 4 --seed 4` ends `result draw at tick 63124 (52:36)` (the session log's pin);
six Malazan building model folders exist of 30 building types (M6-V28). The bug index holds one row each for
BUG-1730, 1731, 1750, 1760, 1761, with 1720 / 1721 fixed and 1536 closed (no lost or doubled row).

### Sim: M7-9, Request 59: a build order per difficulty inside an opening (feature, QA full)

Goal: Let an `ai.json` opening carry its own build order for a difficulty, so the data track can give the Teblor's
Easy and Hard computer players the orders that were measured to work for them (D38, next session). Schema, loader,
validation, content hash and the executor, on test data only. An opening that leaves the field out plays, hashes,
saves and loads exactly as today.

Scope:
- **Schema.** An opening gains an optional object `buildOrders` whose keys are difficulty ids
  (`DataLimits.AiDifficultyIds`: `easy`, `normal`, `hard`) and whose values are step lists under exactly
  `buildOrder`'s rules (the same `BuildAiSteps`, errors at `openings[i].buildOrders.<difficulty>[k]...`, the same
  step limit, the two M7-8 switches, the expand rules). `buildOrder` stays required: it is the list of every
  difficulty without an entry. Example: `"buildOrder": [ ... ], "buildOrders": { "easy": [ ... ], "hard": [ ... ] }`.
- **Producer rulings (write them into docs/03):** (a) entries for all three difficulties are one error at
  `openings[i].buildOrders` (then `buildOrder` could never be played: "write one of them as buildOrder"); (b)
  `"buildOrders": null`, `{}`, an entry that is `null` and an entry that is `[]` are each one error (leaving the field
  or the key out is how a file says "no entry"); (c) an unknown key is an error under the loader's existing
  unknown-property policy; (d) an entry equal to `buildOrder` step for step is allowed; (e) `description`,
  `compositions` and `attack` stay one per opening.
- **Data type.** `AiOpening` gets the per-difficulty lists and one accessor, `BuildOrderFor(AiDifficulty)` (the
  entry, else `BuildOrder`).
- **The AI.** `AiPlayer` resolves its list once at construction (a cached field, e.g. `Steps`) and every reader uses
  it: the `Blackboard`'s step-array size (`AiPlayer.cs` line 47), `BuildOrderExecutor.Run` and `ReopenLost`, and
  anything else indexed by step (the reserve step, the rebuild counters). Nothing under `sim/Rts.Sim/Ai/` reads
  `Opening.BuildOrder` afterwards.
- **Content hash.** `GameData.AddOpening` hashes the lists only when the field is present (a marker, then each
  present difficulty's id and steps, in difficulty order), so today's data hashes as today and the golden's header
  holds.
- **Saves and replays.** No format change. A seat's saved step arrays are as long as its own list; check what a
  save whose step-array lengths differ from the seat's list does today (refused) and that it still does.
- **Docs.** docs/02 "AI opponent" (one paragraph); docs/03 "Implementation (M7-9)" with a **"For the data track"**
  row (the JSON shape, rulings (a)-(e), which list a seat plays, that file order is behaviour inside each list) and
  the `ai.json` schema lines; a "Questions for the Producer" list with recommendations (rule 67). Also write the
  Producer's answers to M6-S13's Questions 1-5 (docs/01, the 2026-11-26 row, (1)) into docs/03 "Implementation
  (M6-S13)".
- **Test data only.** Forge the openings on copies of the shipped data; a helper that forges `buildOrders` must
  tolerate the shipped file already carrying it (look the opening up, then add or edit: rule 58, D38 lands next
  session).
- OUT of scope: any byte under `game/data/**` (factions and common; `game/data/common/ui.json` is the view's this
  session); any other change to how the computer player plays (BUG-1661, 1660, 1555, 1554 / 1556, 1490, 1558 are
  the next sim session); per-difficulty compositions, attack thresholds or time offsets (a Question if you think one
  is needed); BUG-1730, BUG-1731; existing classes under `sim/Rts.Sim.Tests/Content/` and `QA/Content/` (the data
  track's; one exception in criterion 5).

Acceptance criteria:
1. Loader: a file with one or two entries loads; each of rulings (a), (b) (four cases) and (c) has a row with its
   path and message; the step errors of `buildOrder` reproduce under a `buildOrders.<difficulty>` path (at least:
   unknown id, another faction's id, a switch on the wrong action, a duplicate step, an expand step with an id, a
   count out of range, more steps than the limit); several errors in one file are all reported.
2. `BuildOrderFor`: for each of the three difficulties, with and without an entry, the right list.
3. **Twin proof, each difficulty:** a seat of difficulty D on data {`buildOrder` A, `buildOrders.D` B} issues the
   same command stream and reaches the same unit and building census as a seat of difficulty D on data
   {`buildOrder` B}, tick for tick, for at least 12,000 ticks on one two-seat match per difficulty (compare the
   state hash every tick if it does not fold the data hash; else the commands and the census); and a seat of another
   difficulty on the first data plays A (same twin against {`buildOrder` A}). A and B must differ in a step that
   comes due before tick 2,000, in a rebuilt building, in a `throughReserve` train step and in the expand step's
   position, so the rebuild rule, the reserve and both switches run on a per-difficulty list.
4. **Off is identical:** `git diff <base> -- game/` is empty; the golden replay and its header are unchanged; the
   shipped content hash equals the golden header's (the existing rows; never a literal: rule 73); the default loop
   is green with no existing expected value edited.
5. The only existing tests edited are shape pins that count `AiOpening`'s / the JSON type's members
   (`DataContentHashTests` and `DataNullPolicyTests` gain rows). If a class under `Content/` or `QA/Content/` goes
   red only because `AiOpening` gained a member, make the smallest fix there and name the file in the first lines of
   your report (the data developer edits only `UnitHitBonusText*`, `D36Bug1720QaTests` and new `D37*` files).
6. Hash: data with the field differs from data without; two files differing in one step of `buildOrders.hard` hash
   differently; the key order inside the JSON object does not change the hash.
7. Save: a save taken mid-order (steps done, one command pending) by an Easy and a Hard seat on per-difficulty data
   loads and stays tick-identical to the unsaved twin for 6,000 ticks; a save with a wrong step-array length is
   refused.
8. Determinism: a four-seat match (easy, normal, hard, hard seats of the forged faction) twice in one process and
   once across two processes: equal hashes at every checkpoint.
9. No new allocation in a think (the list is a cached field); the existing AI cost and allocation rows are green.
10. A source row: no file under `sim/Rts.Sim/Ai/` contains `Opening.BuildOrder`.
11. Docs as scoped; `DataValidationTests` green on the shipped data.
12. Green: `dotnet build RtsGame.sln --no-incremental` 0 errors / 0 warnings after the last edit; the default loop
    (`--filter "Category!=Perf&Category!=Stress&Category!=Soak"`) 0 failed, 21 skipped, run in full after the last
    edit (rule 62). Do **not** run the whole Stress category (rule 53: no tick changes with the field absent); run
    your own new Stress rows only.

Design references: `studio/STATE.md` "Requests for the sim track", Request 59; docs/01 2026-11-26 row (3); docs/03
"Implementation (M5-8a)" (openings), "(M5-2)" (the executor's semantics), "(M5-4g)" (the reserve), "(M7-6)"
(`reserveArmyShare`: the model for an optional field hashed only when present and its `null` policy), "(M7-8)" (the
two switches); `sim/Rts.Sim/Data/DataLoader.Ai.cs`, `AiOpening.cs`, `GameData.cs` `AddOpening`,
`Ai/BuildOrderExecutor.cs`, `Ai/Blackboard.Save.cs`.

Tests required: loader rows (criterion 1), accessor rows, the three twins, hash rows, the save rows, the
determinism rows (the two-process one may be Stress), the source row.

Constraints: no stat or order in C#; no allocation per think; no `Dictionary` iteration where order matters (the
JSON object's keys are read in difficulty order); no Godot in `Rts.Sim`; docs in the same commit. Process: record
the PID of every `dotnet test` you start and end only those (rules 52 / 69); scratch folders named `m79-...` (rule
63); bug-file updates as a patch in your scratch folder (you cannot write `studio/**`).

QA focus (full): attack the loader (every ruling, mixed valid and invalid entries, a 10,000-step entry, an entry
naming another faction's units, the same step in `buildOrder` and an entry, keys in odd case such as `Easy`);
prove "off is identical" yourself: per-tick hashes of three shipped-data AI matches (one per map, one with a Teblor
seat) against the base commit; apply the "For the data track" row to the shipped Teblor `ai.json` on a scratch
clone (K9KC10S as `easy`, QRD1 as `hard`, from `Content/D36TeblorOrderSearchReportTests.AllVariants`) and check it
loads, that the Easy seat's first 15 minutes equal the K9KC10S-as-`buildOrder` run, and which sim-owned tests go
red on that copy (rule 58: a list for D38's brief, not a finding unless a helper breaks); saves across the
difference (a save made on per-difficulty data refused on data without it, by the hash); an opening picked by a
Normal / Hard seat's seeded draw with entries in only one of two openings. Suites: the default loop on the commit
with your files, then these Stress pin classes once (any moved value is S2): `AiDeterminismTests`,
`HuntStressTests`, `M6Sh2DeterminismStressTests`, `M6Sh2QaStressTests`, `StarvedSaveTwoProcessQaStressTests`,
`ReservePairingsStressTests`, `FarExpandProbeQaStressTests`, `DrawStalledRowsReportTests`; not the whole Stress
category; Perf alone under rule 42 last. End with the `--no-incremental` build (rule 65).

### View: M6-V29, the draw on screen: the banner, the stats screen and a countdown (feature, QA standard)

Goal: The owner's draw rule is in the sim (M6-S13), but a drawn match still shows "Victory" to a player who is
standing, and nothing tells the player the draw clock is running. Show "Draw" wherever a result is shown, and a
countdown while the clock runs, so the player knows the match is about to end and that a kill restarts the clock.

Scope:
- **The result.** `Match.ResultFor` reads `World.MatchDrawn` before it looks for a winner. Producer ruling: in a
  drawn match the local player's side gets **Draw** if it stands at the draw, and keeps **Defeat** if the whole
  side was out before it (nobody is defeated by a draw, and nobody who lost is un-defeated by one); the same answer
  whether the result is latched live, read after loading a save, or reached in replay playback.
- **Every place a result or a winner is shown:** the banner, the stats screen (title, any per-row or per-team
  result; a standing seat's "Defeated at" cell stays "Standing"), the Load Game list if it names a result. Find them;
  a drawn match names no winner anywhere.
- **The countdown notice.** Shown exactly while `World.DrawTicksLeft >= 0` (the clock runs), from its first tick
  (Producer decision: the state is rare and the player should learn at once why the match will end and what stops
  it; the alternative, only the last 60 seconds, is one inbox line away). Text from `game/data/common/ui.json`, a
  new key (proposed English: "No player can train new units. Draw in {0} unless a unit falls."), the time as the
  remaining game time rounded up to a second in the existing clock format. It restarts with the clock, disappears
  the tick the clock stops or the match ends, shows in replay playback and after loading a save taken mid-clock,
  never takes a click, and sits clear of the resource bar, the alert text, the order notice, the minimap and the
  command card at 1152 x 648 and 1920 x 1080. Its label text is rewritten only when the shown second changes.
- **Scenes.** A new scene (e.g. `DrawViewTest`) on a data copy with a short `draw.afterSeconds`: a spent two-seat
  match shows the notice, it counts down, a kill restarts it, the draw shows "Draw" (not "Victory") and the stats
  screen agrees; a local player out before the draw reads "Defeat"; a team match (local out, ally standing at the
  draw) reads "Draw"; a recorded drawn match in playback and a save taken mid-clock show the same. A scene that
  writes store arrays directly calls `World.RecountDraw()`. Pictures at zoom 40 of the notice and of the banner
  into your scratch folder (the Producer copies them for the owner).
- **Docs.** docs/02 "Game flow" 4: replace the sentence "Until the view's next task (M6-V29) the end-of-match banner
  reads the old rule..." with what is built; docs/03 "Implementation (M6-V29)" with Questions for the Producer
  (rule 67); write the Producer's answers to M6-V28's Questions 1-5 (docs/01, the 2026-11-26 row, (2)) into docs/03
  "Implementation (M6-V28)".
- Files: `game/**` outside `game/data/**`, plus `game/data/common/ui.json` (new keys only; handed to you this
  session). No ViewApi change is expected (`MatchDrawn`, `DrawTicksLeft`, `DrawWaitTicks` are public on `World`).
- OUT of scope: any file under `sim/` (a need goes to "Requests for the sim track" in your report); any other data
  file; an alert sound for the countdown unless it is one line reusing an existing clip; building models; BUG-1710.

Acceptance criteria:
1. A drawn match with the local side standing shows "Draw" on the banner and the stats screen, live, in replay
   playback and after a load; never "Victory" (scene rows for each).
2. A local side out before the draw shows "Defeat"; a local player out with an ally standing at the draw shows
   "Draw" (scene rows).
3. No screen names a winner of a drawn match (the stats screen, the Load Game list if it shows results).
4. The notice is visible on every tick with `DrawTicksLeft >= 0` and on no other tick, across: the clock starting,
   a restart by a death, the clock stopping without a draw (a seat can train again), the draw, a win while it
   runs (scene rows; a row that steps the sim through `SimRunner.Ticked`).
5. The shown time equals `ceil(DrawTicksLeft / 20)` seconds in the clock format, on the first tick, mid-way and on
   the last tick; with `DrawWaitTicks == 0` (no rule in the data) the notice never shows.
6. The notice takes no click (a click through it selects what is under it) and overlaps none of the listed HUD
   parts at both sizes (rect rows and the pictures).
7. Every text is read from `ui.json`; the diff adds no player-facing C# literal; `ui.json` gains keys and changes
   none; no other file under `game/data/` changes; `git diff <base> -- sim/` is empty.
8. The existing scenes that play to a result are green unchanged in what they assert, or changed with the reason
   stated: `MatchOverViewTest`, `QaM6V4Test`, `FreeForAllTest`, `TeamsTest`, `AiSeatTest`, `QaM6V5Test`,
   `QaM6V11Test`, `QaM6V14Test`, `QaM6V15Test`, `QaM6V16Test`, `ShellTest`, `StatsScreenCheck`,
   `ReplayPlaybackTest`.
9. Green: build `--no-incremental` 0 / 0; `powershell -File tools/qa/smoke.ps1` PASS; the scene loop all green
   (126 on the base plus yours); the default loop (`--filter "Category!=Perf&Category!=Stress&Category!=Soak"`)
   0 failed, 21 skipped, run in full after the last edit (rule 62).
10. Docs as scoped; you looked at the two pictures before reporting and say what they show.

Design references: docs/02 "Game flow" 4 (the draw paragraph); docs/01 2026-11-26 row (1) (a draw is final) and
(2); docs/03 "Implementation (M6-S13)" (the accessors), "(M6-V4)" and "(M6-V16)" (the banner's rule and sides),
"(M6-V27)" (the order notice: the model for a small data-text notice); `game/scripts/Match.cs` `ResultFor` /
`OnTicked`, `MatchOutcome.cs`, `MatchOverBanner.cs`, `MatchStats.cs`, `AlertHud.cs`, `OrderNoticeView.cs`.

Tests required: the new scene with the rows above; any ViewApi-free unit rows you find useful.

Constraints: views hold no gameplay state (the notice reads the sim each frame or tick, stores nothing but what it
shows); player-facing text only in data; C# only; no sim change. Process: PIDs recorded, end only your own Godot /
test hosts (rules 52 / 69); scratch folders named `m6v29-...` (rule 63); a smoke that fails once on stale import
state is re-run once.

QA focus (standard): the edges of the notice (the tick the clock starts, each restart, the last tick, a win and a
draw on neighbouring ticks, game speed 2x / 4x with several ticks a frame, pause, the pause menu open, a load
mid-clock, replay playback and a seek if playback has one); four-seat and team matches (which seats read Draw,
which Defeat); a long faction or text at 1152 x 648; the banner and stats screen on a drawn match where one seat
fell after the draw (the sim says a draw is final: the view must too). Suites: the default loop, the scene loop,
smoke; no Stress, no Perf.

### Data: D37, the Teblor orders per difficulty, measured (feature, QA standard; no byte under `game/data/`)

Goal: Have the three Teblor build orders ready to ship the moment the sim's Request 59 is on `main` (D38, next
session): find Normal's order now that it no longer has to serve Easy and Hard, confirm Easy's (K9KC10S) and
Hard's (QRD1) on the draw rule's `main`, and close two small check bugs. **Measured and reported only: no byte
under `game/data/` this session.**

Scope:
- (0) Nothing from the owner is waiting (inbox New is empty).
- (1) **Normal alone.** A bounded search for an order that meets every Normal figure of the bar. Start from the
  shipped order M (Normal: defeated 9, second Longhouse 23, Shaman 15, Breaker 14, Rider 22, War-dog 37, Anointing
  17: only the Shaman short) and RSC (9 / 23 / 31 / 12 / 26 / 38 / 13: only the Anointing short), with QA's QSH and
  QSR. **The levers (rule 78), each varied or said not to be:** the Shaman step's count (1 or 2) and position; the
  Shamans' Circle's time; the Anointing step's time and position; the Carver's time; which train steps carry
  `throughReserve`. Not `noReserve` on `armor_1` (inert before 15:00) and never `noReserve` on the expand step.
  **At most eight orders measured on the full 64 Normal seats to 15:00**; reduced screens to choose candidates are
  free but are not evidence. Write them in a **new** report class named `D37...` (Stress category). At most 6
  threads.
- (2) **Easy and Hard re-measured on this `main`:** K9KC10S on Easy's 64 seats and QRD1 on Hard's 64. A 15-minute
  match is never spent, so the draw rule cannot have moved them: they must equal D36's (K9KC10S Easy 1 / 0 / 38 /
  28 / 34 / 42 / 22; QRD1 Hard 11 / 23 / 23 / 40 / 42 / 47 / 25). If one differs, stop and say so in the first
  lines of your report.
- (3) **The bar** (docs/01 2026-11-24 ACCEPT (3)(c), unchanged): Normal: Anointing at least 17, Rider 22, War-dog
  37, Shaman 20, Breaker 12, second Longhouse 18, defeated at most 10. (Easy and Hard as in teblor.md.)
- (4) **Fixtures.** `sim/Rts.Sim.Tests/Content/Fixtures/teblor_standard_d37_easy.json`, `..._normal.json`,
  `..._hard.json`: each the whole `standard` opening as a plain opening (its order as `buildOrder`; today's schema,
  no `buildOrders` field), with a default-loop row per fixture that loads it through the shipped loader on a copy
  of the shipped data and checks its steps are the measured order's and the opening's tooltip is still true. The
  Normal fixture is the order that meets the bar, or, if none does, the nearest (by figures missed, then seats
  short), named as such.
- (5) `docs/factions/teblor.md`: a "The unit mix, per difficulty (D37)" section with the table (order | defeated /
  second Longhouse / Shaman / Breaker / Rider / War-dog / Anointing | figures missed | seats short) for every
  Normal order measured and the two re-measured rows; the lever table; the Producer's answers to D36's Questions
  1-3 written in (docs/01, the 2026-11-26 row, (3) and (4); rule 67); BUG-1761 ("below" -> "above"); new Questions
  with your recommendation.
- (6) **BUG-1760:** in `Content/UnitHitBonusText`, "first-shot" with a hyphen (and with any dash or several
  spaces) counts as first-shot words; QA's skipped row in `QA/Content/D36Bug1720QaTests` is un-skipped and green,
  with rows failing first.
- Files you may change: `docs/factions/teblor.md`; under `sim/Rts.Sim.Tests/Content/` and `QA/Content/`: new files
  named `D37*`, the three fixtures, `UnitHitBonusText*` and its tests, `D36Bug1720QaTests`. **No other existing
  content class** (the sim developer may make a one-line shape fix in one if `AiOpening` gaining a member turns it
  red).
- OUT of scope: any file under `game/data/`; C# outside the two content folders; the `buildOrders` schema (the
  sim builds it beside you; do not use it); shipping an order (D38); the other factions; the Shadow roster;
  closing BUG-1612 (D38).

Acceptance criteria:
1. The report lists every lever of (1) as varied (with the values tried) or not varied (with the reason), and at
   most eight orders on the full Normal row, each with its steps or its difference from M / RSC.
2. The Normal table, with M's and RSC's rows re-measured first (they must equal 9 / 23 / 15 / 14 / 22 / 37 / 17
   and 9 / 23 / 31 / 12 / 26 / 38 / 13; if not, stop and say so). Measure and report: either "order X meets every
   Normal figure" backed by its row, or "none of these meets the bar" with the nearest by the stated rule. Neither
   outcome fails the task; a wrong "nearest" or a claim its row does not back does.
3. The two re-measured rows of (2) equal D36's, or the report's first lines say which cell moved.
4. Three fixtures, each with a green load row, each carrying no `noReserve` on its expand step and no
   `buildOrders` field; the fixture's steps equal the order the table measured.
5. `git diff <base> -- game/` is empty; the shipped data hash still equals the golden header's (the existing rows).
6. teblor.md says what (5) asks, its prose pins are green, and no sentence on the page contradicts the tables.
7. BUG-1760: the QA row un-skipped and green; new rows in both directions (a description claiming a first-shot
   bonus with "first-shot" on a unit without the field fails the check; a true one passes); every shipped
   description of the three factions and the Shadow page's still passes.
8. Green: build `--no-incremental` 0 / 0 after the last edit; the default loop
   (`--filter "Category!=Perf&Category!=Stress&Category!=Soak"`) 0 failed, 20 skipped (21 on the base less the
   BUG-1760 row), run in full after the last edit (rule 62); the `D37` report class is Stress and not in it.

Design references: docs/01 2026-11-24 ACCEPT (1)(e) and (3)(c), the 2026-11-26 row (3) and (4);
`docs/factions/teblor.md` "The unit mix, second search" (the order names, the lever table) and "Questions for the
Producer (D36)"; docs/03 "Implementation (M7-8)" (file order is behaviour); `Content/D36TeblorOrderSearchReportTests`
(`AllVariants`, the row player), `QA/Content/D36TeblorOrderQaStressTests` (QSH, QSR, QSRS);
`studio/bugs/BUG-1760-*.md`, `BUG-1761-*.md`, `BUG-1612-*.md`.

Tests required: the `D37` report class (Stress); the three fixture load rows; BUG-1760's rows (failing first).

Constraints: player-facing text only in data (none changes); no C# outside the two content folders; no data byte.
Process: PIDs recorded, end only your own test hosts (rules 52 / 69); scratch folders named `d37-...` (rule 63);
bug-file updates as a patch in your scratch folder.

QA focus (standard): rule 75: before agreeing with the Normal result, try up to three Normal-row orders of your
own on levers the report says were not varied, and re-run the developer's best Normal row and both re-measured
rows; check each fixture is the order the table measured (load it, diff its steps against the report class's
order); check "nearest" by the stated rule; read teblor.md against the tables line by line. BUG-1760: en dash, em
dash, non-breaking hyphen, "first  shot" with two spaces, "First-Shot" in capitals, "first-shots", and a sentence
with "first" and "shot" far apart (must not count). Suites: the task's filters, the default loop with criterion
8's filter, and only the `D37` / `D36` Stress rows you re-run (rule 53); at most 6 threads.

## Proposed briefs (superseded by the plan above; kept for the record)

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
