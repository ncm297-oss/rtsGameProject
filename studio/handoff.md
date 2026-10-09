# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-08-2144** (scheduled; base `78b9014`; fourth full session of
2026-10-08, cap 8). Decision **GO** on all three tracks: sim **M4-H1** (hardening, 4 / 4), view **BUG-0219 then M4-VH1**
(hardening, 4 / 4), data **D7** (feature, QA light).

## Branch starts and integration order (read first, conductor and builders)

- **Every track branches from `origin/studio/2026-10-08-1435-sim` (249924a)** with **`origin/main` (78b9014) merged in first**
  (verified clean with `git merge-tree`: `main` adds only the D6 data merge and studio files). That branch is the accepted
  M4-3a fog work with `main` 853a60c already merged; it is the tree `main` becomes this session, so every track builds and
  tests on it (the view's hardening on the fog-era sim, the data track's D7 on `BuildingDef.Sight`).
- **Integration order: view, then sim, then data.** The view branch's first commit is the BUG-0219 fix, so once it merges,
  `main` carries M4-3a + the fix and is green on the full suite. The sim branch then adds only M4-H1; the data branch only D7.
- **Gate on every merged result:** the full non-Perf xUnit suite, the 33-scene loop (`tools/qa/scene-loop.ps1`) and smoke.
- Known red on the common base: exactly one row, `QA.ViewApi.UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything`
  (BUG-0219, view-owned). The sim's and data's own full runs show it until they scratch-merge the view's fix commit.
- Bug ids: sim from **BUG-0240**, view from **BUG-0250**, data from **BUG-0260**.

## Where we are

- M0-M3 Done. **M4: 5 / 10** on `main`; criterion 5's sim half (M4-3a) lands this session through the view branch.
- Hardening counters before this session: sim 4 / 4, view 4 / 4, data 2 / 4. After it: sim 0, view 0, data 3.
- Open S2: BUG-0219 only (fixed first by the view). `main` 78b9014: build 0 warnings; non-Perf suite run at this PLAN.
- No inbox notes. The owner's balance answer (D6 entry) is still wanted; D7 ships no number.

## Sim track

### Current session plan: M4-H1, the sim's hardening session · hardening · QA standard

**Goal:** work the sim's debt backlog on the fog-era tree: make the fog's visible bits part of the hashed state (BUG-0215),
re-record the seed-21 replay (BUG-0211), close the M4-3a nits (BUG-0216), hand the data track a shared fight harness
(BUG-0230 item 2), then the older S3 / S4 debt as it fits `max_task_lines` 1,500.

**Scope, in this order (stop when the budget is spent; say which items were not reached):**
1. **BUG-0215 (S3, Producer decision, owner may revisit):** hash the visible bits packed (one `ulong` per 64 cells per
   player, like the explored bits; ≤ ~20 µs a tick on 2,500 units), keep `Version` derived; rewrite docs/03's "a save
   re-stamps at load" (M6 saves the visible bits); flip QA's skipped `FogQaTests.TwoWorldsWithTheSameStateHash_PlayTheSameFuture`
   (the `Bug0215_...` row then asserts the opposite); `StateHashTests` row for the visible bits; docs/01's M4-3a row (d).
   Golden regenerated **once** with the usual proof (every `k` line equal with the fog arrays excluded locally; say so in the
   commit).
2. **BUG-0211 (S3):** re-record `studio/bugs/BUG-0146-seed21-wood-wedge.replay` from `M3PlayableTest -- --seed 21` on the
   post-item-1 build (a headless scene run; no view file edited), set `RecordedDataHash`, restore `GatherWedgeQaTests`
   `CheckpointPrefixTicks` to ≥ 19, prune the `SameGameDataHashes` entries the recording covers; docs/03 Replays: a
   "hash format" note (any hashed-state addition invalidates pre-recorded checkpoints).
3. **BUG-0216 (S4, 4 items):** `FogStore` limit `w*w + h*h` (flip the skipped `MaxSight64_OnA24CellMap_MatchesTheOracle`);
   docs/03's holding-Catapult sentences (a holder's reach beyond its sight needs a spotter); the view / combat 4-tick
   disagreement in docs/03 "Known limits"; `MaxSight` applies to units too (one docs line or the validation).
