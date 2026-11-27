# BUG-1852: An AI seat re-sends a Gather to a gone node 9 thinks in a row (Raraku seed 3 / 4 / 6, tick 34,181)

| Field | Value |
| --- | --- |
| Severity | S3 (a criterion check fails and the match runs past its bound; the seat keeps playing) |
| Status | open (traced only as far as below) |
| Found | 2026-11-26-1847, task M6-S14 (the pin move: `MatchCriterionTests`' Raraku row, `Stress/MatchCriterionSweepQaStressTests`) |
| System | AI economy (`EconomyManager`, the Gather to a remembered node; `NoteDroppedGather` / `NoteDroppedRebalance`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MatchCriterionSweepQaStressTests&FullyQualifiedName~raraku"`
   (seeds 3, 4 and 6 are one match; listed in `Known` with this id since M6-S14).
2. `SWEEP | raraku | 4 | NO END | 34182 (28:29) | ... | refused Gather:NoResourceNode 27 | streak 9`; the error:
   `tick 34181: player 0 Gather unit 45 (gen 6) type 0 at (166, 150): refused 9 thinks in a row`. The worst stuck unit:
   unit 45 (a Malazan Laborer) at (86.3, 112.2), goal (163.9, 138.4), since 21,433.

## Expected
A Gather refused because its node is gone is not sent again to that node (BUG-0570 / BUG-1151 mark the node dropped).

## Actual
Player 0 sends the same Gather every think from at least 33,800; each is refused `NoResourceNode`. With the checks off the
match ends at 39,630 (seat 1). `MatchCriterionTests`' Raraku row moved from seed 3 to seed 2 under rule 45 (the match also
ends past the 36,000 bound).

## Notes
- Not traced in M6-S14: which path re-sends it (the worker is stuck Moving, so `NoteDroppedGather`, which judges an Idle
  worker, may never see it), and whether the base has the same path dormant. Probably not caused by an M6-S14 rule (none
  of them sends a Gather); the match reached the state through M6-S14's changed play.
