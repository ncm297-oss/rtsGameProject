# BUG-0791: `BuildOrderExecutor` repeats a failing site search every think once gold is affordable; the storms fuzz's measured think went 0.5 → 1.6 ms

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-16-1654, task M5-4f (the dev's paired Perf A/B; filed by the Producer) |
| System | AI: `Ai/BuildOrderExecutor` (the `build` step's site search), `Stress/AbilityManagerFuzzStressTests.AThink_WithShippedSandstormsAlive_Measured` |
| Fixed by | |

## Repro
1. On `studio/2026-10-16-1654-sim` (8ac7b43): `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AThink_WithShippedSandstormsAlive_Measured"`
   and read the printed think cost; compare with base 4d0c455 in a paired run (BUG-0764's rule).
2. Head 1.57-1.64 ms a think; base 0.47-0.62 ms. The row is measured, not gated (it prints), so the suite stays green.

## Expected
A `build` step that can't find a site waits cheaply: the executor's own rule for a *refused* Build gives the anchor a retry mark so
the search moves on; a search that finds nothing should back off the same way, not scan the placement radius every think.

## Actual
In the storms fuzz scene (two seats with ~1,170 army units each, a crowded map) the quicker `gatherRate` makes the next `build`
step affordable, and its site search fails at every think, costing ~1 ms. About 0.1 ms of the rise is the sized defense
(`DefenseManager.Pick` with every seat attacked every think); the gated rows stay inside their gates (`AiPerfTests` 0.77 / 0.77 ms
base / head, the 8-storm row 0.36 / 0.40 ms, `AiThinkCostQaTests` 0.30-0.38 ms, `AiWaveCostQaTests` 0.36 / 0.33 ms).

## Notes
Fix idea (a few lines, M5-H3 or the next executor task): when `PlayerView.CanPlaceAnywhere` / the site search finds nothing, mark
the step with a "no site until tick" (e.g. one `ai.waveCooldown`-sized wait, or `buildOrderSlack`) and skip it until then; a
`BuildOrderExecutor` test that counts the searches in a boxed-in scene. Not a gameplay bug: the AI still builds once a site is
free. Measured in the shipped data? No: the fuzz scene's crowd is synthetic; the menu-map rows didn't move.
