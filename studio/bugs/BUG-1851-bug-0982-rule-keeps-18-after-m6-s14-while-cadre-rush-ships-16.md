# BUG-1851: After M6-S14 the BUG-0982 rule keeps 18 pop while `cadre_rush` ships 16 (seed 8 now falls at 16 pop)

| Field | Value |
| --- | --- |
| Severity | S3 (a balance claim on malazan.md and two pins no longer hold; no rule is broken) |
| Status | open (for the data track's re-measure after M6-S14; the sim may not tune data, rule 48) |
| Found | 2026-11-26-1847, task M6-S14 (the pin move) |
| System | data balance: Malazan `cadre_rush` `attack.minArmyPop`; tests `Content/AiOpeningPairsReportTests.Bug0982_*`, `QA/Content/DR35ProsePinQaTests.Malazan_Bug0982Prose_*`, `QA/Content/DH6ThresholdQaTests.Bug0982_KeepEighteen_*` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Bug0982"` (the two skipped rows: remove the Skip first).
2. The report (malazan.md "Fast Age II against the raider rush", re-printed by M6-S14): at 16 pop seed 8 sends one wave (7:02)
   and is destroyed at 17:49; at 14 pop seed 8 is destroyed at 16:21; at 18 pop seeds 7 and 8 stand. The rule picks 18.

## Expected
The re-stated rule (D33, BUG-1524) picks the shipped value, or the data track re-measures and decides.

## Actual
"BUG-0982's rule now picks 18 pop but cadre_rush ships 16". M6-S14 skipped the report's pick row and the page prose row with
this id (both handed to it, rule 68) and re-printed the table; `QA/Content/DH6ThresholdQaTests.Bug0982_KeepEighteen_HoldsOnTwoMoreSeeds`
(not handed) is red in the Stress category ("Expected 36, Actual 32").

## Notes
- The sim's M6-S14 changed AI play (the worker floor, the hunt's resume, the rally point's memory); no number was tuned.
