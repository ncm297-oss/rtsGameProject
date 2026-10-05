# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans.

## Where we are

M0 Done. M1 is 5/8: sim core (M1-1), data loader (M1-2), terrain + nav grid + spatial hash
(M1-3/4a), flow fields + cache + build cap + local movement incl. shoving (M1-4b/c/d-1/d-2), and
the cross-map scenario (M1-5). Branch `studio/2026-10-05-1234` ACCEPTED: build 0/0; 1090 passed /
13 skipped / 1103 in one full run (2 m 6 s); smoke PASS. The conductor merges it to main. Open
bugs: 11 S3, 4 S4, none block. Sessions today: 3/10. Feature sessions since last hardening: 3/4,
so the session after the next one is a hardening session.

**First things at PLAN:**
1. `git log` main: confirm 98c92b5 (or the merge of it) is on main and the Producer's docs edits
   landed (docs/03 BUG-0037/0038 note, docs/01 change-log row, roadmap criterion 5 ticked).
2. Verify a claim: e.g. `LocalMovementTests.CrowdThroughAThreeCellGap_10Seeds_NoneGiveUp_AllArrive`
   fails if `byte act = progress ? ActWalk : queued ? ActQueued : ActStuck;` in `MovementSystem.Plan`
   is reverted to `progress ? ActWalk : ActStuck` (18 give-ups expected).
3. Inbox: no New notes at the time of writing. The owner changed the studio-session skill to a
   two-track (sim + view) model during session 1234; read the skill as it is now.

What the sim does now (`MovementSystem`, docs/03 "Local movement"): Moving units sorted by (goal
cell, slot); build pass (oldest order first, cap 2); **Plan** per unit from start-of-tick state:
aim, push, sidestep, `Constrain` against standing non-groupmates (friendly shovable Idle units
yield; *enemies holding ground are hard walls*, re-checked after the clips with a `ClosestAllowed`
fallback); arrival within 1 m or touching an arrived groupmate; no progress -> stuck (count up) or
*queued* (count holds, floor 1) when a groupmate just ahead made progress last tick; stuck 20 ticks
-> give up. **Apply** moves walkers; **ApplyShoves** moves shoved Idle units; `RecheckAnchors`.
Scratch on `World`: `ShoveStep/ShoveNeighbors/ShovedGoals/AnchorQueue/AnchorLinked/WallNormals/
WallLimits`, `MaxUnitSpeed`. No new hashed state since M1-4c.

## Next task candidates

1. **M1-6: replay format + determinism test + one golden replay (feature, QA full, design clear:
   up to 1,500 lines).** Roadmap criterion 6; docs/03 "Save/load and replays" (header: format
   version, game version, data hash, map id, seed, player setups; command log `(tick, player,
   command)`; periodic state hashes) and "Testing strategy" (golden replays in
   `sim/Rts.Sim.Tests/Replays/`, regenerate with a test flag, explain in the commit). Suggested
   shape: (a) **first, BUG-0014**: mix the seed into `SimRng` so `ulong.MaxValue` differs from 0
   (un-skip `MapQaTests.SeedMaxValue_AndSeedZero_GiveDifferentMaps`); this changes every map hash,
   so regenerate the 15 pinned hashes in `MapGeneratorTests` and the QA `PreBug0015*` oracle
   pins in the same commit and say why. (b) `Replay` type in `Rts.Sim` (plain .NET, no Godot): a
   recorder that captures accepted commands per tick and a `StateHash` every N ticks (N in
   `SimConfig` or a constant; 100 matches the scenario twins), a serializer to a small text/binary
   format with the header, a player that re-runs a replay into a fresh `Simulation` and compares
   checkpoints; refuse on data-hash mismatch (a stable hash of the loaded `GameData`). (c) Tests:
   round-trip (record -> write -> read -> replay equal hashes), determinism (same run twice ->
   same hash; different seeds differ), one golden replay checked in (a cross-map march, seed 1,
   ~1,500 ticks: small file) with a regenerate flag (env var or `[Trait]`), a corrupted/truncated
   file is rejected cleanly, the data-hash mismatch message comes from data if player-facing.
   Watch: the replay must be rebuildable from `Command` structs only (no closures, no handles that
   depend on allocation order outside the sim); `Command.SpawnUnit` is test-only today, decide
   whether spawns are commands in the log (they are commands, so yes). No allocation per tick in
   the recorder's hot path beyond a preallocated buffer (or document it as non-sim tooling if it
   runs outside `Simulation.Tick`). `StateHash` currently excludes `NavGrid.Version` (fine at M1).
2. M1-7 perf test (500 moving units < 4 ms, p99 < 8 ms; already measured at 0.14-0.6 ms; add the
   documented row + "maps > 256 unsupported" note for BUG-0023).
3. M1-8 CLI in `tools/` (headless scenario, prints hashes and timings; may reuse the replay player).
4. Hardening (after M1-6, it will be 4/4): M1-4d-3 crowd routing + BUG-0037/0038 (same code),
   BUG-0034 + the chaos-sensitive `MoreGoalsThanCacheSlots` row, BUG-0030, BUG-0025/0026, BUG-0005.
   Then M1 sign-off once M1-7/M1-8 are done (QA coverage for Replays row must be ✅ Unit /
   Invariant fuzz / Determinism first).

## Watch out for

- Budget: M1-5 ran ~2,300 changed lines against a < 150-line production target; production was
  ~165 lines (fix round included) and the rest tests, accepted. Keep production slices small.
- Movement code is chaos-sensitive: `MovementSystemTests.MoreGoalsThanCacheSlots_..._AtMost22PercentGiveUp`
  and the QA crowd rows shift under float-noise-only changes (a 0.1 mm margin moved 2,500->4 from
  1,105 to 1,121). Any task touching `MovementSystem` should expect to re-measure and must report
  every bound it re-sets.
- Determinism: all unit-unit interaction reads start-of-tick state; neighbor order ascending slot
  from `QueryRadius`; documented limit: reversed spawn order can differ at the last bit when 3+
  units touch. M1-6's golden replay pins all of this: any later movement change that alters
  trajectories will need a regenerated golden with an explanation.
- `MovementConstants`: `GiveUpTicks` 20, `PushAfterStuckTicks` 10, `StuckFraction` 0.25,
  `ArrivalSpacing` 0.6, `ShoveSpacing` 0.5, `AvoidRange` (queued reach). `ShoveRoundingMargin`
  1e-4 and `WallTolerance` 1e-5 are private tolerances in `MovementSystem`.
- Every Perf or allocation test in `SerialCollection`; allocation asserts via `AllocationProbe.AssertZero`.
  BUG-0034: the 10-tick distinct-targets perf row can flake under full-suite load.
- `StateHash` covers `State/Goal/GoalCell/OrderTick/StuckTicks/BestRemaining`, cache metadata and
  `Move`'s handle; `Speed/Radius`, spatial hash, scratch arrays and field contents are derived.
  `FlowFieldCache.Get`/`TryGetCached` move hashed LRU state: sim-only.
- Generator pins: 15 hashes in `MapGeneratorTests` and QA `PreBug0015*` oracles (all change with
  BUG-0014). `NavGrid.Flood` has no bounds checks (ring must stay blocked).
- `TestSim.Config(...)` is the one way tests build a `SimConfig`. `ArchitectureTests` and
  `SpatialHashTests.Source_UsesNoHashCollectionsOrLinq` grep sim source. `SimRng` is a mutable
  struct: always `ref world.Rng(stream)`. Soak rows (~100 s) are in the suite; a full run is ~2 min.
- One implement commit per session; ask for a report line after the first part, not a second commit.
