# Handoff: brief for the next session

Written by the Producer at the ACCEPT step of session **2026-11-08-1556** (session 3 / 8 of 2026-11-08; sim only: view and data
STOP at PLAN). Sim **M5-9a round 2 (the starved-side wave) is ESCALATED**: the code is sound and QA-verified (two full passes,
criteria 1-2 / 4-8 met, BUG-1154 S3 and BUG-1155 S4 found and fixed in the one fix round), but criterion 3 is not: **Pale Hills
seed 1 Normal v Normal ends at 68,664, outside its 36,000 bound (BUG-1153, S2)**, and both the developer and QA say no
starved-wave setting changes that (30 / 60 / 120 s: no result / 68,664 / 71,660). The remaining lever is a different system
(the AI's expansion, BUG-1068's (a)), so the owner's choice is in STATE "Waiting on you" **with a Producer default** that the
next sim PLAN applies if the inbox says nothing. **The branch `studio/2026-11-08-1556-sim` (f32501b + the Producer's studio
commit) is pushed; nothing of M5-9a is on `main`.** `main` = dd3befa (M5-5 + the plans): build 0 / 0, non-Perf 8,610 / 0 / 19,
smoke PASS, golden `30C51BB619A7E650`, formats 6 / 7 / replay 9. **M5 7 / 9**, M6 5 / 10. **Open S2: BUG-1153 (on the branch
only; `main` has no S1 / S2).** Counters after: **sim 1 / 4 (an escalated session does not advance it), view 1 / 4, data 2 / 4.**
Bug ids next: **all tracks from 1157.**

**PLAN 2026-11-09-0426 (Producer):** inbox New empty, so the default stands: **sim GO, M5-9b on the held branch
`studio/2026-11-08-1556-sim`** (brief below, refreshed with one design point found at PLAN: the executor's `expand` places a
**Town Hall (275 gold)**, which a broke seat can never pay, so the far expansion must place the gold-free depot / supply cache
(0 / 75 wood) instead); **view STOP (cheap), data STOP (cheap)**. `main` 5a736ec = dd3befa + `studio/**` only (verified by
`git diff --stat`): the Producer's scratch archive of 5a736ec (rule 38) builds **0 / 0**; `Category!=Perf&Category!=Stress`
**8,203 / 0 / 16 in 52 m** (every skip with an id); `Rts.Cli match --map vathar_crossing --seed 1` ends at **22,794**, hash
`851529AC383EE168` (the M5-5 claim verified). Sessions today 0 / 8 before this one.

## Standing rules (1-46)

Unchanged 1-45 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle; 43: every sim task that
can change Normal AI behaviour names which M5-5 rows it expects to move and re-measures the bound comments and docs/03's M5-5 table
in the same commit, and a row that stops ending is a finding, not a re-bound; 44: a session that dies after ACCEPT is re-verified on
a scratch clone and integrated first, a new task starts on a fresh branch; 45: a brief whose task changes Normal AI behaviour says
what to do when a pinned criterion row stops ending: the developer tries the smallest end-game lever named in the bug files behind
a data knob with 0 = off and reports both outcomes; an Easy-v-Normal pin is not an "Easy unchanged" proof). **New 46
(2026-11-08-1556):** a branch held twice gets a **terminal acceptance rule** in its third brief (what lands under each measured
outcome, written before the session), so no branch lives a fourth session; the rule may re-bound a row only with the finding's
bug id in the constant's comment and the Producer decision in docs/03 and STATE.

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rule 42 before filing. The full non-Perf run is
  ~8,740 rows: 1 h 15 m alone, **2 h 50 m beside QA's probes** (this session). The default (non-Stress) loop is ~2 min.
