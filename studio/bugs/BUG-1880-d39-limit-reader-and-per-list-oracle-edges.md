# BUG-1880: D39 content checks: "N ... at once" is read as a unit limit; the per-list composition oracle throws on a list without the Age II research

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-11-26-1847, task D39 (QA) |
| System | content tests (`sim/Rts.Sim.Tests/Content/UnitLimitText.cs`, `sim/Rts.Sim.Tests/QA/Content/AiContentQaTests.cs`) |
| Fixed by | D-H11 (session 2026-11-27-0817, data branch e61b6920): `UnitLimitText` reads a number as a limit only when it counts the unit itself; `AiContentQaTests.CompositionProblems` returns a named line. Proof: `QA/Content/D39UnitLimitQaTests.ATargetCountAtOnce_IsNotAUnitLimit` and `AHardEntryWithoutTheAgeResearch_LoadsAndTheCompositionOracleDoesNotThrow` un-skipped, red on the base (QA re-checked both). What is left of the reader's misreadings is BUG-1920. |

Two small edges in D39's test helpers, grouped. Neither touches shipped data or play; both fail loudly, never silently.

## Repro
Both rows are in `sim/Rts.Sim.Tests/QA/Content/D39UnitLimitQaTests.cs`, skipped with this id. Remove the `Skip` and run
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D39UnitLimitQaTests"`.

1. `ATargetCountAtOnce_IsNotAUnitLimit`: a unit with no `maxAlive` (the War-dog stand-in, stripped) and the description
   "Strikes up to 3 foes at once." or "Throws at most 2 spears at once."
2. `AHardEntryWithoutTheAgeResearch_LoadsAndTheCompositionOracleDoesNotThrow`: Malazan opening 0 with a `buildOrders.hard`
   entry that copies `buildOrder` minus its Age II research. The loader takes it (no order rule).

## Expected
1. A count of targets or projectiles "at once" is not a limit on how many of the unit live at once; the check passes.
2. `AiContentQaTests.CompositionProblems(d, "malazan", 0, "buildOrders.hard")` returns a named problem ("... buildOrders.hard:
   no Age II research step") or treats every build step as Age I. It does not throw.

## Actual
1. `teblor_war_dog description maxAlive: 'up to 3' reads as the unit limit, data none` (and `'at most 2'`). `AliveWords`
   matches the bare `at once`, so any multi-target or volley sentence with limit words reads as a unit limit. A future unit
   description ("hurls up to 3 spears at once") would need rewording to get past the check.
2. `System.InvalidOperationException: Sequence contains no elements` at `AiContentQaTests.cs:144`
   (`order.Single(s => s.Action == Research && s.Target == age2)`). It was latent before D39 for `buildOrder`. D39 makes it
   reachable from any `buildOrders` entry, where a short Easy list that never ages up is a plausible shape.
   `Shipped_PassesEveryOracle` would then fail with the bare LINQ message and no list named.

## Notes
- 1: requiring the subject (a "can / may live", "alive" word, or the unit's own name or "of them") in place of the bare "at
  once", or dropping "at once" from `AliveWords`, would keep every current row green ("No more than 7 can live at once"
  matches `live`).
- 2: `SingleOrDefault` plus a problem line keeps the oracle's "names the list" contract.
