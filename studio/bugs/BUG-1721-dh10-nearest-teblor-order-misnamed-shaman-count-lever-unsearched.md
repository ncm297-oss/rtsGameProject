# BUG-1721: D-H10 names A as the nearest Teblor order, but orders that train one Shaman miss less than half as much

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (D36, 2026-11-25-1437; QA verified) |
| Found | 2026-11-25-0924, task D-H10 (QA) |
| System | data (Teblor `standard` opening, BUG-1612's unit mix), docs/factions/teblor.md "The unit mix (BUG-1612, D-H10)" |
| Fixed by | D36 (2026-11-25-1437): teblor.md corrected and the bounded search done (`Content/D36TeblorOrderSearchReportTests`) |

## Repro
QA's report class `sim/Rts.Sim.Tests/QA/Content/DH10TeblorOrderQaStressTests.cs` (Stress), on the branch at 0f8f210:
```
$env:QA_DH10_VARIANTS="M,A,QA1,QRD1,QA1T"; $env:QA_DH10_SEEDS="1,2,3,4,5,6,7,8"; $env:QA_DH10_THREADS="14"
dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DH10TeblorOrderQaStressTests.QA_Orders_Row_Report" --logger "console;verbosity=detailed"
```
(D34's row through `D34TeblorStepOrderReportTests.Play`, 64 seats a difficulty, to 15:00; M and A here equal the
developer's figures map for map.)

## Expected
teblor.md names the nearest candidate and the Producer's questions read the table (brief D-H10 criterion 3; rule 75 asks
QA to try orders of its own before agreeing that none meets the bar).

## Actual
None of QA's eight full-row orders meets the bar either, so "nothing ships" stands. But every order the developer
measured keeps the Shaman step's `count` 2, and changing it to 1 (the opening's only Shaman train step; "fielded" needs one
Shaman) halves the shortfall. Full rows, misses marked `!`, figures defeated / 2nd Longhouse / Shaman / Breaker / Rider /
War-dog / Anointing:

| order | Easy | Normal | Hard | misses | seats short |
| --- | --- | --- | --- | --- | --- |
| M (shipped) | 1 / 0 / 16! / 30 / 4! / 27! / 28 | 9 / 23 / 15! / 14 / 22 / 37 / 17 | 11 / 23 / 22 / 39 / 33! / 46 / 23 | 5 | 50 |
| A (dev's nearest) | 1 / 0 / 36 / 11! / 28 / 48 / 9! | 9 / 23 / 28 / 11! / 28 / 41 / 13! | 11 / 18 / 40 / 38 / 35! / 42! / 20 | 6 | 34 |
| QA1 = A, Shaman count 1 | 1 / 0 / 36 / 20 / 28 / 48 / 10! | 9 / 23 / 28 / 11! / 26 / 41 / 13! | 11 / 18 / 40 / 39 / 38! / 46 / 25 | 4 | 17 |
| QRD1 = `throughReserve` on Rider and War-dog only, Shaman count 1 | 1 / 0 / 17! / 29 / 36 / 47 / 18! | 9 / 23 / 19! / 13 / 25 / 38 / 12! | 11 / 23 / 23 / 40 / 42 / 47 / 25 | 4 | 16 |
| QA1T = QA1, Carver and Anointing at 9:30 | 1 / 0 / 25 / 36 / 44 / 51 / 5! | 11! / 23 / 17! / 11! / 27 / 40 / 18 | 12 / 18 / 32 / 42 / 45 / 49 / 30 | 4 | 20 |

So teblor.md's "the nearest candidate is A" and "No candidate brings Hard's Riders past 37" hold only for the
developer's eleven; QRD1 and QA1T meet every Hard figure (Riders 42 / 45), and QRD1 is 1 Shaman short on Normal and 2
Anointings short on Easy. Questions for the Producer 1 ("the bar may not be reachable by one opening") rests on the
same incomplete search. Six more QA orders (QA1L, QRD1T, QRD1E, QRD1F full rows; QL11, QL12, QRD, QS1 reduced) are in
QA's report `studio/qa/2026-11-25-0924-D-H10.md`; none meets the bar.

## Notes
- The figures swing a lot for small edits (QRD1F, the Circle and Shaman moved ahead of the Breaker: Normal Breakers 13 ->
  0), so a short search around QRD1 / QA1 (Shaman count 1, which steps carry `throughReserve`, the Circle's time) may find
  an order that meets the bar; that is a fix-round or next-session call for the Producer.
- A Shaman count change is a step change: the brief allows steps "if needed"; the developer's report class restricts
  itself to "never re-counted" by its own choice.
- The tooltip ("Shamans, Breakers, Destrier Riders ... may follow") stays true with one Shaman.

## Fix (D36, 2026-11-25-1437)
- teblor.md "The unit mix (BUG-1612, D-H10)": "the nearest candidate is A" now reads "the nearest of D-H10's own
  eleven", with the correction (QA1 / QRD1 nearer); "No candidate brings Hard's Riders past 37" is limited to D-H10's
  eleven (QRD1 42, QA1T 45); Question 1 carries the Producer's answer (not accepted as shown: its premise rested on the
  two-Shaman search) and Question 2 its answer (A does not ship).
- The search the bug asked for: `Content/D36TeblorOrderSearchReportTests` (Stress), every lever of the brief varied
  (57 orders screened, the count 1 / 2, the Shaman's position, the switches on all three train steps, the Circle's,
  Anointing's and Carver's time, the Anointing's position, `noReserve` on `armor_1`), six full rows (the shipped order,
  re-measured equal to D-H10's, and five candidates). None meets the bar; the nearest is QRD1, re-measured equal to QA's
  figures (4 figures missed, 16 seats short). New section teblor.md "The unit mix, second search (BUG-1612, BUG-1721, D36)".
