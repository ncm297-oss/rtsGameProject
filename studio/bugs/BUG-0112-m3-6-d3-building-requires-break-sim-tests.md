# BUG-0112: With D3's building requires merged, 25 of M3-6's sim tests fail (merge hazard for the data track)

| Field | Value |
| --- | --- |
| Severity | S3 (merge blocker for D3: `main` goes red when the data branch merges on top of this one) |
| Status | open |
| Found | 2026-10-07-1415, task M3-6 |
| System | tests: construction fuzz, never-seal, requirement fuzz |
| Fixed by | |

## Repro
1. Scratch clone of `studio/2026-10-07-1415-sim` (8928418).
2. Copy `game/data/factions/{malazan,whirlwind}/buildings.json` from the data worktree (D3, 8bcca04: Shock Halls need
   the infantry hall; Caster Hall, Siege Works, Watch Tower need `age_ii`).
3. `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf` → 26 failed / 3123 passed.

## Expected
After the planned merge order (sim, view, data) the suite is green except the golden replay, which the data track
regenerates after its merge (handoff "regen order").

## Actual
Besides `ReplayGoldenTests.CrossMapSeed1_ReproducesEveryCheckpoint` (planned), 25 failures in sim-owned tests that
assume no shipped building has a requirement:
- `Stress/ConstructionFuzzStressTests.FiveHundredActions_OracleAgrees_InvariantsHold` seeds 2-8 (7) and
  `FiveHundredPlacements_EveryVerdictMatchesTheFloodOracle` seeds 1-8 (8): `seed 6 step 58: 8 at (19, 105): CanPlace
  Requires, oracle seals True`. The flood oracle compares `CanPlace` with geometry only; a gated type now answers
  `Requires` first.
- `NeverSealTests.FiveHundredRandomLegalPlacements_NeverLeaveAPocket` seeds 1-8 (8): `7 placed, 0 refused as sealing`
  (random types are now mostly refused for `Requires`, so the test's coverage floor fails).
- `RequirementFuzzTests.Twins_StayIdentical_SpendingBalances_AndFinishedCountsMatchARecount_EveryTick` seeds 2-3 (2):
  `the infantry requirement flipped only 1 times` (its fixture rewrites Malazan's requires but keeps Whirlwind's
  shipped ones, so the random stream and its coverage floor change). No invariant broke.

## Notes
- No product bug: every failure is a test oracle or coverage floor written for empty building requires. The data
  track can't fix them (sim-owned files), so either the sim fixes them before merge (load those tests' data with
  building `requires` cleared, or give the players Age II and every hall, or teach the oracle the `Requires` rule) or
  the conductor plans a sim fix right after the D3 merge.
- This branch's own new tests and the QA tests added this session pass with D3's files (121 / 1 skipped).
