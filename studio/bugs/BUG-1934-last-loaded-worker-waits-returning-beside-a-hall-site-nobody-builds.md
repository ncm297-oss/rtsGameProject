# BUG-1934: A seat's last loaded worker waits Returning for good beside a hall site nobody builds

| Field | Value |
| --- | --- |
| Severity | S3 (a losing seat sends nothing for 7,819 ticks; QA's silent-seat oracle) |
| Status | open (triaged at the 2026-11-27-1857 ACCEPT: the sim's next AI task; `MatchCriterionSweepQaStressTests` Pale Hills seed 2 is in `Known` with this id by the Producer's ruling, rule 57) |
| Found | 2026-11-27-1857, task M6-S15 (the pin move: `Stress/MatchCriterionSweepQaStressTests` Pale Hills seed 2; filed from the developer's patch, confirmed by QA) |
| System | AI economy (`EconomyManager.Sites`: the join rule's MaxJoins), worker delivery |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MatchCriterionSweepQaStressTests"` (Stress): the row
`pale_hills, 2` (now in `Known`).

## Expected
A seat with a worker and a drop-off site builds the site (or its worker drops the load and builds it).

## Actual
p0 loses its Depot about 40,000; its only drop-off is a Keep site whose three joins are spent; its last Laborer stands
Returning with 10 wood from about 42,000 to its defeat at 48,914 (bank 1,935 gold / 45 wood). Green on the base (the
match there ended at 59,700, p0): the match path moved with M6-S15 C.
