# BUG-1859: M6-S14 criterion 13 not met: the default loop is red in three rows (format-10 fixture saves) and the Stress category in one (BUG-1851)

| Field | Value |
| --- | --- |
| Severity | S2 (an acceptance criterion not met) |
| Status | open |
| Found | 2026-11-26-1847, task M6-S14 (QA, tier full; the developer's report names the same rows as BUG-1850, S3) |
| System | tests: `Content/D32PreFixtureTests`, `QA/Content/D31LandingQaTests` (data track's folders); save format 11 |
| Fixed by | |

## Repro
1. On `0058e09d`: `dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf&Category!=Stress&Category!=Soak"`.
2. Red: `D31LandingQaTests.ASaveMadeBeforeD31_IsRefusedForItsDataHash_WithTheUiText`,
   `D32PreFixtureTests.ASaveMadeBeforeD32_IsRefusedForItsDataHash_WithTheUiText`,
   `D32PreFixtureTests.OnTheDiffBasesData_BothFixturesLoad`: the fixture saves are format 10 and the format-11 reader refuses
   them `FormatVersionMismatch` before it reads their data hash (expected `DataMismatch` / `None`).

3. The Stress category (QA's whole-category run on a fresh clone of 0058e09d): `QA/Content/DH6ThresholdQaTests.Bug0982_KeepEighteen_HoldsOnTwoMoreSeeds`
   is red, not skipped ("rule over 5 seeds picks 18; shipped 16": the developer's BUG-1851), in a data-track class not handed
   over.

## Expected
Criterion 13: "The default loop ... is green after the last edit, the handed classes included; the Stress category is green
apart from rows skipped with a bug id".

## Actual
Three red rows (QA's run and the developer's: 10,837 passed / 3 failed / 21 skipped of 10,861). The refusal itself is right
(the brief moved the format: "saves from before stop loading"); the rows pin the old reason.

## Notes
- This is the brief's conflict as much as the developer's (rule 68: "a brief never writes 'the default loop green' beside
  rows it forbids the developer to fix"): the format bump was certain at PLAN, and these two classes were not handed over.
  Read with the developer's BUG-1850 (same rows, filed S3); this file records the criterion failure.
- Cheapest honest cure (tests only, so no Stress re-run, rule 72): in the two data-track files, each refusal row asserts
  `FormatVersionMismatch` on the raw bytes and then relabels the version to `SaveFormat.CurrentVersion` and re-checksums
  (`SaveBytes.Rechecksum`) to keep its `DataMismatch` proof; `OnTheDiffBasesData_BothFixturesLoad` loads the relabelled
  bytes. This is exactly what M6-S14 did to its own `Save/PreM73bRefusalTests`. It works because both fixtures seat only
  human players, and docs/03 M6-S14 states that "a save without an AI seat differs from format 10 only in this number".
  About ten lines across `sim/Rts.Sim.Tests/Content/D32PreFixtureTests.cs` and
  `sim/Rts.Sim.Tests/QA/Content/D31LandingQaTests.cs`, both data-track files: the Producer either hands them to the sim
  developer for one tests-only fix round, or (rule 70) skips the three rows with `Skip = "BUG-1850: ..."` and puts the cure
  first in the data track's next brief. `main` takes no red default-loop row by ruling (rule 70).
- The Stress row (BUG-1851) cannot be cured by tests: the BUG-0982 rule now picks 18 while `cadre_rush` ships 16, a data
  balance call the sim may not make (rule 48). Cure: `Skip = "BUG-1851: ..."` on that one row (one line in
  `sim/Rts.Sim.Tests/QA/Content/DH6ThresholdQaTests.cs`, a data-track file, so by hand-over or ruling), its re-measure first
  in the data track's next task, as M6-S14 already did for the two handed `Bug0982_*` rows.
- A second Stress row is red on 0058e09d and was not in the developer's report: `M5H5HuntAfterDefeatQaTests` Vathar
  Crossing 2v2 seed 6, an AI cast onto an ally's building, filed on its own as BUG-1861 (it needs a code cure or a ruling, not
  a test edit).
