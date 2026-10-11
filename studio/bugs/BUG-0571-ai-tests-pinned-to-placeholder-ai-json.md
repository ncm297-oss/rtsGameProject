# BUG-0571: Ten M5-2 AiPlayerTests depend on the one-step placeholder ai.json and fail once D14's real build orders merge

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-11-0634, task M5-2 |
| System | sim tests (`Ai/AiPlayerTests`) |
| Fixed by | |

## Repro
1. In a scratch clone: `git clone --branch studio/2026-10-11-0634-sim <repo> x`, then
   `git merge origin/studio/2026-10-11-0634-data` (merges clean: D14 4b0c496, both `ai.json` files with real build orders).
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiPlayerTests"`

## Expected
The brief: the data track (D14) writes real `ai.json` build orders this session, and integration is sim -> view -> data. The
sim's tests must hold with any valid shipped `ai.json`, or pin their own build order through `AiScenes.DataWithAi` (as
`TenStepBuildOrder` and the refused-Build tests already do).

## Actual
10 failures on the merge (all green on each branch alone):
```
ShippedSteps_Run_BothFactions(Easy / Normal / Hard)   Assert.Single() Failure: The collection contained 2 matching items
HouseRule_OneHouseWhenNearTheCap_NoneWhileOneIsBuilding  Assert.Single() Failure: The collection contained 2 items
HouseRule_AtTheRulesCap_PlacesNone                    Assert.DoesNotContain() Failure: Filter matched in collection
CompositionFill_TrainsTheMostUnderWeightedUnit (x5)   Assert.Single() Failure: The collection did not contain any matching items
```
`ShippedSteps_Run_BothFactions` asserts exactly one Train and `StepDone[0]` (D14's step 0 is `train laborer, count 15`). The
house and composition tests run the shipped order next to what they measure: D14's opening steps send houses (Billets) and
Trains, so "one house" sees two and "the first think the executor sent nothing" never comes.

## Notes
Not a product bug, but it turns `main` red at the data merge this session. Fix in the sim tests: give the house and
composition scenes a quiet build order via `AiScenes.DataWithAi` (one step that can't fire), and make the shipped-steps test
assert the shipped content's first command (step 0's kind and target, the step's count respected) instead of "exactly one
Train". Separately, 5 older loader tests fail on the data branch alone (`AiLoaderQaTests` x3, `AiLoaderTests.BothShippedFactions_...`,
`DataValidationTests.BrokenAiJson_...duplicate step`): their find/replace strings target the placeholder file; that is the
data track's QA to report.