- **This session took 12.5 h wall clock** (build 2.7 h, QA 2.9 h, fix 3.3 h, re-check 3 h): QA's full non-Perf run twice plus
  the 42-match tally twice. Next session: QA runs the full non-Perf **once** (at the re-check, on the fixed commit) and the tally
  once; the inspection round runs the targeted suites and the probes.
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
- A developer must never kill another track's Godot or `testhost` process.
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it.
- **A brief that relaxes a judge must name what the relaxed query cannot see** (M5-H5's lesson).
- **Pinned matches on `main` (unchanged by this session):** `MatchCriterionTests` (Vathar 22,794 / Raraku 28,083 / Pale Hills
  34,219 / generated 21,789 / FFA 52,305 / 2v2 34,510, bounds in the constants), `MapFileTests.NormalVersusNormal_OnANewMap_*`
  (34,219 / 22,794), `MapSlice2StressTests.KnownStalls` (29,562 / 55,631 / 26,034), `StandardPresetPinQaTests` (7 digests), QA's
  `NonHardMatchPinQaTests` (8 Easy / Normal whole matches), `HardKnobsPinTests`, the Malazan / Whirlwind page tables,
  `D21OpeningsQaTests` / `DH6ThresholdQaTests` + `KnownBeatenAlive` ({(1, 1, 19, 0)} and {}), the golden header, QA's
  `MatchCriterionSweepQaStressTests.Known` (BUG-1151 generated 4, BUG-1152 Raraku 1 / 2).
- **On the M5-9a branch (f32501b) the pins read:** Vathar 23,043 (seat 1) / Raraku 28,083 / **Pale Hills 68,664, red against
  36,000 (BUG-1153)** / generated **seed 2** 15,691 (bound 24,000) / FFA 58,155 / 2v2 35,648; `NonHardMatchPinQaTests` 8 new
  digests (Pale Hills 1 Normal `70635F3A0D539227 36000 2598`; the old 8 reproduced at Normal strength 0 + knob 0 by
  `CounterCompositionTests.TheNonHardPins_WithNormalStrengthZero_*`); Pale Hills seed 3 ends at 31,891 with no hunt (the mid-hunt
  rows on seed 5, 45,358); **un-skipped and green there:** Pale Hills seed 2 (53,797) and its FFA (80,278), Raraku seed 5
  (52,360; still in the sweep's `Known` under BUG-1152); `StandardPresetPinQaTests` row 5 moved; the malazan / whirlwind page
  cells re-printed; QA's `RebuildLookMatch` seed-2 window 41,000. **Red on the branch:** `MatchCriterionTests` both Pale Hills rows,
  `MapFileTests(pale_hills)`, `HuntStressTests.FourAiSeats_OnPaleHills_*(seed 1, FFA)` (no result by 144,000). Every one of these
  moves again with M5-9b.
- **Known stalls (skipped with ids on `main`):** Pale Hills seed 2 (BUG-1068 / 1116), `EndGameHardeningTests.ALoneIdleWorker_*`
  (BUG-1116), QA's `M5H5HuntAfterDefeatQaTests.A2v2_TheDefenseToo_*` (BUG-1149), `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150).
  On the branch also BUG-1156's behavioural row (`CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*`).
- **On the three map files seeds 3 / 4 / 6 are one match** (the seed only picks the openings).
- **Criterion 6 (the owner wins on Easy, loses to Hard) is owner-only:** M5 cannot be signed off by the Producer until the owner
  writes the result in the inbox.
- **The new perf row** (`AiPerfTests.EightHardSeats_Starved_Undefended_*`) has ~12 % headroom (0.884 ms of 1 ms): the first to
  flake under load; rule 42.

---

## Sim track

### Where we are
M5-9a round 2 ESCALATED (1 fix round, QA full FAIL twice on criterion 3 only); the branch is pushed. The starved wave works
(scripted rows exact at every difficulty, the retreat loop converges, a mid-stamp save follows in a second process, 0 B, the
knob-0 twins exact) and it ended three stalls (Pale Hills seed 2 and its FFA, Raraku seed 5), but Pale Hills seed 1 is a slow
raze: at 36,000 the loser has 0 army, 29 workers and 13-17 buildings it keeps rebuilding with 4,000 wood, and the winner's 10-14
archers need 32,000 more ticks. Underneath, both sides are broke from 24:00 because the AI never reaches the far mines
(BUG-1068 (a)). Counter 1 / 4.

### Current session plan (session 2026-11-09-0426): M5-9b, the far expansion (BUG-1068 (a)), on the held branch — feature, QA full

**The inbox was silent at the 2026-11-09-0426 PLAN, so this default applies** (the owner's other choices stay in STATE "Waiting
on you" and may still arrive: "ship with the hour bound", "more gold on Pale Hills", "drop the counter shift"; an inbox note
during the session is for the ACCEPT step, not the developer).

**Two facts found at PLAN that the design must honour:** (1) `BuildOrderExecutor`'s `expand` step places the **Town Hall
(275 gold / 275 wood)** by `ExpansionMine` (within `ExpandReachCells`); at 24:00 on Pale Hills both seats hold 16-46 gold and
thousands of wood, so a far expansion that needs a Town Hall never happens. docs/02 "Buildings" already says the Camp exists
"because workers need a drop-off near forests and expansions, and making them build a full Town Hall for that is wrong": the
far expansion places the faction's **cheapest `DropOff` building that costs no gold** (`malazan_depot` / `whirlwind_supply_cache`,
0 / 75, 25 s), found from data (the cheapest `DropOff` type with `CostGold` 0; with none such, the Town Hall and the seat waits
for the gold). (2) `AiPlayer.InGatherReach` returns true for **any** node once the reach has lifted (the head ring seen or the
sweep ended), so a far mine the seat has **seen** is already a gather target today; the far mines go untouched only because
nothing ever looks at the unused starts (the sweep ends on the first forest + mine + enemy building). So the scout leg is the
first half of the fix and the depot the second (without it workers walk 260 m round trips).

**Branch:** the conductor starts the sim worktree from `origin/studio/2026-11-08-1556-sim` (f32501b + the Producer's studio
commit), merges `origin/main` (only `studio/**` moved), and builds on it. **Diff base for ACCEPT: c11f5f0** (M5-9a rounds 1-2 +
M5-9b: nothing of it is on `main`). Rules 43, 45 and **46 (the terminal rule below)** apply. Bug ids from 1157.

**Goal.** The AI's economy stops dying at 24:00 on the big maps: a seat whose known gold in reach is spent goes and takes the
map's unclaimed mines wherever they are, so Normal v Normal matches are decided by play rather than by who is left holding a
ranged remnant. Then every pin the branch carries is re-measured once more and M5-9a + M5-9b land together under the rule.

**Scope (in):**
1. `rules.json` `ai.farExpandReach` (meters, 0-512, **0 = off**; ship what the measurement says, start at the map's diagonal
   so "any distance" on a 256 map) and, if the measurement needs it, `ai.farExpandGold` (the gold level under which the rule
   arms, 0-5,000; start at the pick's cost, i.e. reuse `Starved()`'s (i)). Validated (`DataValidationTests`: missing, negative,
   over the limit) and hashed (`DataContentHashTests` regenerated with the reason).
2. The rule, in the executor's `expand` handling (M5-3 / M5-4g): a seat **arms** the far expansion when its gold is short for
   production's pick (the starved check's (i), already computed in the census), no own worker gathers gold, and it knows no
   free gold node inside `ai.gatherReach` of a drop-off (`KnowsNode(Gold)` false). Armed, it (a) **sends its scout (or, with no
   scout alive, one worker) to the nearest unused start location** it has not yet looked at: start locations are map data every
   seat is given (`SimConfig` / the map's start list), not a fog read, so this is no cheat (say so in docs/03); and (b) once it
   **knows** a free gold node within `farExpandReach` of its hall that is not inside `gatherReach` of a known enemy drop-off, it
   orders the **gold-free depot** (see the PLAN facts above; the Town Hall only when the seat can pay for one now) in gather
   reach of that node by the existing site search (`placementSearchRadius` round the node, `buildingSpacing`, `HallNear`'s
   exclusion of mines by an own or seen hall), with one worker sent as the builder (`PickBuilder`'s rule), and shifts gatherers
   as M5-4g's gather shift does once the drop-off stands. The `expand` step itself is **not** reopened (its Town Hall rule is
   unchanged; the far expansion is its own executor-level rule or a `MilitaryManager`-style manager, the developer's call, said
   in docs/03). One far expansion at a time; the arm clears when a gold worker is at work again. Knob 0: nothing runs, and with
   Normal `counterStrength` 0, `starvedWaveAfter` 0 and `farExpandReach` 0 today's play bit for bit.
3. Save / load: any new blackboard state (the armed flag, the start index looked at) goes in `CTRM` after `StarvedSince`
   (formats stay **8 / 9**: no third bump, neither is on `main`); the reader bounds it (`Malformed` otherwise);
   `SaveRefusalTests` + `PreMapFixtures` updated.
4. Re-measure (rule 43) every row in the watch-out list above (the M5-5 rows, `MapFileTests`, `MapSlice2`, the mid-hunt rows,
   `KnownStalls`, both `KnownBeatenAlive`, `StandardPresetPinQaTests`, the 8 `NonHardMatchPinQaTests`, QA's sweep / probes /
   `RebuildLookMatch` / `StarvedSaveTwoProcess`, the page cells (rule 40), the 42-match tally), and both Pale Hills FFAs
   (seeds 1 and 2) to 144,000.
5. **The terminal rule (rule 46; the Producer's acceptance rule, from BUG-1153's triage):** Pale Hills seed 1 Normal v Normal
   with every (a)-(e) check clean: **(a) ends inside 36,000:** BUG-1153 fixed, nothing re-bounded. **(b) ends between 36,000 and
   72,000:** set the three 1v1 Pale Hills rows' bound (`MatchCriterionTests` both rows, `MapFileTests`) to **72,000** with a
   comment naming BUG-1153 and "Producer decision 2026-11-08-1556", re-print docs/03's M5-5 table; BUG-1153 goes to S3 in the
   patch. **(c) no result at 72,000:** leave the rows red, report the end-game shape; the Producer holds the branch and asks the
   owner. The seed-1 FFA (`HuntStressTests`): if it still has no result by 144,000, a skipped row under BUG-1068 / BUG-1116 with
   the measurement. Any **other** pinned row that stops ending is a finding, not a re-bound (rule 43).
6. Tests: `Ai/FarExpandTests` (scripted: the flat scene with the reach's mines emptied, a free far mine the seat has seen /
   not seen, a known enemy drop-off by it; the scout leg to an unused start; the arm clears when a gold worker is at work;
   knob 0 never arms; a save mid-arm follows), the knob-0 twin on one whole Normal v Normal match (equal command log), the 8
   old digests reproduced with strength 0 + both knobs 0.
7. Docs: docs/02 "AI opponent" one sentence (the AI expands to the far mines when its own are spent); docs/03 "Implementation
   (M5-9b)" (the rule, the knobs, why the start list is no cheat, the pins table), the M5-5 table; docs/05 criterion 9's
   parenthesis (the Producer ticks); BUG-1068 / 1153 / 1116 / 1152 patches in the scratchpad.

**Out of scope:** BUG-1156 (the saturated producer; D26 / hardening), BUG-1010, BUG-1116's house rule, D26's content, any map
data change, a format bump, any change to Normal's `counterStrength` or the counters table, the hunt's sweep order.

**Acceptance criteria:** (1) build 0 / 0, the full non-Perf run green but for the rows rule 5 names with their ids, every skip
with an id, `AiPerfTests` 6 / 6 alone (rule 42), smoke PASS; (2) M5-9a's criteria still met (its tests green; the 8 old digests
and the golden header `30C51BB619A7E650` reproduced with strength 0 + both knobs 0); (3) the scripted seat arms, scouts the
unused start, expands to the far mine once known and never to one by a known enemy drop-off, clears when gold is gathered,
never arms at knob 0, follows a mid-arm save; (4) Pale Hills seed 1 settled per rule 5 and its outcome stated; (5) every other
pinned row green or in `Known` under an existing id with the measurement, no re-bound outside rule 5; (6) the Hard-v-Normal
tally does not invert; (7) the knobs validated and hashed, docs updated, no player-facing text in C#, no allocation on the
think path, the AI's state out of `StateHash`; (8) the fog oracle clean on every think of every probed match (the far mine
must have been **seen**; the start list is the only map-data read).

