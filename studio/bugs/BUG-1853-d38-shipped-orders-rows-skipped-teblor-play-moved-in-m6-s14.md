# BUG-1853: The D38 shipped-orders rows no longer read D37's table after M6-S14 moved Teblor play (skipped for the data track's re-measure)

| Field | Value |
| --- | --- |
| Severity | S4 (two report rows skipped by the brief's rule; no game behaviour is wrong) |
| Status | open (the data task after M6-S14 re-measures the three Teblor orders and re-points the rows at its own table) |
| Found | 2026-11-26-1847, task M6-S14 (the brief's handover, rule 68 / 70) |
| System | tests: `Content/D38ShippedOrdersReportTests.ShippedFile_SixtyFourSeats_EqualD37sRows_Report`, `QA/Content/D38ShippedOrdersQaStressTests.QA_ShippedFile_SixtyFourSeats_EqualTheBriefsRows_Report` |
| Fixed by | |

## Repro
1. Remove the Skip and run either row (Stress, 30+ minutes each).
2. QA's row on the M6-S14 tree: `Easy: QA 9 / 0 / 45 / 29 / 35 / 47 / 11, brief 1 / 0 / 38 / 28 / 34 / 42 / 22; Normal: QA 13 /
   37 / 34 / 2 / 27 / 33 / 15, brief 9 / 23 / 23 / 18 / 30 / 40 / 18; Hard: QA 9 / 26 / 18 / 34 / 37 / 43 / 21, brief 11 /
   23 / 23 / 40 / 42 / 47 / 25`.

## Expected / Actual
The rows compare the shipped file's 21 figures with D37's table; Teblor play moved with M6-S14's AI cures (the worker
floor first among them), as the Producer's ruling at the 2026-11-26-0717 ACCEPT foresaw, so both are skipped
`BUG-1853: re-measured by the data track after M6-S14`.
