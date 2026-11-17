# BUG-1552: rule 68's handover missed two content classes that an AI re-measure moves

| Field | Value |
| --- | --- |
| Severity | S3 (process) |
| Status | fixed (2026-11-16-2036 ACCEPT: rule 68's list in the handoff now names seven classes) |
| Found | 2026-11-16-2036, task M6-S12 (the developer) |
| System | studio process (the Producer's brief) |
| Fixed by | the handoff's rule 68 amendment; no code |

## Repro
1. Any sim task that moves Normal AI play while the data track is stopped.

## Expected
The brief hands over every content class whose page cells the re-measure moves.

## Actual
`QA/Content/D20PinQaTests.HouseNote_*` and `Content/AiSpendingReportTests.GeneratedNormal_PagesAndDocs03_QuoteTheReport`
went red (the pages' "Houses at the cap" notes and "Spending on the menu's map" sentences); neither was in M6-S12's
handed list. The developer re-printed the pages' cells under rule 40 and edited neither test.

## Notes
The full list for the next such brief: `Content/AiOpeningPairsReportTests`, `Content/HolyWarFirstWaveReportTests`,
`Content/AiSpendingReportTests`, `QA/Content/DR35ProsePinQaTests`, `QA/Content/TeblorOpeningQaTests`,
`QA/Content/D21OpeningsQaTests`, `QA/Content/D20PinQaTests`.
