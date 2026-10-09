# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-08-1814** (the resumption of 1435; base `dd5b5b9`; third full
session of 2026-10-08, cap 8), revised after the integration. View **M4-V3** (BUG-0210 + projectile and impact views, carrying
the held M4-V2; 2 fix rounds): **ACCEPT, on `main` (853a60c, green)**. Data **D6** (balance report, Sapper self-splash numbers,
BUG-0200; 0 fix rounds): **ACCEPT**, merges after the view. Sim **M4-3a** (fog of war, high-ground vision, vision-gated
targeting; 1 fix round): **ACCEPT, then ESCALATE at integration**: with `main` merged in (a84ff0d) the full non-Perf suite has
one red row, a view-owned QA row from before fog (**BUG-0219**, S2); the branch is pushed as
`origin/studio/2026-10-08-1435-sim` and **not merged**.

## Where we are

- **M0-M3 Done. M4: 5 / 10 criteria ticked.** Criterion 5 (fog): the sim half is accepted and waits on the held branch; the
  shader, unit hiding and minimap fog are the view's M4-V4; the ghost list and shooting towers are the sim's M4-3b.
- **First thing next session (view track, before anything else): BUG-0219.** `sim/Rts.Sim.Tests/QA/ViewApi/UnitPickerQaTests.cs`
  `ThreeQueuedAttacks_ThenStop_ClearsEverything` (line ~280) queues three Attacks on Raiders 20 m from a lone Heavy Infantry
  (sight 14 m); M4-3a drops an Attack on an unseen target, so the row fails at line 292 (`{1,1}` expected, `{0,0}` actual).
  Fix: `CombatScenes.Spot(sim, 0, h[1])`, `h[2]`, `h[3]` after the spawn (reveals them to player 0; they stay outside the 14 m
  scan, so the row's "nothing pulls it back after Stop" still holds). Every assertion kept. Then the **conductor merges the
  view branch, then the held sim branch** (re-merge `main` into it first), with **the full non-Perf xUnit suite + the 33-scene
  loop + smoke green on the merged result** (the new integration rule: both QAs had run only the scene loop on their merges).
- **Hardening counters: sim 4 / 4, view 4 / 4, data 2 / 4.** After BUG-0219 the session is a **hardening session for sim and
  view** and a **feature session for data** (D7, the towers' `sight`, which needs M4-3a on `main`: plan it to start after the
  sim merge within the session, or STOP the data track cheaply if the merge slips).
- **Open S1 / S2: BUG-0219 only.** Open S3: BUG-0211, 0215 (sim, new), BUG-0220 (view, pre-existing flake), plus the older S3
  debt in STATE. Open S4: BUG-0216 (sim), 0222 / 0226 (view), 0230 (data), the older ones.
- **Bug ids next session:** sim from **BUG-0240**, view from **BUG-0250**, data from **BUG-0260** (0211-0219, 0220-0226, 0230 taken).
- **`main` now (853a60c):** the view's M4-V2 + M4-V3; non-Perf 3,927 / 14 / 0; `tools/qa/scene-loop.ps1` is on `main`; smoke
  PASS. The sim branch a84ff0d = M4-3a + `main` 853a60c: build 0 warnings, 4,003 / 15 / 1 (BUG-0219 only).
- **Process note for the owner (For your review):** the 1435 session was declared dead at 18:14 by the 3-hour lock rule while
  still in its fix loop; nothing was lost (this session resumed every track), but the lock window is shorter than a heavy
  three-track session with a fix round. The owner's routine file should refresh the lock at each step or widen it to ~5 h.
  The 1435 conductor's incident note sits uncommitted in the owner's checkout as
  `studio/sessions/2026-10-08-1435-incident.md`; its content is folded into this session's log, so the owner can delete it.

## Sim track

### Next task candidate: M4-H1, the sim's hardening session (4 / 4), QA standard, on the held branch

Start from `origin/studio/2026-10-08-1435-sim` (a84ff0d) with `origin/main` merged in. Work the debt backlog in this order,
as many as fit `max_task_lines`:

1. **BUG-0215 (S3, Producer decision, owner may revisit):** the fog's visible bits depend on where the units stood at the
   *last update*, so they are state, not derived: **hash them too**, packed like the explored bits (one `ulong` per 64
   cells per player; the brief allowed ~20 µs a tick), keep `Version` derived, and rewrite the docs/03 "a save re-stamps at
   load" sentence (M6 saves the visible bits). Flip QA's skipped `FogQaTests.TwoWorldsWithTheSameStateHash_PlayTheSameFuture`;
   `Bug0215_...` then asserts the opposite. Golden regen once with the usual proof (fog arrays excluded locally: every `k` line
   equal). Update docs/01's M4-3a row (d).
2. **BUG-0211 (S3):** re-record `studio/bugs/BUG-0146-seed21-wood-wedge.replay` from `M3PlayableTest -- --seed 21` on the
   post-M4-3a build (a headless scene run, no view file edited), set `RecordedDataHash`, restore `CheckpointPrefixTicks` to
   19 or more, prune the `SameGameDataHashes` entries the new recording covers. Add a "hash format" note to docs/03 Replays:
   any hashed-state addition invalidates pre-recorded checkpoints.
3. **BUG-0216 (S4):** item 1 `maxLimit = w*w + h*h` (flip the skipped `MaxSight64_OnA24CellMap_MatchesTheOracle`); item 2
   docs/03's holding-Catapult sentences; items 3-4 one docs line each.
4. **BUG-0230 item 2 (data request):** make `Scenario/CounterTriangleTests`' `Fight` / `TimeToKill` public helpers (or move
   them to `CombatScenes`) so `Content/CounterTriangleMarginsTests` calls the same code instead of a copy.
