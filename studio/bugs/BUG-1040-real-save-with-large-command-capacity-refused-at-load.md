# BUG-1040: A real save with a command capacity over 65,536 saves fine and is then refused `Malformed` at load

| Field | Value |
| --- | --- |
| Severity | S2 (the brief's BUG-0990 item: "never refuses a real save of an empty world at large capacities"; a regression from a840d45) |
| Status | open |
| Found | 2026-10-26-2108, task M5-H4 (QA, full) |
| System | save / load: `Simulation.Load` pre-check, `SaveFormat.MaxCommandCapacity`, `SimConfig.Validate` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningM5H4QaTests.ARealSave_WithACommandCapacityAboveTheLoadBound_Loads"`
   with the row's `Skip` removed.
2. The row builds `new SimConfig(5, 2, 64, commands)` on the flat 16 map (no unit, nothing queued), ticks once, `TrySave`s
   (true, `SaveError.None`) and `TryLoad`s the bytes.

## Expected
A save the sim itself wrote for a config `SimConfig.Validate` accepts loads (`TrySave`'s contract: "TryLoad of the bytes gives a
sim whose StateHash equals this one's"; the brief: never refuse a real save at large capacities). At a840d45 all three rows
load.

## Actual
At 4f4a423: `commands` 65,537, 100,000 and 1,000,000 each `Expected: None, Actual: Malformed`. `SimConfig.Validate` accepts any
`CommandCapacity >= 1`, `TrySave` writes it, and the header's own range check accepts 1 to 1,000,000 (`InCapacity`), but the
new pre-check in `Simulation.Load` refuses `CommandCapacity > SaveFormat.MaxCommandCapacity` (65,536). The player gets a save
file that can never be loaded.

## Notes
- The shipped game uses 4,096, so no menu match hits it today; any tool, test or future config with a larger queue does.
- Fix options (the Producer's call): bound the same value where the config is made (`SimConfig.Validate` refuses
  `CommandCapacity > SaveFormat.MaxCommandCapacity`, so a sim that can't be loaded can't be made), or have `TrySave` refuse it
  (rule 36: the codes are frozen, so an existing one). Either way the save and the load must agree on one bound.
- Also (S4, same item): a header capacity outside its range is documented as `InvalidHeader` (docs/03 "Save format"), but
  this one capacity bound returns `Malformed`.
- Regression row: `QA/SimHardeningM5H4QaTests.ARealSave_WithACommandCapacityAboveTheLoadBound_Loads` (skipped with this id;
  un-skip with the fix).