4. **BUG-0230 item 2 (data request):** make `Scenario/CounterTriangleTests`' `Fight` / `TimeToKill` public helpers (or move
   them into `CombatScenes`) with the scene parameters named, so `Content/CounterTriangleMarginsTests` can call them next
   session (**do not edit `Content/`**: data-owned). Note the new helper's name in the session report.
5. Then as they fit: **BUG-0149** (S3, `ChaseStall` across a target switch; flip
   `CombatFriendExceptionQaTests.StalledChaser_SwitchesToAnEnemyBehindAWall_WalksRoundAndFightsIt`, keep seed 4 of
   `CrowdRowSweepStressTests.FiveHundredUnitsTo500RandomGoals` green), **BUG-0157** (S3, an AttackMove to a new point keeps a
   chase whose target is in sight; flip the skipped jittered-spam row), **BUG-0144** (S3), **BUG-0134** (S3, `CheckAnyOfReachable`
   honours `researchedAt` / `trainedAt`; flip `ABuildingRequiringATechResearchedOnlyAtItself_IsAnError`), **BUG-0158** (S4,
   warm-up run or medians), **BUG-0113 / 0094** (S4), **BUG-0153 items 3-4**, **BUG-0142 items 1-2**, **BUG-0133** (S4).
   **BUG-0151** stays (crowd-cost work after M4).
6. Docs drift sweep: docs/03 "Known limits" gains the fog's (above); the `TightBlob2500` headroom note (4.49-4.54 of 4.6 ms
   alone; fails under load, BUG-0158); every bug file touched gets its status and "Fixed by" (commit + test).

