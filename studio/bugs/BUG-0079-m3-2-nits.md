# BUG-0079: M3-2 nits (missing cost is three errors, tests over budget, --workers 0 wording)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-06-1503, task M3-2 |
| System | data loader, CLI docs, studio budget |
| Fixed by | item 1: b4b423c (M3-3), `DataLoader.BuildBuildings` reports a missing `cost` once; QA `BuildingDataQaTests.MissingCostObject_IsOneError_LikeAMissingFootprint` un-skipped. Item 2 (budget note) waived by the Producer. Item 3 in M3-H1 (4abbf37, session 2026-10-07-0800): docs/03 "CLI" now reads "N above 0 needs `--forests` or `--mines` above 0" |

## Repro / Actual
1. **Missing `cost` reports three errors.** Remove `cost` from the first building of
   `factions/malazan/buildings.json`: the loader reports `buildings[0].cost: missing required field`,
   `buildings[0].cost.gold: missing required field` and `buildings[0].cost.wood: missing required field`.
   A missing `footprint` is deliberately one error ("A missing footprint is one error, not three" in
   `DataLoader.BuildBuildings`). Test: `QA/BuildingDataQaTests.MissingCostObject_IsOneError_LikeAMissingFootprint`
   (skipped with this id). Every other broken building field tried (16 rows: absurd numbers, wrong
   types, empty slot / description, upper-case slot) is exactly one clear error.
2. **Test lines over the brief's budget.** The brief caps tests at 1,000 lines; the diff adds 1,286
   test lines (the developer's report says about 1,270 and flags it). Production code is 1,309 of
   1,500. Producer's call; noted so the budget stays meaningful.
3. **`--workers 0` without resources.** docs/03 says `--workers` "needs `--forests` or `--mines` above
   0, else exit 1"; `run --seed 3 --units 10 --workers 0` (no resources) runs and exits 0, printing
   `player 0 gold 200 wood 200`. Sensible behaviour; the docs sentence should say "N above 0".

## Expected
One error per missing object, like `footprint`; budgets met or explicitly waived; docs match the CLI.
