# BUG-1340: M7-7 Part A's "the Shaman stands with the army" figures count the other Shaman as the army

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-13-2306, task M7-7 (QA) |
| System | AI measurement (`Stress/FrenzyCastReportTests`), docs/03 "Implementation (M7-7)" |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FrenzyCastReportQaStressTests"` (QA's independent
   re-measure: the same 32 matches, the same copy, read from the world, the army = own non-worker units that are **not
   Shamans**, split before / after 5:00).
2. Compare with the diagnostic column of docs/03's M7-7 table (2 Shamans spawned at 0:00).

## Expected
The second candidate cause ("Shaman away from the army") measured against the army the Frenzy is for. The brief: "N own
army units within 8 m ... the Shaman's distance to the army centroid".

## Actual
`FrenzyCastReportTests.Measure` counts every army slot except the caster, so the other spawned Shaman is "the army".
In the opening minutes the Teblor's army *is* the two Shamans, standing 0.9 m apart, so most of the diagnostic's ready
thinks measure the gap between them:

| diagnostic, 32 matches | developer (docs/03) | QA, Shamans not counted as army |
| --- | --- | --- |
| ready thinks | 14,852 | 14,996 (9,568 before 5:00, 5,428 after) |
| no army at all besides Shamans | not reported | 9,549 of 9,568 before 5:00; 1,624 of 5,428 after |
| own army within 8 m, 1+ / 2+ / 4+ | 10,918 / 1,912 / 0 | 2,639 / 250 / 0 |
| distance to army centroid, median | 2.0 m | before 5:00 4.7 m (19 thinks with any army); after 7.1 m |
| own army **attacking** within 8 m, 2+ | 31 | 0 |
| today's rule (2+ enemies within 8 m) | 53 | 56 |

The shipped-opening column is unaffected (no spawned second Shaman; QA: 3 of 32 matches field one, at 13:09, 13:51,
14:44; alive 0 / 0 / 2; 79 ready thinks, 48 with 2+ army units within 8 m, median 17.6 m).

So docs/03's sentence "when Shamans exist (the diagnostic) they stand with the army (median 2.0 m from its centroid, an
own unit within 8 m in 74 % of the ready thinks)" is not what the numbers show: with Shamans spawned at 0:00 the Shaman
has two non-Shaman army units within 8 m in 1.7 % of its ready thinks and four never. The data-track paragraph's "Today's
cast rule then casts about two Frenzies a match with two Shamans in the army" rests on the same column.

## Notes
- The named cause, "too few Shamans", stands: QA reproduced the shipped and landing figures. The proposed own-fighting
  rule would fire even less than the developer reports (0, not 31, at 2+), so not building Part B is still right.
- The diagnostic spawns Shamans at 0:00, which is not how the AI fields them (Age II, a share of a larger army), so
  neither column says where a trained Shaman stands in a fight. When the data track fields Shamans, re-measure candidate
  2 with Shamans left out of the army count (and say whether to count other Shamans at all) before ruling it out.
- Fix: in `Measure`, leave units of the caster's type out of the army (or report both), re-run, and correct the docs/03
  table, the "cause" bullet and the "For the data track (M7-7)" paragraph.
