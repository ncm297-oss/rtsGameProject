# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-10-2334** (sixth full session of 2026-10-10, cap 8). Base `main` fa74130
(the 2026-10-10-1954 integration: M4-5, M4-V7 slice 2, D11 + D-H3 all landed; build 0 warnings). **M4: 10 / 10 criteria met**, coverage
✅ ✅ ✅ (Unit / Invariant fuzz / Determinism) on every M4 system row, no open S1 / S2. This session is **every track's end-of-M4 work**:
sim **M4-H3** (hardening), view **M4-VH3** (hardening), data **D12** (the casters' `detector 10`). The **M4 sign-off** is the Producer's at
this session's ACCEPT if all three are accepted (retro in docs/05, M5 set to Next), else at the next.

## Where we are

- M0-M3 Done. M4 10 / 10. Counters before this session: sim 3 / 4, view 3 / 4, data 0 / 4 (sim and view take the end-of-milestone
  hardening regardless; they reset to 0 / 4 after it).
- **Golden** `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`: `data-hash` **889D6BEA3A49F08E** (verified on `main` at this PLAN).
  **Only D12 moves it this session** (`data-hash` + `checksum`; every `k` line equal; appended to `GatherWedgeQaTests.SameGameDataHashes`).
  **The sim's M4-H3 leaves the golden bit-identical**: a fix that would change its `k` lines (a movement change the cross-map march feels)
  is out of scope this session (document it in the bug instead).
- **Integration order this session: data → sim → view** (data moves the golden alone; the sim's fixes touch no `game/data/**`; the view
  touches neither). Expected conflicts: `studio/bugs/README.md` (union by id), `studio/qa/coverage.md` (sim + view rows: keep both).
- **Memory bound** 231,920,000: 231,890,440 used (29,560 B margin). BUG-0480's scratch (`int[UnitCapacity]` ≈ 16,408 B) fits once; any
  further per-slot array re-baselines, itemised in the brief and the report (rule 7).
- **`EmptyTick(2500)`** on its 500 µs gate on base and head alike (BUG-0451): the counting rule stands (a Perf failure counts only alone and
  above base). M4-H3 decides (see the sim brief).
- **Bug ids this session:** sim from **BUG-0510**, view from **BUG-0520**, data from **BUG-0530**.
- Standing rules unchanged (1-8: full suite incl. Perf alone + the merged scene loop for vision / combat / loader changes; a new `UnitState`
  ships its `ui.json` line; whoever moves `data-hash` adds it to `SameGameDataHashes`; `ui.json` keys named in the brief; a sim behaviour
  change a view scene asserts on gets a named item in the view brief; a new `Command` kind goes into `CommandDoorFuzzStressTests`;
  per-slot memory itemised; the shipped `data-hash` pinned in the golden only, never a literal).
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).

## Sim track

### Current session plan: M4-H3, the end-of-M4 sim hardening · hardening · QA full for the BUG-0461 rule, standard for the rest · up to ~1,500 lines incl. QA

**Goal.** Close the sim's M4 debt so M4 can be signed off: the three M4-5 bugs, the cast walk-in that drops silently behind own
attackers, the ordered chase that gives up a reachable building, and the perf-gate decision; then as much of the older backlog as fits.
Every fix gets a regression test that failed first.

