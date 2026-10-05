# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

## Current session plan (2026-10-04-2056, PLAN: GO)

**TASK_ID: M1-4c — Make the flow-field build cap deterministic and fair; de-flake the suite.**
Base b4cb014 (main). Checks: build 0/0; non-Perf 791 passed / 7 skipped; Perf serial 53 / 3
skipped; smoke PASS; no open S1/S2; sessions today 1/10 before this one.

Goal: close BUG-0021/0022 (cache metadata is sim state and must be hashed; misses served oldest
order first with a cap of 2 and a cache sized to the unit cap) and BUG-0024/0017 (the full
one-process suite must be green on this PC). Steering/separation (M1-4d) builds on this.

Scope, in order (one implement commit; report a line after part A before starting B):
- A. Test infra only: one xUnit collection with `DisableParallelization = true` holding every
  `Category=Perf` test and every test that reads `GC.GetAllocatedBytesForCurrentThread`
  (whole classes may join it); thresholds unchanged. A shared allocation helper that, on a
  non-zero delta, re-runs the measured block once and fails with both deltas; the 0-byte assert
  stays. Run the full suite 3 times in one process: all green.
- B. Sim: hash the cache's metadata in `StateHash` (per slot RequestedCell + Version + last-use
  clock, plus clock and count); per-unit hashed `OrderTick` (tick the Move applied); build pass
  serves misses by (oldest OrderTick in the goal group, GoalCell), up to
  `MaxFieldBuildsPerTick = 2`; cache capacity from a pure `FlowFieldCache.CapacityFor(unitCapacity,
  cellCount)` = clamp(unitCapacity / 8, 32, 128), then min with max(32, 64 MB / bytesPerField);
  `Get`/`TryGetCached` become `internal`; `FlowFieldCache` + `MovementSystem` remarks corrected
  ("(M1-4c)" references for steering become M1-4d). Docs/03 updated. Out of scope: save/load,
  steering, separation, arrival slots, shoving, time-sliced builds, BUG-0005/0014/0023.
- Budget ~250 production lines + tests. Zero allocation on the tick path (preallocated buffers for
  the extra sort; note a (OrderTick, GoalCell, slot) key doesn't fit 64 bits: prefer a second
  per-group miss buffer keyed (oldestOrderTick << 32 | GoalCell)).

Acceptance criteria: see the Producer's PLAN output (11 items) in the session log.

_Written: 2026-10-04 14:00 (session 2026-10-04-0120, ACCEPT)._

## Where we are

M0 Done. M1 is 3/8 plus half of criterion 4: sim core (M1-1), data loader (M1-2), terrain + nav
grid (M1-3), ramp walls + spatial hash (M1-4a), flow fields + LRU cache + `Move` + build cap
(M1-4b). Branch `studio/2026-10-04-0120` @ b707114: build 0/0; 844 passed / 10 skipped / 854;
smoke PASS. The conductor merges it to main. Open bugs: 7 S3, 3 S4, none block. Sessions today: 1/10.

**The full suite in one process is flaky on this PC (BUG-0024, pre-existing):** wall-clock Perf
asserts and one allocation assert (BUG-0017, failed 2 of 3 Producer runs, 8,112 bytes) trip under
xUnit parallel load; each passes alone. Reliable check until fixed:
`dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf"` then
`dotnet test sim/Rts.Sim.Tests --filter Category=Perf -- xUnit.ParallelizeTestCollections=false`.
If the PLAN-mode check sees a red run, rerun with these two commands before calling main red.

What exists in the sim: `World(config)` owns `Heightmap`, `NavGrid`, `Spatial`, `FlowFields`
(`FlowFieldCache`, 32 fields, LRU, version-tagged, preallocated), `MoveOrder` scratch, `Data`
(`SimConfig.Data`, required `GameData`). Units: `Position/PrevPosition/Velocity/Facing/Owner/TypeId/
Speed/Radius/State/Goal/GoalCell`. Commands: `Noop`, `SpawnUnit` (drops unknown TypeId), `Move`
(drops stale/foreign/off-map). Tick: commands -> spatial rebuild -> `MovementSystem.Run` (units
sorted by (goal cell, slot); one field fetch per goal; at most `MaxFieldBuildsPerTick` = 1 build;
waiting units stay Moving at velocity 0; arrival only inside the goal cell within `CellSize/2`;
steps into blocked cells refused). No separation, shoving, arrival slots, Stop/Hold, order queues.

