# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session **2026-10-17-1624** (the third full session of 2026-10-17, 3 / 8). Base `main` =
**bf8026e** (the 0554 integration: sim M5-P2 + view M6-V11 + **the data re-print of BUG-0900, landed**; BUG-0900 is `fixed`). PLAN checks on
`main`: build 0 warnings (23 s); smoke **PASS** (menu + `--skirmish`, `Rts.Sim 0.0.1`, no ERROR); the full non-Perf suite **6,631 passed / 0 failed / 20 skipped
of 6,651 in 31 m 3 s** (the 8 BUG-0900 rows green; `main` wholly green for the first time since 0554's sim merge). Golden `data-hash` **C059AD620DE581F1** (unmoved). Counters at PLAN: **sim 4 / 4, view 4 / 4 →
both hardening this session**; data 1 / 4 (STOP: nothing unblocked). Bug ids next: **sim from BUG-0910, view from BUG-0920, data from
BUG-0930.** Open S1 / S2: **none**; S3 20, S4 18.

## Where we are

- **M5 3 / 9** (criteria 1, 2, 7 ticked; 3, 4, 5 advanced; the owner's note 5 done on the sim side: slices 1-2). **M6 2 / 10** (criteria
  2 and 4 ticked; 1 waits on team (Requests 38), 3's save half on Requests 42, 5's art pass on the owner's look call).
- **Waiting on the owner:** the art call (docs/04 "Look test (M6-V10)"); the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780, "densest cluster";
  the Perf fresh-boot run (BUG-0764: **not in the inbox at this PLAN, so M5-H3 re-baselines**); the fonts / music / export templates.
- **Inbox at this PLAN:** empty. Check again at ACCEPT / the next PLAN: an owner answer outranks the plans below.

## Standing rules (1-31)

Unchanged 1-30 (see the 2036 handoff in git history for the long form). **31 (BUG-0900, from 25):** a sim brief that touches movement,
chasing or combat timing names `Content/CounterTriangleMarginsTests` (both pages' "Balance baseline" tables), `Content/AiSpendingReportTests`
(the "Spending on the menu's map" paragraphs) and `Content/BuildingKillTimeReportTests` as measured data cells it can move; the dev reports
the moved cells in its report; the data track re-prints them on the merged tree the same session (a data fix-mode dev at integration when
the data track had no task). **This session's sim hardening touches movement and chasing: rule 31 applies at the integration.**

---

## Sim track

### Current session plan (2026-10-17-1624): M5-H3, the sim's end-of-cycle hardening (counter 4 / 4)

**Type** hardening (counter → 0 / 4). **QA tier** full (movement, chasing, determinism touched). **Size** up to `max_task_lines` (1,500),
batched most valuable first; stop when the budget is spent and list what's left, bug by bug.

**Goal.** Work the sim's debt backlog (STATE "Debt backlog: sim track"): the Perf gates, the five slice-2 S3s, the stand-point bug, the
executor's site search, and the S4 leftovers, with no behaviour change beyond the bug fixes and no golden move unless a fix demands it
(then once, in the same commit, with the reason).

**Scope (in), in order.**
1. **BUG-0764 (S3), the Perf gates.** The owner's fresh-boot run did not come. **Producer decision (owner may revisit): re-baseline, not a
   calibration row.** Measure every `Category=Perf` row alone on an idle tree (no other `dotnet` / Godot process; `dotnet test ... --filter
   Category=Perf` on base bf8026e, then on head); every row red alone on **both** gets its threshold re-set to the measured number plus the
   file's existing margin policy (at most +25 % over the measurement), with the old threshold, the measurement, the date and "BUG-0764"
   in the row's comment; a row red on head only is a regression, not a re-baseline. docs/03 "Testing strategy" gets a dated paragraph (why
   the thresholds moved, how to re-baseline next time, paired A/B stays the rule for feature tasks). Why not a calibration row: 124 Perf
   rows over 71 files would change; that is a session of its own and it does not fit beside the fixes below.
2. **BUG-0886 (S3)**: `OrderedChaseReachTests.AnOrderedAttackOnAHallRingedByTrees_*` takes its order (a spotter, `CombatScenes.Spotter`) and
   asserts it was taken before asserting the give-up; red at 3ec22ee by construction (say so in the report).
3. **BUG-0885 (S3)** with **BUG-0046**: the 10 v 10 flank scene's spawn-order parting (slice 1's claims in slot order at tick 1; wall clips
   at 24-28). Either make the claim order and the clip order slot-free (sorted by a stable key) or document the slot-order class in docs/03
   "Determinism" and close both as the same known limit; a decision either way, with QA's `Criterion1Scene_BothSpawnOrders_FirstParting`
   un-skipped or re-worded to the decision.
4. **BUG-0790 (S3)**: `EconomySystem.StandPoint` / `WalkToFootprint` offer a spot in reach on the face's diagonal corner cells (or the
   crowd-arrival rule stops short of a gap on a stand point); un-skip `GatherPocketStressTests (MineOneSide, 40)` and
   `Bug0790FaceProbeQaTests`' regression row; seeds 1-8 green.
