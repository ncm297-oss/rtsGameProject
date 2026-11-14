# BUG-1341: M7-7 worker carry, small findings (docs wording, a literal assumption in a test, an untested AI path)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-13-2306, task M7-7 (QA) |
| System | economy (`units.json` `carry`), docs/03, `Economy/WorkerCarryTests` |
| Fixed by | |

## Repro
Read the files named below.

## Expected / Actual
1. **docs/03 "Implementation (M7-7)", "Hash and formats" bullet:** "the cost is one nullable read per gather step".
   `UnitDef.Carry` is a plain `int` (0 = absent) since the developer's own fix for
   `DataLoaderQaTests.GameDataTypes_ExposeOnlyImmutableMembers`; `GameData.CarryOf` is two array reads and a compare.
   The sentence describes the first version.
2. **`WorkerCarryTests.AGatherOrder_ToAWorkerHoldingItsOwnFullLoad_ReturnsIt`** writes a load of 12 with the comment
   "above the rules' 10, below its own 15". It assumes `rules.json` `workerCarry` is below 12: at a `workerCarry` of 12
   to 14 the row still passes but no longer tests "above the rules' value, below its own"; at 15 or more it tests
   nothing it names. Derive the load from `Rules.WorkerCarry` (QA's `WorkerCarryQaTests` picks its values around the
   loaded one).
3. **`EconomyManager.FullLoad` has no focused test** (the developer says so). QA did not add one either: the method is
   private and reached only through `NoteDroppedGather`. The AI matches with carries 1 / 7 / 15 / 40
   (`Stress/WorkerCarryQaStressTests`) run through it without a broken invariant.

## Notes
- For the data track, not the sim's: `docs/factions/teblor.md`'s Gatherer sentence ("not in the data: `rules.json`
  `workerCarry` is one number for every faction") is now out of date about the schema. The developer flagged it in
  docs/03's "For the data track (M7-7)".
