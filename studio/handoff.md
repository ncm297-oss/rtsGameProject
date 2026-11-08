# Handoff: brief for the next session

_Updated at the 2026-11-08-1316 PLAN (session 2 / 8): the 0726 session died after ACCEPT, before integration; its sim branch is resumed and
integrated as accepted (re-verified on a scratch clone: build 0 / 0, targeted 550 / 0 / 0, smoke PASS), then sim runs **M5-9a** on a fresh
branch; view and data STOP (cheap). The "Current session plan" sections below are this session's._

Written by the Producer at the ACCEPT of session **2026-11-08-0726** (session 1 / 8 of 2026-11-08, started 07:26; sim only: view and
data STOP at PLAN). Sim **M5-5, the 20-minute AI-vs-AI headless test, ACCEPT with no fix round (QA PASS_WITH_ISSUES: two new S3).**
`main` after the conductor's merge carries: `Ai/MatchCriterionTests` + `Ai/CriterionMatch` (the literal row Vathar Crossing seed 1 in
the default loop, 22,794; Raraku / Pale Hills / generated, FFA, 2v2 as Stress; six two-process rows; the half-way save),
`Simulation.RefusedForTests` + `Commands/CommandRefusal` (the apply methods return their refusal), `Rts.Cli match`, QA's
`CommandRefusalSeamFuzzQaTests` / `MatchCriterionSweepQaStressTests`, docs/03 "Implementation (M5-5)" + the CLI section, docs/05
criterion 5 ticked, docs/01's row. **No pin moved; golden `30C51BB619A7E650` unchanged; formats 6 / 7, replay 9; the data hash
unmoved.** **M5 7 / 9** (left: criterion 6 owner-only, criterion 9 = D14 c), M6 5 / 10. **Open S1 / S2: none** (S3 23, S4 15; new
BUG-1151 / 1152). Counters after: **sim 1 / 4, view 1 / 4, data 2 / 4.** Bug ids next: **all tracks from 1153.**

## Standing rules (1-43)

Unchanged 1-42 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle; the `AiPerfTests`
class is run row by row). **New 43:** every sim task that can change Normal AI behaviour names, in its brief, which of the M5-5 rows
it expects to move (end tick, winner, refusals) and re-measures `MatchCriterionTests`' bound comments and docs/03's M5-5 table in the
same commit; the literal row (Vathar seed 1 within 24,000) may move its end tick but must keep ending with a winner inside the bound,
or the task says why the bound itself changes. **New 44 (2026-11-08-1316):** when a session dies after its ACCEPT and before
integration, the next Producer does not re-run QA on an unchanged branch head: it re-verifies on a scratch clone (build, the targeted
rows the ACCEPT leaned on, smoke), fills the placeholders in the session log and STATE, and the conductor integrates that branch first;
a new task for the same track starts on a fresh branch from `main` after the merge, never on the resumed branch.

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rule 42 before filing. The full non-Perf run is now
  ~8,600 rows: 1 h 48 m beside QA's harnesses, about 50 m alone. The default (non-Stress) loop gained ~60 s in M5-5 (the literal row
  26 s, the save row 14 s, the CLI's literal row ~20 s); if it keeps growing, the CLI literal row is the first to move to Stress.
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
- A developer must never kill another track's Godot or `testhost` process. The scene loop's own timeout handles a hung scene.
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer (or QA, as
  this session) applies it. Say so in every brief ("bug files: leave a patch").
- **A brief that relaxes a judge must name what the relaxed query cannot see** (M5-H5's lesson).
- **Pinned matches (must move only with a listed reason):** `MatchCriterionTests` (Vathar 22,794 / Raraku 28,083 / Pale Hills
  34,219 / generated 21,789 / FFA 52,305 / 2v2 34,510, bounds in the constants), `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks`
  (34,219 / 22,794), `MapSlice2StressTests.KnownStalls` (29,562 / 55,631 / 26,034), `StandardPresetPinQaTests` (7 digests), QA's
  `NonHardMatchPinQaTests` (8 Easy / Normal whole matches; Pale Hills seed 1 Normal `69C4358E67DF7FC3 34219`), `HardKnobsPinTests`,
  the Malazan / Whirlwind page tables, `D21OpeningsQaTests` / `DH6ThresholdQaTests` and their `KnownBeatenAlive` lists ({(1, 1, 19, 0)}
  and {}), the golden header, QA's `MatchCriterionSweepQaStressTests.Known` (BUG-1151 generated seed 4, BUG-1152 Raraku 1 / 2: a Known
  row asserts it still fails, so a fix must remove the entry).
