# BUG-1432: Criterion 5 not met (Stop): a worker stopped while overlapped stays inside its mate's radii sum for 3-12 s; standing units never separate under today's rules

| Field | Value |
| --- | --- |
| Severity | S4 (filed S2 by QA against criterion 5 as written; re-triaged by the Producer at the 2026-11-14-2156 ACCEPT) |
| Status | wontfix (Producer decision 2026-11-14-2156, owner may revisit: a Stop does not push an overlapped pair apart; they part when either next moves) |
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

## Re-check 2026-11-14-2156, fix round 1 (4377d68): still open, S2; a Producer decision, can't be fixed in scope
No rule was added (no production change in 4377d68). docs/03's criterion-5 bullet now gives the analysis and names
BUG-1432. QA re-ran the off control in the default loop
(`GatherOverlapQaTests.Off_TwoIdleLaborersOverlapped_SeparationTime_Report`): unchanged. QA agrees with the developer's
reasoning. The Stop ends the gather pair, so the stopped worker and its mate become an Idle worker and a gatherer, and
criterion 4 requires that pair to stay on today's rules, which never separate two standing units. Meeting the Stop clause
needs either a standing-overlap rule for every pair (moves the shipped hash) or a knob-on-only exception to criterion 4.
**Criteria 4 and 5 conflict as written.** The Producer picks: amend criterion 5 (Stop: "separates when either unit next
moves", 2.8-11.5 s today) or schedule a rule.

## Producer ruling (2026-11-14-2156 ACCEPT)
- The brief's criteria 4 and 5 contradicted each other (the Producer's error at the PLAN): a Stop ends the gather
  pair, criterion 4 keeps every non-gather pair on today's rules, and today's rules never separate two standing
  units. Criterion 5 is amended to: **a Move separates the pair within 2 s (met: 3-5 ticks) and no unit ever steps
  past its speed (met); after a Stop the pair parts when either unit next moves** (measured: 2.8-11.5 s in the
  scenes, because a passing worker shoves one of them).
- Why not a rule: separating standing units is a change to every pair in the game (a hash move, every pin
  re-measured) for a case the player causes on purpose and that looks the same as today's knob-off game, where two
  walking Laborers can already end 2.8 cm apart. If the owner wants standing workers to spread out, it is one inbox
  note ("stopped workers should spread") and its own task.
- `GatherOverlapTests.Criterion5_*` keep their measured figures as reports.