## Next task candidates

1. **M1-4c: make the build cap deterministic and fair; de-flake the suite.** First commit (test
   infra, no sim change): BUG-0024 (one xUnit collection with `DisableParallelization = true` for
   every `Category=Perf` test and every allocation-measuring test; thresholds unchanged) + BUG-0017
   (on a non-zero delta re-measure the block once and report both deltas; keep the 0-byte assert).
   Then the sim part, per the Producer decision in docs/01 and docs/03 "Flow fields": hash the
   cache's metadata (RequestedCell, Version, LRU clock per slot) in `StateHash`; add a hashed
   per-unit `OrderTick`; serve misses oldest order first (sort key (OrderTick, GoalCell, slot) for
   the build pass, grouping by goal cell otherwise); count cap of 2 builds per tick (document the
   cost: ~1.4 ms Debug worst on the default map, budget 4 ms average); cache capacity
   `max(32, UnitCapacity / 8)` (80 KB per field on 128); `FlowFieldCache` remarks corrected;
   un-skip QA's BUG-0021 and BUG-0022 tests (the 64-group test may need its window tuned to the
   new cap; QA decides, never the dev alone). Tests: hash changes when cache metadata differs; two
   sims with different pre-warm diverge no more (BUG-0021 test green); FIFO fairness: every group
   that keeps an order for N ticks gets a field; zero alloc with the new sort; determinism and
   perf suites still green. Budget ~250 production lines.
2. **M1-4d: steering, separation, arrival slots, shoving + BUG-0005** (docs/03 "Local movement").
   Separation via `Spatial.QueryRadius` with data radii; arrival slots around the target; shoving of
   idle units; per-player command buckets. Closes roadmap criterion 4. Measure query cost first
   (sort-dominated in crowded buckets).
3. M1-5 scenario test (200 units across 128x128 with obstacles). Needs 4d.
4. M1-6 replay + determinism golden: first commit fixes BUG-0014 (mix the seed in `SimRng`).
5. M1-7 perf test: also document that maps above 256 are unsupported (BUG-0023).

## Watch out for

- Budget discipline: M1-4b ran ~690 production lines vs 500 (M1-4a ~660 vs 450). Keep 4c to the
  cap/hash/suite work; nothing from 4d.
- One implement commit per session (session skill rule); ask for a report line after the first
  part, not a second commit.
- Generator pins: 15 hashes in `MapGeneratorTests.Generate_MatchesMapsFromBeforeTheFastRampChecks`
  and QA `PreBug0015*` oracles. `NavGrid.Flood` has no bounds checks (ring must stay blocked).
- `FlowField.NearestPassable` ring search: exit test is `r*r <= bestD2` on purpose (a ring-r cell
  at exactly r^2 can tie and win on index). QA oracle-tested on 400 grids; don't "simplify" it.
- `CellQueue` is a 4-bucket Dial queue: correct only while every step costs >= 1 and the live cost
  span is < 3 whole numbers. Nav cost bytes (M3 forests) must not break that; if costs < 1 are
  ever wanted, switch to a heap.
- `StateHash` now covers `State/Goal/GoalCell` and `Move`'s handle; `Speed/Radius` are derived
  from `TypeId` and stay out. The flow-field cache is not hashed yet (BUG-0021, task 1).
- `TestSim.Config(...)` is the one way tests build a `SimConfig` (shipped data, loaded once).
- `ArchitectureTests.SimSource_UsesNoForbiddenApis` and `SpatialHashTests.Source_UsesNoHash
  CollectionsOrLinq` (now `Spatial/`, `Pathfinding/`, `Movement/`) grep sim source.
- `QA/DataLoaderQaTests.GameDataTypes_ExposeOnlyImmutableMembers` reflects over `Rts.Sim.Data`.
- `SimRng` is a mutable struct: always `ref world.Rng(stream)`.
- Soak rows (`Category=Soak`, ~100 s) are in the suite; quick loop:
  `--filter "Category!=Perf&Category!=Soak"` (~45 s).
- .sln Release config builds the game project in Debug (BUG-0002, S4): irrelevant until M6.
