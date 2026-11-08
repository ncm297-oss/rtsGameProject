# Handoff: brief for the next session

**PLAN 2026-11-08-0726 (session 1 / 8 of 2026-11-08, scheduled, base `e9758d5` = `origin/main`): sim GO on M5-5 (below), view STOP
(cheap), data STOP (cheap).** Checks: inbox New empty; a scratch clone of `e9758d5` builds 0 / 0 and the targeted AI / golden / data-hash /
save-refusal / map-file set is 455 / 455 in 41 s (the full non-Perf run on this exact code tree was 8,537 / 0 / 19 at the 2306 ACCEPT);
open S1 / S2 none; docs/02 l.427 carries the "defeated player's buildings forgotten" rule; `PlayerView.IsDefeated`,
`SaveRefusalTests.AForgedLeg_OnAPointBelowTheRing_IsMalformed` and the golden literal's removal from `GoldenDecouplingQaTests` verified.

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

### Current session plan (2026-11-08-0726): M5-5, the 20-minute AI-vs-AI headless test (criterion 5) · feature (counter 0 / 4 → 1 / 4) · QA full

**Goal.** M5 criterion 5 reads "AI-vs-AI headless test: a 20-minute match completes without errors and one side wins." The sim has
every piece (the owner's win rule, the hunt, rebuilding, the forgotten ghosts); what is missing is the committed, repeatable proof:
a match that runs headless for its whole length with every error we can name checked on every tick and think, ends with exactly one
winner, and does so in two processes with the same hashes; plus a CLI verb so the owner can run the same match from a prompt. This is
a test-and-tooling task: **no AI rule change is expected**, and none may ship unlisted.

**Producer's reading of the criterion (record it in docs/05 at the tick):** the literal row is a 2-seat Normal v Normal match that
ends with a winner **within 24,000 ticks (20:00)** and error-free: Vathar Crossing seed 1 (ends at 22,794 = 19:00 today) is that row.
The other maps are covered by the same row shape with a **documented bound per map** (Pale Hills seed 1 ends at 34,219 = 28:31: bound
36,000; Raraku seed 1: measure, bound the next 6,000 above with at least 10 % slack, 72,000 at most; generated: pick the lowest seed
in 1-6 that ends under 24,000, else document the shortest and bound it). A row's bound is a constant in the test with the measured end
tick in its comment.

**"Without errors" means all of these, asserted inside the row on every tick / think (name each in docs/03):**
(a) no exception; (b) `PlayerViewOracle` clean on every think of every seat (the existing `Ai/PlayerViewOracle` tally through
`AiPlayer.ThoughtForTests`); (c) the census invariant QA used in M5-H5 (known enemy buildings = the world's live enemy buildings the
seat has seen, minus defeated seats' ghosts); (d) every unit and building position on the map, no NaN, every alive handle's
generation valid; (e) **refused AI commands counted by kind and bounded**: the sim does not expose per-command results, so add a
test-only seam (`Simulation.RefusedForTests` or the equivalent: `internal Action<Command, CommandRefusal>?`, invoked only where a command
is dropped, zero cost and zero allocation when null, never read by the sim, never hashed) **or** judge through `Blackboard`'s retry
marks and `SentForTests`; the row prints refusals per kind and asserts that only the documented kinds occur (a Build on a cell that
became blocked, a Train over the cap or without the cost, an order on a unit that died the same tick) and that no kind repeats on the
same target more than `N` times in a row (the dev sets `N` from a measured run and writes why). A refusal kind outside that list is a
finding, not a thing to add to the list silently: file it (patch) and ask in the report.

**Scope.**
1. `Ai/MatchCriterionTests` (or a better name): one `[Theory]` row per map: `raraku`, `pale_hills`, `vathar_crossing`, `generated`,
   2 Normal seats (Malazan v Whirlwind, starts 0 and 1), seed as chosen, the checks above, the result printed per 6,000 ticks as
   `MapFileTests.PlayToTheEnd` does, `Assert` on `MatchOver`, exactly one of two defeated, inside the bound. `Category` `Stress` if a
   row takes over ~2 min; the Vathar row should be plain if it fits (the criterion's literal row must run in the default loop).
2. **Two-process determinism** for every row (the `MapFileTests.RunChild` pattern, as `EndGameHardeningTests.Raraku_Seed1_*`): the
   per-tick hash fold, the end tick, the winner and the hunts started equal in a child.
3. One **4-seat FFA** and one **2v2** row on a map and seed that end (QA's M5-H5 table: Vathar FFA seeds 2-5, Pale Hills FFA 3 / 4;
   2v2 Pale Hills 2 / 4 / 5 / 6, Vathar 1 / 2 / 4 / 5), same checks, bound 72,000, `Stress`.
4. A **save at the half-way tick of the Vathar row**, loaded in a fresh sim and followed to the end hash-equal (the `SaveTestRun`
   helpers): the criterion's match survives a save.
5. **CLI `match` verb** in `tools/Rts.Cli` (`CliRunner`): `Rts.Cli match --map raraku|pale_hills|vathar_crossing|generated --seed <n>
   [--players 2..4] [--teams a,b,...] [--ai easy|normal|hard[,...]] [--ticks <n>] [--record <path>] [--data <dir>]`; prints the
   6,000-tick lines, the result (winner / draw / no result), the end tick, the state hash and the per-tick fold, and the refusal
   counts; exit 0 on a result, a distinct code on "no result by --ticks", usage errors as the other verbs. In-process tests in
   `Cli/CliTests` (usage rows, a short `--ticks 600` run on each map, the output's hash equal to the test row's at the same tick).
   Update `Usage` and docs/03's CLI section; the view's code is not touched.
6. docs/03 "Implementation (M5-5)": the reading of the criterion, the per-map table (seed, end tick, winner, waves, hunts, refusals by
   kind), the bounds, which seeds do not end and why (BUG-1068 gold, BUG-1116 the lone unit), the seam. docs/05 criterion 5's line is
   the Producer's at ACCEPT; leave the test names in the report.
7. **Riders only if a few lines and the pins listed below do not move:** BUG-1149 (the defense's AttackMove onto an ally's building
   edge: route the defense's point through `MilitaryManager.SightingPoint`; teams only, so no 1v1 pin can move; the two skipped
   `M5H5HuntAfterDefeatQaTests.A2v2_TheDefenseToo_*` rows are the gate: un-skip them if green), BUG-1150's push rule (nearest clear
   exit). Skip both if either needs more than ~40 lines or moves a 2v2 digest.
8. **OUT:** BUG-1068's gold (a rule change: its own task), the sweep order (BUG-1116: its own task gated by QA's hide / leg rows),
   Hard tuning, D14 c, the house rule, any change to Easy / Normal / Hard behaviour, the view, `game/data/**`.

**Acceptance criteria.**
1. Build 0 warnings; non-Perf green (every skip with a bug id); the `AiPerfTests` rows green alone (rule 42); smoke PASS.
2. The four per-map rows exist and are green: each ends with `MatchOver`, exactly one side defeated, inside its documented bound,
   with checks (a)-(e) asserted on every tick / think; the Vathar Crossing row ends within 24,000 ticks (the literal criterion) and
   runs in the default (non-Stress) loop.
3. Every row (the four maps, the FFA, the 2v2) is hash-equal in two processes (end tick, winner, per-tick fold, hunts); the Vathar
   row's half-way save loads and follows to the same end hash.
4. `Rts.Cli match` runs each map headless from a prompt and prints a result whose state hash at tick 600 equals the test row's; usage
   errors and the no-result exit code tested in-process; `Usage` and docs/03 updated.
5. **No pin moved** unless listed with why: `NormalVersusNormal_OnANewMap_EndsWithin36000Ticks` (34,219 / 22,794), `KnownStalls`
   (29,562 / 55,631 / 26,034), `StandardPresetPinQaTests`, `NonHardMatchPinQaTests` (Pale Hills seed 1 Normal `69C4358E67DF7FC3 34219`),
   `HardKnobsPinTests`, the faction-page tables, `D21OpeningsQaTests` / `DH6ThresholdQaTests`'s `KnownBeatenAlive`, the golden header
   `30C51BB619A7E650`; save formats 6 / 7 and replay 9 unchanged; the data hash unmoved (no `game/data` change).
6. docs/03 "Implementation (M5-5)" with the table and the reading; every refusal kind the rows see is named there with its cause.

**Design references.** docs/05 M5 criterion 5 (l.701-706); docs/02 "AI opponent" (the win rule l.~520-530, the hunt and the forgotten
ghosts l.427-435); docs/03 "Implementation (M5-4b)", "(M5-4d)", "(M5-4d slice 2)" and "(M6-S8)" for the matches' measured numbers;
CLAUDE.md rules 2-5.

**Tests required.** The rows of scope 1-4; `Cli/CliTests` rows for the verb (usage, a 600-tick run per map, the hash check, the
no-result exit); a unit test that the seam costs nothing when null (an allocation-free tick with the hook unset; the existing
`AllocationFree` pattern) and never changes the hash (hook set v unset, same fold).

**Constraints.** No Godot, no wall clock, no `System.Random`; the seam allocates nothing per tick when null and is never read by the
sim; no new knob in `rules.json` / `ai.json`; no player-facing text; no `game/data/**` edit; **bug files: leave a patch in the
scratchpad** (the hook blocks `studio/**`); Perf rows alone before calling one red (rule 42); do not kill another process's Godot or
`testhost`. Size: the design is clear, up to 1,500 hand-written lines (generated content and QA's suites don't count, rule 39).

**QA focus (full).** (1) **What the rows cannot see:** list the error classes the checks (a)-(e) miss (a unit stuck for ever on a
reachable target, an AI seat that sends no command for N minutes while alive, gold / wood that never moves, a wave that never leaves)
and run each as an extra oracle on the four rows; anything that fires is a finding with the tick. (2) The refusal seam: fuzz a
scripted seat that sends deliberately bad commands (dead handle, wrong owner, off-map point, over-cap Train) and assert every one is
reported once with the right kind and the hash equals a run without the hook. (3) **Seed robustness:** each per-map row's shape on
seeds 1-6 (24 matches, the bound 72,000): print the table; a chosen seed that is the only one ending is a note in docs/03, not a
failure. (4) Two-process determinism under load (the three test hosts at once) for the four rows, then alone if any differs (rule 42
applies to time only; a hash difference is S1). (5) The CLI: every usage row, `--teams` with a one-team setup refused, `--record` plays
back through `Rts.Cli play` to every checkpoint, the no-result exit code at `--ticks 60`. (6) The rider(s) if taken: the 2v2 digests of
`M5H5HuntAfterDefeatQaTests` before and after; `NonHardMatchPinQaTests` 8 / 8 unchanged. (7) Re-run the pins in criterion 5 and the
golden; the data hash. Scale numbers: 24 seed-sweep matches + the 6 rows twice (two processes) + the 2v2 / FFA skipped rows.

**Then (sim):** BUG-1010's lever (defend-then-counter), D14 c's schema (criterion 9; the data track waits on it), the Hard tuning
sample if the owner asks; the next hardening at 4 / 4 (BUG-1149, BUG-1150, BUG-1058, 1044's rest, 1067, BUG-1116's sweep order
with QA's rows as the gate, BUG-1068's gold candidate (a), the movement S3s, `HasMapId`'s note, `HUNT`'s vector range checks).

---

## View track

### Current session plan (2026-11-08-0726): STOP (cheap)

Inbox New is empty, so no owner item; no unblocked roadmap work: M6's remaining criteria wait on the owner (the art call, music /
fonts / export templates, ".NET 10 installed", a friend's playtest), M7+'s view work waits on the art call. Hardening counter 1 / 4,
not due. Debt for the view's next hardening: BUG-1126 (ruling in the file), BUG-1147 item 1, BUG-1115 item 4, BUG-0860 / 0830 / 0890 /
0861: not a session's worth alone, and S3 / S4 don't jump the queue.

---

## Data track

### Current session plan (2026-11-08-0726): STOP (cheap)

Inbox New is empty (no review answers); no schema the data track needs is on `main` (D14 c's is the sim's, after M5-5); the sim's
hardening moved no Normal pin and re-printed no faction-page cell, and M5-5 is a test-and-tooling task that must move no pin either,
so no prose or `KnownBeatenAlive` re-measure is owed (rules 40 / 41). Next real work: the owner's answers if any land (the D21 / D22 /
D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780); D14 c's content when its schema ships; M7's rosters when M7's
schemas exist.
