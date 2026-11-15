# BUG-1432: Criterion 5 not met (Stop): a worker stopped while overlapped stays inside its mate's radii sum for 3-12 s; standing units never separate under today's rules

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open (needs a Producer call: the criterion cannot hold "under today's rules") |
| Found | 2026-11-14-2156, task M6-S9 (QA; reported as "partly met" by the developer) |
| System | sim: movement (standing units, `MovementSystem`), the M6-S9 brief |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherOverlapTests.Criterion5" --logger "console;verbosity=detailed"`:
   the Stop rows print 230 ticks (two Laborers) and 56 ticks (a mixed pair) back to the radii sum; the test allows 1,200 for
   a Stop (40 for a Move).
2. QA's control with the knob off: `--filter "FullyQualifiedName~GatherOverlapQaTests.Off_TwoIdleLaborersOverlapped"`:
   two Idle Laborers spawned 0.4 m apart (radii sum 0.8 m) are still 0.4 m apart after 1,200 ticks.

## Expected
Brief criterion 5: "a worker overlapped with another gets a Move or Stop; the pair separates under today's rules within
2 s, and no unit's step in a tick exceeds its speed plus the overlap removed".

## Actual
- Move: separates in 3-5 ticks, no step past speed (QA's watch: 0 m past speed over every scene). Met.
- Stop: 230 ticks (11.5 s) for two Laborers, 56 ticks (2.8 s) for a mixed pair. The pair separates only when the mate next
  walks off, or a walker shoves the stopped unit. Not met.
- Off, two standing units that overlap never push apart (QA control above): there is no rule today that would separate
  them, so the clause cannot hold "under today's rules".

## Notes
- The new state is only reachable more often: today a pair can already end overlapped (the developer's table (a) shows two
  Laborers 2.8 cm apart at 0 on the tree line), and the same never-separates behaviour applies.
- The developer added no rule (correctly: the brief scoped movement rules to gather pairs). The fix is a Producer decision:
  amend the criterion (Stop: separates when either unit next moves), or schedule a standing-overlap rule.
