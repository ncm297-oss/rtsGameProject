# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-07-2306** (session 3 / 8 of 2026-11-07, started 23:06; sim only: view and
data STOP at PLAN). Sim **M5-H5, the sim's hardening, ACCEPT after one fix round.** `main` after the conductor's merge carries:
BUG-1119 / 1120 / 1118 (item 1) / 1059 / 1148 fixed, the `HuntRing` load range tightened, docs/02 / 03 / 01 updated. **No pin
moved; golden `30C51BB619A7E650` unchanged; formats 6 / 7, replay 9; the data hash unmoved.** M5 6 / 9, M6 5 / 10. **Open S1 / S2:
none** (S3 21, S4 15). Counters after: **sim 0 / 4, view 1 / 4, data 2 / 4.** Bug ids next: **all tracks from 1151** (QA used
1148-1150 this session).

## Standing rules (1-42)

Unchanged (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle; the `AiPerfTests`
class is run row by row).

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rule 42 before filing. A full non-Perf run beside
  QA's match harnesses took 1 h 15 m this session (about 45-50 m alone).
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
- A developer must never kill another track's Godot or `testhost` process. The scene loop's own timeout handles a hung scene.
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it
  at ACCEPT and fills in the hashes. Say so in every brief ("bug files: leave a patch").
- **A brief that relaxes a judge must name what the relaxed query cannot see** (M5-H5's BUG-1148: `CanPlace` ignores units in
  the way and the never-seal rule; "simpler check" cost a fix round).
- **Pinned matches (must move only with a listed reason):** `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks`
  (Pale Hills seed 1 at 34,219, Vathar at 22,794), `MapSlice2StressTests.KnownStalls` (29,562 / 55,631 / 26,034),
  `StandardPresetPinQaTests` (7 digests), QA's `NonHardMatchPinQaTests` (8 Easy / Normal whole matches, every tick and command;
  Pale Hills seed 1 Normal `69C4358E67DF7FC3 34219`), `HardKnobsPinTests`, the Malazan / Whirlwind page tables, `D21OpeningsQaTests`
  / `DH6ThresholdQaTests` and their `KnownBeatenAlive` lists ({(1, 1, 19, 0)} and {}), the golden header.
- **Known stalls (skipped with ids):** Pale Hills seed 2 `MapSlice2StressTests.NormalVersusNormal_PaleHillsSeed2_Stalemate_...`
  (BUG-1068) and `HuntStressTests.FourAiSeats_OnPaleHills_Seed2_KnownStall` (BUG-1116 / 1068: gold starvation, every seat under
  25 gold by 48,000); the lone-worker row `EndGameHardeningTests.ALoneIdleWorker_NearWhereTheHuntBegins_IsSeenBeforeThePerimeterLeg`
  (BUG-1116); QA's `M5H5HuntAfterDefeatQaTests.A2v2_TheDefenseToo_*` (BUG-1149); `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150).
- **Measured for M5-5's planning (QA, 24 four-seat Normal matches to 72,000 = 60 min):** FFA ends 6 / 12, 2v2 8 / 12. The no-result
  causes: gold starvation on Pale Hills (BUG-1068: the `expand` step looks only 40 m, the two empty starts' 18,000 gold is never
  touched), the lone-unit gap (one seat keeps 1-3 units and no building against a 60-unit hunt for 40 min: BUG-1116's sweep
  order), or two live seats still fighting at the hour. **A 20-minute criterion must pick its maps and seeds knowing this.**

---

## Sim track

### Current session plan (next): M5-5, the 20-minute AI-vs-AI headless test (criterion 5) · feature (counter 0 / 4 → 1 / 4) · QA full

**Goal.** M5 criterion 5: "AI-vs-AI headless test: a 20-minute match completes without errors and one side wins." The sim has
the pieces (defeat rule, the owner's win rule, the hunt, rebuilding); what is missing is the committed, repeatable test on the
three hand-made maps (and the generator) that proves it, plus whatever small sim fix the chosen matches need to end inside the
bound. Keep it honest: pick the match set from the measurements above and docs/03's tables, and say in docs/03 which seeds end
and which don't and why (the three open causes have bug ids).

**Direction (the brief to write at PLAN):**
1. A `Stress`-category (or plain, if under ~2 min each) xUnit row per map (Raraku, Pale Hills, Vathar Crossing, generated
   256 seed 1): 2 Normal seats, seeded, runs to `MatchOver` within 24,000 ticks (20 min) **or** a documented bound per map if
   20 min is wrong for the 512 m maps (Pale Hills seed 1 ends at 34,219 = 28:31; then the criterion's text may need the
   Producer's reading: "a match completes and one side wins" with the bound per map listed), every tick's command stream
   validated (no refused AI command except the known kinds), `PlayerViewOracle` on every think, and the result printed.
2. One 4-seat FFA and one 2v2 row on a map and seed that end (QA's table: Vathar FFA seeds 2-5, Pale Hills FFA 3 / 4; 2v2
   Pale Hills 2 / 4 / 5 / 6, Vathar 1 / 2 / 4 / 5).
3. The CLI (`tools/Rts.Cli`) gets (or already has: check) a `match` verb that runs the same thing headless and prints the
   result and hashes, so the owner can run it.
4. Riders only if a few lines: BUG-1150's push rule (nearest clear exit), BUG-1149 through `SightingPoint` for the defense.
5. **OUT:** BUG-1068's gold (a rule change: its own task or data), the sweep order (BUG-1116: its own task with QA's hide / leg
   rows as the gate), Hard tuning, D14 c.

**Acceptance criteria (draft).** 1. Build 0 warnings, non-Perf green, Perf rows alone (rule 42), smoke PASS. 2. The criterion's
rows exist for the three maps + generated, deterministic (two processes hash-equal), every one ends with a winner inside its
bound, and docs/05 criterion 5 can be ticked with the rows named. 3. No pin moved unless listed with why (a sim rule change is
not expected). 4. docs/03 "Implementation (M5-5)" with the per-map table (ticks to a result, winner, waves, hunts).

**Then (sim):** BUG-1010's lever (defend-then-counter), D14 c's schema (criterion 9; the data track waits on it), the Hard tuning
sample if the owner asks; the next hardening at 4 / 4 (BUG-1149, BUG-1150, BUG-1058, 1044's rest, 1067, BUG-1116's sweep order
with QA's rows as the gate, BUG-1068's gold candidate (a), the movement S3s, `HasMapId`'s note, `HUNT`'s vector range checks).

---

## View track

### Current session plan (next): STOP (cheap) unless the inbox has an owner item

No unblocked roadmap work: M6's remaining criteria wait on the owner (the art call, music / fonts / export templates, ".NET 10
installed", a friend's playtest). An inbox item is a feature session, QA standard. Debt for the view's next hardening (counter
1 / 4): BUG-1126 (ruling in the file), BUG-1147 item 1, BUG-1115 item 4 (the art pass's theme): not a session's worth alone.

---

## Data track

### Current session plan (next): STOP (cheap)

The sim's hardening moved no Normal pin and re-printed no faction-page cell, so nothing to re-word; both `KnownBeatenAlive`
allowances are green on the hardened sim (rule 41 satisfied). No schema the data track needs is on `main` yet (D14 c's is the
sim's, after M5-5); no owner review answers waiting. Next real work: the owner's answers if any land (the D21 / D22 / D23 / D24 /
D-H7 tables; the Shock pass; `goldShare`; BUG-0780); D14 c's content when its schema ships; M7's rosters when M7's schemas exist.
