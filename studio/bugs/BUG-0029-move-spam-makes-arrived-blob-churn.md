# BUG-0029: Re-issuing the same Move every tick makes an arrived blob churn indefinitely

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-05-0742, task M1-4d-1 |
| System | movement (crowded arrival) |
| Fixed by | M1-4d-1 fix round 1, commit 878fb62 (`Simulation.ApplyMove` same-goal-cell rule; QA re-check: 0/20 Moving, 0 m walked under spam); LocalMovementTests.ArrivedBlob_ReorderedToTheSamePointEveryTick_StaysIdleAndStill, LocalMovementTests.SameMoveSpammedEveryTick_ToAWalledInUnit_StillGivesUpOnTime, QA/MoveQaTests.Move_SpammedEveryTick_... restored |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SettledBlob_ReorderedToSamePointEveryTick" --logger "console;verbosity=detailed"`

## Expected
A group that already arrived, re-ordered to the same point, settles again at once (the pre-4d QA
test asserted "re-ordering an arrived unit re-arms Moving; it must settle again within a tick").
Click spam and AI order refreshes are normal.

## Actual
```
spam ticks 100-200: up to 15/20 Moving (at the end 15), 134.9 m walked in total by a blob that had already arrived
```
Every Move re-arms all 20 units, so there is no Idle groupmate to arrive against; only units within
1 m of the point can stop, the rest walk and shove inward each tick and never give up (each Move
resets the stuck counter). The developer changed `QA/MoveQaTests.Move_SpammedEveryTick_...` to
check settling only after the spam stops, which hides this.

## Notes
Possible direction: a Move to the unit's current goal cell/point while it is Idle and arrived
keeps it Idle, or crowded arrival also counts Moving groupmates already standing in the blob.

## QA verification (2026-10-05-0742 re-check round 1)
Verified at 878fb62: `SettledBlob_ReorderedToSamePointEveryTick_Churn_Report` now prints "up to
0/20 Moving, 0.0 m walked" (was 15/20, 134.9 m). A settled 500-blob given the same Move every tick
allocates 0 bytes. Side effect of the cell-granular rule: BUG-0030.