- **Known stalls (skipped with ids):** Pale Hills seed 2 (`MapSlice2StressTests.NormalVersusNormal_PaleHillsSeed2_Stalemate_...`,
  BUG-1068; `HuntStressTests.FourAiSeats_OnPaleHills_Seed2_KnownStall`, BUG-1116 / 1068), `EndGameHardeningTests.ALoneIdleWorker_*`
  (BUG-1116), QA's `M5H5HuntAfterDefeatQaTests.A2v2_TheDefenseToo_*` (BUG-1149), `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150).
- **On the three map files seeds 3 / 4 / 6 are one match** (the seed only picks the openings): a "seeds 1-6" sweep is 18 games.
- **The refusal seam's blind spots:** the AI's door (`TryEnqueue` false on a full 1,024 queue) and a queued order dropped at
  phase 7; no row fills the queue and the AI queues no shift orders today. A task that changes either adds a row.
- **Criterion 6 (the owner wins on Easy, loses to Hard) is owner-only:** M5 cannot be signed off by the Producer until the owner
  writes the result in the inbox. After M5-9a (criterion 9) and the end-of-M5 hardening, the sim track routes around to M6's sim
  items if any, else M7's sim systems (scale / per-unit population, Giant armor, regeneration: docs/05 M7), with M5 held at 8 / 9.

---

## Sim track

### Where we are
M5-5 accepted. Criterion 5 ticked; criterion 9 (D14 c, counter-composition) is the last the studio can do itself. Counter 1 / 4.

### Next task candidates (in order)
1. **M5-9a (feature, counter 1 / 4 → 2 / 4, QA full): D14 c's counter-composition, the sim's half.** Design: docs/05 M5 criterion 9
   ("the AI shifts its army mix toward what counters the enemy units it has seen (fog-limited, decayed memory; strength per
   difficulty, 0 on Easy); think cost stays inside the 1 ms gate"), the Producer assessment in STATE (For your review, "D14
   feasibility verdict" item (c): each enemy unit type seen once, fading over a few minutes; the template counters table in docs/02
   "Factions" l.27: Line counters Shock, Ranged counters casters and light units, Shock counters Ranged, casters counter clumped
   Line, siege counters buildings; a strength per difficulty). The sim ships: the `ai.json` schema (`difficulties.<d>.counterStrength`
   0..1, Easy 0, validated, hashed: the golden's data hash moves once; and a per-slot `counters` table if the template's isn't already
   in `units.json` `attack.bonus` form: check `UnitDef` first), `ProductionManager`'s mix shift (the wanted slot share nudged by the
   seen enemy slots' counters × strength, never below the build order's minimum), the memory (`PlayerView`-fed, per enemy slot, a
   tick stamp decayed over `rules.json` `ai.counterMemory` seconds; saved: **save format 8 / replay 10**, the oldest-format rule
   with byte-for-byte fixtures as M6-S7 did), a docs/02 "AI opponent" paragraph (none exists: the brief must add it), docs/03
   "Implementation (M5-9a)". Minimum data: Normal / Hard strengths at the recommended values, the template table's entries only.
   **Rule 43:** name which M5-5 rows may move (all six Normal rows can: the mix shift changes Normal) and re-measure them plus the
   `NonHardMatchPinQaTests` 8 digests, `KnownStalls`, `MapFileTests`' pin, the faction-page cells (rule 40), `KnownBeatenAlive`
   (rule 41: the data track STOPs this session, so the sim re-measures). Perf: `AiPerfTests` 8 seats under 1 ms, 0 B a think. Size:
   uncertain (a new system): ~800 lines, the first slice; the data track fills the content as D26 next session.