5. **BUG-0887 / 0888 / 0889 (S3)**: the alternating-point spam swing (a kept side survives a re-issued order to a point within the line's
   width?), the cliff-end walk-back budget (size the budget to the whole line when the kept side was forced), the never-strike keeps on
   halls (an ordered chaser 0.02 m out of reach, or queued behind own ranged on a hall, must either strike or give up within
   `GiveUpScans`). Fix what fits; file the rest as left with a line each.
6. **BUG-0791 (S4)**: a "no site until tick" mark per executor step and a search-count test. **BUG-0850 (S4)**: an Armory row + a real
   cast in the dev stats tests. **BUG-0144 (S3)**, **BUG-0113 item 2 (S4)**, **BUG-0704 items 2-3 (S4)** if the budget allows.
7. docs/03 for every behaviour touched; `studio/bugs` statuses with the proving test named.

**Scope (out).** New rules; pathing slice 3; Requests 42 (next feature session); any data file (`game/data/**`, `docs/factions/**`); the view.
If a fix moves a counter-triangle / spending / kill-time cell, **report the cell** (rule 31); do not edit the faction pages.

**Acceptance criteria.** (1) Every bug fixed has a regression test that failed first (named), status `fixed` with the commit. (2) The full
non-Perf suite green on the branch. (3) `ReplayGoldenTests` green; a golden move only with the reason in the commit and docs/03. (4) Perf:
every re-baselined row names its old threshold, measurement and date; paired A/B on the 500 moving / 2,500 blob / both brawls within
noise; the BUG-0764 decision in docs/03 "Testing strategy". (5) Crowd rows (`FlankCrowdRowsReport`, `CrowdRows`) within their bands; the
owner's flank scene still 8-10 / 10. (6) Determinism twins for every movement change (same-order every tick; the spawn-order decision
recorded). (7) The report lists every `Content/` measured cell the fixes moved (or "none").

**Design references.** docs/03 "Implementation (pathing slice 2)" (incl. "Fix round 1"), "Implementation (pathing slice 1)", "Combat
implementation" (chasing, give-up), "Pathfinding" (local movement, BUG-0045 / 0046), "Determinism", "Testing strategy" (the Perf table and
the collection rule), docs/02 "Economy" (stand points).

**Constraints.** No Godot; no wall clock / RNG; no allocation in the tick; no dictionary iteration; `SimMath` only; no data stat in C#;
nothing new hashed unless it decides a tick.

### Next after this
Requests 42 (the save format, incl. `MatchStats` and `UnitStore.Flank`), M5-4c slice 2 (criterion 4: `gatherBonus`, `focusFire`,
`pullBackWounded`), Requests 38 (teams), D14 b schema, M5-4d (expansion / rebuilding / high-ground scouting / hunting the last units),
D14 c, M5-5 (the 20-minute AI-v-AI test), M5-6. Pathing slice 3 (the far-corner fill order, unit-v-unit crowding) when the owner asks.

### Watch-outs (sim)
- `UnitStore.Flank` is hashed once non-zero: a movement fix that changes where a unit flanks moves hashes in any flank scene, not the
  golden (no flank in it). The owner's older recordings already expired where the rule fires (For your review says so).
- Criterion 1's HI row sits on its 8 / 10 bound and moves 7-9 with crowd timing: re-run `FlankTests` after any movement change.
- Two builds share the machine this session (data is STOP): wall-clock Perf rows still flake under load; measure BUG-0764 alone.

---

## View track

### Current session plan (2026-10-17-1624): M6-VH3, the view's end-of-cycle hardening (counter 4 / 4)

**Type** hardening (counter → 0 / 4). **QA tier** standard. **Size** up to `max_task_lines` (1,500), batched; stop when spent.

**Goal.** Work the view's debt backlog (STATE "Debt backlog: view track"): the LFS hang and the loop timeout first, the stats-screen
polish, the replay folder's edge cases, and the older S4s; no new feature.

