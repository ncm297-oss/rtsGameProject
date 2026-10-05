# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-04 (session 2026-10-04-2056, ACCEPT). Current session plan added 2026-10-05-0742._

## Current session plan (2026-10-05-0742): M1-4d-1 — separation, crowded arrival, stuck give-up, adjacent-cell steering

Checks: inbox empty; main @ 5f960c1 clean, build 0/0, `Category!=Perf` 849 passed / 6 skipped;
verified M1-4c claims (`MaxFieldBuildsPerTick = 2`, `Get`/`TryGetCached` internal, `World` uses
`CapacityFor`); no docs drift; open S1/S2: none; sessions today 0/10 (first of the day).

**Goal.** Units heading to one point stop stacking on it and stop walking through each other:
soft unit-unit separation, arrival for a crowd (not just for the one unit that reaches the exact
point), a give-up rule for units that can't make progress, and direct steering when the goal is one
cell away. Second half of roadmap criterion 4; shoving + BUG-0005 are M1-4d-2.

**Scope (IN).**
- Separation: per Moving unit, one `Spatial.QueryRadius` (radius = own `Radius` + max data radius,
  1.0 m, from `GameData`, not a literal) into a preallocated `World` scratch buffer; sum a push away
  from every overlapping live unit, weighted by overlap depth; add to the flow/goal desired velocity;
  clamp the step to `Speed`. Separation reads start-of-tick positions (two passes: compute all steps,
  then apply) so the force is symmetric and doesn't depend on walk order.
- Crowded arrival (Producer decision, owner may revisit): a Moving unit arrives when it is within
  `ArrivalDistance` of its goal inside the goal cell (today's rule), OR when it overlaps an Idle unit
  that has the same `GoalCell` and itself arrived there (`State == Idle`, same `GoalCell`), so a group
  packs into a blob around the point instead of fighting over it. This replaces "target offsets in a
  loose formation" in docs/03 "Local movement" for M1; formation offsets can return at M2 with group
  commands.
- Stuck give-up: a new hashed per-unit counter (`StuckTicks` or similar) counts consecutive ticks a
  Moving unit moved less than a fraction of its speed; past `MovementConstants.GiveUpTicks` (~1 s = 20
  ticks) it goes Idle. Reset by `Move` and by progress.
- Adjacent-cell steering: when the goal cell is a legal 8-neighbor step from the current cell (no
  corner cutting: both side cells passable), aim straight at the goal instead of the neighbor center.
- Blocked-cell refusal stays: a combined step whose destination cell is blocked is refused (try the
  flow step alone before giving up on the tick, so separation can't pin a unit forever).
- Docs/03 "Local movement" implementation paragraph, StateHash paragraph, MovementConstants remarks.

**Scope (OUT).** Shoving of idle units, `Stop`/`Hold`, BUG-0005 command buckets, BUG-0025/0026,
formation offsets, flying units, path smoothing beyond the adjacent-cell case, any `game/` change.

**Acceptance criteria.**
1. 50 units ordered to one open point all go Idle within 600 ticks, no two with centers closer than
   0.5 × (r_i + r_j) at the end (loose pack), none in a blocked cell, none off the map.
2. 200 units across the default map (extend `TwoHundredUnits_OneMove_...`) still all arrive within the
   current limit and are never on blocked ground.
3. Two units walking head-on past each other in a 1-cell corridor never overlap by more than 50% of
   the smaller radius, and both arrive.
4. A unit whose every step is refused (walled in by idle units of another goal) goes Idle after
   `GiveUpTicks`; a unit making progress never does; a new `Move` resets the counter.
5. Adjacent-cell steering: a unit one diagonal cell from its goal with the corner open heads straight
   at it; with the corner blocked it still takes the legal route (regression for BUG-0020 stays green).
6. Determinism: two sims, same seed + commands, hash-equal every tick with 300 units in 4 crowded
   groups for 1,000 ticks (extend `Determinism_TwoSims_...`). New per-unit state is in `StateHash`
   (`StateHashTests` cover it).
7. Zero allocation per tick with 500 moving units in one blob (`AllocationProbe.AssertZero`, in
   `SerialCollection`).
8. Perf (Serial, `Category=Perf`): 500 units converging on one point, average tick < 4 ms, worst < 8 ms;
   report 1,000 and 2,500.
9. `FieldBuildOrderTests` and `QA/FieldBuildFairnessQaTests` unchanged and green.
10. docs/03 updated in the same commit; no data stat in C# (radius/speed from data; new
    `MovementConstants` are geometry/tuning and documented).

**Design references.** docs/03 "Local movement", "Spatial hash" (`QueryRadius` contract: ascending
slots, count may exceed buffer), "Determinism", "State hash", tick phases 8-9; docs/02 "Unit collision
radius is 0.4-1.0 m" (one size class).

**Constraints at risk.** No per-tick allocation (scratch buffers on `World`, sized from
`UnitCapacity`); no `Math.Atan2`/`Sin` (use `SimMath`); no LINQ; `Array.Sort` only on preallocated
buffers; every Perf/alloc test in `SerialCollection`. Keep (goal cell, slot) order for the build pass.
Budget: ~800 changed lines; if separation + arrival alone fill it, drop adjacent-cell steering to 4d-2
and say so in the report.

## Where we are

M0 Done. M1 is 3/8 plus most of criterion 4: sim core (M1-1), data loader (M1-2), terrain + nav
grid (M1-3), ramp walls + spatial hash (M1-4a), flow fields + LRU cache + `Move` (M1-4b), hashed
cache metadata + oldest-order-first build cap of 2 + `CapacityFor` (M1-4c). Branch
`studio/2026-10-04-2056`: build 0/0; 905 passed / 9 skipped / 914 in one process (~1m50s, green
in 10 of 10 runs across dev/QA/Producer); smoke PASS. The conductor merges it to main. Open bugs:
5 S3, 3 S4, none block. Sessions today: 2/10.

**The suite is reliable again in one process** (BUG-0024/0017 fixed): `dotnet test
sim/Rts.Sim.Tests` is the whole check. Quick loop: `--filter "Category!=Perf&Category!=Soak"`.

What exists in the sim: `World(config)` owns `Heightmap`, `NavGrid`, `Spatial`, `FlowFields`
(`FlowFieldCache`, `CapacityFor(units, cells)` fields = 32..128, LRU, version-tagged, hashed
metadata, `Get`/`TryGetCached` internal), `MoveOrder` + `FieldMisses` scratch, `Data`. Units:
`Position/PrevPosition/Velocity/Facing/Owner/TypeId/Speed/Radius/State/Goal/GoalCell/OrderTick`.
Commands: `Noop`, `SpawnUnit`, `Move` (stamps `OrderTick`). Tick: commands -> spatial rebuild ->
`MovementSystem.Run`: sort Moving units by (goal cell, slot); `BuildMissingFields` touches cached
fields, sorts misses by (oldest OrderTick, goal cell), builds at most 2; walk reads cached fields
only; waiting units stay Moving at velocity 0; arrival only inside the goal cell within
`CellSize/2`; steps into blocked cells refused. No separation, shoving, arrival slots, Stop/Hold.

## Next task candidates

1. **M1-4d: steering, separation, arrival slots, shoving + BUG-0005** (docs/03 "Local movement").
   Separation via `Spatial.QueryRadius` with data radii; arrival slots around the target so a
   group doesn't stack on one point; shoving of idle units; per-player command buckets (BUG-0005,
   O(n^2) insertion sort). Closes roadmap criterion 4. Measure query cost first: one query per unit
   per tick at 500/1,000/2,500 units against the 4 ms average budget (crowded-bucket queries are
   sort-dominated). Split if over ~800 lines: 4d-1 separation + arrival slots, 4d-2 shoving +
   BUG-0005. Keep the (goal cell, slot) update order; separation makes units interact, so the
   determinism tests matter more now. Any change to who-moves-when must keep
   `FieldBuildOrderTests` and `QA/FieldBuildFairnessQaTests` green.
