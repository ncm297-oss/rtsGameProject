# BUG-1172: The loader's unit / building key lookup is a linear walk per row since M7-3, so a load is quadratic in the row count (10k-unit Perf row 2.5x slower, near its bound)

| Field | Value |
| --- | --- |
| Severity | S3 (Perf headroom; no gameplay effect; shipped data has 14 units and 20 buildings) |
| Status | open |
| Found | 2026-11-10-1236, task M7-3 (QA, full) |
| System | data loader (`DataLoader.KeyId`, `DataLoader.Factions.cs`; callers in `DataLoader.cs`, `.Techs.cs`, `.Passives.cs`, `.Abilities.cs`, `.Ai.cs`) |
| Fixed by | |

## Repro
1. `git archive` the diff base (dacd5b9) and the M7-3 head into two scratch folders and build both.
2. In each, alone and interleaved: `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~TenThousandUnitFile_LoadsUnderTwoSeconds" --logger "console;verbosity=detailed"`.
   - base: `10k-unit load: 559 ms` / `562 ms`
   - head: `10k-unit load: 1344 ms` / `1500 ms` (bound 2,000 ms; same 117 MB allocated)

## Expected
The load stays linear-ish in the number of rows (the M7-3 brief changed the id *order*, not the lookup's cost), so
`QA/DataLoaderQaTests.TenThousandUnitFile_LoadsUnderTwoSeconds` keeps its headroom (its comment: "~0.2-0.3 s alone, ~0.7 s
under the parallel suite; 2 s leaves headroom on a slower machine").

## Actual
M7-3 replaced every `Array.BinarySearch(unitKeys / buildingKeys, key)` with `KeyId` = `Array.IndexOf` (the id tables are
ordinal only within a faction), and the per-row passes (pass 2 of the units, `CheckSlots`, `trainedAt`, passives, abilities,
tech requires and effects, the AI's ids) each call it once per row: O(rows^2) string compares. At 10,000 rows the load is
2.5x slower and within 1.3x of the Perf bound, so the row will turn red under the parallel suite or another track's load.

## Notes
- No gameplay or determinism effect: lookups are load-time only and their results are unchanged.
- Fix sketch: keep the faction-ordered `keys` array as the id table and build a load-time sorted index beside it (keys
  sorted ordinally + their ids, binary search into it), or a load-time `Dictionary<string, int>` used only for lookups
  (never iterated, so order can't leak). Either restores O(n log n) / O(n) without changing any id.
- `GameData.Find*` is a walk too now; its callers are setup / UI / test calls on tens of entries, so it doesn't matter there.