2. **BUG-1010's lever (defend-then-counter, M5-4g's unbuilt slice 2)** if M5-9a lands small, else the hardening at 4 / 4.
3. **The end-of-M5 sim hardening** (after criterion 9): BUG-1149 (via `SightingPoint`; the two skipped 2v2 rows are the gate),
   BUG-1150, BUG-1151 (mark the gone node when a non-idle worker's Gather is dropped, or skip it in `Rebalance`), BUG-1152 (a gatherer
   to wood when every gold-affordable option is blocked on wood), BUG-1058, BUG-1044's rest, BUG-1067, BUG-1116's sweep order (QA's
   hide / leg rows as the gate), BUG-1068's gold candidate (a), the movement S3s, `HasMapId`'s note, `HUNT`'s range checks, BUG-1124's
   record. Each AI change re-measures the M5-5 rows (rule 43).

### Current session plan (2026-11-08-1316, session 2 / 8; sim worktree resumes `studio/2026-11-08-0726-sim` for M5-5's integration, then M5-9a on a fresh branch from `main`)

**TASK_ID M5-9a · Counter-composition, the sim's half (D14 c, M5 criterion 9) · feature (counter 1 / 4 → 2 / 4) · QA full · ~800 lines
(a new system: slice 1; QA's suites and regenerated pins don't count, rule 39).**

**Goal.** The AI shifts its army mix toward what counters the enemy units it has seen, from a fog-limited memory that fades over a few
minutes, by a strength per difficulty (Easy 0). Today `ProductionManager` trains the opening's composition blind to what the enemy fields;
the owner asked (2026-10-14, D14 c) for an AI that adapts its army to the player's. This task ships the schema, the memory, the shift and
the proof; the data track tunes the numbers next session (D26).

**Scope (in).**
1. **Schema, sim-owned.** `ai.json` `difficulties.<d>.counterStrength` (required, 0..1, `Fraction`-validated like `gatherBonus`, hashed in
   `GameData`'s data hash next to `GatherBonus`); minimum data in both factions' files: easy **0**, normal **0.5**, hard **1.0**.
   `rules.json` `ai.counterMemory` (seconds, > 0, loader converts to ticks; ship **180**) and `ai.counters`: an object keyed by the enemy
   **slot name** seen (`worker`, `line`, `ranged`, `shock`, `caster`, `siege`, `unique`: `UnitSlot` names in snake_case) whose value is the
   list of own slot names that counter it, from docs/02 "Faction template" l.27's "Countered by" column: `worker: [shock]`,
   `line: [caster, siege]`, `ranged: [shock]`, `shock: [line]`, `caster: [shock, ranged]`, `siege: [line, shock]`, `unique: []`. Validation:
   every key and value a known slot name, no duplicates, every slot present as a key (an empty list is fine). Both in `AiRulesDef`
   (`CounterMemoryTicks`, a `CountersBySlot` bit mask or bool[7,7]) and `DataValidationTests`.
2. **The memory** (`Blackboard`, per enemy slot, 7 entries): in `AiPlayer.Think`'s census loop (the `View.NextUnit` pass that already
   counts enemies, l.549-577), count the enemy **non-worker** units seen this think per `Data.Units[u.TypeId].Slot`; after the loop, for
   each slot: if the count seen now ≥ the faded value, set `SeenSlotCount[s] = count`, `SeenSlotTick[s] = Now`. The faded value of a slot
   is `SeenSlotCount[s] × max(0, 1 − (Now − SeenSlotTick[s]) / CounterMemoryTicks)`, so a sighting fades linearly to nothing after
   `counterMemory` and a bigger sighting replaces a smaller fading one. Workers are counted nowhere (the AI always sees workers; a Shock
   shift because it saw a laborer is not a counter). Allies' units are not enemies (`View.IsEnemy`). Fog-limited by construction: the
   loop only sees what `PlayerView` answers.
