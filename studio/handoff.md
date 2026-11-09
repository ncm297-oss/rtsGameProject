# Handoff: brief for the next session

Written by the Producer at the ACCEPT step of session **2026-11-08-1316** (session 2 / 8 of 2026-11-08; sim only: view and data
STOP at PLAN). The session first integrated M5-5 (the resumed 0726 branch, rule 44: on `main` at c11f5f0), then ran sim **M5-9a,
counter-composition (the sim's half), which is REJECTED on its first round**: the code, schema, memory, shift, save section, docs
and most pins are sound (the Producer read the whole diff and re-ran the targeted suites), but the Normal counter shift turns the
**Pale Hills seed 1 criterion row into a stalemate (BUG-1153, S2)**, criterion 7 is not met, and QA never ran (the developer
reported BLOCKED). **The branch `studio/2026-11-08-1316-sim` (29879eb + the Producer's studio commit) is pushed for the next sim
session to resume**; nothing of M5-9a is on `main`. `main` = c11f5f0 (M5-5 + the plan): build 0 / 0, non-Perf 8,610 / 0 / 19,
smoke PASS, golden `30C51BB619A7E650`, formats 6 / 7 / replay 9. **M5 7 / 9**, M6 5 / 10. **Open S2: BUG-1153 (on the branch
only; `main` has no S1 / S2).** Counters after: **sim 1 / 4 (a rejected session does not advance it), view 1 / 4, data 2 / 4.**
Bug ids next: **all tracks from 1154.**

## Standing rules (1-44)

Unchanged 1-44 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle; 43: every sim task that
can change Normal AI behaviour names which M5-5 rows it expects to move and re-measures the bound comments and docs/03's M5-5 table
in the same commit, and a row that stops ending is a finding, not a re-bound; 44: a session that dies after ACCEPT is re-verified on
a scratch clone and integrated first, a new task starts on a fresh branch). **New 45 (2026-11-08-1316):** a brief whose task
changes Normal AI behaviour says what to do when a pinned criterion row stops ending: the developer does not stop at BLOCKED; it
first tries the smallest end-game lever already named in the bug files (today BUG-1068's (b)) behind a data knob with 0 = off, and
reports both outcomes. A pin that is a whole Easy-v-Normal match is not an "Easy unchanged" proof; the proof is the knob-0 twin.

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rule 42 before filing. The full non-Perf run is
  ~8,650 rows: 1 h 15 m alone. The default (non-Stress) loop is ~2 min.
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
- **On the M5-9a branch the pins already read:** Vathar 23,043 (seat 1) / Raraku 28,083 (unchanged) / **Pale Hills no end
  (BUG-1153)** / generated 26,853 (bound raised to 30,000 by Raraku's rule, seed 2 ends at 15,691) / FFA 58,155 / 2v2 35,648;
  `NonHardMatchPinQaTests` 8 new digests (the old ones reproduced at Normal strength 0 by
  `CounterCompositionTests.TheNonHardPins_WithNormalStrengthZero_*`); Pale Hills seed 3 ends at 31,891 with no hunt (the
  `AiDeterminismTests` / `HuntStressTests` mid-hunt rows moved to seed 5, 45,358); Raraku seed 2 clean (its `Known` entry out),
  **Raraku seed 5 no end (BUG-1152's wood starvation; sweep + `MapSlice2 OtherSeeds`)**; `StandardPresetPinQaTests` row 5 moved;
  the malazan / whirlwind page cells re-printed (the diff). Every one of these moves again with round 2's lever.
- **Known stalls (skipped with ids on `main`):** Pale Hills seed 2 (BUG-1068 / 1116), `EndGameHardeningTests.ALoneIdleWorker_*`
  (BUG-1116), QA's `M5H5HuntAfterDefeatQaTests.A2v2_TheDefenseToo_*` (BUG-1149), `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150).
- **On the three map files seeds 3 / 4 / 6 are one match** (the seed only picks the openings).
- **Criterion 6 (the owner wins on Easy, loses to Hard) is owner-only:** M5 cannot be signed off by the Producer until the owner
  writes the result in the inbox.

---

## Sim track

### Where we are
M5-9a REJECTED on round 1 (0 fix rounds, no QA); the branch is pushed. The code is right; the match economy underneath is not: a
gold-starved side whose army is under its wave threshold, with an enemy building still known, never attacks and never hunts
(BUG-1068's pattern, measured at M5-H5), and the shift's reshuffle of a fragile end game tips Pale Hills seed 1 into it. Counter 1 / 4.

### Current session plan (2026-11-08-1556, session 3 / 8): M5-9a round 2, the starved-side wave — feature, QA full

**Branch:** the conductor starts the sim worktree from `origin/studio/2026-11-08-1316-sim` (61f44df = 29879eb + the Producer's
studio commit, whose `studio/**` content is identical to `origin/main` 99d11bb), merges `origin/main` (trivial: only
`studio/**` moved on `main`), and builds on it. **Diff base for ACCEPT: c11f5f0** (the whole M5-9a, rounds 1 + 2: nothing of it is on
`main`). Rule 45 applies: no BLOCKED; partial results per row.

**`main` verified at this PLAN (rule 38, scratch clone of 99d11bb):** build 0 / 0; `Category!=Perf&Category!=Stress` **8,203 / 0 / 16
in 25 m 25 s** (that filter still runs the Stress-by-name rows, so it is not the ~2 min loop; not a hang). Smoke not re-run: `game/`
unchanged since the 1316 PASS.

**Goal.** Make the M5-9a branch green without a skip or a re-bound: a gold- (or wood-) starved AI that can no longer grow attacks
with what it has (BUG-1068 candidate (b), the fix for BUG-1153 S2), behind a data knob with 0 = off, then re-measure every pin
the round moved and run the QA full pass round 1 never got.

**Scope (in):**
1. `rules.json` `ai.starvedWaveAfter` (seconds, 0-600, to ticks, **0 = off**; start at 60, ship what the measurement says) and
   `ai.starvedMinArmy` (whole number 1-50, ship 1); both validated (`DataValidationTests` rows: missing, negative, over the limit,
   non-integer army) and hashed (`DataContentHashTests` regenerated with the reason).
2. The rule, computed in the census after the counter-memory update (no new pass over the view): a seat is **starved** on a think
   when (i) it cannot pay for any unit of its current composition now (gold or wood short for every entry), (ii) for each resource
   kind that is short in (i) it knows no free node of that kind it may gather (`AiPlayer.KnowsNode(kind)`: known, not empty, in
   `ai.gatherReach`) and no own worker is gathering that kind, (iii) it has at least `starvedMinArmy` free combat units (the same
   `Free` filter `TryStart` uses), and (iv) the match is not over. Not evaluated while `Board.Defending` (the stamp is cleared then:
   `KnowsNode` hides nodes near the defense point while a raid is on, which must not read as starvation). The first starved think
   stamps `Blackboard.StarvedSince = Now`; any think where (i)-(iv) stop holding clears it. Once `Now - StarvedSince >=
   starvedWaveAfter` (ticks), `MilitaryManager.TryStart` starts a wave with every free army unit ignoring the half-pop / cost
   threshold (the cooldown still applies; target rule unchanged; `WaveMinHalfPop` / `WaveMinCost` 0 as the hunt sets them, so the
   defense may pull any member), and `TryHunt` starts a hunt even when `WavesStarted == 0`. Every difficulty (an Easy stall is still
   a stall). Knob 0: the stamp is never set and nothing else changes: with Normal `counterStrength` 0 too, today's play bit for bit.
3. Save / load: `StarvedSince` in the `CTRM` section per seat (formats stay **8 / 9**: no third bump, neither is on `main`); the
   reader requires 0 or at most the seat's saved `Now` (`Malformed` otherwise); `SaveRefusalTests` + `PreMapFixtures` updated.
4. Re-measure (rule 43) and settle every row the branch's watch-out list names: `MatchCriterionTests` (Pale Hills seed 1 **must end
   inside 36,000**; if it does not at 60 s, report the end-game shape at 30 / 60 / 120 and stop at the measurement: no re-bound, no
   skip), the generated row (seed 1 if under 24,000, else seed 2 if it is, else Raraku's rule with the reason), `MapFileTests`,
   `MapSlice2 OtherSeeds` Raraku seed 5 (ends, or stays in QA's `Known` under BUG-1152 with the number), the mid-hunt rows
   (`AiDeterminismTests` / `HuntStressTests`) on whichever Pale Hills seed still hunts, `KnownStalls`, both `KnownBeatenAlive` lists,
   `StandardPresetPinQaTests`, the 8 `NonHardMatchPinQaTests` digests, QA's `RebuildLookMatch` / sweep rows, the malazan / whirlwind
   page cells (rule 40). Pale Hills seed 2 (BUG-1068's stalemate, skipped): run it; if the lever ends it inside 72,000, un-skip the
   seed-2 rows and say so in the BUG-1068 / BUG-1116 patches.
5. The Easy proof: extend `CounterCompositionTests.TheNonHardPins_WithNormalStrengthZero_*` to set `starvedWaveAfter` 0 as well
   (all 8 old digests reproduced), keep `AnEasyMatch_WithTheMemoryFed_*`; add `AStarvedSeat_WavesAfterTheWindow_AndNotBefore`
   (scripted: mines emptied, no gold worker, an army under threshold; the wave starts on the first think at or after the window),
   `AStarvedSeat_ThatRegainsIncome_ClearsTheStamp`, `ASeatMidStamp_SavedAndLoaded_WavesAtTheSameTick`, and a knob-0 twin on one
   whole Normal v Normal match (equal command log).
6. Docs: docs/02 "AI opponent" one sentence on the starved wave; docs/03 "Implementation (M5-9a)" the rule, the knobs, the
   re-measured pins table; docs/05 criterion 9's parenthesis (the Producer ticks); BUG-1068 / 1152 / 1153 (and 1116 if seed 2
   ends) as patches in the scratchpad.

**Out of scope:** BUG-1068 (a) (the expansion reach), any map-data change, BUG-1152's gather fix, BUG-1010, D26's content, any
change to Normal's `counterStrength` or the counters table, a format bump.

**Acceptance criteria:** (1) build 0 / 0, the full non-Perf run green with every skip carrying an id, `AiPerfTests` 5 / 5 alone
(8 Hard seats under 1 ms, 0 B), smoke PASS; (2) round 1's criteria 2-6 and 8 still met (its tests still green); (3) Pale Hills seed
1 Normal v Normal ends inside 36,000 with a winner and every (a)-(e) check clean, in `MatchCriterionTests` (both rows),
`MapFileTests` and QA's sweep, without a skip or a bound change; (4) with `starvedWaveAfter` 0 and Normal `counterStrength` 0 all
8 old digests reproduce, and the golden header `30C51BB619A7E650` is unchanged; (5) the scripted starved seat waves on the first
think at or after the window and not before, a regained income clears the stamp, a mid-stamp save follows to the same wave tick;
(6) every pinned row in scope 4 is either green or in `Known` under an existing id with the measurement, and no row is re-bounded
without the generated row's rule; (7) the Hard-v-Normal 42-match tally (28 / 9 / 5) does not invert (Hard still wins more than it
loses); (8) the knobs are validated and hashed, docs/02 / 03 / 05 and the page cells updated, no player-facing text in C#, no
allocation on the think path, the AI's state out of `StateHash`.

**Design references:** docs/02 "AI opponent" (the AI keeps playing while it has anything to play with; Easy / Normal / Hard
blocks); docs/03 "Implementation (M5-9a)" on the branch (the memory, the shift, `CTRM`), "Implementation (M5-5)" (the per-map
table and bounds), "Implementation (M5-4d)" (the hunt's rule), "Implementation (M5-4f)" (the defense's pull from a wave);
BUG-1068's M5-H5 section (candidate (b)); BUG-1153's probe (the 24:00 gold exhaustion, the 10-archer army under 14 pop / 861).

**Constraints most at risk:** no per-tick / per-think allocation (the starved check reuses the census pass); no `Dictionary`
iteration; determinism (the stamp is saved, so a loaded match waves on the same tick); the knob-0 twin bit for bit; the AI's state
stays out of `StateHash`; data-driven (both numbers in `rules.json`, nothing hard-coded); docs in the same commit.

**QA focus (full):** round 1's whole list (fog: the memory never counts an unseen unit, the oracle on every think; the knob-0 /
strength-0 twins on all 8 pins and the golden; the formula's edges: a memory of one slot, a faded total just above 0, a composition
with no counter; save / load fuzz incl. forged `CTRM` counts / ticks / `StarvedSince` in the future; perf: 8 Hard seats under 1 ms,
0 B, alone after 30 s idle, rule 42; every pin re-run independently in its own process; the behavioural sanity row: a Hard seat
that has seen 20 Horse Raiders for 3 minutes trains visibly more Line than one that saw none) **plus the lever's own:** (a) the
scripted starved seat waves on exactly the first think at or after the window, not one think earlier, at every difficulty; (b) a seat
that regains a gold worker or sees a free mine in reach clears the stamp; (c) a seat under a raid whose mines lie near the defense
point is never counted starved (the `Defending` guard); (d) no starved wave fires before tick 12,000 on any of the 8 pinned matches
(early poverty with known mines is not starvation); (e) the four-seat FFA / 2v2 rows and Pale Hills seed 2 to 72,000 with every
(a)-(e) check; (f) the Hard-v-Normal tally over the same 42 matches as M5-4c slice 2; (g) a save mid-stamp loaded in a second process
follows to the same wave tick and end hash; (h) a starved wave that retreats charges again after the cooldown rather than sitting
(the loop must converge: the seat dies or wins within the bound on every pinned map).

### Round 2's original skeleton (kept for reference): M5-9a round 2 (feature, counter 1 / 4 → 2 / 4, QA full), resume `studio/2026-11-08-1316-sim`
**Not a fresh branch (rule 44 is for accepted work): the next session checks the branch out, merges `origin/main` if it moved
(it should not: nothing else is planned), and builds on 29879eb.** Size: round 1 is ~1,100 hand-written lines; round 2 adds the
lever (~150-300) and re-measures. The brief should carry round 1's criteria 1-8 with the corrections below, plus:

1. **The lever: a starved side attacks with what it has (BUG-1068 candidate (b), the fix for BUG-1153).** `rules.json` `ai.starvedWaveAfter`
   (seconds, 0-600, to ticks; **0 = off**, which must reproduce today's play bit for bit together with Normal strength 0; ship a value
   the developer measures, start at 60) and, if needed, `ai.starvedMinArmy` (whole number, ship 1). Rule, checked in the census: a
   seat is *starved* on a think when (i) its gold is under the cheapest unit its current composition can train, (ii) no own worker
   gathers gold and it knows no free gold node in `ai.gatherReach` of a hall or depot (the `KnowsNode` / `InGatherReach` reads M5-4g /
   M6-S5 built), and (iii) it has at least `starvedMinArmy` combat units. Once starved for `starvedWaveAfter` running (a stamp on the
   `Blackboard`, saved in `CTRM` or a sibling section under the same format numbers 8 / 9: no third bump), `MilitaryManager` starts
   a wave as if the threshold were met (the wave's target rule unchanged; a hunt if no building is known, as M5-4d). The stamp
   resets when any of (i)-(iii) stops holding. Every difficulty (an Easy stall is still a stall: the Easy pins at 36,000 are matches
   that never ended). No allocation; the AI's state stays out of `StateHash`.
2. **Re-measure everything in the branch's watch-out list once more** and settle the rows the round left failing: `MatchCriterionTests`
   Pale Hills seed 1 must end inside 36,000 (if the lever does not end it, the task reports what the end game looks like at
   `starvedWaveAfter` 30 / 60 / 120 and stops there: the Producer then decides between BUG-1068 (a), the expansion reach, and a data
   change; **do not re-bound, do not skip**); Raraku seed 5 (`MapSlice2 OtherSeeds` + the sweep) should end too, else it stays in
   `Known` under BUG-1152 with the measurement; the generated row keeps seed 1 if it ends under 24,000 again, else seed 2 if that
   does, else Raraku's rule with the reason; the mid-hunt rows (`AiDeterminismTests` / `HuntStressTests`) on whichever Pale Hills
   seed still hunts; `KnownStalls`; the two `KnownBeatenAlive` lists; the page cells (rule 40).
3. **The Easy proof, restated (round 1's correction stands):** QA's "Easy" pins are Easy-v-Normal matches, so their digests move
   with the Normal seat; the proof is (a) all 8 old digests reproduced with Normal strength 0 **and** `starvedWaveAfter` 0 (extend
   `TheNonHardPins_WithNormalStrengthZero_*`), (b) an Easy match with sightings equals one with `counters` emptied.
4. **QA full, every focus of round 1's brief** (fog, the knob-0 twins, the formula's edges, save / load fuzz incl. the new stamp,
   perf 8 Hard seats under 1 ms / 0 B, every pin re-run independently, the behavioural sanity row) **plus the lever's own:** a seat
   made starved by script (mines emptied, no gold worker) waves after exactly the window and not before; a seat that regains a
   gold worker resets the stamp; the four-seat FFA / 2v2 rows and Pale Hills seed 2 (BUG-1068's true stalemate: does the lever end
   it? If yes, un-skip and close the seed-2 rows); the Hard-v-Normal 42-match tally (M5-4c slice 2's 28 / 9 / 5) must not invert.
5. Docs: docs/02 "AI opponent" a sentence on the starved wave; docs/03 "Implementation (M5-9a)" the rule, the knob, the pins moved
   (second table); BUG-1068 / 1152 / 1153 files updated by patch; docs/05 criterion 9's parenthesis (the Producer ticks).
6. **If round 2 is rejected too, the track STOPs** ("the same task rejected twice") and the owner is asked; so the brief tells the
   developer to report partial results (the lever's effect on each row) rather than BLOCKED.

### After it
BUG-1010's lever (defend-then-counter) if small; the end-of-M5 sim hardening at 4 / 4 (BUG-1149 / 1150 / 1151 / 1152 / 1058 /
1044 / 1067, BUG-1116's sweep order, BUG-1068's (a), the movement S3s, `HasMapId`'s note, `HUNT`'s range checks, BUG-1124's record).
Then M7's sim systems (M5 held at 8 / 9 for criterion 6).

---

## View track

### Current session plan (2026-11-08-1556): STOP (cheap)
Inbox New empty: no owner item (the art call, ".NET 10 installed", templates, sound notes); every remaining M6 criterion needs the
owner; the view's debt (BUG-1126 / 1147 / 1115 item 4 / 0860 / 0830 / 0890 / 0861) is not a session's worth and the counter is 1 / 4.
The sim's full QA pass runs three hosts' worth of pins alone this session, so the CPU is better left to it.

### Where we are
STOP (cheap) this session. Counter 1 / 4. M6 5 / 10; every remaining M6 criterion needs the owner (the art call, music / fonts /
export templates, ".NET 10 installed", a friend's playtest).

### Next task candidates
- An owner item from the inbox (the art call, the SDK, templates, sound notes): a feature session, QA standard.
- Otherwise STOP (cheap). Debt for the view's next hardening (not a session's worth): BUG-1126 S4, BUG-1147 S4 item 1, BUG-1115
  item 4, BUG-0860 / 0830 / 0890 / 0861.
- **Not yet, noted:** a view of the AI's counter memory on the stats screen ("adapted to your Line") only if the owner asks.

---

## Data track

### Current session plan (2026-11-08-1556): STOP (cheap)
M5-9a's schema is still on its branch (round 2 runs this session), no review tweak is waiting (inbox New empty), M5-5 moved no pin
and re-printed no cell on `main` (rules 40 / 41). D26 starts the session after M5-9a lands.

### Where we are
STOP (cheap) this session. Counter 2 / 4. **M5-9a's schema (`counterStrength`, `ai.counterMemory`, `ai.counters`) is still not on
`main`** (the branch is rejected), so D26 waits one more session.

### Next task candidates
- **D26: D14 c's content** once M5-9a lands: the per-difficulty `counterStrength` for both factions against the design (the branch
  ships Easy 0 / Normal 0.5 / Hard 1.0 as the minimum; D26 may tune, and may add a Line counter to the Malazan Age I `standard` mix,
  which today has none against a Whirlwind Raider army: docs/03 "Implementation (M5-9a)"'s worked example), the faction pages' AI
  paragraph, the prose round the re-printed cells, `Content/` pins, the owner's review table. QA light (standard if a balance sample
  is needed).
- The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780).
- M7's rosters when M7's schemas exist.
