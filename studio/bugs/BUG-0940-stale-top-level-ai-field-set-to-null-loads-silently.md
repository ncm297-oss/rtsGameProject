# BUG-0940: a pre-M5-8a top-level `buildOrder` / `compositions` / `attack` set to `null` loads with no error

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-17-2344, task M5-8a (QA, full tier) |
| System | data loader: `Data/DataLoader.Ai.cs` (`BuildAiDef`), `Data/DataJson.cs` (`AiFileJson`) |
| Fixed by | M5-H4, session 2026-10-26-2108 |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiOpeningsQaTests.AnOldTopLevelFieldSetToNull_IsRefused"`
   (skipped with this id; remove the `Skip` to run it).
2. The row: the shipped data with `factions/malazan/ai.json` given a top-level `"buildOrder": null` (or `"compositions": null`,
   `"attack": null`) next to the valid `openings`.

## Expected
docs/03 "Implementation (M5-8a)": the old top-level fields "are each one error ("moved into openings[]"), so a stale file
can't half-load"; docs/03 M5-1: "Unknown fields ... are errors, as everywhere". An unknown top-level field set to `null` is an
error (`AiOpeningsQaTests.AnUnknownTopLevelFieldSetToNull_IsAnError`: "could not be mapped to any .NET member").

## Actual
The file loads clean (0 errors). `AiFileJson.BuildOrder` etc. are `JsonElement?`, and System.Text.Json reads a JSON `null`
into a `Nullable<JsonElement>` as `null`, so `if (j.BuildOrder != null)` in `BuildAiDef` doesn't fire. The three removed fields
are the only top-level keys that accept `null` without an error.

## Notes
- No gameplay effect: a `null` field carries no steps, so nothing half-loads. It is the one hole in "stale fields are refused".
- Fix idea: check `JsonElement?.HasValue` is not enough (it is false for `null`); either read the fields as `JsonNode?` /
  `object?` with a presence flag, or a small converter that records "the key was present". Then un-skip the row.

## Fix (M5-H4, session 2026-10-26-2108)
- `AiFileJson.BuildOrder` / `Compositions` / `Attack` are read as `Present` (`Data/DataJson.cs`), whose converter handles
  null (`HandleNull`) and skips any value, so a key set to `null` is seen and refused "moved into openings[]" like any other
  value. `QA/AiOpeningsQaTests.AnOldTopLevelFieldSetToNull_IsRefused` un-skipped (its three rows failed before, green now).