2. BUG-0025 (S3) + BUG-0026 (S4) follow-up, one small task: when every cache slot holds a live
   field, evict the one whose group has the newest order (not the lowest cell), and rotate the
   same-tick tie-break by tick. Un-skip `LiveGoalsOneOverCapacity_OlderOrdersNeverWait...`. Must
   land before M5.
3. M1-5 scenario test (200 units across 128x128 with obstacles). Needs 4d.
4. M1-6 replay + determinism golden: first commit fixes BUG-0014 (mix the seed in `SimRng`).
5. M1-7 perf test: also document that maps above 256 are unsupported (BUG-0023). M1-8 CLI.

## Watch out for

- Budget discipline: 4b ran ~690 production lines; 4c held at ~170. 4d is the biggest movement
  slice left; plan only the first half if the brief grows past ~800 changed lines.
- One implement commit per session (session skill rule); ask for a report line after the first
  part, not a second commit.
- Every Perf or allocation-measuring test must be in `SerialCollection` (`SerialCollectionTests`
  enforces it); allocation asserts go through `AllocationProbe.AssertZero`. A re-run only proves
  something when `setup` repeats the same work.
- `StateHash` covers `State/Goal/GoalCell/OrderTick`, the cache metadata, and `Move`'s handle;
  `Speed/Radius`, the spatial hash, `MoveOrder`/`FieldMisses` and field contents are derived.
  `NavGrid.Version` is not hashed (fine until passability can change, M3).
- `FlowFieldCache.Get`/`TryGetCached` move hashed LRU state: sim-only, never from views/AI/tests
  that compare hashes across sims unless both sides do the same calls.
- Generator pins: 15 hashes in `MapGeneratorTests.Generate_MatchesMapsFromBeforeTheFastRampChecks`
  and QA `PreBug0015*` oracles. `NavGrid.Flood` has no bounds checks (ring must stay blocked).
- `FlowField.NearestPassable` ring search: exit test is `r*r <= bestD2` on purpose. Don't "simplify".
- `CellQueue` is a 4-bucket Dial queue: correct only while every step costs >= 1 and the live cost
  span is < 3 whole numbers. Nav cost bytes (M3 forests) must not break that.
- `TestSim.Config(...)` is the one way tests build a `SimConfig` (shipped data, loaded once).
- `ArchitectureTests.SimSource_UsesNoForbiddenApis` and `SpatialHashTests.Source_UsesNoHash
  CollectionsOrLinq` (`Spatial/`, `Pathfinding/`, `Movement/`) grep sim source.
- `SimRng` is a mutable struct: always `ref world.Rng(stream)`.
- Soak rows (`Category=Soak`, ~100 s) are in the suite.
- .sln Release config builds the game project in Debug (BUG-0002, S4): irrelevant until M6.