3. **The shift** (`ProductionManager.TrainOne`): the effective weight of composition entry k becomes
   `mix.Weights[k] × (1 + counterStrength × countered_k)`, where `countered_k` is the share of the faded enemy total (Σ over the 7 slots)
   that unit k's slot counters (Σ faded[e] for every e that `counters[e]` lists k's slot, ÷ the total; 0 when the total is 0). So a Line
   unit facing an all-Shock army gets up to × 2 on Hard and × 1.5 on Normal; nothing is ever weighted **below** the opening's own weight,
   so every composition unit is still trained, and a unit the current composition does not list is **not** added (D26 may add it to the
   openings' compositions). With strength 0 the multiplier is exactly × 1 (`weights[k] × (1 + 0 × x)` in float is `weights[k]`), so
   **Easy's matches are bit-identical to today**: a pin test proves it. No allocation: the 7 faded values and the per-slot
   `countered` scratch are fixed arrays on the board or the AI.
4. **Save / load.** `SeenSlotCount[7]` / `SeenSlotTick[7]` saved with the AI seat (`AIST` or a new section, the developer's call, written
   only for a match with an AI seat): **new save format numbers** for AI-seat matches under the oldest-format rule (`SaveHeader.VersionFor`:
   formats 4 / 5 for human-only matches and the 6 / 7 fixtures stay byte for byte, as M6-S7 / M5-4d did; `PreMapFixtures` proves it);
   a loaded seat re-saves byte-identical and follows to the same end hash. **Replay format unchanged** (playback runs no AI; the AI's
   commands are in the log) unless the developer finds a reason, stated in the report. The AI's state stays out of `StateHash`.
5. **Data hash and pins (rules 40 / 41 / 43).** The new knob moves the data hash: regenerate `DataContentHashTests` and the golden
   header's data hash in the same commit with the reason; the golden **state** hash `30C51BB619A7E650` must not move (no AI seat). Normal
   and Hard behaviour changes, so these may move and are re-measured in the same commit: the six M5-5 rows in `MatchCriterionTests`
   (each must still end with a winner inside its bound: Vathar seed 1 within 24,000; Raraku / Pale Hills 36,000; generated 24,000; FFA /
   2v2 72,000, or the report says why the bound changes, and a row that stops ending is a finding, not a re-bound), `MapFileTests`'
   `NormalVersusNormal_OnANewMap_*` pin, `MapSlice2StressTests.KnownStalls`, `StandardPresetPinQaTests` (AI-seat rows),
   `HardKnobsPinTests`, QA's `NonHardMatchPinQaTests` (the Normal digests; the Easy digests **must not** move), `D21OpeningsQaTests` /
   `DH6ThresholdQaTests` + their `KnownBeatenAlive` lists (rule 41: the data track STOPs, so the sim re-measures; the lists today are
   {(1, 1, 19, 0)} and {}), QA's `MatchCriterionSweepQaStressTests.Known` (BUG-1151 / 1152 rows: if a row no longer reproduces, remove
   the Known entry and say so in the report; the bugs stay open unless their cause is fixed), docs/03's M5-5 table, and the pinned
   cells on `docs/factions/malazan.md` / `whirlwind.md` (rule 40: those cells only; the data track re-words the prose next session).
   The handoff's "Pinned matches" list in the report with every new number.
6. **Perf.** `AiPerfTests` unchanged thresholds: 8 seats under 1 ms a think in Debug, 0 B a think (rule 42 when judging).
7. **Docs.** docs/02 "AI opponent": a new bullet (the AI counters what it has seen: the memory, the fade, the strength per difficulty,
   the knobs). docs/03 "Implementation (M5-9a)": the schema, the memory and the formula with a worked example (a Normal Malazan seeing
   6 Raiders and 2 Crossbowmen: the weights before / after), the save section, the pins moved. docs/05 criterion 9's line gets the usual
   parenthesis (the Producer ticks). docs/01: the Producer's row.

**Scope (out).** Adding units to a composition the opening lacks; per-unit `counteredBy` overrides in `units.json` (D26 may request it);
any change to wave timing, defense, hunt or the executor; a view of the memory on the stats screen; the Shock pass / `goldShare`
proposals; BUG-1010's defend-then-counter lever; BUG-1151 / 1152 (hardening); any `docs/factions/**` prose beyond the pinned cells.

