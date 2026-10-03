# BUG-0009: Null or blank entries in `requires` / `tags` are copied into GameData

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-03-1151, task M1-2 |
| System | data loader |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.DataLoaderQaTests.NullEntryInRequiresOrTags_IsRejected`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~NullEntryInRequiresOrTags"`

By hand: set `malazan_crossbowman.requires` to `[null]` or `malazan_sapper.tags` to `["", null]`.

## Expected
Every other string field goes through `Checker.Text`/`Id` and a null or blank value is a
`DataError`. List entries should get the same check (ideally `Id`, since tags and requires are ids).

## Actual
Loads `Ok`; `UnitDef.Requires` contains a `null` string and `UnitDef.Tags` contains `""` and
`null`, even though the property is typed `ImmutableArray<string>` (non-nullable). A later system
that matches tags or resolves requires will hit a null or never match.

## Notes
`BuildUnit` does `ImmutableArray.CreateRange(u.Requires ?? new List<string>())` with no per-entry
check. Duplicate entries (`["infantry", "infantry"]`) are also accepted; probably harmless.

**Producer triage (2026-10-03-1151):** S4 confirmed. Fix in the first commit of M1-3 alongside BUG-0007: run each `requires`/`tags` entry through `Checker.Id`, un-skip `NullEntryInRequiresOrTags_IsRejected`.
