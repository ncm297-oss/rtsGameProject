# BUG-0641: D-H4 nits: the house-rule report's list of internal reads is incomplete, and two inputs are re-derived beside the AI

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-13-0146, task D-H4 |
| System | QA tests (AI content) |
| Fixed by | |

## Repro
1. Read `sim/Rts.Sim.Tests/QA/Content/AiHouseRuleReportTests.cs`.

## Actual
1. The class comment says the file reads four `Blackboard` fields and "everything else comes from GameData or the world". It
   also reads the internal `Simulation.AiOf`, `AiPlayer.Board`, `PendingOrder.Kind / AppliesAt / Cell` and `PendingKind.Build`
   (all `internal`, reached through `InternalsVisibleTo`). A rename of any of those breaks the file at compile time too.
2. `thinkTicks` (`Math.Max(1, def.Difficulty(Normal).ThinkTicks)`) and `house` (`FindIndex` by faction + House slot) copy how
   `AiPlayer` derives `ThinkTicks` and `HouseType`. Today they agree (the run's due/think counts match the developer's), but if
   the sim changes either derivation the report silently samples the wrong ticks or the wrong building instead of failing.
   `house` is also the list index, which equals `BuildingDef.Id` only while ids are assigned in load order.
3. The "workers" column changed meaning: it was `AiPlayer.WorkerCount` (as of the last think), and is now a live count at the
   sample tick. Same numbers on this run; worth a word in the comment.
4. Criterion 1 says "BUG-0610 fixed (statuses set)", but `BUG-0610` and the README index still say `open`. The brief's file list
   kept the developer out of `studio/bugs/`, so the Producer or conductor has to set it.

## Notes
None of this changes a number today. Suggest: name every internal the file reaches in the comment, and either read
`ThinkTicks` / `HouseType` from the AI (they are already reachable the same way as `Board`) or assert the copies equal them once.
