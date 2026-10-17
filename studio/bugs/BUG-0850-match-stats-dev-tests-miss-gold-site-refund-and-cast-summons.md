# BUG-0850: M6-S1 developer tests miss two exclusions: the gold line of a site refund, and summons from a real cast

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-17-0056, task M6-S1 |
| System | match stats tests (`Economy/MatchStatsTests`, `Stress/MatchStatsFuzzStressTests`) |
| Fixed by | |

## Repro
Mutation runs in a scratch copy of the branch (the production code in the worktree was not touched):

1. **Gold site refund.** In `ConstructionSystem.ApplyCancel`, change the gold refund line from `world.AddToTotal(...)` to
   `world.Ledger.Deliver(...)`, so a cancelled site's gold counts as gathered. The developer's whole M6-S1 suite
   (`MatchStatsTests` + `MatchStatsFuzzStressTests`) still passes. `Refunds_AndADevGrant_AreNotGathered` cancels a
   **House**, and a House costs wood only (`malazan_billet`: gold 0), so the gold line never runs. The fuzz injects no
   cancels, so it never sees a refund.
2. **Summons.** `AFinishedQueueItem_CountsAsTrained_ASummonAndADevSpawnDoNot` and the samples row make their "summon" with
   the store seam (`UnitStore.TrySpawn(..., expiresAtTick, ...)`). Neither goes through `SummonSystem.Summon` (a cast
   resolve). The code is correct today. A later change that counted summons in the cast path would only be caught by
   the new QA row.

## Expected
Brief criterion 1: "a test that the excluded path does not [count] (a delivery v a refund ...)". Every refund line should
be covered: queue cancel, raze, site cancel in both gold and wood, and collapse.

## Actual
The developer rows cover the queue refund (both resources) and the wood half of the site refund. The new QA tests cover
the rest. `QA/MatchStatsQaTests.ASiteCancelledAtZeroAndPartBuilt_RefundsGoldAndWood_NothingGatheredOrBuilt` (the Armory,
100 / 100) fails under mutant 1. `ASummonCast_IsNotTrained_CountsInArmyValueWhileItLives_AndItsExpiryCountsNothing` casts
`zz_test_summon_brief`. `Stress/MatchStatsQaFuzzStressTests` injects about 60-120 cancels per row, and the refund-as-gathered
mutant (`PlayerLedger.Refund` also counting) fails all 6 rows.

## Notes
This is a test-coverage gap, not a product bug: the shipped behaviour is right. It can be closed by pointing the
developer's refund row at a building that costs gold and wood, or by relying on the QA rows above.
