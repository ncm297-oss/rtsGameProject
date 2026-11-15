# BUG-1400: TeblorLandingOpeningReportTests.TheRows_ChangeTheMatch_FromTheFirstFrenzyOn_Report fails since D31 (Stress)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-14-1246, task M8-1 (QA, sim track; not caused by M8-1) |
| System | data track content tests (`sim/Rts.Sim.Tests/Content/`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TeblorLandingOpeningReportTests.TheRows_ChangeTheMatch"`
   (Stress category, about 30 s).
2. Fails on the M8-1 head 62542ff **and on its base 93c2f3b** (a clean clone of each, run alone), so it predates M8-1.

## Expected
The row passes: "without the rows" and "with the rows" see the same first Frenzy tick, and the two runs diverge from it.

## Actual
```
System.AggregateException : One or more errors occurred.
 (Assert.Equal() Failure: Expected: -1 Actual: 15271)
 (Assert.Equal() Failure: Expected: -1 Actual: 17411)
 (Assert.Equal() Failure: Expected: -1 Actual: 15191)
   at TeblorLandingOpeningReportTests.<>c__DisplayClass..b__0 (line 178: Assert.Equal(castOff, castOn))
```
The "off" run never sees a cast (`castOff` stays -1) while the "on" run casts at 12:43, 14:30 and 12:39 in three of the
eight pairings.

## Notes
- Likely cause: the "off" copy is the shipped data (`Make(v0, rows: false, ...)` applies no edit), and the test looks for
  its cast as the status `frenzied` (`off.FindStatus("frenzied")`). Since D31 (7eeff92) the shipped data already carries the
  rows, so its Frenzy applies `teblor_frenzied` and the "off" cast is never seen. The test was written at D30 (290cc71),
  before the rows shipped; with the rows shipped, "without the rows" no longer exists in this harness, so the row needs
  re-thinking (or retiring), not just the status name.
- The default loop (`Category!=Stress`) does not run it, so it went unnoticed; the sim track's "default loop with Stress"
  (rule 53) shows it red. The file is the data track's (`Content/`); not touched by M8-1.

## Triage (Producer, 2026-11-14-1246 ACCEPT)
S3 stands; it does not block M8-1 (the row is red on the base too) or D32 (D32 did not touch the file). It is a Stress
row, so the default loop is green. Rule 57 ruling: the row stays red on `main` for one session and its fix (re-think
or retire the row: "without the rows" no longer exists on the shipped data) is the **first item of the data track's
next brief**; until then every Stress run's expected result names this one row.