**Scope, most valuable first:**
1. **BUG-0461 (S3), QA full.** The cast rule (Producer default, owner may revisit): a cast's walk-in whose caster is blocked by an **own
   unit within 1.5 m in its direction of travel** does not count stuck ticks; and a cast's walk-in has **its own patience of 3 s** of
   genuinely stuck ticks (not the plain Move's 1 s) before the cast is dropped. A dropped cast raises an `AbilityEvent` with a **`Dropped`**
   flag (the view ignores it this session; an M5 / M6 HUD item). The 0.17 m 2-tick limit cycle on flat ground (replay 3; BUG-0035 / 0037
   family) is a movement bug: fix it if it is a few lines **and leaves the golden's `k` lines bit-identical**, else document it in docs/03
   "Known limits" and leave BUG-0461's movement half open with a note. The three sandbox replays in the bug file are the regression scenes
   (rebuilt as xUnit rows: a caster hemmed in by own `Attacking` units at a ramp top and on flat ground must still cast once the way clears
   or must wait the full 3 s before dropping). `M4PlayableTest` must still pass on seeds 1 and 6 in the merged scene loop.
2. **BUG-0480 (S3).** `StealthSystem.OnStealthStarted` writes into `World.ShoveNeighbors` while `PassiveSystem.OnDeath` iterates it: give
   detection its own scratch (`World.DetectNeighbors`, `int[UnitCapacity]`, itemised in the memory note), un-skip QA's
   `AnOnDeathPassiveApplyingStealthed_ReachesEveryListedFriend(true)`; docs/03 "Implementation (M4-5)" known limit (2) updated.
3. **BUG-0481 (S3).** The loader requires **exactly one `revealed` kind when any `stealthed` kind is present** (error at `statuses`); a
   `Data/StealthLoaderTests` row; QA's `StealthedWithoutARevealedStatus_IsRefusedAtLoad` un-skipped; the docs/03 `statuses.json` bullet.
4. **BUG-0482 (S4)** if a few lines: a reveal replaces the oldest non-stealth entry when the store is full; else document the limit.
5. **BUG-0451 decision (Producer):** one profiling pass on the idle path of `EmptyTick(2500)` (movement / scan / hash of 2,500 idle
   units); if a buy-back of **≥ 5 %** is found in ≤ ~200 lines, take it; else **re-baseline the gate to 550 µs** with the trend itemised in
   the row's comment (M4-4a → M4-5 steps). Either way the bug file records the measurement and closes (`fixed` or `wontfix`).
6. **BUG-0401 (S3).** An ordered chase (`CombatMode.Ordered`) whose target is reachable (finite flow-field cost at the unit's cell) never
   counts stalled scans; an unreachable one still gives up. The seed-6 `QaGhostViewTest` layout as an xUnit row (a ramp detour round a cliff).
7. Then as they fit: BUG-0302 b / c (the memory itemisation note; `AbilityPerfTests` phases run twice), BUG-0275 items 1-2 (**item 1 only
   if it moves no golden `k` line**; else decide and record), BUG-0271 / 0272 (the dev recommends `wontfix` as documented limits: decide and
   record), BUG-0144, BUG-0142 items 1-2, BUG-0113 item 2, BUG-0094, BUG-0391, BUG-0422 items 2-3; bug files set `fixed` / `wontfix` with
   the proving test named; the README index rows.
8. docs/03 updated with every behaviour change; a docs/01 row for each rule decided (BUG-0461's cast patience and own-blocker rule,
   BUG-0401's reachable-chase rule) marked "Producer decision, owner may revisit".

**Out of scope:** any feature; `game/data/factions/**` (D12 writes both `units.json` this session); `game/data/common/**` (no data-hash
move from the sim); `ui.json`; `DataContentHashTests.cs`; `cross_map_seed1.replay` (bit-identical at the end); `GatherWedgeQaTests.SameGameDataHashes`.

**Acceptance criteria:**
1. BUG-0461: a caster hemmed in by own `Attacking` units within 1.5 m ahead counts no stuck ticks and casts when the way clears (xUnit rows
   from replays 1 and 2's layouts); a genuinely stuck walk-in drops the cast after 3 s, not 1 s; the drop raises an `AbilityEvent` with
   `Dropped`; each row fails on the base; `M4PlayableTest` seeds 1 and 6 pass in the merged loop.
2. BUG-0480: QA's row un-skipped and green; the scratch itemised (memory delta stated; margin after it stated).
3. BUG-0481: shipped data minus `revealed` is refused at `statuses` with one error; QA's row un-skipped.
4. BUG-0451: the measurement in the bug file; either `EmptyTick(2500)` passes alone with ≥ 5 % headroom under 500 µs, or the gate is 550 µs
   with the trend itemised in the comment.
5. BUG-0401: the ramp-detour row passes (the building is attacked), fails on the base; an unreachable target still gives up.
6. Full suite incl. Perf alone green (BUG-0451 rule); smoke PASS; merged scene loop 41 / 41; 0 warnings; the golden file byte-identical to
   `main`'s (`git diff --stat` shows no `Replays/` change).
7. docs/03 + docs/01 rows; every worked bug file `fixed` / `wontfix` with the proving test; README index rows.

**Design references:** docs/02 "Ability system" (the player clicks a target; a dropped click is not acceptable silently), docs/03
"Implementation (M4-4a)" walk-in rule, "Implementation (M4-5)" known limits, docs/03 "Who is shoved", the BUG-0461 / 0401 / 0451 files.

**Tests required:** regression rows above (fail-first on base, named in the report); `Vision/StealthTests` + QA's stealth rows green;
`ReplayGoldenTests` green on the untouched golden; `AllocationTests` 0 B rows still green after the scratch; Perf alone.

**Constraints:** no allocation in per-tick code (the scratch is pre-sized); no Dictionary iteration; every number (1.5 m, 3 s) in
`rules.json` or `SimConfig` with a loader default, never a C# literal in a system; `Rts.Sim` warnings as errors; no `game/data/**` edit.

## View track

### Current session plan: M4-VH3, the end-of-M4 view hardening · hardening · QA standard · up to ~1,500 lines incl. QA

**Goal.** Close the view's M4 debt so M4 can be signed off: discs and rings that cross a cliff, the sandbox's stated range, and the
smaller nits.

**Scope, most valuable first:**
1. **BUG-0430 (S3) + BUG-0462 item 1.** A zone disc, the `selfAura` hover ring and the targeting ring are flat planes at the centre's
   height, so across a 4 m cliff they sink under the higher ground or float over the lower. Project them onto the terrain: a Godot `Decal`
   projecting a runtime-generated disc texture downward over the zone's footprint (height span ≥ 2 levels), **or** a terrain-hugging mesh
   whose vertices sample `TerrainHeight.At` (depth test on), **or** a heightmap-sampling shader. The dev chooses and says why. Windowed
   stills at the cliff (`qa-v6c-cliff-zoom30/50` as the before) looked at; if a mesh, a test that every rim vertex sits on `TerrainHeight.At`
   within 0.05 m on a two-level map; a zone centred on a plateau edge shows its disc on both the plateau and the low ground.
2. **BUG-0490 (S4):** `TickBudget`'s comment and docs/03 state the measured range (QA's 3,081 / 4,488); one `FillMaxHp` a frame shared by
   `UnitViews` and `CombatViews`.
3. **BUG-0431 (S4):** a distinct own-storm rim colour (not within 0.06 of Whirlwind's `#C8892E`), checked in a windowed still.
4. **BUG-0462 item 3** (marker rows merge at zoom 60: fix or an M6 note), **BUG-0400**, **BUG-0250 item 2**, **BUG-0148 item 1**,
   **BUG-0126 items 5 / 6**; then, if budget remains, **export hygiene** (exclude `game/tests/` from the release build; load `game/data/`
   and `ui.json` in a `.pck`-safe way), docs/03 "Build and export".
5. Bug files + README rows; docs/03 per change.

**Out of scope:** stealth visuals (M8); any `game/data/**`, replay or `DataContentHashTests.cs` edit; any `sim/**` edit outside
`sim/Rts.Sim/ViewApi/**` read-only additions. The sim's M4-H3 adds an `AbilityEvent` `Dropped` flag and a longer cast walk-in patience:
`M4PlayableTest` works round BUG-0461 with plain Moves, so no scene change is expected; the view ignores `Dropped` this session.

**Acceptance criteria:**
1. BUG-0430: on a two-level map a Sandstorm disc crossing the cliff is visible on both levels (the still looked at and named in the
   report); the hover and targeting rings the same; the fog rule for discs (`ZoneDiscs`) unchanged and QA's oracle rows still green.
2. BUG-0490: the comment and docs/03 carry the measured range; exactly one max-hp fill per frame (a test or an allocation / call-count row).
3. BUG-0431: own-storm rim colour differs from `#C8892E` by more than 0.06 in every channel distance measure the test uses; a still.
4. Every worked bug's regression (xUnit or a scene row) failed first; merged scene loop 41 / 41; smoke PASS; 0 warnings; no new
   player-facing literal; docs/03; bug files `fixed` / `wontfix` with the proving test; README rows.

**Design references:** docs/03 "Implementation (M4-V6c)" ("a disc on the ground per zone"), "Implementation (M4-V7)" (`TickBudget`),
docs/03 "Dumb views"; the BUG-0430 / 0462 / 0490 / 0431 files.

**Constraints:** dumb views (no gameplay state in views; `ViewApi` additions read-only, hash twin unchanged); no sim mutation; player-facing
text only from `ui.json`; `--no-fog` / `--no-combat` flags keep working.

## Data track

### Current session plan: D12, the casters' detection · feature · QA light · ~500 lines incl. QA

**Base:** `main` fa74130 (the `detector` schema and the `stealthed` / `revealed` statuses are on it). Files this task may write under
`game/data/`: `game/data/factions/malazan/units.json`, `game/data/factions/whirlwind/units.json` (**no other track writes `game/data/**`
this session**); `docs/factions/malazan.md`, `docs/factions/whirlwind.md`; `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`;
`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` + the `SameGameDataHashes` line in `QA/GatherWedgeQaTests.cs` (the one golden regen;
named exception). **The data branch merges first this session.**

**Scope:**
1. `"detector": 10` on **`malazan_cadre_mage`** and **`whirlwind_priest`** (the two shipped casters; docs/02 line 33 "Caster" class row
   ("detector") and "Stealth and detection" line 248: "all casters (10 m)").
2. A Detector cell on both faction pages (the unit tables or the Unit notes, in the pages' existing style) pinned both ways by
   `Content/UnitContentTests` (or the page compare the pages already use), with mutants that fail by name (a page saying 12 m, data saying 12 m).
3. The `stealthed` / `revealed` texts pinned against docs/02 "Status effects" rows 224-225 (`Stealthed` "Invisible and untargetable to
   enemies without detection", `Revealed` "Cannot be stealthed (overrides Stealthed)"): out of `AbilityContentTests.PendingStatuses`; the
   shipped descriptions in `common/statuses.json` are the sim's (numberless): the data track may propose a rewording in the report, never
   edit `common/**`.
4. One golden regen (`data-hash` 889D6BEA3A49F08E → new; every `k` line equal; appended to `SameGameDataHashes` with the reason "D12:
   casters' detector").
5. BUG-0500 (the two linger patterns, QA's row un-skipped) and the BUG-0470 status line (item 3 was fixed at the 1314 ACCEPT) if a few lines.
6. The review table in the report (unit / field / old → new / why; the two page cells quoted).

**Out of scope:** the balance pass (after the M4 sign-off, on the owner's D6 answer or the Producer default 40-65 %); any `common/**`
edit; any C# outside the data test folders; any other `game/data/**` file.

**Acceptance criteria:**
1. `DataValidationTests` green; both casters load with `UnitDef.Detector` 10 m (a content test asserts it); no other unit has one.
2. The page cells pinned both ways (mutants fail by name, listed in the report).
3. `PendingStatuses` without Stealthed / Revealed; `DescriptionProblems` 0 on shipped text.
4. The golden diff is `data-hash` + `checksum` only (every `k` line equal, stated); `ReplayGoldenTests` + `GatherWedgeQaTests` green;
   `TheShippedContentHash_IsTheGoldenReplaysHeader` green.
5. Non-Perf suite green, 0 warnings; the review table in the report.

**Design references:** docs/02 "Unit classes" (line 33), "Stealth and detection" (lines 244-257), "Status effects" (lines 224-225);
docs/factions/malazan.md and whirlwind.md unit tables; docs/03 "For the data track (M4-5)".

**Constraints:** data only; no C# outside `sim/Rts.Sim.Tests/Content/` and `QA/Content/`; the golden is the only hash pin (rule 8).

## Watch-outs (all tracks)

- **Integration order this session: data → sim → view.** Data alone moves the golden (`data-hash` + `checksum`); the sim's golden is
  bit-identical to `main`'s (checked at ACCEPT by `git diff --stat`); the view touches no replay. Conflicts foreseen: `studio/bugs/README.md`
  (union by id), `studio/qa/coverage.md` (sim + view rows: keep both). docs/03 auto-merges (different sections). The merged scene loop (41,
  both playables last) is the gate.
- **The M4 sign-off** at this ACCEPT if all three tracks are accepted: every required criterion verified (10 / 10 on `main`), coverage ✅ for
  Unit / Invariant fuzz / Determinism on every M4 row (true at this PLAN; the Abilities row's Scenario 🟡 is not a required column), no open
  S1 / S2, the retro in docs/05 (open S3 / S4 listed), M5 set to Next, a For your review entry on how to try M4. If a track is rejected,
  the sign-off waits for that track's redo.
- Perf under three suites fails on base and head alike (`TightBlob2500`, `EmptyTick(2500)`); a failure counts only alone and above base.
- Scratch directories in the shared scratchpad carry the agents' names; never `rm -rf` a shared name. 16 entries in `git worktree list`
  are owner / conductor housekeeping (`git worktree prune` after deleting the folders).
