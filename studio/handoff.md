# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-07-0926** (session 2 / 8 on 2026-11-07; all three tracks accepted: sim
M5-4c slice 2 after two fix rounds, view M5-V2 and data D-R34 with none). `main` after the conductor's merges (sim → view → data;
`studio/qa/coverage.md` both sides kept) carries **M5 criterion 4 ticked: M5 6 / 9, M6 5 / 10.** Golden **30C51BB619A7E650** and the
data hash unmoved; save formats **6 / 7**, replay 9. **Open S1 / S2: none** (S3 22, S4 17). Counters after: **sim 4 / 4 → the
sim's hardening is next, view 1 / 4, data 2 / 4.** Bug ids next: **sim from 1125, view from 1148, data from 1150.**

## Standing rules (1-42)

Unchanged 1-41 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content and
QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit `docs/factions/**`
for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief names the allowance and
the data task that touches it re-measures on the sim's branch, or the two are not planned in the same session). **New, 42 (Perf runs
on this PC, BUG-1124):** the CPU clock roughly halves after a few seconds of sustained load (typeperf `% Processor Performance`
81-120 % under load against 145-205 % idle; 6f6414b shows it too). A Perf failure counts only when the row fails **alone in its own
process after at least 30 s idle**; the `AiPerfTests` class is run row by row by the Producer and QA; BUG-0764's 0.6× gates stay.

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rule 42 before filing. A full non-Perf run beside QA's
  match harnesses took 1 h 34 m - 1 h 43 m this session (53 m alone).
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
- A developer must never kill another track's Godot or `testhost` process. The scene loop's own timeout handles a hung scene.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase. A sim fix round costs ~45-60 minutes.
- **Pinned matches (must move only with a listed reason):** `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks`
  (Pale Hills seed 1 at 34,219, Vathar at 22,794), `MapSlice2StressTests.KnownStalls` (29,562 / 55,631 / 26,034),
  `StandardPresetPinQaTests` (7 digests; Hard-seat rows re-pinned this session: `ai_seed2_2seats_Normal` `0x08DAC14990BB29C5`,
  `ai_seed5_2seats_Hard` `0x12C3558D5A1F3C6C`, `ai_seed3_3seats_Hard` `0xFCD8F85C768FCED4`, `ai_seed4_4seats_Normal`
  `0xFB6A7E60CAB35D32`, the cramped Easy row `0x3257C07089F858CA`), QA's `NonHardMatchPinQaTests` (8 Easy / Normal whole matches,
  every tick and command), `HardKnobsPinTests`, the Malazan / Whirlwind page tables, `D21OpeningsQaTests` / `DH6ThresholdQaTests`
  and their `KnownBeatenAlive` lists ({(1, 1, 19, 0)} and {}), the golden header. **The sim's hardening will move Normal pins on
  purpose (BUG-1119, BUG-1120's hunt point, BUG-1116's house rule): every moved pin is re-printed in the same commit and listed with
  why, and the data's allowances are re-measured on the branch (rule 41).**
- **Known stalls on Pale Hills seed 2** (both skipped with ids): `MapSlice2StressTests.NormalVersusNormal_PaleHillsSeed2_Stalemate_...`
  (BUG-1068) and `HuntStressTests.FourAiSeats_OnPaleHills_Seed2_KnownStall` (BUG-1116 / 1068 / 1119): re-run both at the end of the
  hardening.
- The soak oracle's `HuntToASighting` exemption (`Stress/TeamAiSoakStressTests`, BUG-1120) comes out when the hunt point is fixed.

---

## Sim track

### Current session plan: M5-H5, the sim's hardening · hardening (counter 4 / 4 → 0 / 4) · QA full

**Goal.** The sim's debt since M5-H4 (2026-10-26), most valuable first, as many as fit `max_task_lines`; the AI's open S3s first because
M5-5's 20-minute winner test (the next feature) needs AI-only matches that end. Every Normal pin that moves is re-printed with why.

**In this order (stop where the budget ends; say what was not reached):**
1. **BUG-1119 (S3):** a defeated seat's remembered buildings still count as known enemy buildings, so a survivor too poor for a wave
   never hunts (p3 kept 18 of the defeated p2's on Pale Hills seed 2). Forget them at the census (the defeat is public: a read-only
   `PlayerView.IsDefeated(player)` if needed). Re-measure Pale Hills seed 2 (both skipped rows) after it.
