# BUG-0008: Duplicate JSON keys are silently resolved last-wins

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-03-1151, task M1-2 |
| System | data loader |
| Fixed by | 8928418 (M3-6): `Utf8JsonReader` pre-pass; `QA/DataLoaderQaTests` duplicate rows un-skipped, `RequirementLoaderTests.ADuplicateKey_*`, QA `RequirementQaTests.ADuplicateKey_AtAnyDepth_*` (root and depth 2-4, all seven file kinds, escaped key, BOM) |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.DataLoaderQaTests.DuplicateBonusVsKey_IsReported` and
   `DuplicatePropertyInUnit_IsReported`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Duplicate"`

By hand: in a copy of `game/data/`, edit `factions/malazan/units.json` so the crossbowman reads
`"hp": 55, "hp": 5500,` (or `"bonusVs": { "heavy": 1.3, "heavy": 9.0 }`) and load it.

## Expected
docs/03 "What ships today": unknown fields are errors because "a typo must not silently fall back to
a default". A repeated key is the same class of mistake (usually a copy-paste or merge leftover)
and should be a `DataError` naming file and field, not a silent pick.

## Actual
Loads `Ok`. The last value wins: crossbowman `Hp == 5500`, `BonusVs[heavy] == 9`. Applies to every
object in every data file (unit fields, `attack`, `bonusVs`, damage-table `multipliers`, rules).

## Notes
System.Text.Json on .NET 8 has no built-in duplicate-property rejection for POCOs or
`Dictionary<,>` (the `AllowDuplicateProperties` option arrives in .NET 10). Options: pre-scan each
file with `Utf8JsonReader` (load time only, no reflection) tracking property names per object
depth, or read the dictionaries through a small custom converter that reports repeats.

**Producer triage (2026-10-03-1151):** S3 confirmed, does not block. Needs a `Utf8JsonReader` pre-scan (~40 lines); fix with the M3 data task that adds `buildings.json`/`techs.json`, when the loader grows anyway.
