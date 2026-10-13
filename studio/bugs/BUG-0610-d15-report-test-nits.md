# BUG-0610: D15 small findings: the house-rule report asserts more than "no exception" and is wired to AI internals M5-3 is reworking

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-12-1930, task D15 |
| System | QA tests (AI content) |
| Fixed by | |

## Repro
1. Read `sim/Rts.Sim.Tests/QA/Content/AiHouseRuleReportTests.cs`.

## Expected
The brief: the report test "asserts only no exception", and no D15 row should depend on what sim M5-3 moves this session.

## Actual
1. Line 53 asserts `Assert.Equal(player, ai.Faction)`: small, but it is an assertion beyond "the run finishes".
2. The report reads `AiPlayer` internals directly: `ai.Board.StepPending[i]`, `ai.Board.HousePending`, `ai.Board.StepDueTick`,
   `ai.ThinkTicks` (the faction ai.json difficulty knob), `ai.HouseType`, `PendingOrder.AppliesAt/Cell`.
   `AiContentQaTests.E` does the same (`Board.StepDueTick`, `Board.StepDone`). If M5-3 renames or reshapes the blackboard,
   the whole test project stops compiling, not just the report. The report's
   numbers stay correct only while those fields mean what they mean today.
3. The class comment hard-codes the measured numbers ("pop stalls at 82 / 98", "12 trees of 100 a base", "the 12th
   Billet (step 18) stays due"). Nothing checks them, so they go stale silently after the next AI or map change.

## Notes
None of this is wrong today: all three report rows pass and print (Malazan 36,000 ticks: 11 order houses, 0 by the rule,
cap 98, step 18 due 733 thinks, never done). Suggest: drop the faction assert, and date the comment's numbers as
"measured 2026-10-12" or move them to the session report.