**Acceptance criteria.**
1. `dotnet build RtsGame.sln` 0 warnings / 0 errors; `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf` green (skips only with a bug
   id); `AiPerfTests` 5 / 5 alone (rule 42); `tools/qa/smoke.ps1` PASS.
2. **The shift is real and fog-limited:** `Ai/CounterCompositionTests`: (a) a Normal Malazan seat (`standard`) that sees 8 enemy Raiders
   for one think trains more Heavy Infantry (Line) over the next N minutes than the same seat and seed with the Raiders placed outside
   its sight (both runs scripted through the command API / a test world; the counts compared on `Live + Queued`); (b) the same with Hard
   gives a larger Line share than Normal; (c) the seat whose sighting is outside its vision has a memory of all zeros (and
   `PlayerViewOracle` stays green across the match rows).
3. **The memory fades:** after `counterMemory` seconds with no sighting, the faded values are 0 and the pick equals the baseline
   composition's (a test drives `Now` past the window and asserts the pick); a bigger sighting replaces a smaller fading one, a smaller
   one does not.
4. **Easy is bit-identical:** every Easy digest in `NonHardMatchPinQaTests` unchanged; a new row runs one Easy match with the memory
   fed (sightings happen) and asserts the command log equals a run with `counters` emptied.
5. **Save / load:** a Normal seat saved with a non-empty memory loads with the same `SeenSlotCount` / `SeenSlotTick`, re-saves
   byte-identical and follows to the same end hash (`MatchCriterionTests`' `SavedHalfWay` row still green plus a memory-specific row);
   `PreMapFixtures` byte for byte; a human-only match still writes formats 4 / 5; a forged slot count or a tick in the future is `Malformed`.
6. **Schema:** `DataValidationTests` rows: `counterStrength` missing / 1.5 / −0.1 refused with the path; `ai.counters` with an unknown slot
   name, a duplicate, or a missing key refused; `ai.counterMemory` 0 refused; `DataContentHashTests` regenerated with the reason in the
   commit; `AiContentTests` (or a new `Content/` row) pins easy 0 / normal 0.5 / hard 1.0 for both factions (D26 moves them).
7. **Pins re-measured per rule 43 (scope 5):** every listed pin regenerated in the same commit with the new numbers in the report and
   docs/03's M5-5 table; all six M5-5 rows still end with a winner inside their bounds; `EveryRow_InTwoProcesses_*` 6 / 6; the golden
   state hash unchanged.
8. docs/02 / 03 / 05 updated as in scope 7; no player-facing text in C#; no Godot, wall clock or unseeded RNG in the sim; no per-tick
   allocation.

**Design references.** docs/05 M5 criterion 9 (l.744); docs/02 "Faction template" l.27 (the counters table) and "AI opponent" (l.409);
STATE "For your review" → the Producer assessment of 2026-10-14, "D14 feasibility verdict" item (c); docs/03 "Implementation (M5-2)"
(`ProductionManager`), "(M5-8a)" (openings, compositions as population shares), "(M5-4d)" save format 6 / 7 and the oldest-format rule
(l.8105), "(M6-S7)" fixtures; `AiDifficultyDef` / `DataLoader.Ai.cs` `Fraction` for the knob pattern; `AiRulesDef` for `rules.json` `ai`.

**Tests required.** `Ai/CounterCompositionTests` (criteria 2-5), `DataValidationTests` rows (6), `Content/` pin (6), the regenerated pins
(7), `Save/` format rows (5), `AiPerfTests` unchanged (1). Determinism: the two-process M5-5 rows and the save row.

**Constraints.** CLAUDE.md 4 (no dictionary iteration: the memory is a 7-array indexed by `UnitSlot`; `counters` loaded into a fixed table),
5 (no allocation in `Think` / `TrainOne`), 6 (no stat in C#: strengths, memory, the table all data), 8 (no text). Rule 38 (the Producer
verifies on a scratch clone), 39, 40, 41, 42, 43 (above). **Bug files: leave a patch in the scratchpad** (the hook). Don't kill another
track's processes (none run this session, but the rule stands). The task starts on a **fresh branch from `main` after M5-5's merge**, not
on the resumed branch (rule 44).

**QA focus (full).** (1) **The fog:** feed a seat sightings just outside its vision range, under an ally's vision only (2v2: an ally's
sight must not fill the memory unless `PlayerView` shares it, which it does for teams since M6-S5: assert the memory follows exactly what
the view answers), stealthed units, units seen then lost: the memory must equal what a fog-only observer records, and `PlayerViewOracle`
every think. (2) **Bit-identical Easy** across all 8 Easy / Normal pins' Easy rows and a 20-minute Easy match with heavy sightings.
(3) **The formula's edges:** a total of 0 (no division), one slot seen, every slot seen, a composition with a single unit (`cadre_rush`
age 1: nothing to shift to; no crash, no change), `counters` with every list empty (bit-identical to today on Normal: the shift is a
no-op), strength 1 with an all-Shock enemy (Line's weight exactly doubles). (4) **Save / load fuzz:** save at every think boundary of a
2,000-tick window, load, re-save byte-identical, follow 600 ticks to the same hash; forged `SeenSlotTick` > Now and counts < 0 refused.
(5) **Perf under load:** 8 Hard seats, 2,600 units, every seat seeing every enemy slot: think under 1 ms, 0 B (rule 42). (6) **The
pins:** re-run every pinned match independently and compare to the developer's new numbers; the six M5-5 rows in two processes; any
row that stops ending inside its bound is S2. (7) **Behavioural sanity, not tuning:** a Normal Whirlwind `raider_rush` against a Normal
Malazan `standard` on Vathar seed 1: does the Malazan side's Line share actually rise after the first raid (log the mix per minute)? If it
does not move at all at strength 0.5, that's a finding (S3, the shift is too weak to see), not a failure of a criterion.

---

## View track

### Where we are
STOP (cheap) this session. Counter 1 / 4. M6's remaining criteria wait on the owner (the art call, music / fonts / export templates,
".NET 10 installed", a friend's playtest); M7+'s view work waits on the art call.

### Next task candidates
- An inbox item (the art call → the art pass; ".NET 10 installed" → the target move; templates → `tools/export.ps1`; sound notes →
  the re-cut) is a feature session, QA standard.
- Otherwise STOP (cheap). Debt for the view's next hardening (not a session's worth alone): BUG-1126 S4 (ruling in the file),
  BUG-1147 S4 item 1, BUG-1115 item 4, BUG-0860 / 0830 / 0890 / 0861.
