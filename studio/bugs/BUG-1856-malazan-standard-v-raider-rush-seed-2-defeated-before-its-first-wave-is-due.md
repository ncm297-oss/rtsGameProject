# BUG-1856: Malazan `standard` against `raider_rush` on seed 2 is defeated before its first wave is due (after M6-S14)

| Field | Value |
| --- | --- |
| Severity | S4 (a balance finding; a named allowance) |
| Status | open (for the data track's re-measure after M6-S14) |
| Found | 2026-11-26-1847, task M6-S14 (the pin move: `Content/AiOpeningPairsReportTests`, `Stress/ReservePairingsStressTests`) |
| System | data balance (Malazan `standard`'s wave, the menu map) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~NormalAiVersusAi_EveryOpeningPair_BothWave_NoDeadEndStep"`, row (0, 1).
2. `malazan: waves started 0 (first at never); Age II at never; defeated at 16:27; thinks defending 632`; peak army 14 pop.

## Expected / Actual
Both sides wave in every pairing on its smallest seed. Since M6-S14 this side never reaches its wave (pop and cost
together) while raided all match and falls at 16:27 (under M6-S12 / D33 it waved once and reached Age II at 13:07). Named in
`AiOpeningPairsReportTests.DefeatedBeforeDue` (reported, not failed) and counted in `ReservePairingsStressTests`'
no-wave sides (1 -> 2).