**Scope (in), in order.**
1. **BUG-0860 (S3)**: `LookTest` null-checks every `GD.Load` through `Fail(...)` (and the unguarded `_skeleton!`) and reaches phase 4 with a
   FAIL line instead of hanging; **`tools/qa/scene-loop.ps1` gets a per-scene timeout** (a parameter, default generous; a timed-out scene is
   FAIL with "timeout" in the row). A row in the loop's own output proves the timeout fires (a scene that sleeps, or the LFS-pointer
   injection QA used).
2. **BUG-0890 (S4)**: the stats graph: a sub-minute match's time axis (a 10 s step, or the match length as the last label); zero / equal
   lines visible (a thicker line, a faint outline, or lines offset by a pixel per player); the grey seat's line readable on the backdrop;
   every heading bottom-aligned. A windowed 1280 × 720 screenshot looked at; a row staging a seat that reached Age II and was then defeated.
3. **BUG-0830 (S4)**: the replay folder: unreadable files don't hold cap slots and can be deleted from Load Game; never prune the path just
   written; a `ui.json` label marks playback as a replay; the list reads headers only; one path separator in the dev log.
4. **BUG-0550 item 2**, **BUG-0281 item 2** (the fog-look pass), **BUG-0663 (S4)**, the `--colour` collision warning's wording, **BUG-0861**
   (`export_presets.cfg` excludes `assets/models/looktest/`) if the art pass hasn't.
5. docs/03 for every behaviour touched; `studio/bugs` statuses with the proving scene named.

**Scope (out).** Save / load (Requests 42 first); the art pass (the owner's call); export (the owner's templates); teams (Requests 38);
any sim change (`sim/` diff empty, ViewApi read-only additions only); `game/data/**`.

**Acceptance criteria.** (1) Every bug fixed has a regression row that failed first (named). (2) Smoke PASS; `tools/qa/scene-loop.ps1` all
scenes green with the new timeout (a flaky wall-clock row rerun alone). (3) `sim/` diff empty. (4) The screenshot taken and described.
(5) `ShellTest`'s key test covers any new `ui.json` key. (6) docs/03 updated.

**Design references.** docs/04 "Look test (M6-V10)" (`LookTest`); docs/03 "Implementation (M6-V11)" (the graph), "Implementation (M6-V9)"
(the replay store), "Debug tooling" / "Build and export" (the scene loop, `export_presets.cfg`).

**Constraints.** Views hold no gameplay state; text only from `ui.json`; no sim write; one node per graph.

### Next after this
Save / load when Requests 42 lands (criterion 3), `tools/export.ps1` when the owner's templates arrive, teams (Requests 38, criterion 1),
criterion 6's playable run, the art pass when the owner answers the look call, criterion 7's hand-made maps.

### Watch-outs (view)
- Two Godot / dotnet builds share the machine: wall-clock scene rows flake under load; rerun alone before filing.
- A clone on the owner's second machine needs `git lfs pull` before `LookTest` runs (BUG-0860 is item 1).

---

## Data track

### Current session plan (2026-10-17-1624): STOP (cheap), nothing unblocked

- BUG-0900's re-print is on `main` (bf8026e); the inbox is empty (no D19 table answer, Shock pass, `goldShare`, BUG-0780 or "densest
  cluster" reply); the D14 b openings need the strategies schema (not on `main`); the 12th-house `ai.json` step is parked (moves the
  golden: batch with the next hash-moving data change); the M7-M9 rosters need schemas the roadmap hasn't reached (scale / pop,
  Giant armor, regeneration, multi-spawn).
- **At the integration (rule 31):** if M5-H3's movement / chasing fixes move a `Content/` measured cell, the conductor dispatches a data
  fix-mode dev on the merged tree (as bf8026e did): re-print from the harness, fixtures re-pointed, no `game/data` change, push only when
  the full non-Perf suite is green.
- **Next data task candidates** (first that becomes unblocked): an owner answer from the inbox; the D14 b openings once the sim ships the
  schema; a margins pass toward the 40-65 % band if the owner asks (a sweep report first, then one header move); the 12th-house step
  batched with the next hash-moving data change.

---

## Watch-outs (all tracks)

- **BUG-0764:** the owner's fresh-boot run never came; M5-H3 re-baselines with dated reasons (Producer decision above, owner may revisit).
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/03: each track appends its own rows; the conductor unions them.
- Builds share the machine: wall-clock Perf and scene rows flake under load; rerun alone before filing.