- **Not yet, noted:** when M5-9a lands, the view could show the AI's counter memory in the end-of-match stats ("adapted to your
  Line") only if the owner asks; no request filed.

### Current session plan (2026-11-08-1316)
**STOP (cheap).** Inbox New is empty; every remaining M6 criterion needs the owner (the art call, music / fonts / export templates, ".NET 10
installed", a friend's playtest); the view's debt is not a session's worth and its hardening is not due (1 / 4). Nothing on the sim's
M5-9a needs a view change.

---

## Data track

### Where we are
STOP (cheap) this session. Counter 2 / 4. No schema it needs is on `main` yet; M5-5 moved no pin and re-printed no page cell.

### Next task candidates
- **D26: D14 c's content** once M5-9a's schema is on `main`: the per-difficulty `counterStrength` for both factions against the
  design's recommendation, the counters table's entries if it is data, the faction pages' AI sections (a paragraph each: what the
  AI adapts to), `Content/` tests pinning the numbers; QA light (standard if a balance sample is needed). The review table for the
  owner as rule "merge, then review".
- The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780).
- M7's rosters when M7's schemas exist.

### Current session plan (2026-11-08-1316)
**STOP (cheap).** No schema it needs is on `main` (M5-9a's `counterStrength` / `ai.counters` land this session), no review tweak is in the
inbox, and M5-5 moved no pin and re-printed no page cell (rules 40 / 41). Next session: **D26** against M5-9a's schema (the strengths per
faction, the pages' AI paragraph, the prose round any re-printed cells, `Content/` pins, the owner's review table).
