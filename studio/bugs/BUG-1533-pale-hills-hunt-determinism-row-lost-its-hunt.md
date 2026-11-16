# BUG-1533: Stress rows `AiDeterminismTests.PaleHills_ToTheEndWithAHunt_*` and `HuntStressTests.PaleHills_SavedMidHunt_*` red, not named in the M6-SH2 report: Pale Hills seed 1 now ends with no hunt

| Field | Value |
| --- | --- |
| Severity | S2 (Stress red outside the rows M6-SH2's criterion 11 allows; the developer's report says "Stress is red only in rows named here"; the two-process determinism proof of a match with a hunt no longer has a hunt) |
| Status | open |
| Found | 2026-11-15-2136, task M6-SH2 (QA) |
| System | AI end game (hunt) / test premise; moved by the drop-off rule (BUG-1520) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiDeterminismTests.PaleHills_ToTheEndWithAHunt"`
2. Fails at line 173: `no hunt started` (after about 4 minutes; the child process is never started).

## Expected
The row (M5-4d criterion 6) plays a Normal v Normal Pale Hills match that ends **after a hunt** and proves the per-tick
hash fold, end tick and hunt count equal in two processes.

## Actual
Measured by QA on three trees:

| Tree | Result |
| --- | --- |
| base `fdc8710` | green: `DA9AD77B564693ED D65E8790680C5062 42384 3` (ends at 42,384, 3 hunts) |
| base + item 0 only (the drop-off rule, no retrain) | red: `no hunt started` |
| M6-SH2 head | red: `no hunt started` |

Pale Hills seed 1 now ends at 27,498 with no hunt (`MatchRowsM6S10Report`, both trees), so the row's premise is gone.
Not a determinism break: nothing differs between processes; the row stops before comparing.

## Same cause, a second row
`Stress/HuntStressTests.PaleHills_SavedMidHunt_FollowsTheRunToTheEnd` (`MidHuntSeed`, also Pale Hills seed 1) fails on the
head with `no hunt to save in` (line 235). So the save-taken-mid-hunt proof is gone too. Both rows need a Pale Hills seed
that hunts on this tree.

## Notes
- The developer re-staged the sibling row `StarvedSaveTwoProcessQaStressTests` off Pale Hills seed 1 for the same reason
  (it ends before any stamp) but did not see this one. The row's own history (seed 3 -> 5 -> 1) shows the fix: move
  `HuntSeed` to a Pale Hills seed that ends after a hunt on this tree, with the reason in its comment (rules 43 / 45).
- Hunt-path determinism across processes is otherwise covered only by `MatchCriterionTests.EveryRow_InTwoProcesses_*`
  rows whose matches happen to hunt.
