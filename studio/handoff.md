# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

## Current session plan (2026-10-05-1013, PLAN)

TASK_ID: M1-4d-2 · TASK_TITLE: Add shoving of idle units; fix BUG-0031; re-tighten the loosened
give-up assertions · SESSION_TYPE: feature · QA_TIER: full · Budget: ~800 changed lines.

Goal: finish roadmap criterion 4 ("steering, separation, arrival, shoving"): standing Idle friendly
units step aside for walkers, so groups stop giving up en masse against each other's blobs
(BUG-0028) and the two loosened assertions can say "every unit arrives" again. BUG-0031 is folded in
because it is a few lines in `Constrain`, the same code shoving changes.

Scope:
- Shoving in `MovementSystem`, inside the two-pass model: during a walker's Plan, when its step is
  constrained by (or it already overlaps) a standing, alive, Idle unit of the same owner, accumulate
  a shove on that unit in a new `World` scratch array (`ShoveStep`, sized to `UnitCapacity`, cleared
  per tick without allocation). In Apply, after walkers move, each shoved Idle unit takes
  `ClampLength(ShoveStep, its own Speed)`, refused (stays put) when the destination is a blocked
  cell, off the map, or across a blocked corner (`CanStep`/`IsLegalStep`). Moving units are never
  shoved (they plan themselves). Shoved units stay Idle, no order, no `OrderTick` change.
- Anchor rule: a shoved unit keeps `GoalCell` only if it still counts as arrived for its stored
  `Goal` (within `ArrivalDistance`, or touching an arrived groupmate); otherwise `GoalCell = -1`.
  So a shoved unit can be sent back by a re-issued Move, and QA's
  `AfterCrowdsSettle_EveryIdleUnitHoldingAGoal_IsLinkedToItsPoint` stays true.
- BUG-0031: `Constrain` only removes the part of a step that goes deeper into a standing unit; an
  existing overlap is reduced by at most the step length (never a push-out larger than `Speed`
  that lands in a cliff). Un-skip the BUG-0031 theory (3 rows).
- Re-tighten `QA/FieldBuildCapQaTests.BuildCap_500UnitsWith500DistinctGoals_EveryUnitArrives_NoDeadlock`
  and `MovementSystemTests.MoreGoalsThanCacheSlots_BuildsAtMostTheCapPerTick_AndEveryUnitArrives`
  to the measured give-up rate after shoving plus a small headroom, or rename if not "every"; give
  `Stress/LocalMovementStressTests` crowd rows (500 and 2,500 units to 4 points 6 m apart) a real
  arrived-fraction assertion.
- docs/03 "Local movement": implementation paragraph for M1-4d-2 (shove rule, anchor rule,
  Constrain change), replace the "Known limits until shoving" paragraph with the new numbers.
- OUT: BUG-0030 (debt backlog), BUG-0005/0025/0026, Hold/Stop commands, formation offsets,
  shoving enemies or Moving units, any change to `FieldBuildOrderTests` / `QA/FieldBuildFairnessQaTests`,
  flow-field or cache changes, new per-unit hashed state (if one proves necessary, say why and
  cover it in `StateHashTests`).

Acceptance criteria:
1. A walker in a 1-cell corridor blocked by one Idle unit of its owner reaches its goal; the Idle
   unit ends Idle, inside a passable cell, never overlapping the cliff, and never Moving.
2. An Idle unit standing against a cliff is never shoved into a blocked cell or off the map
   (test with the walker pushing straight at the cliff: shove refused, walker slides or gives up,
   no blocked-cell position at any tick).
3. A Moving unit (walking or waiting for a field) is never moved by a shove; an enemy-owned Idle
   unit is never shoved (test with two owners).
4. A shoved arrived unit that leaves its blob and its 1 m arrival radius has `GoalCell = -1`; one
   shoved but still touching its blob keeps it; a shoved unit re-ordered to its old point walks back.
   `AfterCrowdsSettle_EveryIdleUnitHoldingAGoal_IsLinkedToItsPoint` green for all 7 scenarios.
5. BUG-0031 theory un-skipped and green (3 rows) plus a dev regression for the `Constrain` cap
   that fails when the cap is removed.
6. Give-up rates: `BuildCap_500UnitsWith500DistinctGoals...` at most 3% give up (name restored
   if 0, else renamed to say what it asserts); `MoreGoalsThanCacheSlots...` at most 5% of 128;
   crowd 500 to 4 points: at least 80% arrived, 2,500 to 4 points: at least 60% arrived. Report the
   measured numbers; if a target is missed, say why rather than loosening silently.
7. Determinism: `Determinism_300UnitsIn4CrowdedGroups_HashEqualEveryTick_SeedsDiffer` and QA's
   interleaved two-sim test green; a new test runs a walker column through an idle crowd twice with
   reversed spawn order and asserts the same hash.
8. Allocation: `Tick_500UnitsConvergingOnOneBlob_AllocatesNothing` green and a new Serial test with
   200 walkers crossing a settled 300-unit blob allocates 0 bytes.
9. Perf (Debug, report numbers): `Perf_TightBlob_AvgAndWorstTick` 500 units average under 1 ms;
   2,500 not worse than 4.5 ms average (today 3.8). Crossing-the-blob scenario at 500 units under 4 ms average.
10. docs/03 updated in the same commit; `ArchitectureTests` green; `FieldBuildOrderTests` and
    `QA/FieldBuildFairnessQaTests` unchanged and green; full suite green in one `dotnet test`.

Design references: docs/03 "Local movement" (bullets Shoving, Collision; M1-4d-1 implementation
paragraph: plan/apply split, walkers vs standing, groupmates, `Constrain`, arrival and anchors,
give-up rule); docs/05 M1 criterion 4; CLAUDE.md rules 2-5; BUG-0028, BUG-0031 (and BUG-0030's
"forward risk" note, covered by the anchor rule).

Tests required: `LocalMovementTests` additions for criteria 1-5 and 7; `Stress/` or `Serial`
tests for 8-9; re-tightened tests for 6; `StateHashTests` only if hashed state changes.

Constraints: no allocation in Plan/Apply (scratch arrays on `World`, cleared by loop, no LINQ or
closures); all unit-unit reads from start-of-tick state; neighbor order is ascending slot from
`QueryRadius`; shove sums accumulate in the sorted walk order only (that order is deterministic);
no `Math` trig, no wall clock; `MovementConstants` for any new tuning number (documented in
docs/03); one implement commit; report measured rates before asserting them.

QA focus: shove abuse (a walker column shoving a lone unit along a wall for 1,000 ticks: bounded
displacement, never into a blocked cell; two groups shoving each other's blobs back and forth:
terminates, no Idle jitter forever); anchor drift (shoved units with a kept `GoalCell` must be
linked to their point: run the 7 scenarios plus a blob crossed by 200 walkers); enemy and Moving
units never move without an order; reversed-spawn and two-sim hash equality under crossing crowds;
tight-blob and crossing-blob Perf at 500/1,000/2,500 with 0-byte ticks; mutation: remove the
blocked-cell refusal, the owner check, the anchor rule, and the `Constrain` cap, each must fail a
test; re-run the give-up rate rows and report the numbers against criterion 6.

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