2. **The lone-idle-worker hunt gap** (under BUG-1116): `KnownStalls` seed 5's loser's last camp follower stands idle at (391.8, 438.3)
   from 48,000 and a 7-unit hunt never sees it (the sweep points' sight edge). Direction: sweep points spaced so adjacent points' sight
   circles overlap for the smallest army sight, or a final per-cell pass over unseen cells.
3. **BUG-1120 (S3), the hunt point:** a hunt's sighting point that lies on or within 0.5 m of an allied building footprint is pushed
   off it by half a cell toward the hunter (Producer default); then remove the soak oracle's `HuntToASighting` exemption and
   `HardKnobsRecheckQaTests.TheSoakExemption_*` (QA-owned; say so in the report, rule 37's precedent).
4. **BUG-1116 (S3):** the house rule (reopen a House only when `HalfPopCap < hard cap && HalfPopCap − HalfPop <= ai.houseHalfPopMargin`,
   knob validated and hashed) re-measured with 1-3 in: ship it only if Pale Hills seed 1 (36,000), `KnownStalls` and QA's two-process
   row stay green or move for a listed reason.
5. **BUG-1118 item 1 (S4):** a hall site razed in the think it appeared gets a retry mark and the next rebuild goes a cell off the
   anchor (judge the hall Build against "a Keep stood at the anchor since it was sent").
6. **BUG-1068's gold (S3):** Pale Hills seed 2's seats are all under 100 gold by 36,000 (an expansion step the losing openings reach,
   or the map's far mines); measure, fix if a few lines, else write the finding into the bug.
7. **BUG-1058 (S3)** the released scout gathers at the node nearest itself (release it to the judged forest); **BUG-1059 (S4)** drop
   the golden header literal from `GoldenDecouplingQaTests`; **BUG-1044's rest** (the five enemy sites without a revert-failing test);
   **BUG-1067 (S3)** the hall sees its start woodlot or gatherers search `ai.gatherReach` once the reach lifts with no forest known.
8. **The movement S3s** BUG-0911 / 0889 / 0144 (each needs a failing row first; skip any that doesn't yield one in 30 minutes).
9. Notes to close while there: the `HuntRing` load range (`ReadHuntFrom` accepts [-1, points − 1]: tighten to `[RingStart, points − 1]`),
   `HasMapId`'s special case for 6 (a comment or a monotonic rule), `HUNT`'s vectors and tick range-checked as `AIST`'s.
- **OUT:** anything that changes Hard's knobs (M5-4c slice 2 is accepted as is; tuning is later work), `game/data/factions/**` numbers
  (cells re-printed under rule 40 only), the view, M5-5's test itself (the next feature session), D14 c's schema.

**Acceptance criteria.** 1. Build 0 warnings; non-Perf green with every known-stall row either un-skipped and green or still skipped
with its id and a sentence why; the AI Perf rows green alone (rule 42); smoke PASS. 2. Each bug taken has a regression test that
failed first, its file says `fixed` with the commit and test. 3. Every moved pin re-printed in the same commit and listed with why;
the golden header unchanged unless a listed reason; `NonHardMatchPinQaTests` either unchanged or re-pinned with the reason (a Normal
change is expected here). 4. The two Pale Hills seed-2 rows re-run and their state written down. 5. Determinism: two processes on
Pale Hills seed 2 (4 seats) and Raraku seed 1 hash-equal every tick; a save mid-hunt reloads hash-equal 1,000 ticks later; formats
unchanged. 6. docs/03's sections for M5-4d / M5-4d slice 2 / M6-SH1 say what the code does now; docs/02 true.

**QA focus.** The hunt after BUG-1119 on every 4-seat map × seeds 1-6 to 72,000 ticks (how many end, and how); the house rule's
effect on every pinned match; the hunt point near allied footprints under the strict soak oracle; the fog oracle on every think
(`PlayerViewOracle`); the save round-trip of a hunt in progress; the AI Perf rows alone.

### Then (sim)
- **M5-5** (feature): the 20-minute AI-vs-AI headless test on the three maps (criterion 5), then **BUG-1010's lever**
  (defend-then-counter), **D14 c's schema** (criterion 9; the data track waits on it), the Hard tuning sample (a larger Hard v Normal
  report; `ai.focusReach`, the pull-back threshold) if the owner asks.

---

## View track

### Current session plan: STOP (cheap) unless an owner item is in the inbox

No unblocked roadmap work: M6's remaining criteria wait on the owner (the art call, music / fonts / export templates, ".NET 10
installed", a friend's playtest). If an inbox item lands, that is a feature session, QA standard. Debt for the view's next
hardening (counter 1 / 4): **BUG-1126** (the Producer's ruling in the bug file: spaces at a break consumed, a line over the width
only when it is one word; `QaVh5Test`'s fuzz amended), **BUG-1147** item 1 (a `{1}` from `thinkInterval` in the tips, with a load
rule, if the owner wants the numbers live), BUG-1115 item 4 (the art pass's theme). Not a session's worth on their own.

---

## Data track

### Current session plan: STOP (cheap)

No schema the data track needs is on `main` yet (D14 c's is the sim's, after M5-H5 and M5-5), no owner review answers are waiting,
and the sim's M5-4c slice 2 moved no faction-page cell (the difficulty table is still true: Hard 25 %, focus fire yes, pulls back
wounded yes). **After the sim's hardening lands**, a data task is likely: the Normal pins it moves re-print page cells (rule 40 lets the
sim do the cells; the prose round them is the data's, as D-R34 was), and both `KnownBeatenAlive` allowances get re-measured (rule 41:
on the sim's branch, or the session after). Then the owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the
Shock pass; `goldShare`; BUG-0780); D14 c's content when its schema ships; M7's rosters when M7's schemas exist.
