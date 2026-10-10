# BUG-0391: M4-H2 nits: a stale `GoneButRemembered` summary, two hash-test rows on one line

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-0624, task M4-H2 |
| System | combat (ghost orders), tests |
| Fixed by | |

## Repro / Actual
1. **Stale summary.** `CombatSystem.GoneButRemembered`'s `<summary>` still says "Once the ground is in sight (its own sight
   now, or a footprint cell visible in its owner's fog) the player knows". Since BUG-0311, only the fog's footprint cell
   counts (`VisionSystem.UnitSeesFootprint` is now `Fog.SeesFootprint`). docs/03 and `UnitSeesFootprint`'s own summary
   were updated, but this one was not.
2. **Two rows on one line.** In `StateHashTests` (the ghost-field mutation table, around line 1497), the `"Owner"` entry and
   the `"the slot"` entry are now on one line: `... with { Owner = 0 }) },        new object[] { "the slot", ...`. This is
   an edit artifact. It compiles and runs, but it reads as one row.

## Expected
1. The summary states the fog rule only.
2. One `new object[]` row per line, as in the rest of the table.

## Notes
Neither affects behaviour or the suite.
