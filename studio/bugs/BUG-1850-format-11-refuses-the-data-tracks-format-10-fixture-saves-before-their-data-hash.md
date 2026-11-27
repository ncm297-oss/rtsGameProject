# BUG-1850: M6-S14's save format 11 refuses the data track's format-10 fixture saves before their data hash is read: three content rows red

| Field | Value |
| --- | --- |
| Severity | S3 (three default-loop rows red in files the sim track may not edit; no game behaviour is wrong) |
| Status | fixed (the same rows as BUG-1859: M6-S14 fix round 1, tests only, QA verified 2026-11-26-1847) |
| Found | 2026-11-26-1847, task M6-S14 (the developer's default loop) |
| System | tests: `sim/Rts.Sim.Tests/Content/D32PreFixtureTests.cs`, `sim/Rts.Sim.Tests/QA/Content/D31LandingQaTests.cs` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D32PreFixtureTests|FullyQualifiedName~D31LandingQaTests.ASaveMadeBeforeD31"`.
2. `D32PreFixtureTests.ASaveMadeBeforeD32_IsRefusedForItsDataHash_WithTheUiText` and
   `D31LandingQaTests.ASaveMadeBeforeD31_IsRefusedForItsDataHash_WithTheUiText`: expected `DataMismatch`, actual
   `FormatVersionMismatch`. `D32PreFixtureTests.OnTheDiffBasesData_BothFixturesLoad`: the fixture save no longer loads on the
   diff base's rebuilt data (its format is 10).

## Expected
The rows prove that a save from before D31 / D32 is refused and the player reads a text; the brief for M6-S14 moved the save
format to 11 ("saves from before stop loading"), so the refusal is right, but its reason is now the format, which the save
reader checks first (the header's version comes before its data hash).

## Actual
The three rows pin `DataMismatch` (and a load of the format-10 fixture). They are in the data track's folders, not handed to
M6-S14 (rule 68), so the sim developer left them red and named them in its report.

## Notes
- Cure (one of): expect `FormatVersionMismatch` for these fixtures and show `ui.json` `save.format_mismatch` (or relabel
  the fixture's version to `SaveFormat.CurrentVersion` and re-checksum it before the load, as
  `Save/PreM73bRefusalTests` now does for its own fixture, which keeps the `DataMismatch` proof); and
  `OnTheDiffBasesData_BothFixturesLoad` relabels the same way or is restated. The sim's own pre-M7-3b fixture row was
  handled in M6-S14 that second way.