5. **BUG-0149 (S3)**, **BUG-0144 (S3)**, **BUG-0157 (S3)**, **BUG-0134 (S3)**, **BUG-0158 (S4)**, **BUG-0113 / 0094 (S4)**,
   **BUG-0153 items 3-4**, **BUG-0142 items 1-2** as in STATE's sim debt backlog; **BUG-0151** stays (crowd-cost work after M4).
6. Docs drift sweep: docs/03 "Known limits" (the fog's: a holding unit's reach beyond its sight needs a spotter; the view /
   combat 4-tick disagreement), `TightBlob2500` headroom note (4.49-4.54 of 4.6 ms alone; fails under load, BUG-0158).

**QA for this task runs the full non-Perf suite on a merge with the view branch** (BUG-0219 fixed there) and the 33-scene loop,
since the sim branch has never been green on a merged tree.

Then **M4-3b** (towers' `attack` / `detector` fields and buildings that shoot, the last-known-buildings ghost list for the
view, placement needs explored ground; schema → data track D8), M4-4 abilities / statuses / zones, M4-5 stealth / detection,
the four signature abilities, the fog-on sandbox.

### Watch-outs (sim)

- `TightBlob2500_OnePlayer` has ~0.06-0.1 ms of headroom alone; under another track's testhost it fails on base and head
  alike (QA measured 4.73-4.85 head vs 4.76-5.09 base). A Perf failure counts only alone, first run in a fresh process.
- `World.Neighbors` and `FlowFields.BuildScratch` are shared scratch: the fog's layers lie over `BuildScratch` in phase 12.
- Combat counts a unit's **own sight at this tick** as seen (so one-level fights play exactly as before the fog); the view's
  `CanSeeUnit` is the last update's fog, so for up to 4 ticks a unit may shoot what the player's screen still hides
  (BUG-0216 item 3; M4-V4 should know).
- Any test (sim or view) that orders an explicit Attack must stage the target in the owner's sight (`CombatScenes.Spot` /
  `Spotter`, or `AttackStage` in scenes): BUG-0218 and BUG-0219 were both this.
- The hash-twin and replay rows cover the fog; the memory bound of the 1024 world is 230.5 MB (BUG-0214 re-baseline).

## View track

### Next task candidate: BUG-0219 (first, a few lines), then M4-VH1, the view's hardening session (4 / 4), QA standard

0. **BUG-0219 (S2):** the three `CombatScenes.Spot` lines in `UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything`;
   proof: the row green on a scratch merge of the view branch with `origin/studio/2026-10-08-1435-sim`, the full non-Perf suite
   green there. Commit it first and separately so the conductor can merge view, then sim, before the rest of the session.
1. **BUG-0220 (S3, flaky `SfxTest` under CPU contention):** wait on the wall clock (`Time.GetTicksUsec`) in `Expect`, or drive
   `Sfx.Play` with an explicit clock as the criterion-4 block does; 10 runs under load green.
2. **BUG-0226 (S4, 5 items):** the 0.15 m `SeamSlack` lets a disc ~0.5 m up a ramp beside a side wall's foot (count a crossing
   as joined only at ~0 step; fix the `SeamSlack` comment: generated maps have no seams); AttackOrderViewTest's seed-6
   own-Billet row silently skipped (print or move the camera); QaV6Test's Tent row never has the box behind the Raider
   (place it so the ray reaches the box, or reword); docs/03 "10-16 cells behind the Raiders" → from the centre.
3. **BUG-0222 (S4, 3 nits):** a skipping tracker misses a same-target lob reuse (a lob must lie on its launch → target line);
   the lob's last 0.6 m slide (note for M6); the mark ring replaces a live mark while free slots exist (doc or code).
