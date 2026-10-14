# BUG-0701: An AI think's cost grows with every live vision-blocking storm, and the AI perf gate turns them off

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-13-1616, task M5-4c (the developer's bug candidate; measured by QA) |
| System | fog / AI view (`FogStore.NextSeenUnit`), `Ai/AiPerfTests` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AThink_WithShippedSandstormsAlive_Measured" --logger "console;verbosity=detailed"`
   (`Stress/AbilityManagerFuzzStressTests`, Perf-tagged, reports only). Two Hard AI seats on a generated map with about
   2,400 units; N shipped Sandstorms (`blocksVision: true`) of a third player are alive while the AI seats' real thinks are
   timed (Debug).

## Expected
docs/03 gives an AI think 1 ms (`Ai/AiPerfTests`, 8 seats x 2,500 units). Since M5-4c the AI casts Sandstorm itself, so live
storms are a normal part of an AI match, and the gate should measure them.

## Actual
| Live storms | Think avg | Worst |
| --- | --- | --- |
| 0 | 0.264 ms | 0.284 ms |
| 8 | 0.497 ms | 0.735 ms |
| 24 | 0.926 ms | 1.039 ms |

That is linear, about 0.028 ms per live storm per think. While any zone blocks vision (`Zones.BlockerCount > 0`),
`FogStore.NextSeenUnit` stops using its inlined cell test and calls `CanSeeUnit` for every slot, which tests the unit
against every zone. `Ai/AiPerfTests` (M5-4c) loads the data with Sandstorm's `blocksVision` set to false, so the gate
never sees this cost. With the shipped data and every Priest casting at every think (the test clears cooldowns), the
developer measured about 3 ms per seat per think.

## Notes
- This is not over budget at realistic counts. Sandstorm has a 45 s cooldown and a 12 s duration, so about 27% of Priests
  have a storm up; 4 Whirlwind seats with 5 Priests each give about 5 storms. Hence S3, not S2.
- The cost is in the fog cursor (M5-1 / M4-4b-2), not in the ability manager. The ability manager is what now makes storms
  happen in AI-only matches.
- Suggested: keep the fast path and test only units near a blocking zone (e.g. a per-cell "near a blocker" mask like the
  one `FogStore.Update` builds), and give `Ai/AiPerfTests` a row with shipped storms (a fixed count such as 8, not
  cooldowns cleared every think).
