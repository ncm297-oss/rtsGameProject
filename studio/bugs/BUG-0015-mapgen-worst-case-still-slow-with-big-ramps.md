# BUG-0015: Worst valid map params still take ~45 s (Debug) / ~7 s (Release): ramp size and map size are not in the time bound

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-03-2220, task M1-4a |
| System | terrain / map generator |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.RampWallQaTests.WorstValidParams_1024Map_AtCaps_UnderFiveSeconds`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~WorstValidParams_1024Map"`

Params (all pass `Validate`): 1024 x 1024, `Level1Plateaus = Level2Plateaus = 32`,
`RampsPerPlateau = 16`, `RampTries = 128`, `MaxAttempts = 16`, `MinPassableFraction = 1`, plus
`RampWidth = 50`, `RampLength = 100`, `Level1MinSize = 52`, `Level1MaxSize = 200`,
`Level2MinSize = 52`, `Level2MaxSize = 150` (the `bigRamps: true` case).

## Expected
The M1-4a brief, BUG-0013 item: "worst valid case ~2 s". QA focus: "time the worst valid param set
at the new caps (expect ~2 s, fail above 5 s)". docs/03 now says "the worst valid 512 × 512 case
takes under a second".

## Actual
Measured with a scratch console app (generation + NavGrid) over seeds 1, 11 and 99:

| Params (all at the new caps, 1024 x 1024) | Debug (how the tests run) | Release |
| --- | --- | --- |
| default 3 x 4 ramps, Level1MaxSize 400 | 4.3-4.8 s | 0.36-0.41 s |
| ramps 20 x 300 | 12.8-13.9 s | 2.0-2.4 s |
| ramps 100 x 200 | 23-31 s | 2.2-4.7 s |
| ramps 200 x 100 | 31-36 s | 4.5-5.5 s |
| ramps 50 x 100, Level1 52-200 | 42.6-48.2 s | 6.4-7.4 s |

The worst case is now *longer* than BUG-0013's original 35 s. Even default-size ramps at 1024 x 1024
take about 4.8 s in Debug, well past "~2 s".

## Notes
The fix capped the counts (plateaus, ramps, tries, attempts). Each ramp try still costs
O(RampWidth x RampLength) footprint checks, and the BUG-0012 fix lets `RampWidth`/`RampLength` go
up to the map side (1024). Two possible fixes: cap `RampWidth`/`RampLength` at sensible sizes
(docs/02 ramps are chokepoints a few cells wide), or put an overall cell-check budget on placement.
The docs/03 sentence should state the bound the code actually guarantees.

Impact is low. No shipped config comes close; this only matters if params ever come from data. It
is filed as S2 only because the brief made "~2 s" (fail above 5 s) an acceptance criterion. The
Producer may downgrade it.
