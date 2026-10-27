# BUG-1040: A real save with a command capacity over 65,536 saves fine and is then refused `Malformed` at load

| Field | Value |
| --- | --- |
| Severity | S2 (the brief's BUG-0990 item: "never refuses a real save of an empty world at large capacities"; a regression from a840d45) |
| Status | fixed |
| Found | 2026-10-26-2108, task M5-H4 (QA, full) |
| System | save / load: `Simulation.Load` pre-check, `SaveFormat.MaxCommandCapacity`, `SimConfig.Validate` |
| Fixed by | M5-H4 fix round 1 (session 2026-10-26-2108): the config is rejected up front |

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

## Fix (M5-H4 fix round 1)
- Option picked: one bound, the config's. `SimConfig.MaxCommandCapacity` = 65,536 (public const); `SimConfig.Validate`
  throws `ArgumentOutOfRangeException` over it, so a sim whose save can't load can't be made. `SaveFormat.MaxCommandCapacity`
  is that constant. Why: the body can't bound it (an empty queue at 1,000,000 slots writes the same bytes as a forged
  header, so a load that took real ones would allocate ~52 MB for forged ones), and a `TrySave` refusal would leave a sim
  that can never be saved. No signature or `SaveError` code added (standing rule 36).
- The S4 note: the header now range-checks the command capacity (1 to the bound) with the other capacities, so a forged one
  is `InvalidHeader` (as docs/03 says for a capacity outside its range), before the heightmap; the `Malformed` clause in
  `Simulation.Load`'s pre-check is gone.
- Replays share the bound: `Replay.Validate` refuses a command capacity over `SimConfig.MaxCommandCapacity` (`InvalidHeader`),
  so a replay that reads also builds its sim (`ReplayPlayer.Run` and the game's `SimRunner` would otherwise throw from
  `Validate`). No recorder can write one over it.
- The CLI (`tools/Rts.Cli/CliRunner.cs`) sized its queue to `--units` (up to 100,000); it now caps it at the bound (a block
  on the 128 x 128 map holds far fewer spawns). `QA/CliQaTests.Run_UnitsAboveWhatTheBlockHolds_SpawnsWhatFits_AndExits0`
  failed on the bound alone and passes with the cap.
- Tests: `QA/SimHardeningM5H4QaTests.ARealSave_WithACommandCapacityAboveTheLoadBound_Loads` un-skipped and restated (the
  config over the bound is refused by `Validate` / the `Simulation` constructor; at the bound the sim's own save loads);
  `Save/SaveRefusalTests.AForgedCommandCapacity_IsBoundedByTheFormat` (over the bound now `InvalidHeader`, `int.MaxValue` row
  added); `SaveRefusalTests.Serial.AForgedCapacity_AloneOrTogether_IsRefused_UnderAMegabyte` (rows with the command capacity
  expect `InvalidHeader`, new rows "0,2,3,4,5" human and AI keep the pre-check's `Malformed`; all under 1 MB);
  `SaveRefusalTests.Serial.AForgedMaxCapacityHeader_IsMalformed_BeforeTheWorldIsAllocated` (command capacity forged to its
  bound so the row still reaches the pre-check); `QA/ReplayQaTests.HugeDeclaredCapacities_AtTheFormatLimit_ReadButOverItRefused`
  (command capacity reads at 65,536, refused at 65,537 and 1,000,000).
