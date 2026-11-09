# Handoff: brief for the next session

Written by the Producer at the ACCEPT step of session **2026-11-09-0426** (session 1 / 8 of 2026-11-09; sim only: view and data
STOP at PLAN). Sim **M5-9b REJECTED on one line, with M5-9a rounds 1-2 still held on the branch:** every criterion of the task
itself is met and verified (QA full PASS_WITH_ISSUES; the Producer's own CLI run reproduces the headline number), but the
**default test loop is red on the branch: BUG-1160 (S2)**, QA's new `QA/FarExpandQaTests.cs` reads the allocation counter without
`[Collection(SerialCollection.Name)]`, so BUG-0024's guard `SerialCollectionTests.EveryFileThatMeasuresAllocation_*` fails
(8,244 / 1 / 18, twice). `main` must stay green and the Producer does not edit tests, so: REJECT, one attribute to add. The terminal
rule's outcome was **(b)**: Pale Hills seed 1 Normal v Normal ends at **48,858 (40:42)**, seat 1, every check clean, so the three
1v1 Pale Hills rows are bound at **72,000** (a Producer decision, docs/01) and BUG-1153 is S3. **M5 stays 7 / 9 until the merge
(criterion 9 verified, ticked then; criterion 6 owner-only).** New S3 BUG-1157 (decided Pale Hills matches finish late: seeds 2 / 5
past 72,000, five Hard-seat-0 tally no-results), S3 BUG-1158 (the leg replaces killed workers without limit), S4 BUG-1159 (nits).
`main` = 491dae0 (dd3befa + `studio/**`): unchanged, build 0 / 0, non-Perf green, golden `30C51BB619A7E650`, formats 6 / 7, replay 9.
**The branch `studio/2026-11-09-0426-sim` (f6af427 + the Producer's studio commit) is pushed.** Counters after: **sim 1 / 4 (a
rejected session does not advance it), view 1 / 4, data 2 / 4.** Bug ids next: **all tracks from 1161.**

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
- **Pinned matches on the branch (M5-9a + M5-9b; `main`'s are unchanged until the merge):** `MatchCriterionTests` (Vathar 23,043
  seat 1 / Raraku 28,083 / **Pale Hills 48,858, bound 72,000** / generated **seed 2** 15,691, bound 24,000 / FFA 58,155 / 2v2 35,648),
  `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithinItsBound` (Pale Hills 48,858 / Vathar 23,043), `MapSlice2StressTests.KnownStalls`
  (Pale Hills 3 / 4 / 6 = 29,514, generated 6, Raraku 5 52,360), **skipped with the measurement: Pale Hills 2 (79,504) and 5
  (96,321) under BUG-1068 / 1116 / 1157**, `StandardPresetPinQaTests` (7 digests), QA's `NonHardMatchPinQaTests` (8 digests; 4 Pale
  Hills ones new), `HardKnobsPinTests`, the Malazan / Whirlwind page tables, `D21OpeningsQaTests` / `DH6ThresholdQaTests` +
  `KnownBeatenAlive`, the golden header, QA's `MatchCriterionSweepQaStressTests.Known` (BUG-1151 generated 4, BUG-1152 Raraku 1 / 5),
  the hunt rows on **seed 1** (`AiDeterminismTests`, `HuntStressTests` mid-hunt save), `HuntStressTests` FFA seed 2 (97,140) / seed 3 /
  2v2 seed 1; `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall` skipped (no result by 144,000: no free gold with four
  seats). The 8 pre-M5-9a digests reproduce at Normal strength 0 + `starvedWaveAfter` 0 + `farExpandReach` 0
  (`CounterCompositionTests.TheNonHardPins_*`). The Hard v Normal tally: 26 / 11 / 5. **On `main` (unchanged):** the M5-5 list in
  the 2026-11-08-1556 handoff (Vathar 22,794 / Raraku 28,083 / Pale Hills 34,219 / generated 21,789 / FFA 52,305 / 2v2 34,510).
- **Known stalls (skipped with ids on the branch):** Pale Hills 2 / 5 (BUG-1068 / 1116 / 1157), Pale Hills FFA seed 1 (BUG-1068 /
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

### Where we are
M5-9b REJECTED on BUG-1160 alone (one attribute in QA's test file); M5-9a + M5-9b verified and held on
`studio/2026-11-09-0426-sim` (f6af427). **M5 7 / 9 (criterion 9 verified, ticked at the merge; 6 owner-only).** Counter **1 / 4**.

### Current session plan: M5-9b fix, the one line + the landing (feature, QA light; a fix round in the 0426 session if the conductor can run one, else the next sim session)

**Branch:** resume `origin/studio/2026-11-09-0426-sim` (f6af427 + the Producer's studio commit); merge `origin/main` (only
`studio/**` will have moved). **Diff base for ACCEPT: c11f5f0** (M5-9a + M5-9b + this line; nothing of it is on `main`).

**Scope (in), nothing else:** add `[Collection(SerialCollection.Name)]` above `public class FarExpandQaTests` in
`sim/Rts.Sim.Tests/QA/FarExpandQaTests.cs` (line 23; `StarvedWaveQaTests` is the model). Run the default loop
(`Category!=Perf&Category!=Stress&Category!=Soak`): expected **8,245 / 0 / 18**. Apply the BUG-1160 patch (status `fixed`, the
commit) via the scratchpad.

**Out of scope:** any other file. No sim code, no docs, no pin, no bug fix (BUG-1157 / 1158 / 1159 wait for the hardening). If the
developer finds anything else red, it is reported, not fixed, and the Producer decides.

**Acceptance criteria:** (1) `SerialCollectionTests` green and the default loop green (8,245 / 0 / 18 or the same with the row
counts explained); (2) the diff against f6af427 is that one line plus the bug patch; (3) build 0 / 0.

**Terminal rule (rule 46; this is the branch's fourth session, so the rule is the whole brief):** (1)-(3) green → the Producer
ACCEPTs M5-9a + M5-9b as verified at 0426 (criteria 2-8 there; the 0426 session log holds the evidence), ticks criterion 9, and
the conductor merges. Anything else red → the Producer files it and decides on the spot; no fifth session on this branch.

**QA (light):** `SerialCollectionTests` + the default loop on the fixed commit, `FarExpandQaTests` 11 / 13 as before; no probes,
no tally, no Perf beyond `AiPerfTests` 6 / 6 alone if the host is idle (rule 42; optional: it did not change).

### After it (the next PLAN picks; the inbox first)
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

### Current session plan: STOP (cheap) unless the inbox has an owner item
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

### Current session plan: STOP (cheap) until M5-9a / M5-9b are on `main`; then D26, D14 c's content (feature, QA light; standard if it moves Normal or Hard)
**Blocked by one line:** the schemas (`ai.json` `difficulties.<d>.counterStrength`; `rules.json` `ai.counterMemory`, `ai.counters`,
`ai.starvedWaveAfter`, `ai.starvedMinArmy`, `ai.farExpandReach`) are on the held branch, not on `main` (`git grep counterStrength`
on `main` finds nothing until the fix lands). The moment it lands, D26 is the data track's task, in the same session as the sim's
next task if the conductor runs them side by side (rule 41: the sim's hardening keeps off `ai.json`).

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
STOP (cheap) this session. Counter 2 / 4. D26 next once the schemas land.

### Next task candidates
- D26 (above). Then the owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`;
  BUG-0780). M7's rosters when M7's schemas exist.