4. **BUG-0190:** items 1 and 2 are fixed (M4-V3); set the file and README row to `fixed` (residual: BUG-0226 item 1).
5. **BUG-0148 items 1 / 3**, **BUG-0126 items 3-6**, the export-hygiene notes, the minimap dot-timing flake
   (`DotRefresh_2000Units_...` under contention) as in STATE's view debt backlog.
6. Sweep the view's `QA/ViewApi` rows for any other explicit Attack on an unseen target (grep `Command.Attack` there) and
   stage them, so the sim's fog never trips another one.

Then **M4-V4**: the fog shader from `World.Fog.Visibility(player)` / `Version(player)` (an R8 texture re-uploaded on version
change; three states: black / darkened / clear), units hidden when `!Fog.CanSeeUnit(local, slot)` and enemy buildings when
`!CanSeeBuilding` (until M4-3b's ghost list), the minimap's fog layer and its Attack half, `placement.unexplored` text when
M4-3b's placement rule lands; ability feedback (M4-4); the fog-on sandbox Playable.

### Watch-outs (view)

- The fog surface is read-only (`World.Fog`, docs/03 "For the view (M4-V4)"); `Visibility` changes only on update ticks
  (`tick % 4 == 1`), so upload the texture when `Version(player)` moves, not every frame. It is on the held sim branch until
  that merges.
- `AttackStage.cs` (shared staging for the Attack-order scenes) keeps every clicked target inside player 0's sight; any new
  scene or xUnit row that orders an Attack must stage the target seen (an Attack on an unseen enemy is dropped, BUG-0218 / 0219).
- `--no-combat` scenes: DebugOverlayTest, OrdersTest, QaH1Test, QaH2Test, QaM27Test, MinimapTest, SfxTest, QaM24Test (docs/03
  lists them); never the Attack-order or combat-view scenes.

## Data track

### Next task candidate: D7, the towers' sight on both faction pages, QA light (needs M4-3a on `main`)

- Both `buildings.json` carry `"sight": 24` on `malazan_watchtower` / `whirlwind_lookout_tower` (sim-shipped, M4-3a, on the
  held branch) and every other building sees `rules.json` `buildingSight` 12 m. **D7:** a `Sight` column in both pages'
  Buildings tables (24 for the towers, 12 for the rest, pinned to `BuildingDef.Sight` both ways), docs/02 Buildings table
  checked (Watch Tower sight 24 is already there), `BUG-0090`'s tower-sight item closed (attack / detector stay for M4-3b).
  **BUG-0230 items 1 and 3** (anchor the Ages clause regex to the bullet's end; a separate siege table on the Whirlwind page).
  No `game/data/` edit unless the owner answers the margins proposal in the inbox (then the data track's next task is that
  tweak, with a golden `data-hash` regen and a For your review table).
- **Ordering:** D7's pins read `BuildingDef.Sight`, which is on the held sim branch. Either the conductor merges view + sim at
  the session's start (then D7 branches off the new `main`), or the data track branches off `origin/studio/2026-10-08-1435-sim`
  and merges last; if neither, the data track **STOPs cheaply** this session (BUG-0230 items 1 / 3 alone are too small for a
  session, but may ride along with D7).
- The owner has the **balance proposal** under For your review (winner keeps 40-65 % of its cost; every row is above it today;
  the Sapper's self-splash is small: keep it). Inbox answers come before D7.
- After D7: the towers' `attack` / `detector` (after M4-3b), `abilities.json` / `statuses.json` (after M4-4), the full balance
  pass (QA standard) once the fog-on sandbox gives numbers, `ai.json` (M5), M7-M9 factions.

### Watch-outs (data)

- `Content/CounterTriangleMarginsTests` is a copy of the scenario harness until the sim's BUG-0230 item 2 lands; if the sim
  changes the scene, the page's "on the scene of `Scenario/CounterTriangleTests`" claim needs re-measuring.
- The faction pages' existing tables must stay byte-identical (the pins read them); the Balance baseline section is appended.

## Watch-outs (all tracks)

- **Integration gate (new rule, from BUG-0219):** the full non-Perf xUnit suite, the 33-scene loop and smoke, all on the
  **merged** result, before any push to `origin/main`. Scratch-merge QA passes must run the xUnit suite too, not only scenes.
- **Merge order view → sim → data** keeps every intermediate `main` green. Expected conflicts: `studio/qa/coverage.md`
  (append all sides), docs/03 (one subsection per track), docs/01 (rows appended), `studio/bugs/README.md` (union by id).
- **Sessions:** the 3-hour lock window declared a live session dead (the second time: 2026-10-07-2014 was the first). Until
  the owner changes the routine, a conductor should check for running studio processes before resuming (the 1435 conductor's
  process was still alive at 18:5x) and a long session should refresh the lock.
- **Perf:** three tracks' suites at once fail 3-9 wall-clock rows on base and head alike; a failure counts only alone.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file; suggested text under For your review, M1 entry).
