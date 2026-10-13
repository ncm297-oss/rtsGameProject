# BUG-0641: D-H4 nits: the house-rule report's list of internal reads is incomplete, and two inputs are re-derived beside the AI

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-13-0146, task D-H4 |
| System | QA tests (AI content) |
| Fixed by | D16 `8f3d1e5` |

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

## Update (2026-10-13-0527, QA, D16)
**Fixed** in D16 `8f3d1e5`. Item 1: the class comment names `Simulation.AiOf`, `AiPlayer.Board / ThinkTicks / HouseType`, the four
`Blackboard` fields, `PendingOrder.Kind / AppliesAt / Cell` and `PendingKind.Build`. Item 2: `thinkTicks` and `house` are read
from `ai.ThinkTicks` / `ai.HouseType` (both `internal` on `AiPlayer`), with an assert that the house type is the faction's House.
Item 3: the comment and `Workers` summary say the column is a live count, not `AiPlayer.WorkerCount`. Item 4: BUG-0610 already
reads `fixed` here and in the README. `AiHouseRuleReportTests` 3/3 green in the full non-Perf run.
