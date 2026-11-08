# Handoff: brief for the next session

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
or the task says why the bound itself changes.

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

### Current session plan
(none: written by the next PLAN)

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

### Current session plan
(none)

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

### Current session plan
(none)
