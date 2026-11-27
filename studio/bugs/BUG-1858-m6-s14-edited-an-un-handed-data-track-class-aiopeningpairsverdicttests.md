# BUG-1858: M6-S14 edited a data-track test class that was not handed over (`Content/AiOpeningPairsVerdictTests`)

| Field | Value |
| --- | --- |
| Severity | S2 (a change outside the track's files: producer.md "Tracks", rule 68) |
| Status | open (cure: a Producer ruling at the ACCEPT, no code change) |
| Found | 2026-11-26-1847, task M6-S14 (QA, tier full) |
| System | process / track ownership (`sim/Rts.Sim.Tests/Content/` is the data track's) |
| Fixed by | |

## Repro
1. `git diff f40cb16e...HEAD -- sim/Rts.Sim.Tests/Content/AiOpeningPairsVerdictTests.cs` (commit `1779d527`).
2. The brief's handed list (handoff "Track sim: M6-S14", the pin list) names eight content classes and says "you may re-pin
   these classes and the page cells they read, rule 40, and nothing else in the data track's folders".
   `AiOpeningPairsVerdictTests` is not on it.

## Expected
Only the named classes, the D38 skips and the pages' pinned cells change under the data track's folders; a row in another
data-track class that the session's play breaks is left red and named (as the developer did for BUG-1850's three rows), or
handed over by a Producer ruling first.

## Actual
`DefeatedBeforeDue_HoldsOnlyTheMeasuredCase` was rewritten: `Assert.Empty(AiOpeningPairsReportTests.DefeatedBeforeDue)` became
`Assert.Equal(new[] { (0, 1, 2UL, 0) }, ...)` plus `Assert.Equal(2UL, AiOpeningSeeds.SmallestPair(0, 1))`, with a "was" comment.
The developer reported it as a deviation (BUG-1856's case). The edit itself is correct and minimal: it follows the handed
class's list, which M6-S14 changed by right.

## Notes
- Root cause is the brief's: `AiOpeningPairsVerdictTests` pins the handed class's `DefeatedBeforeDue` list, so re-pinning that
  list (allowed) breaks this row (not allowed). Rule 68's handed list should name a class's verdict twin with it.
- Cure: the Producer rules the edit in at the ACCEPT (it is what a hand-over would have produced) and adds
  `Content/AiOpeningPairsVerdictTests` to rule 68's handed list. No re-run is needed: the class is green on 0058e09d.
- The same applies to the developer's inconsistent handling: BUG-1850's three rows (also un-handed) were left red.
