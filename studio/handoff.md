# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

_Written: 2026-10-04 (session 2026-10-04-2056, ACCEPT)._

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