**Design references:** docs/02 "AI opponent" (Easy / Normal / Hard blocks; expansion), docs/03 "Implementation (M5-3)" (the
executor's `expand`, `ai.expandHallReach` 40 m), "(M5-4g)" (the reserve and the gather shift), "(M6-S5)" (`gatherReach`,
`KnowsNode`), "(M5-9a)" (the census, `Starved()`'s (i) / (ii), `CTRM`), "(M5-5)" (the rows and bounds); BUG-1068's M5-H5 section
(the gold map of Pale Hills: 9,000 per start in reach, 18,000 at the unused starts 260 m off) and candidate (a); BUG-1153's
triage (the terminal rule).

**Constraints most at risk:** fog (the AI may read the start list, never a node it has not seen); no per-think allocation
(reuse the census; fixed arrays); determinism (new state saved in `CTRM`); the knob-0 twins bit for bit; data-driven; docs in
the same commit; the size: uncertain work, ~800 lines, QA full.

**QA focus (full):** the fog oracle on every think (a far node counted only after `CanSeeResource`-equivalent sight); the knob-0
/ strength-0 twins on all 8 pins and the golden; the arm's edges (gold exactly at the pick's cost; a mine emptied the same think;
an enemy drop-off appearing by the far mine mid-expansion; the scout dying on the leg; no unused start, as on the generated 2-player
map; a seat with under 75 wood when armed: it must wait, not spam; the depot site taken by an enemy building; the far mine
emptied while the depot is a site); save / load fuzz of the new `CTRM` fields; perf 6 / 6 alone (the starved row's 12 % headroom:
rule 42); every pin independently; the 11 probed matches to 72,000 with (a)-(e) and the silent-seat / stuck-unit /
still-resources oracles; both Pale Hills FFAs to 144,000; **the full non-Perf run once (at the re-check, on the fixed commit)
and the 42-match tally once** (last session's 12.5 h came from running both twice); the sanity row: Pale Hills seed 1 Normal v
Normal: both seats' gold at 30,000 is above 0 and a far depot (or hall) stands by 36,000.

### After it
The end-of-M5 sim hardening at 4 / 4 (BUG-1149 / 1150 / 1151 / 1152 / 1058 / 1044 / 1067 / 1156, BUG-1116's sweep order, the
movement S3s, `HasMapId`'s note, `HUNT`'s range checks, BUG-1124's record); BUG-1010's lever if small. Then M7's sim systems (M5
held at 8 / 9 for criterion 6).

---

## View track

### Current session plan (session 2026-11-09-0426): STOP (cheap)
Inbox New empty at PLAN: no owner item (the art call, ".NET 10 installed", templates, sound notes); every remaining M6 criterion
needs the owner; the view's debt (BUG-1126 / 1147 / 1115 item 4 / 0860 / 0830 / 0890 / 0861) is not a session's worth and the
counter is 1 / 4 (a hardening at 4 / 4). The sim's full QA pass runs hours of pins alone, so the CPU is better left to it.

### Where we are
STOP (cheap). Counter 1 / 4. M6 5 / 10; every remaining M6 criterion needs the owner (the art call, music / fonts / export
templates, ".NET 10 installed", a friend's playtest).

### Next task candidates
- An owner item from the inbox (the art call, the SDK, templates, sound notes): a feature session, QA standard.
- Otherwise STOP (cheap). Debt for the view's next hardening (not a session's worth): BUG-1126 S4, BUG-1147 S4 item 1, BUG-1115
  item 4, BUG-0860 / 0830 / 0890 / 0861.
- **Not yet, noted:** a view of the AI's counter memory on the stats screen ("adapted to your Line") only if the owner asks.

---

## Data track

### Current session plan (session 2026-11-09-0426): STOP (cheap)
M5-9a's schema is still on its branch (held; `git grep starvedWaveAfter` on `main` 5a736ec finds nothing), no review tweak is
waiting (inbox New empty), nothing on `main` moved a pin or re-printed a cell (rules 40 / 41), and the map change ("more gold on
Pale Hills") was not picked, so no data task may touch `game/data/maps/pale_hills.json` while the sim re-measures its pins (rule
41). D26 starts the session after M5-9a + M5-9b land. **If the owner picks "more gold on Pale Hills"**, the
data track's task is the map change instead (`game/data/maps/pale_hills.json`: neutral gold nearer the two 2-player starts, or
more at the unused starts; the sim's `MapFileTests` start rules must still hold; QA standard with the Normal v Normal numbers).

### Where we are
STOP (cheap). Counter 2 / 4. **M5-9a's schema (`counterStrength`, `ai.counterMemory`, `ai.counters`, `ai.starvedWaveAfter`,
`ai.starvedMinArmy`) is still not on `main`** (the branch is held), so D26 waits.

### Next task candidates
- **D26: D14 c's content** once M5-9a lands: the per-difficulty `counterStrength` for both factions against the design (the branch
  ships Easy 0 / Normal 0.5 / Hard 1.0 as the minimum; D26 may tune, and may add a Line counter to the Malazan Age I `standard` mix,
  which today has none against a Whirlwind Raider army: docs/03 "Implementation (M5-9a)"'s worked example), the faction pages' AI
  paragraph and the prose round the re-printed cells, `Content/` pins, the owner's review table; **and BUG-1156's data angle** (the
  shift is invisible while the Line producer is saturated: a second barracks step in the openings, or the docs saying the shift
  shows when the bank, not the producers, limits). QA light (standard if a balance sample is needed).
- The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780).
- M7's rosters when M7's schemas exist.