**Out of scope:** M4-3b (towers' `attack` / `detector`, ghosts, placement on explored ground), any `ViewApi/`, `Content/`,
`QA/Content/`, `game/` file other than `game/data/common/**` if a rule constant needs a home; no behaviour change the docs
don't already describe except the items above.

**Acceptance criteria:**
1. The visible bits are hashed: two worlds with equal state hashes play the same future (the flipped QA row green; the
   `StateHashTests` row proves a flipped visible bit changes the hash); `Fog2500OnePlayer` still < 0.25 ms and 0 B alone;
   golden regenerated once with the exclusion proof stated in the commit.
2. The seed-21 replay is re-recorded on the new hash: `GatherWedgeQaTests` checks ≥ 19 checkpoint ticks (prefix restored),
   `RecordedDataHash` matches, the row and `M3PlayableTest --seed 21` (headless) pass.
3. BUG-0216 items 1-4 closed: the skipped 64-sight row flipped and green; the docs/03 sentences corrected.
4. A public fight harness exists in `sim/Rts.Sim.Tests` (name reported) and `Scenario/CounterTriangleTests` uses it with
   unchanged printed numbers (the pages' claim "on the scene of `Scenario/CounterTriangleTests`" stays true).
5. Every item from 5 that was taken has its skipped row flipped (or its test added) and green; untouched items are listed.
6. Build 0 warnings; non-Perf suite green except the known BUG-0219 row on the sim branch alone (green on a scratch merge
   with the view's BUG-0219 commit, which QA runs); Perf rows alone within budget; smoke PASS; scene loop 33 / 33 on the
   scratch merge with the view branch; docs updated with every behaviour change; bug files set.

**Design references:** docs/03 "Vision, detection, fog" (hashing, the view surface, the 4-tick update), "Replays"
(format 4, checkpoints), "Known limits"; docs/02 "Vision and fog" (the high-ground rule); the bug files named above.

**Tests required:** the flipped QA rows named above; a `StateHashTests` row for the visible bits; the fog fuzz
(`Stress/FogFuzzStressTests`, QA's `FogHostileFuzzQaTests`) and replay round trip still green; `GatherWedgeQaTests` on the new
recording; a regression test for each S3 fixed (the skipped rows are those).

**Constraints:** no per-tick allocation (the packed hash loops over arrays); no `Dictionary` iteration; determinism (golden
regen once, reason in the commit); no Godot in `Rts.Sim`; the sim track never edits `ViewApi/`, `Content/`, `QA/Content/`,
`docs/factions/`; studio files append-only.

**QA focus (standard):** attack the hash change hardest: hash twins after random fog updates, a save-like copy of the hashed
state at a non-update tick resumes to the same future; the replay round trip with the fog; the golden's `k` lines with the fog
excluded; the packed visible bits vs the byte map on every cell (3-level maps, 6 seeds); perf of `AddToHash` on a 1024 map
(bound the cost); the re-recorded replay checks ≥ 19 ticks; for each flipped S3 row, a hostile variant (BUG-0149: two
unreachable then one reachable; BUG-0157: jitter every 1, 2, 3 ticks). Full non-Perf suite on a scratch merge with the
view's branch (its BUG-0219 commit) and the 33-scene loop there.

### Watch-outs (sim)

- Item 2 depends on item 1 (the hash changes); record the replay after item 1 is final.
- `TightBlob2500_OnePlayer` has ~0.06-0.1 ms of headroom alone; a Perf failure counts only alone, fresh process.
- `World.Neighbors` / `FlowFields.BuildScratch` are shared scratch; the fog's layers lie over `BuildScratch` in phase 12.
- Any test that orders an explicit Attack must stage the target in the owner's sight (`CombatScenes.Spot` / `Spotter`).

Then **M4-3b** (towers' `attack` / `detector` fields and buildings that shoot, the last-known-buildings ghost list, placement
needs explored ground; schema → data D8), M4-4 abilities / statuses / zones, M4-5 stealth / detection, the four signature
abilities, the fog-on sandbox.

## View track

### Current session plan: BUG-0219 first, then M4-VH1, the view's hardening session · hardening · QA standard

**Goal:** land the three-line BUG-0219 fix as the first, separate commit (it unblocks the fog on `main`), then work the
view's debt backlog on the fog-era sim: the `SfxTest` flake, the M4-V3 nits, the older S4 leftovers, and a sweep so no other
view test orders an Attack on an unseen target.

**Scope, in this order:**
0. **BUG-0219 (S2) — first commit, nothing else in it:** in `sim/Rts.Sim.Tests/QA/ViewApi/UnitPickerQaTests.cs`
   `ThreeQueuedAttacks_ThenStop_ClearsEverything` (line ~280), after the spawns add `CombatScenes.Spot(sim, 0, h[1])`, `h[2]`,
   `h[3]` (reveals the Raiders to player 0; they stay 20 m out, beyond the 14 m scan, so "nothing pulls it back after Stop"
   still holds). Every assertion kept. Prove: the row green, then the **full non-Perf suite green on the branch** (it already
   contains the fog). Commit as `M4-VH1: fix BUG-0219 ...` before any other change.
1. **BUG-0220 (S3):** `SfxTest` flakes under CPU load (`Expect` waits a 60 ms scene-tree timer against `Sfx.Play`'s 50 ms
   wall-clock rate limit): wait on `Time.GetTicksUsec` in the test, or drive `Sfx.Play` with an explicit clock as the
   criterion-4 block does; 10 runs under load (another testhost running) green.
2. **BUG-0226 (S4, 5 items):** `TerrainHeight.MaxUnder`'s 0.15 m `SeamSlack` (count a crossing as joined only at ~0 step;
   fix the comment: generated maps have no seams; flip the skipped
   `MaxUnderEdgeWalkQaTests.MaxUnder_RandomPointsAndRadii_HangsUnderAQuarterMetre`); `AttackOrderViewTest`'s seed-6 own-Billet
   row silently skipped (print or move the camera); `QaV6Test`'s Tent row never has the box behind the Raider (place it so the
   ray reaches the box, or reword the row); docs/03 "10-16 cells behind the Raiders" → from the centre.
3. **BUG-0222 (S4, 3 nits):** a skipping `ProjectileTracker` misses a same-target lob reuse (a lob must lie on its launch →
   target line; flip the skipped `SkippingObserver_SameTargetLobFromAnotherLaunch_IsANewShot`); the lob's last 0.6 m slide (a
   docs/03 note for M6); `ImpactMarks.Add` replaces a live mark while free slots exist (doc or code).
4. **Sweep:** grep `Command.Attack` in `sim/Rts.Sim.Tests/QA/ViewApi/`, `sim/Rts.Sim.Tests/ViewApi/` and `game/tests/`; every
   explicit Attack on an enemy stages the target seen (`CombatScenes.Spot` / `Spotter` / `AttackStage`); list what was found.
5. As they fit: **BUG-0148 items 1 / 3**, **BUG-0126 items 3-6** (`PropLayout` relists on `NavGrid.Version`; a double Cancel
   enqueues two Cancels; the split minimap Perf rows leave the sum unguarded; the resource bar's ~1.2 KB over 300 repair
   ticks), the minimap dot-timing flake (`DotRefresh_2000Units_...` under contention), export hygiene notes (docs only).
6. Bug files: BUG-0190 is already set `fixed` by the Producer (this PLAN); set each bug fixed here with its commit + test.

**Out of scope:** M4-V4 (the fog shader, unit hiding, the minimap's fog layer and Attack half): next session; any sim file
outside `ViewApi/` (read-only additions only there); `game/data/**`.

**Acceptance criteria:**
1. BUG-0219: the row green on the branch; the full non-Perf suite green on the branch (0 failed, skips each a filed bug);
   the fix is the branch's first commit and touches only that test.
2. BUG-0220: `SfxTest` passes 10 / 10 with another testhost running at the same time (say how the load was produced).
3. BUG-0226 items 1-5 closed: the flipped QA row green; the seed-6 Billet row and the Tent row no longer silently skip; the
   docs line corrected.
4. BUG-0222 items 1-3 closed (item 2 as a docs/03 note is enough).
5. The sweep's result is listed in the report (file, row, how staged); no view test orders an Attack on an unseen target.
6. Build 0 warnings; smoke PASS; scene loop 33 / 33 on the branch; view Perf rows alone within budget; no `Check` or threshold
   loosened (every changed assertion listed); docs/03 updated; bug files set.

**Design references:** docs/03 "Implementation (M4-V3)", "Debug tooling" (`--no-combat`), "For the view (M4-V4)" (the fog
surface, read-only), "Build and export"; the bug files named above.

**Tests required:** the flipped QA rows above; a regression row for BUG-0220 (the clock-driven `Expect`); the scene loop.

**Constraints:** `ViewApi/` stays read-only (no sim state, tick or hash change; hash twins equal); no gameplay in views; no
C# player-facing text; the view never edits `game/data/**`, `sim/**` outside `ViewApi/` and its own test folders.

**QA focus (standard):** run the full non-Perf suite on the branch (the fog is in it) and the scene loop twice, once under
load; attack `MaxUnder` with random points and radii on 3-level maps near ramp feet and side walls (bound the hang); the
tracker's lob reuse with hostile sequences (same owner / type / target, different launch); `SfxTest` 10 runs under load;
confirm the sweep found every explicit Attack in view-owned tests.

### Watch-outs (view)

- The branch already contains the fog: `World.Fog` is there (`Visibility` changes only on `tick % 4 == 1`); combat may fire
  at what `CanSeeUnit` still hides for up to 4 ticks (BUG-0216 item 3): M4-V4 should know; nothing for this session.
- `--no-combat` scenes: DebugOverlayTest, OrdersTest, QaH1Test, QaH2Test, QaM27Test, MinimapTest, SfxTest, QaM24Test; never
  the Attack-order or combat-view scenes.

Then **M4-V4**: the fog shader from `World.Fog.Visibility(player)` / `Version(player)` (an R8 texture re-uploaded on version
change; black / darkened / clear), units hidden when `!Fog.CanSeeUnit(local, slot)` and enemy buildings when `!CanSeeBuilding`
(until M4-3b's ghosts), the minimap's fog layer and Attack half, `placement.unexplored` when M4-3b's rule lands.

## Data track

### Current session plan: D7, the buildings' sight on both faction pages · feature · QA light

**Goal:** the fog gave every building a sight radius (towers 24 m from their own `sight`; every other building 12 m from
`rules.json` `buildingSight`). Put that on both faction pages and in the design doc, pinned to the data both ways, and close
the D6 nits that are the data track's.

**Scope:**
- `docs/factions/malazan.md` and `docs/factions/whirlwind.md`: a **Sight** column in the Buildings table (after Footprint):
  24 for `malazan_watchtower` / `whirlwind_lookout_tower`, 12 for the other nine each. The existing columns stay byte-identical
  except for the inserted column (the pins read them). The towers' "Provides" text keeps "sight 24" (it will carry attack /
  detector until M4-3b).
- `sim/Rts.Sim.Tests/Content/BuildingContentTests.cs`: a pin both ways (page Sight == `BuildingDef.Sight` per building; a
  changed page value or data value fails naming the building and the field); a row that the non-tower buildings carry no
  `sight` in data (they inherit `buildingSight`), and that `rules.json` `buildingSight` == 12 == the page's non-tower column.
- `docs/02-game-design.md` "Vision and fog" paragraph (line ~251): one sentence: buildings see 12 m unless their table row
  says otherwise (`buildingSight` in `rules.json`); Watch Towers 24 m. The Buildings table there already reads "sight 24" for
  the Watch Tower: check, don't restructure.
- **BUG-0230 item 1:** anchor the Ages-clause pin (`TechContentTests.G`'s `AgeClause` regex) to the bullet's end so an appended
  contradicting sentence fails; **item 3:** a separate siege table on the Whirlwind page's "Balance baseline" like the
  Malazan page's (numbers unchanged). Item 2 is the sim's this session (a shared harness); switch
  `Content/CounterTriangleMarginsTests` to it **next** session (D8), not now.
- **BUG-0090:** close the tower-sight item in the file (attack / detector stay open for M4-3b).
- **Files this track may touch:** the two pages, docs/02 (the two spots), `sim/Rts.Sim.Tests/Content/**`,
  `sim/Rts.Sim.Tests/QA/Content/**`, `studio/bugs/BUG-0090*`, `BUG-0230*`, `studio/qa/**`. **No `game/data/` edit** (nothing
  numeric changes; the golden does not move).

**Out of scope:** any balance number (the owner's answer on the D6 proposal is still wanted; nothing changes until it
arrives); `attack` / `detector` for towers (M4-3b); abilities (M4-4); any C# outside the content test folders.

**Acceptance criteria:**
1. Both pages' Buildings tables carry a Sight column: towers 24, the rest 12; the other columns byte-identical to before.
2. A content test pins page Sight == `BuildingDef.Sight` for all 20 buildings both ways (prove with two scratch mutations:
   a page value and, in a scratch copy of the data, a tower's `sight`; both fail naming the building and the field), and
   `buildingSight` 12 == the non-tower column.
3. docs/02 states the 12 m default and the towers' 24 m in the vision paragraph; `grep` finds "12 m" there.
4. BUG-0230 item 1: the appended-contradiction mutant from the bug's repro fails `TechContentTests.G`; the seven other mutants
   still fail. Item 3: the Whirlwind page's siege rows are in their own table with proper columns, numbers unchanged.
5. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Content"` green; `DataValidationTests` green; no diff under
   `game/`, `sim/Rts.Sim/`, `*.replay`; build 0 warnings; BUG-0090 / 0230 files updated.

**Design references:** docs/02 "Buildings" table (Watch Tower: sight 24) and "Vision and fog"; docs/03 "Vision, detection,
fog" (data: `sight` per building, `buildingSight` 12, `DataLimits.MaxSight` 64); `docs/factions/malazan.md` /
`whirlwind.md` Buildings tables; BUG-0230, BUG-0090.

**Tests required:** the Sight pins (both ways) in `BuildingContentTests`; the regex pin's mutant proof (in the QA report);
the existing content suite green.

**Constraints:** the data track writes no C# outside `sim/Rts.Sim.Tests/Content/` and `QA/Content/`; the pages' existing
tables stay byte-identical apart from the new column; docs edits small; no `game/data/` edit this session.

**QA focus (light):** criteria, build / tests, docs conformance: mutate the page (a tower 24 → 18, a Barracks 12 → 14) and the
data (in a scratch copy) and confirm each fails naming the building and field; the BUG-0230 mutants; check both pages' tables
against `buildings.json` by hand for all 20 rows; confirm no replay / golden / `game/` diff.

### Watch-outs (data)

- The branch base contains the fog and `BuildingDef.Sight`; `main` 78b9014 (merged in) has D6's pages and tests.
- The margins harness copy (BUG-0230 item 2) is the sim's this session; D8 switches to it.

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the 33-scene loop and smoke on each **merged** result (view → sim → data).
- Expected conflicts: `studio/qa/coverage.md` (append all sides), docs/03 (one subsection per track), docs/01 (rows appended),
  `studio/bugs/README.md` (union by id).
- **Sessions:** the 3-hour lock window declared a live session dead twice; the owner has the suggestion under For your
  review. A conductor should check for running studio processes before resuming.
- **Perf:** three tracks' suites at once fail 3-9 wall-clock rows on base and head alike; a failure counts only alone.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
