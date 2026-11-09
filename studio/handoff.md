# Handoff: brief for the next session

**PLAN 2026-11-09-1336 (session 2 / 8 of 2026-11-09): sim M5-H6 (the end-of-M5 sim hardening, pulled forward), view STOP
(cheap), data D26a (D14 c's content, the part that moves no pin).** Checks: inbox New empty; `main` e27eefd builds 0 / 0,
smoke PASS, the default loop **8,245 / 0 / 18 in 19 m 28 s** (scratch archive, rule 38); verified `rules.json` `ai.farExpandReach` 384, `PaleHillsBound`
72,000, criterion 9 ticked, BUG-1160's attribute on `main`; open S1 / S2: none; sessions today 1 / 8 before this one. The
briefs are under each track's "Current session plan" below; the rest of this file is the 0426 ACCEPT's handoff, still current.

Written by the Producer at the ACCEPT step of session **2026-11-09-0426** (session 1 / 8 of 2026-11-09; sim only: view and data
STOP at PLAN). Sim **M5-9b ACCEPTED after one fix round, and with it M5-9a rounds 1-2: all of it is on `main` now.** The first
ACCEPT step found the default loop red on the branch (**BUG-1160**, S2: QA's new `QA/FarExpandQaTests.cs` read the allocation
counter without `[Collection(SerialCollection.Name)]`, so BUG-0024's guard failed, 8,244 / 1 / 18 twice) and REJECTED on that line;
fix round 1 added the attribute (4ace852, one line, no other change), QA re-checked (5a74b06), the Producer re-ran the default loop
on a fresh archive (**8,245 / 0 / 18**) and accepted. The terminal rule's outcome was **(b)**: Pale Hills seed 1 Normal v Normal
ends at **48,858 (40:42)**, seat 1, every check clean, so the three 1v1 Pale Hills rows are bound at **72,000** (a Producer
decision, docs/01) and BUG-1153 is S3. **M5 criterion 9 ticked: M5 8 / 9**, only criterion 6 (the owner's play) left; M6 5 / 10.
New S3 BUG-1157 (decided Pale Hills matches finish late: seeds 2 / 5 past 72,000, five Hard-seat-0 tally no-results), S3 BUG-1158
(the leg replaces killed workers without limit), S4 BUG-1159 (nits). `main` after the merge: build 0 / 0, the default loop
8,245 / 0 / 18, QA's Stress / Soak 510 / 0 / 5, `AiPerfTests` 6 / 6 alone, smoke PASS, golden `30C51BB619A7E650`, formats **8 / 9**,
replay 9 (an AI save from before refuses to load; human saves and recordings play). Counters after: **sim 2 / 4, view 1 / 4,
data 2 / 4.** Bug ids next: **all tracks from 1161.**

## Standing rules (1-47)

Unchanged 1-46 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle; 43: every sim task that
can change Normal AI behaviour names which M5-5 rows it expects to move and re-measures the bound comments and docs/03's M5-5 table
in the same commit, and a row that stops ending is a finding, not a re-bound; 44: a session that dies after ACCEPT is re-verified on
a scratch clone and integrated first, a new task starts on a fresh branch; 45: a brief whose task changes Normal AI behaviour says
what to do when a pinned criterion row stops ending; 46: a branch held twice gets a terminal acceptance rule in its third brief, so
no branch lives a fourth session). **New 47 (2026-11-09-0426, BUG-1160):** QA's re-check runs the **default loop on the commit that
includes QA's own suites** (not only the Stress / Soak complement), and a QA file that measures allocation declares
`[Collection(SerialCollection.Name)]` (BUG-0024's guard is in the default loop and reads the source).

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rule 42 before filing. The full non-Perf run is
  ~8,750 rows: ~1 h 15 m alone (QA: 22 m default + 1 h 04 m Stress / Soak on a clone). The default (non-Stress) loop is ~20 min
  with the AI rows now (not 2 min: the M5-5 literal row and the far-expansion rows are in it).
- QA runs the full non-Perf **once** per session (at the re-check, on the fixed commit, **with its own files in**) and the
  42-match tally once.
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
- A developer must never kill another track's Godot or `testhost` process.
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it
  (this session QA applied the developer's patches, which worked; either is fine).
- **A brief that relaxes a judge must name what the relaxed query cannot see** (M5-H5's lesson).
- **Pinned matches on `main` now (M5-9a + M5-9b):** `MatchCriterionTests` (Vathar 23,043
  seat 1 / Raraku 28,083 / **Pale Hills 48,858, bound 72,000** / generated **seed 2** 15,691, bound 24,000 / FFA 58,155 / 2v2 35,648),
  `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithinItsBound` (Pale Hills 48,858 / Vathar 23,043), `MapSlice2StressTests.KnownStalls`
  (Pale Hills 3 / 4 / 6 = 29,514, generated 6, Raraku 5 52,360), **skipped with the measurement: Pale Hills 2 (79,504) and 5
  (96,321) under BUG-1068 / 1116 / 1157**, `StandardPresetPinQaTests` (7 digests), QA's `NonHardMatchPinQaTests` (8 digests; 4 Pale
  Hills ones new), `HardKnobsPinTests`, the Malazan / Whirlwind page tables, `D21OpeningsQaTests` / `DH6ThresholdQaTests` +
  `KnownBeatenAlive`, the golden header, QA's `MatchCriterionSweepQaStressTests.Known` (BUG-1151 generated 4, BUG-1152 Raraku 1 / 5),
  the hunt rows on **seed 1** (`AiDeterminismTests`, `HuntStressTests` mid-hunt save), `HuntStressTests` FFA seed 2 (97,140) / seed 3 /
  2v2 seed 1; `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall` skipped (no result by 144,000: no free gold with four
  seats). The 8 pre-M5-9a digests reproduce at Normal strength 0 + `starvedWaveAfter` 0 + `farExpandReach` 0
  (`CounterCompositionTests.TheNonHardPins_*`). The Hard v Normal tally: 26 / 11 / 5.
- **Known stalls (skipped with ids on `main`):** Pale Hills 2 / 5 (BUG-1068 / 1116 / 1157), Pale Hills FFA seed 1 (BUG-1068 /
  1116), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), QA's `M5H5HuntAfterDefeatQaTests.A2v2_TheDefenseToo_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (BUG-1156),
  `FarExpandQaTests.ALegWorker_KilledAtAGuardedStart_*` (BUG-1158), `FarExpandQaTests.TheLeg_DoesNotWalkToAStartOutsideTheReach` (BUG-1159).
- **On the three map files seeds 3 / 4 / 6 are one match** (the seed only picks the openings).
- **Criterion 6 (the owner wins on Easy, loses to Hard) is owner-only:** M5 cannot be signed off by the Producer until the owner
  writes the result in the inbox.
- Every AI rule change moves Pale Hills digests now (both seats expand there): rule 43's re-measure is the full list above.
- A perf gap QA named: no `AiPerfTests` row has eight seats walking their legs on a map with free starts (the starved row's scene
  has every mine empty and no free start). A hardening item, or the next AI task adds the row.

---

## Sim track

### Current session plan: M5-H6, the end-of-M5 sim hardening (hardening, QA full)

**Goal.** Every sim-side M5 criterion is met and the milestone's hardening is owed before its sign-off (which waits on the
owner's criterion 6), so it runs now rather than at 4 / 4. The first item is the one regression `main` carries: a decided
AI-against-AI match on Pale Hills finishes late since the far expansion (BUG-1157; seed 5 was green at 52,557 and is skipped
at 96,321). Then the far expansion's own two bugs and the carried AI debt, batched to the size budget.

**Scope, in this order (stop where the budget ends; nothing below (c) is required).**
- **(a) The end game, BUG-1157 / BUG-1116 / BUG-1068 (uncertain: measure first, ~800 lines).** Measure each lever alone on
  Pale Hills seeds 2 and 5 Normal v Normal, the five Hard-seat-0 tally no-results (`--ai hard,normal`, seeds 2 / 3 / 6 / 7 / 8)
  and Pale Hills seed 1, then ship the combination that ends the most rows without moving seed 1 past 72,000 or the literal
  row past 24,000: (i) **the hunt looks at the start locations** (the start list every seat already reads for the leg,
  `PlayerView.StartCell`; the loser's remnant is now a depot and workers at a far start: sweep the free / defeated starts
  before the perimeter leg, or insert them into the ring order by distance); (ii) **the sweep order** (BUG-1116's note: the
  perimeter leg deferred while a local point is nearer than the reached ring point; judged on
  `EndGameHardeningTests.ALoneIdleWorker_*` + QA's hide / leg rows); (iii) **the house rule** (BUG-1116: reopen a House step
  only when `HalfPopCap - HalfPop <= AiRulesDef.HouseHalfPopMargin`, the economy's own test, `rules.json`
  `ai.houseWhenPopWithin` 2; re-measured now that a loser has far gold); (iv) BUG-1157's
  "a decided wave keeps its target on the loser's drop-offs". Print a table (lever × row: end tick / winner) in the report
  and in docs/03 "Implementation (M5-H6)". Goal: Pale Hills 2 / 5 un-skipped inside 72,000, the five tally no-results ended,
  seed 1 inside 72,000, the tally not inverted (Hard ahead on every map).
- **(b) BUG-1158 (small):** a leg-death cap: a leg worker killed is replaced at most `ai.scoutReplacements` (2) times in all
  (reuse the knob: the Producer's default; a new `ai.farLegReplacements` only if the scout's count and the leg's must differ,
  say why); past the cap the start is marked looked and the leg goes on (or ends). Un-skip QA's
  `FarExpandQaTests.ALegWorker_KilledAtAGuardedStart_*`.
- **(c) BUG-1159 (small):** `NextStart` filters by `farExpandReach`; a leg ended on a candidate (`EndLeg(stop: false)`) marks
  its start looked only if the start's last point was reached (else re-walked from the point, not the start); fix the
  `SaveRefusalTests.AFormat8Save_WithAForgedFarExpansion_*` summary (or add the generation check it claims). Un-skip
  `TheLeg_DoesNotWalkToAStartOutsideTheReach`.
- **(d) If budget remains, in this order:** BUG-1151 (mark the node when a Gather sent to a non-idle worker is dropped with
  its node unchanged) and BUG-1152 (every gold-affordable option blocked on wood → one gatherer to wood whatever the share),
  their `MatchCriterionSweepQaStressTests.Known` entries out; BUG-1149 (the defense's point through `SightingPoint`) and
  BUG-1150's push rule; the eight-legs perf row (`AiPerfTests`: 8 seats on their legs on a map with free starts, 1 ms / 0 B);
  the `ReopenLost` masking note (measure: a lost Camp hidden by a standing far depot of the same type); `HasMapId`'s note,
  `HUNT`'s vectors and tick range-checked as `AIST`'s; BUG-1058.
- **Out of scope:** `game/data/factions/**` (the data track's D26a runs beside this: rule 41; a page cell re-printed under
  rule 40 is the only allowed edit there, and only table cells, never the "AI build order (Normal)" prose the data track
  appends to), `ai.json` values, BUG-1156 (D26's), BUG-1010, the movement S3s (BUG-0911 / 0889 / 0144: each needs a failing
  row first, their own session), any save-format bump (if a lever needs a saved field, say so and pick another), any new
  knob beyond (b)'s, the view and `game/`.

**Acceptance criteria.**
1. Build 0 / 0; the default loop green on the final commit **with QA's suites in** (rule 47); `AiPerfTests` green alone
   (rule 42); smoke PASS; every skip carries a bug id; no fixed bug's row stays skipped.
2. Pale Hills seeds 2 and 5 Normal v Normal end inside 72,000 and are un-skipped in `MapSlice2StressTests` with the new
   ticks in the comments; Pale Hills seed 1 Normal v Normal ends inside 72,000; the literal row (Vathar seed 1) inside
   24,000; the generated row inside 24,000. If a lever cannot get a row there, the report says which and why, the row stays
   skipped with the new measurement, and criterion 2 is "met with a finding" only if at least seed 5 is back.
3. The 42-match Hard v Normal tally re-run once: no-results fewer than 5, Hard ahead on every map; the figure in docs/03's
   M5-5 reading and BUG-1157.
4. Every pin in the watch-out list re-measured in the same commit (rule 43: `MatchCriterionTests`, `MapFileTests`,
   `KnownStalls`, `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, `HardKnobsPinTests`, the faction page tables (rule 40),
   `D21OpeningsQaTests` / `DH6ThresholdQaTests` + `KnownBeatenAlive`, the hunt rows, `HuntStressTests`, QA's `Known`); a row
   that stops ending is a finding (rule 45: skip with the measurement, file it, say so in the report), never a re-bound.
   Golden `30C51BB619A7E650` unchanged unless a lever changes human-visible play (it should not: the levers are AI-only;
   if it moves, stop and say why). The 8 pre-M5-9a digests still reproduce at strength 0 + knobs 0.
5. BUG-1158: the guarded-start scene loses at most `ai.scoutReplacements` + 1 workers to the leg and keeps its economy;
   QA's row un-skipped; a dev row in `FarExpandTests`.
6. BUG-1159's three items done, QA's row un-skipped; `FarExpandTests` rows for the reach filter and the looked mark.
7. Each bug fixed has a regression test that failed first (named in the report); each lever that moves Normal play is
   described in docs/02 "AI opponent" (one sentence each) and docs/03 "Implementation (M5-H6)" (the measurement table);
   bug files' status updates as a patch in the scratchpad; no Godot / wall clock / `System.Random` / dictionary iteration /
   per-think allocation in the diff; no player-facing text; AI state out of `StateHash` unless it must be saved (then no:
   see out of scope).
8. Size: hand-written sim + tests ≤ 1,500 lines (generated comments and QA's suites don't count, rule 39).

**Design references.** docs/02 "AI opponent" (the hunt bullet, the far-expansion bullet, the rebuild bullet: "a House is
not rebuilt once the population cap is at the hard cap" is the current rule; (a)(iii) tightens it and the sentence moves with
it), docs/03 "Implementation (M5-4d)" (the sweep), "(M5-H5)" (the house rule's two failed tries and the sweep-order note),
"(M5-9b)" (the leg, `FarLooked`, `NextStart`, the start list's provenance), "(M5-5)" (the criterion rows and the refusal
table), BUG-1157 / 1116 / 1068 / 1158 / 1159 / 1151 / 1152 files.

**Tests required.** `EndGameHardeningTests` (new rows per lever shipped), `FarExpandTests` (the cap, the reach filter, the
looked mark), the un-skipped `MapSlice2StressTests` rows, the un-skipped QA rows, every pin re-measured, the tally once,
`AiPerfTests` alone (+ the eight-legs row if (d) reaches it).

**Constraints.** Rule 38 (QA and the Producer verify on a scratch clone); rule 42 (Perf alone); rule 43 / 45 (pins; a row that
stops ending is a finding); rule 41 (no `ai.json`); rule 40 (page cells only); never kill another track's process; the
developer cannot write `studio/**` (patches in the scratchpad). The default loop is ~20 min now: run it on the final commit.

### Where we are
M5-9b ACCEPTED (1 fix round, BUG-1160); M5-9a + M5-9b on `main`. **M5 8 / 9 (criterion 6 owner-only).** Counter **2 / 4**. The sim's
M5 work is complete; the end-of-M5 sim hardening has not run yet (the sign-off waits on criterion 6 anyway). Open: BUG-1157 / 1158
(S3), BUG-1159 (S4), and the carried list. A fresh branch next (rule 44 / 46: nothing is held).

### Next task candidates (the next PLAN picks; the inbox first)
1. **Recommended: the end-of-M5 sim hardening, pulled forward** (hardening, QA full; the counter will be 2 / 4 but every sim-side
   M5 criterion is met, the milestone's hardening is owed before its sign-off, and the debt is heavy: 26 S3). In this order,
   batched to the size budget: **(a) the end game (BUG-1157 / 1116 / 1068):** a decided match should finish: the hunt's sweep order
   (the perimeter leg deferred while a local point is nearer than the reached ring point, judged on `ALoneIdleWorker_*` + QA's
   hide / leg rows) and / or the losing side's rebuild churn (the house rule, BUG-1116, re-measured with the far gold now in play);
   goal: Pale Hills 2 / 5 back inside 72,000 and the five tally no-results ended, Pale Hills seed 1 not past 72,000; **(b) BUG-1158**
   (a leg-death cap, as `ai.scoutReplacements` caps the scout; a few lines); **(c) BUG-1151 / 1152** (the rebalance's gone-mine
   re-send; the last worker on gold), their `Known` entries out; **(d) BUG-1149 / 1150, BUG-1156's sim angle if any, BUG-1058 /
   1044 / 1067, BUG-1159, the movement S3s (BUG-0911 / 0889 / 0144), `HasMapId`'s note, `HUNT`'s range checks, BUG-1124's record,
   the `ReopenLost` masking note** (a lost Camp hidden by the far depot: measure first), the eight-legs perf row. Rules 43 / 45 apply
   to every item that moves Normal play; every pin in the watch-out list re-measured once in the same commit.
2. **Otherwise M7's first sim system** (feature): docs/05 M7 (Teblor): "scale and per-unit population systems proven (1.5× visuals,
   2-3 pop, half-step pop support)" is the sim's first item with a clear design (docs/02 "Population"); the giant armor class and
   regeneration next. Schemas before content (the data rule); no new faction id in C#.
3. BUG-1010's lever if small.

Brief notes: the hardening's brief names the pins it expects to move (every Pale Hills row) and keeps off `ai.json` (rule 41: D26 is
content there); if it moves a page cell, rule 40. Size 1,500 for (b) / (c); (a) is uncertain: measure both levers, ship the one that
ends the most rows without moving Pale Hills seed 1 past 72,000.

---

## View track

### Current session plan (2026-11-09-1336): STOP (cheap)
Inbox New is empty again; nothing changed for the view since the last PLAN. The reasoning below stands.

### Previous plan: STOP (cheap) unless the inbox has an owner item
Inbox New was empty at this session's PLAN; every remaining M6 criterion needs the owner (the art call, music / fonts / export
templates, ".NET 10 installed", a friend's playtest); the view's debt (BUG-1126 / 1147 / 1115 item 4 / 0860 / 0830 / 0890 / 0861)
is not a session's worth and the counter is 1 / 4.

### Where we are
STOP (cheap). Counter 1 / 4. M6 5 / 10.

### Next task candidates
- An owner item from the inbox (the art call, the SDK, templates, sound notes): a feature session, QA standard.
- Otherwise STOP (cheap). **Not yet, noted:** a stats-screen line for the AI's counter memory ("adapted to your army") only if the
  owner asks.

---

## Data track

### Current session plan (2026-11-09-1336): D26a, D14 c's content, the part that moves no pin (feature, QA light)

**Why split.** The sim's M5-H6 runs beside this and re-measures every Pale Hills digest, the Hard tally and the page tables
(rule 43); any `ai.json` change to Normal or Hard play would re-measure the same pins on another branch and the merge would
carry two wrong answers (rule 41). So this session D26 ships only what moves no pin: **no `game/data/**` edit at all.** The
tuning half (D26b: `counterStrength` values if they should differ from the shipped Easy 0 / Normal 0.5 / Hard 1.0, BUG-1156's
`ai.json` angle) is the data track's next task, after M5-H6 is on `main`, as a `standard` task that re-measures the pins.

**Files.** `docs/factions/malazan.md`, `docs/factions/whirlwind.md` (**only a new sub-section appended at the end of "AI
build order (Normal)", after the "High starting resources" bullet and before the next `##` heading; no existing table cell:
the sim may re-print cells under rule 40 in the same session**), `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`.
**Not** `game/data/**`, not `rules.json`, not docs/02 / 03 (a request if a sentence there is wrong).

**Scope.**
1. **The pages' AI paragraph ("How the AI adapts", one per faction):** in the page's words, what docs/02 "AI opponent" says
   about (i) the counter shift (the memory fading over `ai.counterMemory` 180 s, the shift by `counterStrength`: Easy 0 /
   Normal 0.5 / Hard 1.0, this faction's "Countered by" column from its roster table as the `ai.counters` it reads, the
   "tilts, doesn't replace" rule), (ii) the starved charge (`ai.starvedWaveAfter` 60 s / `ai.starvedMinArmy` 1, at every
   difficulty), (iii) the far expansion (`ai.farExpandReach` 384 m, this faction's gold-free drop-off: `malazan_depot` /
   `whirlwind_supply_cache` 0 gold / 75 wood, named as the player reads it). **Quote only data values (knobs, costs, names);
   no match tick, digest or tally** (those move this session). Say where the shift is masked (BUG-1156: a saturated single
   producer; the Line producer in the Malazan Age I mix) in one sentence each.
2. **`Content/` pins:** a test that reads both `ai.json` files and pins `counterStrength` per difficulty to the page's
   paragraph both ways (as `AiContentTests` pins the openings), and the three `rules.json` knobs the paragraph quotes
   (`counterMemory`, `starvedWaveAfter`, `starvedMinArmy`, `farExpandReach`) and the drop-off's cost / name against
   `buildings.json`; the pin fails if a value or a quoted name moves without the page.
3. **BUG-1156's measurement, not its fix:** run `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (skipped) with
   each candidate **locally, uncommitted**: (A) a second Barracks step in the Malazan `standard` opening at the time the
   first wave is due, (B) a Line counter unit in the Age I `standard` mix (if the roster has one beyond Heavy Infantry; say
   if not), (C) no data change (the docs say the shift shows when the bank limits). Report: trained Line / Ranged seen v
   unseen for each, and the opening's own 20-minute pairing outcome (`AiOpeningPairsReportTests`) for (A) / (B); recommend
   one for D26b. Nothing of this is committed except the report's numbers in the session's review table.
4. **The owner's review table:** no unit / field changed this session (say so); the two new page paragraphs quoted in full
   (they are player-facing in the sense the owner reads them, not in-game text); the BUG-1156 measurement table and the
   recommendation; the question for the owner: "keep Normal 0.5 / Hard 1.0, or stronger?".

**Acceptance criteria.**
1. Build 0 / 0; `Content/` + `QA/Content/` green; `DataValidationTests` / `DataContentHashTests` green and **the data hash
   unmoved** (no `game/data` edit); the default loop on the final commit (rule 47).
2. Both pages carry the new sub-section, every number in it equal to the data file it names (the pin proves it), no match
   tick / digest / tally quoted, no existing cell touched (`git diff` on the pages shows one added hunk each).
3. The pin test fails when a `counterStrength`, a quoted knob, or the drop-off's name / cost is changed (the developer shows
   the failing run in the report).
4. BUG-1156's three candidates measured and tabled, nothing of them committed; one recommended with the reason.
5. The review table as in 4 above, in the report for the Producer's For your review entry.

**Design references.** docs/02 "AI opponent" (the three bullets quoted above, the Easy / Normal / Hard blocks), docs/02
"Faction template" ("Countered by"), docs/03 "Implementation (M5-9a)" (the worked example, `CounterMasks`), "(M5-9b)"
(`GoldFreeDropOff`), `docs/factions/<id>.md` roster tables (the "Countered by" column), BUG-1156.

**Constraints.** No C# outside the two test folders; no `game/data/**` this session; rule 38 for QA; never kill another
track's process; the developer cannot write `studio/**`.

### Previous plan: D26, D14 c's content (feature, QA light; standard if it moves Normal or Hard)
**Unblocked now:** the schemas (`ai.json` `difficulties.<d>.counterStrength`; `rules.json` `ai.counterMemory`, `ai.counters`,
`ai.starvedWaveAfter`, `ai.starvedMinArmy`, `ai.farExpandReach`) are on `main`. D26 may run in the same session as the sim's next
task (rule 41: the sim's hardening keeps off `ai.json`; the sim brief says so).

**D26 brief.** **Files:** `game/data/factions/malazan/ai.json`, `game/data/factions/whirlwind/ai.json`, `docs/factions/malazan.md`,
`docs/factions/whirlwind.md`, `sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`. **Not** `game/data/common/rules.json` (sim-owned; a
request if a number there must move) and not the maps. **Scope:** (1) the per-difficulty `counterStrength` for both factions
against docs/02 "AI opponent" (the branch shipped Easy 0 / Normal 0.5 / Hard 1.0 as the minimum; D26 may tune, with the 42-match
tally as the measure if it moves Normal or Hard: rule 43's Pale Hills digests move with any Normal change, so a Normal change is a
`standard` task that re-measures the pins on its own branch and names them); (2) the faction pages' AI paragraph: the shift, the
starved charge, the far expansion, in the pages' words, with the re-printed cells docs/03 "Implementation (M5-9a)" / "(M5-9b)" quote;
(3) **BUG-1156's data angle:** a second barracks step in the Malazan openings, or a Line counter in the Malazan Age I `standard` mix
against a Whirlwind Raider army (docs/03 (M5-9a)'s worked example), or the docs saying the shift shows when the bank, not the
producer, limits: measure the 20-Horse-Raider scene (`CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*`, skipped under
BUG-1156) and pick; (4) `Content/` pins for every new number and name; (5) the owner's review table (unit / field / old → new /
why; every new player-facing string quoted). **Design references:** docs/02 "AI opponent" (Easy / Normal / Hard blocks), docs/02
"Faction template" (the "Countered by" column is `ai.counters`), docs/03 "Implementation (M5-9a)" (the worked example, the pins),
"(M5-9b)".

### Where we are
STOP (cheap) this session. Counter 2 / 4. D26 next (unblocked).

### Next task candidates
- D26 (above). Then the owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`;
  BUG-0780). M7's rosters when M7's schemas exist.
