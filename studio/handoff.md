# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-05 (session 2026-10-05-0742, ACCEPT)._

## Where we are

M0 Done. M1 is 3/8 plus most of criterion 4: sim core (M1-1), data loader (M1-2), terrain + nav
grid (M1-3), ramp walls + spatial hash (M1-4a), flow fields + LRU cache + `Move` (M1-4b), hashed
cache metadata + build cap (M1-4c), separation + crowded arrival + give-up + adjacent-cell steering
(M1-4d-1). Branch `studio/2026-10-05-0742`: build 0/0; 962 passed / 11 skipped / 973 in one
process (~2 min); smoke PASS. The conductor merges it to main. Open bugs: 8 S3, 3 S4, none block.
Sessions today: 1/10.

What the sim does now (`MovementSystem`, docs/03 "Local movement"): Moving units sorted by (goal
cell, slot); build pass (oldest order first, cap 2); **Plan** pass per unit from start-of-tick
state: `Spatial.QueryRadius` into `World.Neighbors`, aim (goal / adjacent-cell goal / next center,
or one further if a standing unit covers it), push (half vs walkers, all vs arrived groupmates),
sidestep right around standing/oncoming units, `Constrain` against standing non-groupmates (hard
walls), refused steps slide along the wall; arrival within 1 m or touching an arrived groupmate,
gated by `ArrivalSpacing` 0.6 (else back off); progress vs `BestRemaining` by 0.25 x speed, else
`StuckTicks`++, 20 -> Idle (`GoalCell` -1; a backing-off unit keeps its goal). **Apply** pass moves
and changes state. `ApplyMove` to the goal cell a unit already holds is the same order. Units:
`.../OrderTick/StuckTicks/BestRemaining` (last two hashed). Scratch on `World`: `Neighbors`,
`PlannedStep/Action/Remaining`, `MaxUnitRadius` (from data). Test seam `World/Simulation(config,
Heightmap?)` internal; `LocalMovementTests.Rows(...)` builds hand-made maps.

## Next task candidates

1. **M1-4d-2: shoving + BUG-0030 + BUG-0031 + re-tighten assertions (BUG-0028).** Closes roadmap
   criterion 4. Scope: (a) shoving: a standing Idle unit (no Hold yet) that a walker would collide
   with steps aside (short step along the walker's push normal, refused into blocked cells, never
   for a unit that is itself Moving); must stay in the two-pass model (standing units' shoves are
   planned from start-of-tick state, applied after; a shoved unit stays Idle and keeps `GoalCell`).
   (b) BUG-0030: `ApplyMove` treats a Move as the same order only when the new point is within
   `ArrivalDistance` of the stored `Goal` (QA suggestion), not merely the same cell; keep the
   BUG-0029 tests green. (c) BUG-0031: `Constrain` must not demand clearing the whole overlap in
   one step: cap the push-out at the step length (or speed) so a unit pinned between a standing
   unit and a cliff can slide out. (d) Re-tighten `QA/FieldBuildCapQaTests.BuildCap_500UnitsWith500
   DistinctGoals_EveryUnitArrives_NoDeadlock` (rename or restore "every unit arrived") and
   `MovementSystemTests.MoreGoalsThanCacheSlots_...`; un-skip BUG-0030/0031 QA tests; consider not
   counting a stuck tick for a Moving unit that is only waiting for a field. Expect the 500 x 4
   points give-up rate (422/500) to drop sharply; set the stress assertions accordingly. Keep
   `FieldBuildOrderTests` and `QA/FieldBuildFairnessQaTests` unchanged. Budget ~600 lines; BUG-0005
   is NOT in this task.
2. **Command buckets + eviction fairness:** BUG-0005 per-player command buckets (O(n^2) insertion
   sort), BUG-0025 (evict the live field with the newest order), BUG-0026 (rotate same-tick
   tie-break). Un-skip the BUG-0005 and BUG-0025 tests. Small task, before M5.
3. M1-5 scenario test (200 units across 128x128 with obstacles). Needs 4d-2.
4. M1-6 replay + determinism golden: first commit fixes BUG-0014 (mix the seed in `SimRng`).
5. M1-7 perf test: also document that maps above 256 are unsupported (BUG-0023). M1-8 CLI.

## Watch out for

- Budget discipline: 4d-1 ran ~1,100 changed lines (~450 production) against an 800 brief; it
  was accepted because the extra was tests, but plan 4d-2 tight (shoving alone first if needed).
- Perf: tight blob of 2,500 units averages 3.8 ms / worst 12.4 ms (Debug); 500 is 0.4/0.8.
  Shoving adds neighbor work for Idle units: measure with QA's `Perf_TightBlob_AvgAndWorstTick`.
- Determinism: all unit-unit interaction must read start-of-tick state (Plan/Apply split);
  neighbor order is ascending slot from `QueryRadius`. Shoving of Idle units must not touch LRU
  state or walk order. Re-run `Determinism_300UnitsIn4CrowdedGroups_...` and QA's interleaved test.
- Arrived Idle units keep `GoalCell` and anchor blobs; given-up units have `GoalCell` -1. QA's
  `AfterCrowdsSettle_EveryIdleUnitHoldingAGoal_IsLinkedToItsPoint` guards stray anchors: shoving
  must not drag an anchor away from its blob (or must clear its `GoalCell` if it does).
- Mutant that survives: back-off writing `BestRemaining` (near-equivalent). Low risk.
- One implement commit per session (session skill rule); ask for a report line after the first
  part, not a second commit.
- Every Perf or allocation-measuring test must be in `SerialCollection`; allocation asserts go
  through `AllocationProbe.AssertZero`.
- `StateHash` covers `State/Goal/GoalCell/OrderTick/StuckTicks/BestRemaining`, cache metadata and
  `Move`'s handle; `Speed/Radius`, spatial hash, scratch arrays and field contents are derived.
  `NavGrid.Version` is not hashed (fine until M3).
- `FlowFieldCache.Get`/`TryGetCached` move hashed LRU state: sim-only.
- Generator pins: 15 hashes in `MapGeneratorTests` and QA `PreBug0015*` oracles. `NavGrid.Flood`
  has no bounds checks (ring must stay blocked). `FlowField.NearestPassable` exit test is
  `r*r <= bestD2` on purpose. `CellQueue` is a 4-bucket Dial queue (step costs >= 1, span < 3).
- `TestSim.Config(...)` is the one way tests build a `SimConfig`. `ArchitectureTests` and
  `SpatialHashTests.Source_UsesNoHashCollectionsOrLinq` grep sim source.
- `SimRng` is a mutable struct: always `ref world.Rng(stream)`. Soak rows (~100 s) are in the suite.
