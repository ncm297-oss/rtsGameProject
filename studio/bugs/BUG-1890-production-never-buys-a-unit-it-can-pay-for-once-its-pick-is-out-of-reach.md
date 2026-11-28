# BUG-1890: A seat that can pay for a unit in gold and wood never trains it once its production pick is out of reach (Pale Hills four-seat seeds 2 and 4 never end)

| Field | Value |
| --- | --- |
| Severity | S3 (two four-seat AI matches never end; measure and report rows, not criterion rows) |
| Status | open (a finding for the next AI task) |
| Found | 2026-11-27-0817, task M6-SH3 (B's pre-trace) |
| System | AI production (`ProductionManager.Pick`: "it waits rather than buy a cheaper one") |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DrawReachPreTraceTests"` (Category Stress; base 0159c425 and
   after M6-SH3 B alike).
2. Pale Hills four-seat FFA, four Normal seats, at 72,000: seed 2, p0 (Malazan) 40 gold / 13,580 wood can pay for a
   Crossbowman (36 / 45), p2 50 gold / 15,735 wood for a Crossbowman or a Laborer (50 / 0); seed 4, p0 52 gold / 11,155
   wood (and 726 gold left on the map within reach). None sends a Train from 48,000 to 72,000.

## Expected
docs/02 "Game flow" 4: a match is drawn once nobody can train; a seat that can train should either use what it has or the
match must be able to end some other way.

## Actual
Production waits for its pick (the most under-weighted composition unit), which the seat cannot pay for; it never buys the
cheaper unit it can afford, and the worker floor is met (26-28 workers). The draw rule rightly counts these seats as able to
train (they can), so the matches never become spent and never end (seed 2 and 4: no end by 144,000).

## Notes
- Candidate cure (new production behaviour, so not done in M6-SH3): when the seat is starved (M5-9a's `Starved()`: income
  can never close the gap to the pick), production buys the most under-weighted composition unit it can pay for now. One
  Crossbowman each would make seed 2 spent within a few thinks (p0 4 gold, p2 14 gold left).
- With BUG-1891 on seed 4.
