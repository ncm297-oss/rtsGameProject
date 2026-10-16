# BUG-0780: Catapults under 2x the Raider's damage per cost on a hall and a Town Hall (surround rule + D19)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-16 D-H5 (data hardening), while fixing BUG-0721 item 1 |
| System | content balance (siege v buildings), `Content/BuildingKillTimeReportTests` |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~BuildingKillTimeReportTests" --logger "console;verbosity=detailed"`,
then read the "today" table's catapult row and the line "siege cells under 2x the Raider's damage per cost".

## Expected
The D18 band (the Producer's default, D18 brief): each siege group does at least 2x "the line units'" damage per second per
gold + wood against the same building. BUG-0721 item 1 asked for the check against both line rows (20 Heavy Infantry, 20 Raiders).

## Actual
Shipped data (D19) on the merged tree (sim M5-P1 surround rule), measured 2026-10-16:

| 2 Catapults (700) | House | Infantry hall | Town Hall |
| --- | --- | --- | --- |
| x Heavy Infantry per cost | 3.9 | 2.3 | 2.3 |
| x Raider per cost | 2.8 | **1.7** | **1.6** |

2 Sappers: 5.7 / 4.8 / 4.8x HI, 4.1 / 3.5 / 3.3x Raider (all over 2x). The Catapult's own times did not move (6.3 / 21.4 /
41.4 s before and after D19); the line rows got faster (D19's Structure column and halved armor, then the surround rule:
20 Raiders 9.8 / 19.4 / 35.8 s), so the per-cost margin shrank. Against the Heavy Infantry every cell still clears 2x, so
the report's 12 band cells stay 12 / 12.

## Notes
Not loosened: `BuildingKillTimeReportTests.AttackerGroups_v_Buildings_TodayAndBeforeD19_Report` pins the two cells under
2x vs the Raider exactly (`malazan_catapult Infantry hall`, `malazan_catapult Town Hall`), so a fix or a further drop turns
it red. The band's wording ("the line units'") is the Producer's default, not an owner decision: either (a) the band is
read as vs the Heavy Infantry (the same faction's line; the Catapult is Malazan) and this is wontfix with that reason, or
(b) a data lever for the owner (Catapult damage or cost, Raider cost; out of D-H5, which moves no number). Producer's call.
